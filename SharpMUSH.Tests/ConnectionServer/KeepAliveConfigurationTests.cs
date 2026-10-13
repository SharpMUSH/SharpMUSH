using Microsoft.Extensions.Configuration;
using SharpMUSH.SocketServer.Services;

namespace SharpMUSH.Tests.ConnectionServer;

public class KeepAliveConfigurationTests
{
	private static KeepAliveOptions From(params (string Name, string Value)[] settings) =>
		KeepAliveOptions.FromConfiguration(new ConfigurationBuilder().AddInMemoryCollection(
			settings.ToDictionary(s => $"KeepAlive:{s.Name}", s => (string?)s.Value)).Build());

	[Test]
	[Arguments("WsIntervalSeconds")]
	[Arguments("WsTimeoutSeconds")]
	[Arguments("TcpUserTimeoutSeconds")]
	public async Task InvalidValuesFailDuringConfiguration(string name)
	{
		foreach (var value in new[] { "-1", "NaN", "Infinity", "2147484" })
			await Assert.That(() => From((name, value))).Throws<ArgumentOutOfRangeException>();
	}

	[Test]
	[Arguments("TcpIdleSeconds")]
	[Arguments("TcpProbeIntervalSeconds")]
	[Arguments("TcpProbeCount")]
	public async Task ProbeSettingsMustBePositiveWholeNumbers(string name)
	{
		foreach (var value in new[] { "0", "-1", "32768" })
			await Assert.That(() => From((name, value))).Throws<ArgumentOutOfRangeException>();
	}

	[Test]
	public async Task ZeroRemainsValid()
	{
		var options = From(("WsIntervalSeconds", "0"), ("WsTimeoutSeconds", "0"), ("TcpUserTimeoutSeconds", "0"));

		await Assert.That(options.WsInterval).IsEqualTo(TimeSpan.Zero);
		await Assert.That(options.WsTimeout).IsEqualTo(TimeSpan.Zero);
		await Assert.That(options.TcpUserTimeout).IsEqualTo(TimeSpan.Zero);
	}

	// Linux resets an idle socket once nothing has arrived for TCP_USER_TIMEOUT and one probe is
	// outstanding, ignoring the probe count. At 20s idle with a 20s user timeout that was one lost
	// probe: idle players were dropped at random, an hour or ten apart.
	[Test]
	public async Task DefaultUserTimeoutCoversEveryProbe()
	{
		var options = From();

		await Assert.That(options.TcpProbeCount).IsGreaterThanOrEqualTo(3);
		await Assert.That(options.TcpUserTimeout).IsEqualTo(options.TcpProbeBudget);
		await Assert.That(options.TcpProbeBudget)
			.IsEqualTo(options.TcpIdle + options.TcpProbeInterval * options.TcpProbeCount);
	}

	[Test]
	public async Task UserTimeoutFollowsTheConfiguredProbes()
	{
		var options = From(("TcpIdleSeconds", "40"), ("TcpProbeIntervalSeconds", "5"), ("TcpProbeCount", "4"));

		await Assert.That(options.TcpUserTimeout).IsEqualTo(TimeSpan.FromSeconds(60));
	}

	[Test]
	public async Task UserTimeoutShorterThanTheProbeBudgetIsRefused()
	{
		await Assert.That(() => From(("TcpUserTimeoutSeconds", "20"))).Throws<ArgumentOutOfRangeException>();
		await Assert.That(() => From(("TcpIdleSeconds", "20"), ("TcpProbeIntervalSeconds", "5"), ("TcpProbeCount", "3"), ("TcpUserTimeoutSeconds", "34.9")))
			.Throws<ArgumentOutOfRangeException>();
		await Assert.That(From(("TcpIdleSeconds", "20"), ("TcpProbeIntervalSeconds", "5"), ("TcpProbeCount", "3"), ("TcpUserTimeoutSeconds", "35")).TcpUserTimeout)
			.IsEqualTo(TimeSpan.FromSeconds(35));
	}
}
