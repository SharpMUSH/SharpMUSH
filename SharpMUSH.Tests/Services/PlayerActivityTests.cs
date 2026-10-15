using NSubstitute;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// MSSP's PLAYERS DAY and PLAYERS WEEK: distinct players seen over the last 24 hours and 7 days, to the hour, kept in
/// the server data so a restart keeps the week.
/// </summary>
public class PlayerActivityTests
{
	private static readonly DateTimeOffset Now = new(2026, 10, 15, 12, 30, 0, TimeSpan.Zero);

	private static (PlayerActivity Activity, IExpandedObjectDataService Data) Create(PlayerActivityData? stored = null)
	{
		var data = Substitute.For<IExpandedObjectDataService>();
		data.GetExpandedServerDataAsync<PlayerActivityData>().Returns(stored);
		return (new PlayerActivity(data), data);
	}

	[Test]
	public async Task EachPlayerCountsOnceInTheDayAndWeek()
	{
		var (activity, _) = Create();

		await activity.RecordAsync(["#1:10", "#2:20"], Now - TimeSpan.FromDays(3));
		await activity.RecordAsync(["#2:20", "#3:30"], Now - TimeSpan.FromHours(2));
		await activity.RecordAsync(["#3:30"], Now);

		await Assert.That(await activity.CountAsync(Now)).IsEqualTo((2, 3));
	}

	[Test]
	public async Task APlayerUnseenForAWeekIsDropped()
	{
		var (activity, _) = Create();

		await activity.RecordAsync(["#1:10"], Now - TimeSpan.FromDays(8));
		await activity.RecordAsync(["#2:20"], Now);

		await Assert.That(await activity.CountAsync(Now)).IsEqualTo((1, 1));
	}

	[Test]
	public async Task TheWeekSurvivesARestart()
	{
		var stored = new PlayerActivityData
		{
			LastSeen = new Dictionary<string, long>
			{
				["#7:70"] = (Now - TimeSpan.FromDays(2)).ToUnixTimeSeconds(),
				["#8:80"] = (Now - TimeSpan.FromHours(1)).ToUnixTimeSeconds()
			}
		};
		var (activity, _) = Create(stored);

		await Assert.That(await activity.CountAsync(Now)).IsEqualTo((1, 2));
	}

	[Test]
	public async Task SamplingWithinTheSameHourWritesOnce()
	{
		var (activity, data) = Create();

		await activity.RecordAsync(["#1:10"], Now);
		await activity.RecordAsync(["#1:10"], Now + TimeSpan.FromMinutes(5));
		await activity.RecordAsync(["#1:10"], Now + TimeSpan.FromMinutes(10));

		await data.Received(1).SetExpandedServerDataAsync(Arg.Any<PlayerActivityData>(), Arg.Any<bool>());
	}

	/// <summary>A dbref recycled for a new player within the week is a second player, not the first one again.</summary>
	[Test]
	public async Task ARecycledDbrefIsAnotherPlayer()
	{
		var (activity, _) = Create();

		await activity.RecordAsync(["#5:100"], Now - TimeSpan.FromDays(2));
		await activity.RecordAsync(["#5:200"], Now);

		await Assert.That(await activity.CountAsync(Now)).IsEqualTo((1, 2));
	}

	/// <summary>A write that fails is tried again on the next sample, even within the same hour.</summary>
	[Test]
	public async Task AFailedWriteIsRetried()
	{
		var (activity, data) = Create();
		data.SetExpandedServerDataAsync(Arg.Any<PlayerActivityData>(), Arg.Any<bool>())
			.Returns(_ => throw new IOException("disk full"), _ => ValueTask.CompletedTask);

		await Assert.That(async () => await activity.RecordAsync(["#1:10"], Now)).Throws<IOException>();
		await activity.RecordAsync(["#1:10"], Now + TimeSpan.FromMinutes(1));
		await activity.RecordAsync(["#1:10"], Now + TimeSpan.FromMinutes(2));

		await data.Received(2).SetExpandedServerDataAsync(Arg.Any<PlayerActivityData>(), Arg.Any<bool>());
	}
}
