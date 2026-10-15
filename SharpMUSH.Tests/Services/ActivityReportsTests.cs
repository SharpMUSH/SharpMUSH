using System.Diagnostics;
using Mediator;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using ActivityFunctions = SharpMUSH.Implementation.Functions.Functions;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Activity;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// The activity reports worked out from fixed data, the ledger that counts poses, and activity()'s options.
/// Every date is named; nothing reads the real clock.
/// </summary>
public class ActivityReportsTests
{
	private static readonly DateOnly AsOf = new(2026, 10, 15);
	private static readonly TimeZoneInfo NewYork = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");

	private static long Ms(DateOnly day, int hour = 12)
		=> new DateTimeOffset(day.ToDateTime(new TimeOnly(hour, 0)), TimeSpan.Zero).ToUnixTimeMilliseconds();

	private static IEnumerable<DateOnly> Days(DateOnly from, DateOnly to, int step = 1)
	{
		for (var day = from; day <= to; day = day.AddDays(step)) yield return day;
	}

	/// <summary>
	/// Ann posed every day from her approval to 15 Sep and once on 1 Oct, at 20:00 UTC, in the Hall. Bo posed
	/// every other day from his approval, at 01:00 UTC, in the Hall. Cy, approved with no date on record, posed
	/// once. The Attic has never had a pose; the Annex is too new to be dead.
	/// </summary>
	private static ActivityData Data(bool scenes = true)
	{
		const long ann = 10, bo = 11, cy = 12, hall = 20, attic = 21, annex = 22;
		var approved = new List<ActivityCharacter>
		{
			new(ann, "#10:1", "Ann", Ms(new DateOnly(2026, 1, 1)), new DateOnly(2026, 1, 5)),
			new(bo, "#11:1", "Bo", Ms(new DateOnly(2026, 7, 1)), new DateOnly(2026, 7, 10)),
			new(cy, "#12:1", "Cy", Ms(new DateOnly(2026, 9, 20)), null)
		};
		var rooms = new List<ActivityPlace>
		{
			new(hall, "#20:1", "Hall", Ms(new DateOnly(2025, 1, 1))),
			new(attic, "#21:1", "Attic", Ms(new DateOnly(2025, 1, 1))),
			new(annex, "#22:1", "Annex", Ms(new DateOnly(2026, 10, 10)))
		};

		var annDays = Days(new DateOnly(2026, 1, 5), new DateOnly(2026, 9, 15)).Append(new DateOnly(2026, 10, 1)).ToList();
		var boDays = Days(new DateOnly(2026, 7, 10), AsOf, 2).ToList();
		var cyDays = new List<DateOnly> { new(2026, 10, 14) };

		var players = annDays.Select(d => new ActivityPlayerDay(d.DayNumber, ann, 5, 1 << 20))
			.Concat(boDays.Select(d => new ActivityPlayerDay(d.DayNumber, bo, 3, 1 << 1)))
			.Concat(cyDays.Select(d => new ActivityPlayerDay(d.DayNumber, cy, 1, 1 << 12)))
			.ToList();
		var roomDays = annDays.Select(d => (Day: d, Who: ann)).Concat(boDays.Select(d => (Day: d, Who: bo)))
			.Concat(cyDays.Select(d => (Day: d, Who: cy)))
			.GroupBy(x => x.Day)
			.Select(g => new ActivityRoomDay(g.Key.DayNumber, hall, g.Count() * 3, scenes ? 1 : 0, g.Select(x => x.Who).ToList()))
			.ToList();
		var last = new Dictionary<long, long>
		{
			[ann] = Ms(annDays[^1], 20), [bo] = Ms(boDays[^1], 1), [cy] = Ms(cyDays[^1]), [hall] = Ms(boDays[^1], 1)
		};

		return new ActivityData(AsOf, approved, rooms, new ActivityWindow(players, roomDays, last), scenes);
	}

	private static ActivityOptions Options => new(Until: AsOf);

