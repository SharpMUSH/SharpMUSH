using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Services;

public class CommandDiscoveryServiceTests
{
	[ClassDataSource<TestClassFactory>(Shared = SharedType.PerClass)]
	public required TestClassFactory Factory { get; init; }

	private ICommandDiscoveryService CommandDiscoveryService => 
		Factory.Services.GetRequiredService<ICommandDiscoveryService>();

	[Test]
	public async ValueTask CommandDiscoveryServiceIsRegistered()
	{
		var service = Factory.Services.GetRequiredService<ICommandDiscoveryService>();
		await Assert.That(service).IsNotNull();
	}
}
