using Microsoft.Extensions.Localization;
using SharpMUSH.Client.Resources;

namespace SharpMUSH.Client.Components.Kit;

/// <summary>
/// "2h ago"-style wording for the boards' meta lines ("Wren · 2h ago", "edited 3 days ago"):
/// just now under a minute, then minutes, hours, and days. Older than a month falls back to the
/// short date, which reads better than "47 days ago".
/// </summary>
public static class RelativeTime
{
	public static string Format(this IStringLocalizer<SharedResource> loc, DateTimeOffset when, DateTimeOffset? now = null)
	{
		var elapsed = (now ?? DateTimeOffset.UtcNow) - when;
		if (elapsed < TimeSpan.FromMinutes(1)) return loc["NavJustNow"];
		if (elapsed < TimeSpan.FromHours(1)) return loc.Plural("NavMinutesAgo", "count", (int)elapsed.TotalMinutes);
		if (elapsed < TimeSpan.FromDays(1)) return loc.Plural("NavHoursAgo", "count", (int)elapsed.TotalHours);
		if (elapsed < TimeSpan.FromDays(31)) return loc.Plural("NavDaysAgo", "count", (int)elapsed.TotalDays);
		return when.ToLocalTime().ToString("MMM d, yyyy");
	}
}
