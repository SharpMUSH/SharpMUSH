using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;
using Testcontainers.MySql;
using TUnit.Core.Interfaces;

namespace SharpMUSH.Tests;

public class MySqlTestServer : IAsyncInitializer, IAsyncDisposable
{
	private const string DatabaseName = "sharpmush_test";

	/// <summary>
	/// Upper bound on the whole readiness wait. Testcontainers' default is one hour, so a stalled
	/// probe used to hang the test session until the CI job timed out, with no cause reported.
	/// </summary>
	private static readonly TimeSpan ReadinessTimeout = TimeSpan.FromMinutes(3);

	/// <summary>
	/// Upper bound on a single readiness probe. A probe that stalls (seen with exec under podman)
	/// is abandoned and retried instead of blocking the wait forever.
	/// </summary>
	private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(15);

	[ClassDataSource<DockerNetwork>(Shared = SharedType.PerTestSession)]
	public required DockerNetwork DockerNetwork { get; init; }

	public MySqlContainer Instance => field ??= new MySqlBuilder("mysql:latest")
		.WithNetwork(DockerNetwork.Instance)
		.WithDatabase(DatabaseName)
		.WithUsername("testuser")
		.WithPassword("testpass")
		.WithReuse(false)
		.WithWaitStrategy(Wait.ForUnixContainer()
			.AddCustomWaitStrategy(new BoundedMySqlProbe(), o => o.WithTimeout(ReadinessTimeout)))
		.Build();

	public async Task InitializeAsync()
	{
		try
		{
			await Instance.StartAsync();
		}
		catch (TimeoutException ex)
		{
			var (stdout, stderr) = await Instance.GetLogsAsync();
			throw new TimeoutException(
				$"MySQL test container {Instance.Id} was not ready within {ReadinessTimeout} " +
				$"(state: {Instance.State}). Last container log lines:{Environment.NewLine}{Tail(stdout + stderr)}",
				ex);
		}
	}

	public async ValueTask DisposeAsync()
	{
		try
		{
			await Instance.StopAsync();
		}
		catch
		{
			// Podman may fail if the network was already removed
		}

		try
		{
			await Instance.DisposeAsync();
		}
		catch
		{
			// Podman may fail if the network was already removed
		}
	}

	/// <summary>
	/// Gets a connection string for a specific database name. Creates the database if it doesn't exist.
	/// </summary>
	public string GetConnectionString(string databaseName)
	{
		var baseConnectionString = Instance.GetConnectionString();
		var builder = new MySqlConnector.MySqlConnectionStringBuilder(baseConnectionString)
		{
			Database = databaseName
		};
		return builder.ConnectionString;
	}

	private static string Tail(string log, int lines = 20) =>
		string.Join(Environment.NewLine, log.Split('\n', StringSplitOptions.RemoveEmptyEntries).TakeLast(lines));

	/// <summary>
	/// The same probe Testcontainers.MySql uses (<c>mysql --wait --silent --execute="SELECT 1;"</c>, with
	/// credentials from the /etc/mysql/my.cnf its startup callback writes), except each exec is bounded
	/// by <see cref="ProbeTimeout"/>. The stock probe passes no cancellation token, so one exec that never
	/// returns blocks readiness for the full wait-strategy timeout.
	/// </summary>
	private sealed class BoundedMySqlProbe : IWaitUntil
	{
		private static readonly string[] Command = ["mysql", DatabaseName, "--wait", "--silent", "--execute=SELECT 1;"];

		public async Task<bool> UntilAsync(IContainer container)
		{
			using var cts = new CancellationTokenSource(ProbeTimeout);
			try
			{
				var result = await container.ExecAsync(Command, cts.Token);
				return result.ExitCode == 0;
			}
			catch (OperationCanceledException) when (cts.IsCancellationRequested)
			{
				return false;
			}
		}
	}
}
