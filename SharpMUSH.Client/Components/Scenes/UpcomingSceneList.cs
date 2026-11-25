using System.Globalization;
using Microsoft.Extensions.Localization;
using SharpMUSH.Client.Components.Kit;
using SharpMUSH.Client.Models;
using SharpMUSH.Client.Resources;

namespace SharpMUSH.Client.Components.Scenes;

/// <summary>
/// Which scheduled scenes the Upcoming Scenes widget lists, and how it says when each one starts.
/// </summary>
public static class UpcomingSceneList
{
	/// <summary>
	/// The first <paramref name="count"/> scenes of <paramref name="schedule"/> (the server's upcoming list, soonest first,
	/// already only the scenes the reader may see) that have a time within <paramref name="days"/> days of
	/// <paramref name="now"/>; <paramref name="days"/> 0 sets no limit. A paused scene whose time has passed is waiting
	/// to be resumed, not coming up, so it is left out; a scene that is late to start stays until the server drops it.
	/// </summary>
	public static IReadOnlyList<SceneSummary> Select(IEnumerable<SceneSummary> schedule, int count, int days, DateTimeOffset now) =>
		[.. schedule
			.Where(scene => scene.ScheduledForUtc is { } due
				&& (days <= 0 || due < now.AddDays(days))
				&& !(scene.IsPaused && due < now))
			.Take(Math.Clamp(count, 1, Models.Widgets.UpcomingScenesConfig.MaxCount))];

	/// <summary>Whether <paramref name="scene"/>'s time has come without it starting.</summary>
	public static bool IsLate(SceneSummary scene, DateTimeOffset now) =>
		!scene.IsPaused && scene.ScheduledForUtc is { } due && due < now;

	/// <summary>"Today · 8:00 PM", "Tomorrow · 8:00 PM", or "Nov 28, 8:00 PM", in the reader's time zone.</summary>
	public static string When(IStringLocalizer<SharedResource> loc, DateTimeOffset due, DateTimeOffset now, CultureInfo? culture = null)
	{
		culture ??= CultureInfo.CurrentCulture;
		var day = DateOnly.FromDateTime(due.ToLocalTime().DateTime);
		var today = DateOnly.FromDateTime(now.ToLocalTime().DateTime);
		var time = due.ToLocalTime().ToString("t", culture);
		return day == today ? $"{loc["RolScheduleToday"]} · {time}"
			: day == today.AddDays(1) ? $"{loc["RolScheduleTomorrow"]} · {time}"
			: DateDisplay.MonthDayTime(due, culture);
	}
}
