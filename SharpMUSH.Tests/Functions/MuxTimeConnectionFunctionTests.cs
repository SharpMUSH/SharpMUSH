using SharpMUSH.Implementation.Services;
using SharpMUSH.Library.Services.DatabaseConversion.TinyMux;

namespace SharpMUSH.Tests.Functions;

/// <summary>TinyMUX's time-writing functions and its connection statistics (CONNINFO).</summary>
public class MuxTimeConnectionFunctionTests : ServerTestBase
{
	[Test]
	[Arguments("exptime(45)", "45s")]
	[Arguments("exptime(12345)", "3h 25m 45s")]
	[Arguments("exptime(123456)", "1d 10h 17m 36s")]
	[Arguments("exptime(3600)", "1h 0s")]
	[Arguments("exptime(0)", "0s")]
	[Arguments("exptime(-5)", "0s")]
	[Arguments("exptime(90x)", "1m 30s")]
	[Arguments("exptime(31556926)", "1y 0s")]
	[Arguments("exptime(2629743)", "1M 0s")]
	[Arguments("exptime(1209600)", "2w 0s")]
	[Arguments("writetime(45)", "45 seconds")]
	[Arguments("writetime(12345)", "3 hours 25 minutes 45 seconds")]
	[Arguments("writetime(123456)", "1 day 10 hours 17 minutes 36 seconds")]
	[Arguments("writetime(61)", "1 minute 1 second")]
	[Arguments("writetime(0)", "0 seconds")]
	[Arguments("singletime(45)", "45s")]
	[Arguments("singletime(12345)", "3h")]
	[Arguments("singletime(123456)", "1d")]
	[Arguments("singletime(0)", "0s")]
	[Arguments("digittime(2345)", "00:39")]
	[Arguments("digittime(123456)", "1d 10:17")]
	[Arguments("digittime(59)", "00:00")]
	[Arguments("digittime(-1)", "00:00")]
	[Arguments("exptime(12345) / [writetime(123456)]", "3h 25m 45s / 1 day 10 hours 17 minutes 36 seconds")]
	[Arguments("[singletime(12345)] / [digittime(123456)] / [exptime(3600)]", "3h / 1d 10:17 / 1h 0s")]
	public async Task TimeWriting(string expression, string expected)
		=> await Assert.That(await Eval(expression)).IsEqualTo(expected);

	[Test]
	public async Task RestartSecsIsTheRestartTimeInSeconds()
	{
		var seconds = long.Parse(await Eval("restartsecs()"));
		await Assert.That(await Eval($"convsecs({seconds})")).IsEqualTo(await Eval("restarttime()"));
	}

	[Test]
	public async Task UnknownPlayer()
	{
		foreach (var function in (string[])["conntotal", "connmax", "connlast", "connnum", "connleft"])
		{
			await Assert.That(await Eval($"{function}(*NoSuchPlayer{Guid.NewGuid():N})")).IsEqualTo("#-1 PLAYER NOT FOUND");
		}
	}

