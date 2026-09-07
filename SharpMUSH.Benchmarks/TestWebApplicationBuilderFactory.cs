using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Serilog;
using SharpMUSH.Configuration;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server;

namespace SharpMUSH.Benchmarks;

/// <summary>
/// A <see cref="WebApplicationFactory{TProgram}"/> for benchmarks that wires configuration
/// through environment variables only — it does NOT call <c>startup.ConfigureServices</c>
/// directly to avoid the double-registration that occurs when <c>Server.Program</c> also
/// runs its own startup through <see cref="WebApplicationFactory{TProgram}"/>'s entry-point
/// host-builder path.
/// </summary>
public class TestWebApplicationBuilderFactory<TProgram>(
		string configFile,
		DatabaseProvider databaseProvider = DatabaseProvider.Lightning,
		string? surrealEndpoint = null,
		string? lightningPath = null) :
	WebApplicationFactory<TProgram> where TProgram : class
{
	protected override void ConfigureWebHost(IWebHostBuilder builder)
	{
		// Wire database configuration through the environment variables read by Server.Program.
		// This is the same approach the test suite uses (ServerWebAppFactory) and avoids
		// calling startup.ConfigureServices() a second time, which would double-register
		// the "compiled-expressions" FusionCache and throw at first resolution.
		if (databaseProvider == DatabaseProvider.SurrealDB)
		{
			// No Testcontainer: SurrealDB runs embedded in-process. mem:// keeps benchmark runs
			// isolated and disk-free, matching the test suite's default.
			Environment.SetEnvironmentVariable("SHARPMUSH_DATABASE_PROVIDER", "surrealdb");
			Environment.SetEnvironmentVariable("SHARPMUSH_SURREALDB_ENDPOINT", surrealEndpoint ?? "mem://");
		}
		else
		{
			// No Testcontainer: LMDB is a plain directory. The caller (LightningBaseBenchmark)
			// owns the directory's lifetime, matching the test suite's ServerWebAppFactory.
			Environment.SetEnvironmentVariable("SHARPMUSH_DATABASE_PROVIDER", "lightning");
			if (!string.IsNullOrEmpty(lightningPath))
				Environment.SetEnvironmentVariable("SHARPMUSH_LIGHTNING_PATH", lightningPath);
		}
		Log.Logger = BenchmarkHelpers.CreateBenchmarkLogger();

		// Only override services that benchmarks specifically need to differ from production.
		builder.ConfigureTestServices(sc =>
		{
			// A plain object, not an NSubstitute proxy: the parser reads Configuration.CurrentValue on
			// every function call, and the proxy's interception showed up at 5% of a benchmark's
			// allocations - cost that production never pays.
			var options = new FixedOptions(ReadPennMushConfig.Create(configFile));

			sc.RemoveAll<IOptionsWrapper<SharpMUSHOptions>>();
			sc.AddSingleton<IOptionsWrapper<SharpMUSHOptions>>(options);
		});
	}

	private sealed class FixedOptions(SharpMUSHOptions value) : IOptionsWrapper<SharpMUSHOptions>
	{
		public SharpMUSHOptions CurrentValue => value;
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing)
		{
			Environment.SetEnvironmentVariable("SHARPMUSH_DATABASE_PROVIDER", null);
			Environment.SetEnvironmentVariable("SHARPMUSH_SURREALDB_ENDPOINT", null);
			Environment.SetEnvironmentVariable("SHARPMUSH_LIGHTNING_PATH", null);
		}

		base.Dispose(disposing);
	}
}
