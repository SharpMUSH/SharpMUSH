using Microsoft.Extensions.Configuration;
using SharpMUSH.ConnectionServer.Services;

namespace SharpMUSH.Tests.ConnectionServer;

public class KeepAliveConfigurationTests
{
	[Test]
	[Arguments("WsIntervalSeconds")]
	[Arguments("WsTimeoutSeconds")]
	[Arguments("TcpUserTimeoutSeconds")]
	public async Task InvalidValuesFailDuringConfiguration(string name)
	{
		foreach (var value in new[] { "-1", "NaN", "Infinity", "2147484" })
		{
			var config = new ConfigurationBuilder().AddInMemoryCollection(
				new Dictionary<string, string?> { [$"KeepAlive:{name}"] = value }).Build();
			await Assert.That(() => KeepAliveOptions.FromConfiguration(config)).Throws<ArgumentOutOfRangeException>();
		}
	}

	[Test]
	public async Task ZeroRemainsValid()
	{
		var config = new ConfigurationBuilder().AddInMemoryCollection(
			new Dictionary<string, string?>
			{
				["KeepAlive:WsIntervalSeconds"] = "0",
				["KeepAlive:WsTimeoutSeconds"] = "0",
				["KeepAlive:TcpUserTimeoutSeconds"] = "0"
			}).Build();
		await Assert.That(KeepAliveOptions.FromConfiguration(config))
			.IsEqualTo(new KeepAliveOptions(TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero));
	}
}
