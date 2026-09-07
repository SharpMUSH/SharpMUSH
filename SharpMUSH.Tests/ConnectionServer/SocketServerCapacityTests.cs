using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace SharpMUSH.Tests.ConnectionServer;

public class SocketServerCapacityTests
{
	[ClassDataSource<NatsTestServer>(Shared = SharedType.PerClass)]
	public required NatsTestServer Broker { get; init; }

	[Test]
	public async Task UpgradedConnectionLimitUsesConfiguration()
	{
		await using var app = await SharpMUSH.ConnectionServer.Program.CreateHostBuilderAsync(
			["--ConnectionServer:MaxConcurrentUpgradedConnections=123"],
			$"nats://localhost:{Broker.Instance.GetMappedPublicPort(4222)}");
		var options = app.Services.GetRequiredService<IOptions<KestrelServerOptions>>().Value;
		await Assert.That(options.Limits.MaxConcurrentUpgradedConnections).IsEqualTo(123L);
	}

	[Test]
	[Arguments("0")]
	[Arguments("-1")]
	public async Task NonPositiveLimitIsRejected(string limit)
	{
		await Assert.That(async () => await SharpMUSH.ConnectionServer.Program.CreateHostBuilderAsync(
			[$"--ConnectionServer:MaxConcurrentUpgradedConnections={limit}"]))
			.Throws<ArgumentOutOfRangeException>();
	}
}
