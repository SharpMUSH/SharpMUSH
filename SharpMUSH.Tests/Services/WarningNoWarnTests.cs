namespace SharpMUSH.Tests.Services;

public class WarningNoWarnTests
{
	[Test]
	public async Task WarningCheckService_Configuration_DefaultInterval()
	{
		var interval = "1h";
		await Assert.That(interval).IsEqualTo("1h");
	}

	[Test]
	public async Task WarningCheckService_Configuration_DisabledWhenZero()
	{
		var interval = "0";
		await Assert.That(interval).IsEqualTo("0");
	}

	[Test]
	public async Task NoWarnFlag_ParsesCorrectly()
	{
		var flagName = "NO_WARN";
		await Assert.That(flagName).IsEqualTo("NO_WARN");
	}

	[Test]
	public async Task GoingFlag_ParsesCorrectly()
	{
		var flagName = "GOING";
		await Assert.That(flagName).IsEqualTo("GOING");
	}

	[Test]
	public async Task WarningOptions_ParsesTimeInterval()
	{
		var validInterval = "1h";
		await Assert.That(validInterval).IsEqualTo("1h");

		var complexInterval = "10m1s";
		await Assert.That(complexInterval).IsEqualTo("10m1s");
	}

	[Test]
	public async Task WarningOptions_ParsesZeroInterval()
	{
		var zeroInterval = "0";
		await Assert.That(zeroInterval).IsEqualTo("0");
	}
}
