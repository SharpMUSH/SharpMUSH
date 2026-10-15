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

		await activity.RecordAsync([1, 2], Now - TimeSpan.FromDays(3));
		await activity.RecordAsync([2, 3], Now - TimeSpan.FromHours(2));
		await activity.RecordAsync([3], Now);

		await Assert.That(await activity.CountAsync(Now)).IsEqualTo((2, 3));
	}

	[Test]
	public async Task APlayerUnseenForAWeekIsDropped()
	{
		var (activity, _) = Create();

		await activity.RecordAsync([1], Now - TimeSpan.FromDays(8));
		await activity.RecordAsync([2], Now);

		await Assert.That(await activity.CountAsync(Now)).IsEqualTo((1, 1));
	}

	[Test]
	public async Task TheWeekSurvivesARestart()
	{
		var stored = new PlayerActivityData
		{
			LastSeen = new Dictionary<string, long>
			{
				["7"] = (Now - TimeSpan.FromDays(2)).ToUnixTimeSeconds(),
				["8"] = (Now - TimeSpan.FromHours(1)).ToUnixTimeSeconds(),
				["not a dbref"] = Now.ToUnixTimeSeconds()
			}
		};
		var (activity, _) = Create(stored);

		await Assert.That(await activity.CountAsync(Now)).IsEqualTo((1, 2));
	}

	[Test]
	public async Task SamplingWithinTheSameHourWritesOnce()
	{
		var (activity, data) = Create();

		await activity.RecordAsync([1], Now);
		await activity.RecordAsync([1], Now + TimeSpan.FromMinutes(5));
		await activity.RecordAsync([1], Now + TimeSpan.FromMinutes(10));

		await data.Received(1).SetExpandedServerDataAsync(Arg.Any<PlayerActivityData>(), Arg.Any<bool>());
	}
}