	[Test]
	public async Task StatisticsCountTheOpenSessionAndRecordItsEnd()
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "ConnStats");
		var disconnected = false;
		try
		{
			// Total 1000, longest 600, last 30, 4 sessions, last left at 1700000000.
			await Cmd($"&CONNINFO {player.DbRef}=1000 600 30 4 1700000000");

			await Assert.That(await Eval($"connnum({player.DbRef})")).IsEqualTo("5");
			await Assert.That(await Eval($"connlast({player.DbRef})")).IsEqualTo("30");
			await Assert.That(await Eval($"connleft({player.DbRef})")).IsEqualTo("1700000000");
			await Assert.That(await Eval($"connmax({player.DbRef})")).IsEqualTo("600");
			var total = long.Parse(await Eval($"conntotal({player.DbRef})"));
			await Assert.That(total).IsGreaterThanOrEqualTo(1000).And.IsLessThan(1100);

			var before = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
			await ConnectionService.Disconnect(player.Handle);
			disconnected = true;
			await WebAppFactoryArg.QueueBarrierAsync();

			var record = ConnectionStatistics.Record.Parse(await Eval($"get({player.DbRef}/CONNINFO)"));
			await Assert.That(record.Sessions).IsEqualTo(5);
			await Assert.That(record.LongestSeconds).IsEqualTo(600);
			await Assert.That(record.TotalSeconds).IsGreaterThanOrEqualTo(1000).And.IsLessThan(1100);
			await Assert.That(record.LastSeconds).IsLessThan(100);
			await Assert.That(record.LastLogout).IsGreaterThanOrEqualTo(before);
			await Assert.That(await Eval($"connnum({player.DbRef})")).IsEqualTo("5");
		}
		finally
		{
			if (!disconnected) await ConnectionService.Disconnect(player.Handle);
		}
	}

	/// <summary>A session is measured from its login, not from when the socket opened.</summary>
	[Test]
	public async Task SessionCountsFromTheLogin()
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "ConnStatsLogin");
		var disconnected = false;
		try
		{
			var now = DateTimeOffset.UtcNow;
			ConnectionService.Update(player.Handle, "ConnectionStartTime", now.AddSeconds(-5000).ToUnixTimeMilliseconds().ToString());
			ConnectionService.Update(player.Handle, "LoginTime", now.AddSeconds(-50).ToUnixTimeMilliseconds().ToString());

			// me is the caller, as TinyMUX reads it.
			var live = long.Parse(await EvalAs(player.DbRef, "connmax(me)"));
			await Assert.That(live).IsGreaterThanOrEqualTo(50).And.IsLessThan(100);

			await ConnectionService.Disconnect(player.Handle);
			disconnected = true;
			await WebAppFactoryArg.QueueBarrierAsync();

			var record = ConnectionStatistics.Record.Parse(await Eval($"get({player.DbRef}/CONNINFO)"));
			await Assert.That(record.LastSeconds).IsGreaterThanOrEqualTo(50).And.IsLessThan(100);
			await Assert.That(record.TotalSeconds).IsGreaterThanOrEqualTo(50).And.IsLessThan(100);
			await Assert.That(record.Sessions).IsEqualTo(1);
		}
		finally
		{
			if (!disconnected) await ConnectionService.Disconnect(player.Handle);
		}
	}

	[Test]
	public async Task NoRecordCountsFromZero()
	{
		var player = await TestIsolationHelpers.CreateTestPlayerAsync(WebAppFactoryArg.Services, Mediator, "ConnStatsNone");
		await Assert.That(await Eval($"[connnum({player})] [connlast({player})] [connleft({player})] [conntotal({player})] [connmax({player})]"))
			.IsEqualTo("0 0 0 0 0");
	}

	private static readonly DateTimeOffset T0 = DateTimeOffset.FromUnixTimeSeconds(1_000_000);

	[Test]
	public async Task OneSessionAddsAllOfItself()
	{
		var record = ConnectionStatistics.Ended(new(10, 50, 5, 2, 0), T0, T0.AddSeconds(100), []);
		await Assert.That(record).IsEqualTo(new ConnectionStatistics.Record(110, 100, 100, 3, T0.AddSeconds(100).ToUnixTimeSeconds()));
	}

	/// <summary>
	/// Two sessions overlapping: the older one ending first adds only the time before the newer began; the newer one,
	/// ending later, is then the oldest and adds the rest. The overlap is counted once.
	/// </summary>
	[Test]
	public async Task OverlappingSessionsCountTheirTimeOnce()
	{
		var first = ConnectionStatistics.Ended(default, T0, T0.AddSeconds(100), [T0.AddSeconds(40)]);
		await Assert.That(first.TotalSeconds).IsEqualTo(40);
		await Assert.That(first.LongestSeconds).IsEqualTo(100);

		var second = ConnectionStatistics.Ended(first, T0.AddSeconds(40), T0.AddSeconds(150), []);
		await Assert.That(second.TotalSeconds).IsEqualTo(150);
		await Assert.That(second.LongestSeconds).IsEqualTo(110);
		await Assert.That(second.Sessions).IsEqualTo(2);
	}

	/// <summary>A newer session ending while an older one goes on adds nothing; its length is from the older one's start.</summary>
	[Test]
	public async Task NewerSessionEndingFirstAddsNothing()
	{
		var record = ConnectionStatistics.Ended(default, T0.AddSeconds(40), T0.AddSeconds(60), [T0]);
		await Assert.That(record.TotalSeconds).IsEqualTo(0);
		await Assert.That(record.LongestSeconds).IsEqualTo(0);
		await Assert.That(record.LastSeconds).IsEqualTo(60);
		await Assert.That(record.Sessions).IsEqualTo(1);
	}

	[Test]
	[Arguments("", 0, 0, 0, 0, 0)]
	[Arguments("1 2 3 4 5", 1, 2, 3, 4, 5)]
	[Arguments("1 -2 x", 1, 0, 0, 0, 0)]
	public async Task ParseReadsWhatItCan(string text, long total, long longest, long last, long sessions, long logout)
		=> await Assert.That(ConnectionStatistics.Record.Parse(text))
			.IsEqualTo(new ConnectionStatistics.Record(total, longest, last, sessions, logout));

	/// <summary>A TinyMUX import keeps each player's record under the same name and layout.</summary>
	[Test]
	public async Task TinyMuxImportKeepsConnInfo()
	{
		await Assert.That(TinyMuxNames.Attributes[224]).IsEqualTo(ConnectionStatistics.Attribute);
		await Assert.That(TinyMuxNames.DroppedAttributes.ContainsKey(224)).IsFalse();
	}
}
