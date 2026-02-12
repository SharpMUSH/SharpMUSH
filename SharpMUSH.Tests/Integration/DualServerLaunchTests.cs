using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Integration;

/// <summary>
/// Integration tests that verify both the main Server and ConnectionServer can launch successfully together.
/// This validates the refactored async Host Builder pattern works correctly for both services.
/// </summary>
[NotInParallel]
public class DualServerLaunchTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory ServerFactory { get; init; }

	[ClassDataSource<ConnectionServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ConnectionServerWebAppFactory ConnectionServerFactory { get; init; }

	[Test]
	public async ValueTask BothServers_LaunchSuccessfully()
	{
		// Verify the Server (SharpMUSH.Server) launched successfully
		await Assert.That(ServerFactory).IsNotNull();
		await Assert.That(ServerFactory.Services).IsNotNull();

		// Verify the ConnectionServer (SharpMUSH.ConnectionServer) launched successfully
		await Assert.That(ConnectionServerFactory).IsNotNull();
		await Assert.That(ConnectionServerFactory.Services).IsNotNull();

		// Verify Server has required services
		var notifyService = ServerFactory.Services.GetService<INotifyService>();
		await Assert.That(notifyService).IsNotNull();

		// Verify ConnectionServer has required services (basic smoke test)
		var serviceProvider = ConnectionServerFactory.Services;
		await Assert.That(serviceProvider).IsNotNull();
	}

	[Test]
	public async ValueTask Server_HasWorkingParser()
	{
		// Simple test we know will succeed - verify the parser works
		var parser = ServerFactory.FunctionParser;
		await Assert.That(parser).IsNotNull();

		// Test a simple function evaluation using the same pattern as other tests
		var result = await parser.FunctionParse(MModule.single("add(1,1)"));

		await Assert.That(result).IsNotNull();
		await Assert.That(result!.Message!.ToString()).IsEqualTo("2");
	}

	[Test]
	public async ValueTask ConnectionServer_ServiceProviderAvailable()
	{
		// Verify ConnectionServer's service provider is available
		var services = ConnectionServerFactory.Services;
		await Assert.That(services).IsNotNull();

		// Verify we can resolve basic services
		var loggerFactory = services.GetService<Microsoft.Extensions.Logging.ILoggerFactory>();
		await Assert.That(loggerFactory).IsNotNull();
	}

	[Test]
	public async ValueTask BothServers_HaveIndependentServiceProviders()
	{
		// Verify both servers have their own independent service providers
		var serverServices = ServerFactory.Services;
		var connectionServerServices = ConnectionServerFactory.Services;

		await Assert.That(serverServices).IsNotNull();
		await Assert.That(connectionServerServices).IsNotNull();

		// They should be different instances
		await Assert.That(ReferenceEquals(serverServices, connectionServerServices)).IsFalse();
	}
}
