using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Services;

public class CommandDiscoveryServiceTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private ICommandDiscoveryService CommandDiscoveryService =>
		WebAppFactoryArg.Services.GetRequiredService<ICommandDiscoveryService>();

	[Test]
	public async ValueTask CommandDiscoveryServiceIsRegistered()
	{
		var service = WebAppFactoryArg.Services.GetRequiredService<ICommandDiscoveryService>();
		await Assert.That(service).IsNotNull();
	}
}
