using Core.Arango;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Serilog;
using Serilog.Sinks.SystemConsole.Themes;
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
		ArangoConfiguration? acnf,
		string configFile,
		DatabaseProvider databaseProvider = DatabaseProvider.ArangoDB,
		string? memgraphUri = null,
		string? surrealEndpoint = null) :
	WebApplicationFactory<TProgram> where TProgram : class
{
	protected override void ConfigureWebHost(IWebHostBuilder builder)
	{
		// Wire database configuration via environment variables that Server.Program's
		// ArangoStartupStrategyProvider / Startup constructor will pick up.
		// This is the same approach the test suite uses (ServerWebAppFactory) and avoids
		// calling startup.ConfigureServices() a second time, which would double-register
		// the "compiled-expressions" FusionCache and throw at first resolution.
		if (databaseProvider == DatabaseProvider.Memgraph)
		{
			Environment.SetEnvironmentVariable("SHARPMUSH_DATABASE_PROVIDER", "memgraph");
			if (!string.IsNullOrEmpty(memgraphUri))
				Environment.SetEnvironmentVariable("MEMGRAPH_URI", memgraphUri);
		}
		else if (databaseProvider == DatabaseProvider.SurrealDB)
		{
			// No Testcontainer: SurrealDB runs embedded in-process. mem:// keeps benchmark runs
			// isolated and disk-free, matching the test suite's default.
			Environment.SetEnvironmentVariable("SHARPMUSH_DATABASE_PROVIDER", "surrealdb");
			Environment.SetEnvironmentVariable("SHARPMUSH_SURREALDB_ENDPOINT", surrealEndpoint ?? "mem://");
		}
		else
		{
			if (acnf is null)
				throw new ArgumentNullException(nameof(acnf), "Arango configuration is required for ArangoDB benchmarks.");

			// Pass the test-container connection string through the env var that
			// ArangoKubernetesStartupStrategy reads.
			Environment.SetEnvironmentVariable("ARANGO_CONNECTION_STRING", acnf.ConnectionString);
		}

		var log = new LoggerConfiguration()
			.Enrich.FromLogContext()
			.WriteTo.Console(theme: AnsiConsoleTheme.Code)
			.MinimumLevel.Verbose()
			.CreateLogger();

		Log.Logger = log;

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
			Environment.SetEnvironmentVariable("ARANGO_CONNECTION_STRING", null);
			Environment.SetEnvironmentVariable("SHARPMUSH_DATABASE_PROVIDER", null);
			Environment.SetEnvironmentVariable("MEMGRAPH_URI", null);
			Environment.SetEnvironmentVariable("SHARPMUSH_SURREALDB_ENDPOINT", null);
		}

		base.Dispose(disposing);
	}
}