	[Test]
	public async Task SummaryComparesTheWindowWithTheOneBefore()
	{
		var summary = ActivityReports.Summary(Data(), Options, TimeZoneInfo.Utc);

		await Assert.That(summary.AsOf).IsEqualTo(AsOf);
		await Assert.That(summary.Approved).IsEqualTo(3);
		// 16 Sep - 15 Oct: Ann once, Bo, Cy. 17 Aug - 15 Sep: Ann and Bo.
		await Assert.That(summary.Posed).IsEqualTo(3);
		await Assert.That(summary.PosedBefore).IsEqualTo(2);
		await Assert.That(summary.Poses).IsEqualTo(5 + 15 * 3 + 1);
		// Cy's approval has no date on record, so it is not new in the window.
		await Assert.That(summary.NewApprovals).IsEqualTo(0);
		await Assert.That(summary.RoomsUsed).IsEqualTo(1);
		await Assert.That(summary.RoomsDead).IsEqualTo(1);
		await Assert.That(summary.Drifting).IsEqualTo(1);
		await Assert.That(summary.Scenes).IsNotNull();
	}

	[Test]
	public async Task WithoutScenesTheSceneNumbersAreNull()
	{
		var summary = ActivityReports.Summary(Data(scenes: false), Options, TimeZoneInfo.Utc);
		var rooms = ActivityReports.Rooms(Data(scenes: false), Options);

		await Assert.That(summary.Scenes).IsNull();
		await Assert.That(summary.ScenesBefore).IsNull();
		await Assert.That(rooms.Used.Single().Scenes).IsNull();
	}

	[Test]
	public async Task ARegularWhoSlowedDownIsDrifting()
	{
		var drifting = ActivityReports.Drifting(Data(), Options);

		var ann = drifting.Players.Single();
		await Assert.That(ann.Name).IsEqualTo("Ann");
		await Assert.That(ann.RecentDays).IsEqualTo(1);
		await Assert.That(ann.BaselineDays).IsEqualTo(60);
		await Assert.That(ann.LastPose).IsEqualTo(DateTimeOffset.FromUnixTimeMilliseconds(Ms(new DateOnly(2026, 10, 1), 20)));
	}

	[Test]
	public async Task CohortsFollowTheApprovalMonthAndLeaveUnfinishedPeriodsOpen()
	{
		var cohorts = ActivityReports.Cohorts(Data(), Options);

		await Assert.That(cohorts.Undated).IsEqualTo(1);
		var january = cohorts.Cohorts.Single(c => c.Month == "2026-01");
		await Assert.That(january.Approved).IsEqualTo(1);
		await Assert.That(january.Retained).IsEquivalentTo(new double?[] { 1, 1, 1, 1, 1, 1 });
		var july = cohorts.Cohorts.Single(c => c.Month == "2026-07");
		await Assert.That(july.Retained).IsEquivalentTo(new double?[] { 1, 1, null, null, null, null });
		// Cy has no approval on record, so counts in the month he was created.
		await Assert.That(cohorts.Cohorts.Single(c => c.Month == "2026-09").Approved).IsEqualTo(1);
		await Assert.That(cohorts.Cohorts[0].Month).IsEqualTo("2026-10");
	}

	[Test]
	public async Task RoomsSplitIntoUsedAndDead()
	{
		var rooms = ActivityReports.Rooms(Data(), Options);

		await Assert.That(rooms.Used.Select(r => r.Name)).IsEquivalentTo(new[] { "Hall" });
		await Assert.That(rooms.Used[0].People).IsEqualTo(3);
		await Assert.That(rooms.Dead.Select(r => r.Name)).IsEquivalentTo(new[] { "Attic" });
		await Assert.That(rooms.Dead[0].LastPose).IsNull();
	}

