using DotNet.Testcontainers.Containers;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using SharpMUSH.Library;
using SharpMUSH.Library.Definitions;

namespace SharpMUSH.Benchmarks;

/// <summary>
/// Base class for all Lightning-backed benchmarks.
/// Lightning (LMDB via Lightning.NET) runs embedded in-process against a plain directory - no
/// database Testcontainer. It still spins up NATS and
/// wires up the full DI stack, providing a ready-to-use <see cref="IMUSHCodeParser"/>.
/// </summary>
[Config(typeof(AdaptiveBenchmarkConfig))]
public class LightningBaseBenchmark
{
	public LightningBaseBenchmark() =>
		Log.Logger = new LoggerConfiguration()
			.WriteTo.Console()
			.MinimumLevel.Information()
			.CreateLogger();

	protected TestWebApplicationBuilderFactory<Server.Program>? _server;
	protected ISharpDatabase? _database;
	private IContainer? _natsContainer;
	private string? _lightningPath;

	[GlobalSetup]
	public virtual async ValueTask Setup()
	{
		_natsContainer = await BenchmarkHelpers.StartNatsContainerAsync().ConfigureAwait(false);
		Environment.SetEnvironmentVariable("NATS_URL",
			$"nats://localhost:{_natsContainer.GetMappedPublicPort(4222)}");

		var configFile = Path.Combine(AppContext.BaseDirectory, "mushcnf.dst");

		// No container, no shared server: LMDB is a plain directory. Give each run its own so
		// benchmark iterations never collide with a prior run's data.
		_lightningPath = CreateDataDirectory();

		_server = new TestWebApplicationBuilderFactory<Server.Program>(
			configFile: configFile,
			databaseProvider: DatabaseProvider.Lightning,
			lightningPath: _lightningPath);

		_database = _server!.Services.GetRequiredService<ISharpDatabase>();
	}

	[GlobalCleanup]
	public async ValueTask Cleanup()
	{
		if (_natsContainer is not null)
			await _natsContainer.DisposeAsync().ConfigureAwait(false);

		_server?.Dispose();
		Environment.SetEnvironmentVariable("NATS_URL", null);

		if (_lightningPath is not null && Directory.Exists(_lightningPath))
		{
			try
			{
				Directory.Delete(_lightningPath, recursive: true);
			}
			catch (IOException)
			{
				// Best-effort: a lingering LMDB lock file (mdb.lck) can outlive the writer thread's
				// join by a few milliseconds under load. Leaving the temp directory behind costs
				// disk, not correctness.
			}
		}
	}

	protected async Task<IMUSHCodeParser?> TestParser() =>
		await BenchmarkHelpers.CreateTestParser(_database!, _server!.Services).ConfigureAwait(false);

	private IMUSHCodeParser? _baseParser;
	private DBRef _executor;

	/// <summary>
	/// Returns a parser over a fresh top-level state. Invocation and call-depth counters live on the
	/// state, so reusing one parser across benchmark iterations would eventually time only the
	/// function-limit short circuit.
	/// </summary>
	protected IMUSHCodeParser FreshParser()
	{
		if (_baseParser is null)
		{
			_baseParser = _server!.Services.GetRequiredService<IMUSHCodeParser>();
			_executor = BenchmarkHelpers.ExecutorDbRef(_database!).ConfigureAwait(false).GetAwaiter().GetResult();
		}

		return _baseParser.FromState(BenchmarkHelpers.FreshState(_executor));
	}

	/// <summary>
	/// Picks a per-run LMDB data directory under the real filesystem, never under
	/// <see cref="Path.GetTempPath"/> - on this machine (and most CI runners) that path is a tmpfs
	/// mount, which serves every write from RAM and hides the fsync cost every Lightning commit
	/// pays on real disk. <see cref="Environment.SpecialFolder.LocalApplicationData"/> is backed by
	/// disk, so benchmark numbers reflect what production actually costs. The
	/// <c>SHARPMUSH_LIGHTNING_BENCH_PATH</c> environment variable overrides the root (still with a
	/// fresh guid subdirectory beneath it), letting a runner point this at a specific disk without
	/// editing this file.
	/// </summary>
	internal static string CreateDataDirectory()
	{
		var root = Environment.GetEnvironmentVariable("SHARPMUSH_LIGHTNING_BENCH_PATH");
		if (string.IsNullOrEmpty(root))
		{
			root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
		}

		return Path.Combine(root, "sharpmush-bench", Guid.NewGuid().ToString("N"));
	}
}
