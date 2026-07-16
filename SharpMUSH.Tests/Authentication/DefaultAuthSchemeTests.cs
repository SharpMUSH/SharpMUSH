using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NSubstitute;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Server;
using SharpMUSH.Server.Authentication;

namespace SharpMUSH.Tests.Authentication;

/// <summary>
/// Verifies the default authentication scheme <see cref="Startup.ConfigureServices"/> wires up per
/// environment: <see cref="AccountSessionAuthenticationHandler.SchemeName"/> in Production,
/// <see cref="DebugAuthenticationHandler.SchemeName"/> in Development. The integration test host
/// (<c>ServerWebAppFactory</c>) always runs in Development, so without this test the production branch —
/// the single most important fact of the JWT-to-account-session auth consolidation — had no coverage.
/// </summary>
public class DefaultAuthSchemeTests
{
	/// <summary>
	/// Runs the real <see cref="Startup.ConfigureServices"/> against a bare <see cref="ServiceCollection"/>
	/// for <paramref name="environmentName"/> and resolves the registered <see cref="AuthenticationOptions"/>.
	/// This is safe without a live DB/NATS server or a full host boot:
	/// <list type="bullet">
	///   <item>Every DB-backed singleton (<c>ISharpDatabase</c>, <c>IConnectionStateStore</c>,
	///   <c>NatsJetStreamMessageBus</c>, etc.) is registered behind a lazy <c>AddSingleton(sp =&gt; ...)</c>
	///   factory — the migration/connect calls inside them only run when the service is actually resolved,
	///   which this test never does.</item>
	///   <item>Passing <c>arangoConfig: null</c> skips the <c>services.AddArango(...)</c> call entirely (see
	///   the <c>if (databaseProvider == DatabaseProvider.ArangoDB &amp;&amp; arangoConfig is not null)</c>
	///   guard in <c>Startup.ConfigureServices</c>), so no Arango connection is attempted.</item>
	///   <item><c>Implementation.Services.PluginCatalog.Build</c> (run synchronously, not behind a lazy
	///   factory) delegates to <c>PluginLoaderService.LoadAll</c>, which checks
	///   <c>Directory.Exists(pluginsRoot)</c> first and no-ops when there's no <c>plugins/</c> directory
	///   under the test host's <see cref="AppContext.BaseDirectory"/> — no DLL load is attempted.</item>
	///   <item><c>.ValidateOnStart()</c> on <c>SharpMUSHOptions</c>/<c>ColorsOptions</c> registers a hosted
	///   service that validates when the generic host starts; a bare <c>BuildServiceProvider()</c> never
	///   starts hosted services, so no validation (and no dependency on config sections this test doesn't
	///   provide) runs.</item>
	/// </list>
	/// </summary>
	private static AuthenticationOptions BuildAuthenticationOptions(string environmentName)
	{
		var environment = Substitute.For<IHostEnvironment>();
		environment.EnvironmentName.Returns(environmentName);
		environment.ApplicationName.Returns("SharpMUSH.Server");
		environment.ContentRootPath.Returns(AppContext.BaseDirectory);

		var configuration = new ConfigurationBuilder().Build();
		var services = new ServiceCollection();

		var startup = new Startup(
			arangoConfig: null,
			colorFile: "colors.json",
			natsUrl: "nats://localhost:4222",
			databaseProvider: DatabaseProvider.ArangoDB,
			memgraphUri: null);

		startup.ConfigureServices(services, configuration, environment);

		using var provider = services.BuildServiceProvider();
		return provider.GetRequiredService<IOptions<AuthenticationOptions>>().Value;
	}

	[Test]
	public async Task Production_DefaultsToAccountSession()
	{
		var options = BuildAuthenticationOptions(Environments.Production);

		await Assert.That(options.DefaultScheme).IsEqualTo(AccountSessionAuthenticationHandler.SchemeName);
	}

	[Test]
	public async Task Development_DefaultsToDebugAuth()
	{
		var options = BuildAuthenticationOptions(Environments.Development);

		await Assert.That(options.DefaultScheme).IsEqualTo(DebugAuthenticationHandler.SchemeName);
	}
}
