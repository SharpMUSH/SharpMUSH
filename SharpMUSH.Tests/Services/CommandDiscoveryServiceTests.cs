using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Services;

public class CommandDiscoveryServiceTests: TestClassFactory
{
	private ICommandDiscoveryService CommandDiscoveryService => 
		Factory.Services.GetRequiredService<ICommandDiscoveryService>();

	[Test]
	public async ValueTask CommandDiscoveryServiceIsRegistered()
	{
		var service = Factory.Services.GetRequiredService<ICommandDiscoveryService>();
		await Assert.That(service).IsNotNull();
	}
}
