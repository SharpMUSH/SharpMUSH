using Microsoft.Extensions.Configuration;
using SharpMUSH.SocketServer.Configuration;

namespace SharpMUSH.Tests.ConnectionServer;

public class ReplayConfigurationTests
{
	[Test]
	public async Task EveryReplaySettingIsReadFromConfiguration()
	{
		var config = new ConfigurationBuilder().AddInMemoryCollection(
			new Dictionary<string, string?>
			{
				["Replay:RetentionHours"] = "2",
				["Replay:MaxBytes"] = "1048576",
				["Replay:MaxFrames"] = "50",
				["Replay:PageSize"] = "8"
			}).Build();

		await Assert.That(ReplayOptions.FromConfiguration(config)).IsEqualTo(new ReplayOptions
		{
			Retention = TimeSpan.FromHours(2),
			MaxBytes = 1048576,
			MaxFrames = 50,
			PageSize = 8
		});
	}

	[Test]
	public async Task AnEmptyReplayPageFailsDuringConfiguration()
	{
		var config = new ConfigurationBuilder().AddInMemoryCollection(
			new Dictionary<string, string?> { ["Replay:PageSize"] = "0" }).Build();

		await Assert.That(() => ReplayOptions.FromConfiguration(config)).Throws<ArgumentOutOfRangeException>();
	}
}