	[Test]
	public async Task HoursAreShownInTheZoneAsked()
	{
		var utc = ActivityReports.Hours(Data(), Options, TimeZoneInfo.Utc);
		var newYork = ActivityReports.Hours(Data(), Options, NewYork);

		// Ann's one pose in the window was on Thursday 1 Oct at 20:00 UTC: 16:00 in New York (UTC-4).
		await Assert.That(utc.Players[4][20]).IsGreaterThan(0);
		await Assert.That(newYork.Players[4][16]).IsGreaterThan(0);
		await Assert.That(newYork.Players[4][20]).IsEqualTo(0);
		await Assert.That(newYork.Zone).IsEqualTo("America/New_York");
	}

	[Test]
	public async Task BadOptionsAreRefusedWithAReason()
	{
		await Assert.That(ActivityReportService.Check(new ActivityOptions(Days: 0)).Value).IsTypeOf<Error<string>>();
		await Assert.That(ActivityReportService.Check(new ActivityOptions(Baseline: 10, MinimumDays: 11)).Value).IsTypeOf<Error<string>>();
		await Assert.That(ActivityReportService.Check(new ActivityOptions(Zone: "Nowhere/Land")).Value).IsTypeOf<Error<string>>();
		await Assert.That(ActivityReportService.Check(new ActivityOptions(Zone: "Europe/London")).Expect<TimeZoneInfo>().Id)
			.IsEqualTo("Europe/London");
	}

	[Test]
	public async Task ActivityOptionsAreReadFromJson()
	{
		var read = ActivityFunctions.ReadActivityOptions("""{"days":7,"until":"2026-10-01","zone":"UTC","limit":5}""")
			.Expect<ActivityOptions>();
		await Assert.That(read.Days).IsEqualTo(7);
		await Assert.That(read.Until).IsEqualTo(new DateOnly(2026, 10, 1));
		await Assert.That(read.Limit).IsEqualTo(5);

		await Assert.That(ActivityFunctions.ReadActivityOptions("").Expect<ActivityOptions>()).IsEqualTo(new ActivityOptions());
		await Assert.That(ActivityFunctions.ReadActivityOptions("""{"weeks":2}""").Expect<Error<string>>().Value).IsEqualTo("UNKNOWN OPTION WEEKS");
		await Assert.That(ActivityFunctions.ReadActivityOptions("""{"days":"7"}""").Expect<Error<string>>().Value).IsEqualTo("DAYS MUST BE A WHOLE NUMBER");
		await Assert.That(ActivityFunctions.ReadActivityOptions("""{"until":"15/10/2026"}""").Value).IsTypeOf<Error<string>>();
		await Assert.That(ActivityFunctions.ReadActivityOptions("[1]").Value).IsTypeOf<Error<string>>();
	}

	[Test]
	public async Task TheLedgerCountsOncePerPoseAndFlushesOneBatch()
	{
		var mediator = Substitute.For<IMediator>();
		var sent = new List<MergeActivityCommand>();
		mediator.When(m => m.Send(Arg.Any<MergeActivityCommand>(), Arg.Any<CancellationToken>()))
			.Do(call => sent.Add(call.Arg<MergeActivityCommand>()));
		var ledger = new ActivityLedger(new Lazy<IMediator>(mediator), TimeProvider.System, NullLogger<ActivityLedger>.Instance);
		var at = new DateTimeOffset(2026, 10, 15, 21, 0, 0, TimeSpan.Zero);

		// A pose, and the scene's copy of it made while the command runs, count once; the next pose counts again.
		ledger.RecordPose(7, 3, at);
		using (ledger.PoseScope())
		{
			ledger.RecordPose(7, 3, at.AddMilliseconds(500));
		}

		ledger.RecordPose(7, 3, at.AddSeconds(1));
		ledger.RecordPose(8, null, at);
		ledger.RecordScene(3, at);
		await ledger.FlushAsync();
		await ledger.FlushAsync();

		var batch = sent.Single().Batch;
		await Assert.That(batch.Players.Single(p => p.Player == 7).Poses).IsEqualTo(2);
		await Assert.That(batch.Players.Single(p => p.Player == 7).Hours).IsEqualTo(1 << 21);
		await Assert.That(batch.Players.Single(p => p.Player == 8).Poses).IsEqualTo(1);
		var room = batch.Rooms.Single();
		await Assert.That(room.Room).IsEqualTo(3);
		await Assert.That(room.Poses).IsEqualTo(2);
		await Assert.That(room.Scenes).IsEqualTo(1);
		await Assert.That(batch.LastPoses[7]).IsEqualTo(at.AddSeconds(1).ToUnixTimeMilliseconds());
	}

