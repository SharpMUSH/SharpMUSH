using System.Globalization;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Services.Activity;

/// <summary>An approved character, as the reports see one.</summary>
/// <param name="Dbref">Its dbref number.</param>
/// <param name="Objid">Its objid.</param>
/// <param name="Name">Its name now.</param>
/// <param name="CreatedMs">When it was created (Unix millis); counts kept under its number from before then were another object's.</param>
/// <param name="ApprovedOn">The UTC day the audit log says it was approved, or null when there is no record.</param>
public sealed record ActivityCharacter(long Dbref, string Objid, string Name, long CreatedMs, DateOnly? ApprovedOn)
{
	public int CreatedDay => DayOf(CreatedMs);

	internal static int DayOf(long ms) => DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime).DayNumber;
}

/// <summary>A room, as the reports see one.</summary>
public sealed record ActivityPlace(long Dbref, string Objid, string Name, long CreatedMs)
{
	public int CreatedDay => ActivityCharacter.DayOf(CreatedMs);
}

/// <summary>Everything a report reads, gathered once.</summary>
/// <param name="AsOf">The last day counted.</param>
/// <param name="Approved">Every approved character.</param>
/// <param name="Rooms">Every room.</param>
/// <param name="Window">The kept activity from the earliest day any report needs to <see cref="AsOf"/>.</param>
/// <param name="ScenesRecorded">Whether something counts scenes (the Scene plugin).</param>
public sealed record ActivityData(
	DateOnly AsOf,
	IReadOnlyList<ActivityCharacter> Approved,
	IReadOnlyList<ActivityPlace> Rooms,
	ActivityWindow Window,
	bool ScenesRecorded);

/// <summary>
/// The staff activity reports, worked out from <see cref="ActivityData"/>. Pure: the same data and options
/// give the same report, with no clock and no database.
/// </summary>
/// <remarks>
/// The summary, drifting list, cohorts and hours count approved characters only; the rooms report counts
/// every pose made in a room. A count kept under a dbref from before its object was created belonged to an
/// earlier object with that number and is left out.
/// </remarks>
public static class ActivityReports
{
	/// <summary>How many 30-day periods after approval the cohorts table follows.</summary>
	public const int RetentionPeriods = 6;

	/// <summary>The length of one retention period.</summary>
	public const int PeriodDays = 30;

