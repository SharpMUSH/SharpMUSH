using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Services;

public class CommandDiscoveryServiceTests
{
	[ClassDataSource<WebAppFactory>(Shared = SharedType.PerTestSession)]
	public required WebAppFactory WebAppFactoryArg { get; init; }

	private ICommandDiscoveryService CommandDiscoveryService => 
		WebAppFactoryArg.Services.GetRequiredService<ICommandDiscoveryService>();

	[Test]
	public async ValueTask CommandDiscoveryServiceIsRegistered()
	{
		// Verify the service is properly registered in DI container
		var service = WebAppFactoryArg.Services.GetRequiredService<ICommandDiscoveryService>();
		await Assert.That(service).IsNotNull();
	}
}