	[Test]
	public async Task AFailedFlushKeepsTheCounts()
	{
		var mediator = Substitute.For<IMediator>();
		var sent = new List<MergeActivityCommand>();
		var fail = true;
		mediator.When(m => m.Send(Arg.Any<MergeActivityCommand>(), Arg.Any<CancellationToken>()))
			.Do(call =>
			{
				if (fail) throw new IOException("disk full");
				sent.Add(call.Arg<MergeActivityCommand>());
			});
		var ledger = new ActivityLedger(new Lazy<IMediator>(mediator), TimeProvider.System, NullLogger<ActivityLedger>.Instance);
		var at = new DateTimeOffset(2026, 10, 15, 21, 0, 0, TimeSpan.Zero);

		ledger.RecordPose(7, 3, at);
		await ledger.FlushAsync();
		fail = false;
		ledger.RecordPose(7, 3, at.AddMinutes(5));
		await ledger.FlushAsync();

		await Assert.That(sent.Single().Batch.Players.Single().Poses).IsEqualTo(2);
	}

	/// <summary>
	/// A large game: 3,000 approved characters and 400 rooms over a year of days, about half a million day
	/// records. Every report must come back well inside a second or two.
	/// </summary>
	[Test]
	public async Task ReportsStayQuickOnALargeGame()
	{
		var random = new Random(15);
		var created = Ms(AsOf.AddDays(-400));
		var approved = Enumerable.Range(1000, 3000)
			.Select(i => new ActivityCharacter(i, $"#{i}:1", $"P{i}", created, AsOf.AddDays(-random.Next(1, 365))))
			.ToList();
		var rooms = Enumerable.Range(10, 400).Select(i => new ActivityPlace(i, $"#{i}:1", $"R{i}", created)).ToList();
		var players = new List<ActivityPlayerDay>();
		var roomDays = new Dictionary<(int, long), ActivityRoomDay>();
		var last = new Dictionary<long, long>();
		for (var back = 365; back >= 0; back--)
		{
			var day = AsOf.AddDays(-back).DayNumber;
			foreach (var character in approved)
			{
				if (random.Next(0, 3) != 0) continue;
				players.Add(new ActivityPlayerDay(day, character.Dbref, random.Next(1, 40), 1 << random.Next(0, 24)));
				var room = 10 + random.Next(0, 200);
				roomDays[(day, room)] = new ActivityRoomDay(day, room, 10, 0, [character.Dbref]);
				last[character.Dbref] = Ms(AsOf.AddDays(-back));
				last[room] = Ms(AsOf.AddDays(-back));
			}
		}

		var data = new ActivityData(AsOf, approved, rooms, new ActivityWindow(players, roomDays.Values.ToList(), last), true);
		var options = new ActivityOptions(Until: AsOf, Days: 90, Months: 12);

		var watch = Stopwatch.StartNew();
		var summary = ActivityReports.Summary(data, options, NewYork);
		ActivityReports.Drifting(data, options);
		var cohorts = ActivityReports.Cohorts(data, options);
		var report = ActivityReports.Rooms(data, options);
		ActivityReports.Hours(data, options, NewYork);
		watch.Stop();

		await Assert.That(players.Count).IsGreaterThan(300_000);
		await Assert.That(summary.Approved).IsEqualTo(3000);
		await Assert.That(cohorts.Cohorts.Count).IsEqualTo(12);
		await Assert.That(report.DeadTotal).IsEqualTo(200);
		await Assert.That(watch.Elapsed).IsLessThan(TimeSpan.FromSeconds(5))
			.Because($"five reports over {players.Count} day records took {watch.Elapsed}");
	}
}