	/// <summary>
	/// The day a character counts as approved from, when its account was approved on <paramref name="approved"/>:
	/// that day, or the day the character was made (<paramref name="createdMs"/>) when that is later.
	/// </summary>
	public static DateOnly ApprovedFrom(DateOnly approved, long createdMs)
	{
		var created = DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeMilliseconds(createdMs).UtcDateTime);
		return created > approved ? created : approved;
	}

	/// <summary>The first day any report needs for <paramref name="options"/>, ending on <paramref name="asOf"/>.</summary>
	public static int FirstDayNeeded(DateOnly asOf, ActivityOptions options)
	{
		var end = asOf.DayNumber;
		var firstMonth = new DateOnly(asOf.Year, asOf.Month, 1).AddMonths(1 - options.Months).DayNumber;
		return Math.Min(Math.Min(end - 2 * options.Days + 1, end - options.Days - options.Baseline + 1),
			Math.Min(firstMonth, end - options.DeadDays + 1));
	}

	public static ActivitySummary Summary(ActivityData data, ActivityOptions options, TimeZoneInfo zone)
	{
		var end = data.AsOf.DayNumber;
		var window = (From: end - options.Days + 1, To: end);
		var before = (From: end - 2 * options.Days + 1, To: end - options.Days);
		var days = ApprovedDays(data);

		var scenes = data.ScenesRecorded ? SceneCount(data, window) : (int?)null;
		var scenesBefore = data.ScenesRecorded ? SceneCount(data, before) : (int?)null;
		var hours = HourGrid(data, options, zone, days);
		var peak = Peak(hours);

		return new ActivitySummary(
			data.AsOf,
			options.Days,
			data.Approved.Count,
			Posers(days, window),
			Posers(days, before),
			Poses(days, window),
			Poses(days, before),
			scenes,
			scenesBefore,
			data.Approved.Count(c => c.ApprovedOn?.DayNumber is { } d && d >= window.From && d <= window.To),
			UsedRooms(data, window).Count,
			DeadRooms(data, options).Count,
			DriftingRows(data, options, days).Count,
			peak,
			zone.Id);
	}

	public static ActivityDrifting Drifting(ActivityData data, ActivityOptions options)
	{
		var rows = DriftingRows(data, options, ApprovedDays(data));
		return new ActivityDrifting(data.AsOf, options.Days, options.Baseline, options.MinimumDays, rows.Count,
			rows.Take(options.Limit).ToList());
	}

	public static ActivityCohorts Cohorts(ActivityData data, ActivityOptions options)
	{
		var end = data.AsOf.DayNumber;
		var window = (From: end - options.Days + 1, To: end);
		var days = ApprovedDays(data);
		var firstMonth = new DateOnly(data.AsOf.Year, data.AsOf.Month, 1).AddMonths(1 - options.Months);

		var cohorts = new List<ActivityCohort>();
		for (var month = new DateOnly(data.AsOf.Year, data.AsOf.Month, 1); month >= firstMonth; month = month.AddMonths(-1))
		{
			var monthEnd = month.AddMonths(1).AddDays(-1).DayNumber;
			var members = data.Approved
				.Where(c => CohortDay(c) is var d && d >= month.DayNumber && d <= monthEnd)
				.ToList();
			if (members.Count == 0)
			{
				cohorts.Add(new ActivityCohort(Month(month), 0, 0, Enumerable.Repeat<double?>(null, RetentionPeriods).ToList()));
				continue;
			}

			var active = members.Count(c => PosedBetween(days, c.Dbref, window.From, window.To));
			var retained = new List<double?>();
			for (var period = 1; period <= RetentionPeriods; period++)
			{
				// A period counts once it has ended for the cohort's last member too.
				if (monthEnd + PeriodDays * period - 1 > end)
				{
					retained.Add(null);
					continue;
				}

				var kept = members.Count(c =>
				{
					var start = CohortDay(c) + PeriodDays * (period - 1);
					return PosedBetween(days, c.Dbref, start, start + PeriodDays - 1);
				});
				retained.Add((double)kept / members.Count);
			}

			cohorts.Add(new ActivityCohort(Month(month), members.Count, active, retained));
		}

		var undated = data.Approved.Count(c => c.ApprovedOn is null);
		return new ActivityCohorts(data.AsOf, options.Days, undated, cohorts);
	}

	public static ActivityRooms Rooms(ActivityData data, ActivityOptions options)
	{
		var end = data.AsOf.DayNumber;
		var used = UsedRooms(data, (end - options.Days + 1, end));
		var dead = DeadRooms(data, options);
		return new ActivityRooms(data.AsOf, options.Days, options.DeadDays, used.Count, dead.Count,
			used.Take(options.Limit).ToList(), dead.Take(options.Limit).ToList());
	}

	public static ActivityHours Hours(ActivityData data, ActivityOptions options, TimeZoneInfo zone)
		=> new(data.AsOf, options.Days, zone.Id, HourGrid(data, options, zone, ApprovedDays(data))
			.Select(IReadOnlyList<double> (row) => row).ToList());

	/// <summary>The approved characters' kept days, by dbref, leaving out days from before the character existed.</summary>
	private static Dictionary<long, List<ActivityPlayerDay>> ApprovedDays(ActivityData data)
	{
		var approved = data.Approved.ToDictionary(c => c.Dbref);
		var days = new Dictionary<long, List<ActivityPlayerDay>>();
		foreach (var day in data.Window.Players)
		{
			if (!approved.TryGetValue(day.Player, out var character) || day.Day < character.CreatedDay) continue;
			if (!days.TryGetValue(day.Player, out var list))
			{
				list = [];
				days[day.Player] = list;
			}

			list.Add(day);
		}

		return days;
	}

	private static int Posers(Dictionary<long, List<ActivityPlayerDay>> days, (int From, int To) range)
		=> days.Count(kv => kv.Value.Any(d => d.Day >= range.From && d.Day <= range.To));

	private static long Poses(Dictionary<long, List<ActivityPlayerDay>> days, (int From, int To) range)
		=> days.Values.SelectMany(list => list).Where(d => d.Day >= range.From && d.Day <= range.To).Sum(d => (long)d.Poses);

	private static bool PosedBetween(Dictionary<long, List<ActivityPlayerDay>> days, long dbref, int from, int to)
		=> days.TryGetValue(dbref, out var list) && list.Any(d => d.Day >= from && d.Day <= to);

	private static int SceneCount(ActivityData data, (int From, int To) range)
		=> data.Window.Rooms.Where(r => r.Day >= range.From && r.Day <= range.To).Sum(r => r.Scenes);

	private static int CohortDay(ActivityCharacter character) => character.ApprovedOn?.DayNumber ?? character.CreatedDay;

	private static string Month(DateOnly month) => month.ToString("yyyy-MM", CultureInfo.InvariantCulture);

	private static DateTimeOffset? LastPose(ActivityData data, long dbref, long createdMs)
		=> data.Window.LastPoses.TryGetValue(dbref, out var ms) && ms >= createdMs
			? DateTimeOffset.FromUnixTimeMilliseconds(ms)
			: null;

	private static List<ActivityDrifter> DriftingRows(ActivityData data, ActivityOptions options,
		Dictionary<long, List<ActivityPlayerDay>> days)
	{
		var end = data.AsOf.DayNumber;
		var recentFrom = end - options.Days + 1;
		var baselineFrom = recentFrom - options.Baseline;

		var rows = new List<(ActivityDrifter Row, double Drop)>();
		foreach (var character in data.Approved)
		{
			if (!days.TryGetValue(character.Dbref, out var list)) continue;
			var baseline = list.Where(d => d.Day >= baselineFrom && d.Day < recentFrom).ToList();
			if (baseline.Count < options.MinimumDays) continue;

			var recent = list.Where(d => d.Day >= recentFrom && d.Day <= end).ToList();
			var baselineRate = (double)baseline.Count / options.Baseline;
			var recentRate = (double)recent.Count / options.Days;
			// Drifting: posing on at most half as many days as before.
			if (recentRate > baselineRate / 2) continue;

			rows.Add((new ActivityDrifter(character.Objid, character.Name, LastPose(data, character.Dbref, character.CreatedMs),
				recent.Count, baseline.Count, recent.Sum(d => (long)d.Poses), baseline.Sum(d => (long)d.Poses)),
				baselineRate - recentRate));
		}

		return rows
			.OrderByDescending(r => r.Drop)
			.ThenBy(r => r.Row.LastPose ?? DateTimeOffset.MinValue)
			.ThenBy(r => r.Row.Name, StringComparer.OrdinalIgnoreCase)
			.Select(r => r.Row)
			.ToList();
	}

	private static List<ActivityRoom> UsedRooms(ActivityData data, (int From, int To) range)
	{
		var rooms = data.Rooms.ToDictionary(r => r.Dbref);
		return data.Window.Rooms
			.Where(r => r.Day >= range.From && r.Day <= range.To
				&& rooms.TryGetValue(r.Room, out var room) && r.Day >= room.CreatedDay)
			.GroupBy(r => r.Room)
			.Where(g => g.Sum(r => r.Poses) > 0)
			.Select(g =>
			{
				var room = rooms[g.Key];
				return new ActivityRoom(room.Objid, room.Name,
					g.Sum(r => (long)r.Poses),
					g.Count(r => r.Poses > 0),
					g.SelectMany(r => r.Posers).Distinct().Count(),
					data.ScenesRecorded ? g.Sum(r => r.Scenes) : null,
					LastPose(data, room.Dbref, room.CreatedMs));
			})
			.OrderByDescending(r => r.Poses)
			.ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
			.ToList();
	}

	/// <summary>Rooms older than the dead-room span with no pose in it: never posed in first, then the longest quiet.</summary>
	private static List<ActivityRoom> DeadRooms(ActivityData data, ActivityOptions options)
	{
		var since = data.AsOf.DayNumber - options.DeadDays + 1;
		return data.Rooms
			.Where(room => room.CreatedDay < since)
			.Select(room => (Room: room, Last: LastPose(data, room.Dbref, room.CreatedMs)))
			.Where(r => r.Last is null || DateOnly.FromDateTime(r.Last.Value.UtcDateTime).DayNumber < since)
			.OrderBy(r => r.Last ?? DateTimeOffset.MinValue)
			.ThenBy(r => r.Room.Name, StringComparer.OrdinalIgnoreCase)
			.Select(r => new ActivityRoom(r.Room.Objid, r.Room.Name, 0, 0, 0, data.ScenesRecorded ? 0 : null, r.Last))
			.ToList();
	}

	/// <summary>
	/// [weekday][hour] in <paramref name="zone"/>: approved characters posing in that hour, averaged over how
	/// often the hour came round in the window.
	/// </summary>
	private static double[][] HourGrid(ActivityData data, ActivityOptions options, TimeZoneInfo zone,
		Dictionary<long, List<ActivityPlayerDay>> days)
	{
		var end = data.AsOf.DayNumber;
		var from = end - options.Days + 1;
		var totals = new long[7, 24];
		var occurrences = new int[7, 24];
		var buckets = new (int Weekday, int Hour)[options.Days * 24];

		for (var day = from; day <= end; day++)
		{
			var start = new DateTimeOffset(DateOnly.FromDayNumber(day).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
			for (var hour = 0; hour < 24; hour++)
			{
				var local = TimeZoneInfo.ConvertTime(start.AddHours(hour), zone);
				var bucket = ((int)local.DayOfWeek, local.Hour);
				buckets[(day - from) * 24 + hour] = bucket;
				occurrences[bucket.Item1, bucket.Item2]++;
			}
		}

		foreach (var list in days.Values)
		{
			foreach (var day in list)
			{
				if (day.Day < from || day.Day > end) continue;
				for (var hour = 0; hour < 24; hour++)
				{
					if ((day.Hours & (1 << hour)) == 0) continue;
					var (weekday, local) = buckets[(day.Day - from) * 24 + hour];
					totals[weekday, local]++;
				}
			}
		}

		var grid = new double[7][];
		for (var weekday = 0; weekday < 7; weekday++)
		{
			grid[weekday] = new double[24];
			for (var hour = 0; hour < 24; hour++)
			{
				grid[weekday][hour] = occurrences[weekday, hour] == 0
					? 0
					: Math.Round((double)totals[weekday, hour] / occurrences[weekday, hour], 2);
			}
		}

		return grid;
	}

	private static ActivityPeak? Peak(double[][] grid)
	{
		ActivityPeak? best = null;
		for (var weekday = 0; weekday < 7; weekday++)
		{
			for (var hour = 0; hour < 24; hour++)
			{
				if (grid[weekday][hour] > (best?.Players ?? 0)) best = new ActivityPeak(weekday, hour, grid[weekday][hour]);
			}
		}

		return best;
	}
}
