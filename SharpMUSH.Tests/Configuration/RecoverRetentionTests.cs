using SharpMUSH.Configuration;

namespace SharpMUSH.Tests.Configuration;

/// <summary>recover_keep_players and recover_keep_objects: forever, or a time of at least a day.</summary>
public class RecoverRetentionTests
{
	[Test]
	[Arguments("forever", null)]
	[Arguments("FOREVER", null)]
	[Arguments("30d", 30 * 86400)]
	[Arguments("1d12h", 36 * 3600)]
	[Arguments("24h", 86400)]
	public async Task ReadsForeverOrATimeOfAtLeastADay(string setting, int? seconds)
	{
		await Assert.That(RecoverRetention.TryParse(setting, out var keep)).IsTrue();
		await Assert.That(keep).IsEqualTo(seconds is { } s ? TimeSpan.FromSeconds(s) : null);
	}

	[Test]
	[Arguments("0")]
	[Arguments("0d")]
	[Arguments("10m")]
	[Arguments("23h")]
	[Arguments("")]
	[Arguments("30 days")]
	[Arguments("99999999d")]
	public async Task RefusesAnythingThatCouldEmptyTheList(string setting)
		=> await Assert.That(RecoverRetention.TryParse(setting, out _)).IsFalse();
}
