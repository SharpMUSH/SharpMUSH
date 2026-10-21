using System.Globalization;
using System.Text.RegularExpressions;

namespace SharpMUSH.Library.Time;

[Flags]
public enum DateExpressionComponents { None = 0, Date = 1, Clock = 2, Zone = 4, Year = 8, Relative = 16 }
public enum DateExpressionFailureKind { Incomplete, Ambiguous, Unsupported, Invalid }
public sealed record ResolvedDateExpression(DateTimeOffset Instant, string Zone, DateExpressionComponents Explicit, string Normalized);
public sealed record DateExpressionFailure(DateExpressionFailureKind Kind, string Message);
public union DateExpressionResult(ResolvedDateExpression, DateExpressionFailure);

/// <summary>Bounded English calendar grammar. All reference times are supplied by the caller.</summary>
public static class DateExpressionParser
{
	public const int MaximumLength = 512;
	private static readonly CultureInfo English = CultureInfo.InvariantCulture;
	// No wall-clock timeout, which a busy machine trips on valid input: the input is capped at
	// MaximumLength and no pattern nests ambiguous quantifiers, so a match is at worst quadratic in it.
	private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
	private static Match Match(string input, string pattern) => Regex.Match(input, pattern, Options);
	private static DateExpressionFailure Fail(string message, DateExpressionFailureKind kind = DateExpressionFailureKind.Invalid) => new(kind, message);
	private const string Weekday = @"(?:mon(?:day)?|tue(?:sday)?|wed(?:nesday)?|thu(?:rsday)?|fri(?:day)?|sat(?:urday)?|sun(?:day)?)";
	private const string Number = @"(?:[0-9]{1,6}|a|an|one|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve)";
	private const string Unit = @"(?:fortnights?|weeks?|days?|hours?|minutes?|seconds?|[wdhms])";

	public static DateExpressionResult Parse(string expression, string zoneName, DateTimeOffset reference)
	{
		if (expression.Length > MaximumLength) return Fail("EXPRESSION TOO LONG (MAXIMUM 512 CHARACTERS)");
		var text = Regex.Replace(expression.Trim(), @"\s+", " ");
		if (text.Length == 0) return Fail("GIVE A DATE AND TIME", DateExpressionFailureKind.Incomplete);
		try { return ParseCore(text, zoneName, reference); }
		catch (ArgumentOutOfRangeException) { return Fail("DATE OR OFFSET OUT OF RANGE"); }
		catch (OverflowException) { return Fail("DATE OR OFFSET OUT OF RANGE"); }
	}

	private static DateExpressionResult ParseCore(string text, string zoneName, DateTimeOffset reference)
	{
		if (Match(text, @"\b(?:every|daily|weekly|monthly|yearly|recurring)\b").Success)
			return Fail("RECURRENCES ARE NOT SUPPORTED; GIVE ONE DATE AND TIME", DateExpressionFailureKind.Unsupported);
		var startsIsoDate = Match(text, @"^[0-9]{4}-[0-9]{2}-[0-9]{2}(?:[T ]|$)").Success;
		if (!startsIsoDate && (Match(text, @"[0-9]\s*(?:-|to|until)\s*[0-9].*(?:am|pm)\b").Success
			|| Match(text, @"[0-9]{1,2}:[0-9]{2}(?:-|\s+(?:to|until|-)\s+)[0-9]{1,2}:[0-9]{2}").Success))
			return Fail("TIME RANGES ARE NOT SUPPORTED; GIVE ONE TIME", DateExpressionFailureKind.Unsupported);
		if (Match(text, @"\b(?:tonight|evening|morning|afternoon|weekend)\b").Success)
			return Fail("GIVE AN EXACT DATE AND CLOCK TIME", DateExpressionFailureKind.Incomplete);
		if (Match(text, @"\b[0-9]{1,2}/[0-9]{1,2}(?:/[0-9]{2,4})?\b").Success)
			return Fail("NUMERIC DATE ORDER IS AMBIGUOUS; USE YYYY-MM-DD OR A MONTH NAME", DateExpressionFailureKind.Ambiguous);

		var explicitParts = DateExpressionComponents.None;
		var suffix = Match(text, @"^(?<body>.+) (?<zone>(?:[A-Za-z_+-]+/)[A-Za-z0-9_+./-]+|UTC|Z|[+-][0-9]{2}:[0-9]{2})$");
		if (suffix.Success)
		{
			zoneName = suffix.Groups["zone"].Value;
			text = suffix.Groups["body"].Value;
			explicitParts |= DateExpressionComponents.Zone;
		}
		// Only explicit formats: DateTime.TryParse would accept partial/trailing text and host-dependent defaults.
		var offset = Match(text, @"^[0-9]{4}-[0-9]{2}-[0-9]{2}[T ][0-9]{2}:[0-9]{2}(?::[0-9]{2}(?:\.[0-9]{1,7})?)?(?:Z|[+-][0-9]{2}:[0-9]{2})$");
		if (offset.Success && DateTimeOffset.TryParse(text, English, DateTimeStyles.AssumeUniversal, out var instant))
		{
			TimeZoneInfo instantZone;
			if (explicitParts.HasFlag(DateExpressionComponents.Zone))
			{
				if (!TryZone(zoneName, out instantZone)) return Fail("INVALID TIMEZONE; USE AN IANA NAME, UTC OR A NUMERIC OFFSET");
				if (instantZone.GetUtcOffset(instant) != instant.Offset)
					return Fail("WRITTEN OFFSET AND TIMEZONE CONFLICT; GIVE ONE ZONE OR A MATCHING OFFSET", DateExpressionFailureKind.Ambiguous);
			}
			else
			{
				var name = instant.ToString("zzz", English);
				instantZone = TimeZoneInfo.CreateCustomTimeZone(name, instant.Offset, name, name);
			}
			return Resolved(instant, instantZone, explicitParts | DateExpressionComponents.Date | DateExpressionComponents.Clock | DateExpressionComponents.Year | DateExpressionComponents.Zone);
		}
		if (!TryZone(string.IsNullOrWhiteSpace(zoneName) ? "UTC" : zoneName, out var zone))
			return Fail("INVALID TIMEZONE; USE AN IANA NAME, UTC OR A NUMERIC OFFSET");
		if (Match(text, @"(?:^| )(?:CST|EST|PST|MST|CDT|EDT|PDT|MDT|BST|IST)$").Success)
			return Fail("TIMEZONE ABBREVIATION IS AMBIGUOUS; USE AN IANA NAME OR OFFSET", DateExpressionFailureKind.Ambiguous);
		if (TimePrecisions.TryParseInstant(text, out var epoch))
			return Resolved(epoch, zone, explicitParts | DateExpressionComponents.Date | DateExpressionComponents.Clock);

		var localReference = TimeZoneInfo.ConvertTime(reference, zone).DateTime;
		var clockMatch = Match(text, @"^(?:(?<date>.*?)\s*(?:\bat\s+)?)?(?<clock>noon|midnight|[0-9]{1,2}(?::[0-9]{2}(?::[0-9]{2}(?:\.[0-9]{1,3})?)?)?\s*(?:am|pm)|[0-9]{1,2}:[0-9]{2}(?::[0-9]{2}(?:\.[0-9]{1,3})?)?)$");
		TimeSpan? clock = null;
		var dateText = text;
		if (clockMatch.Success)
		{
			if (!TryClock(clockMatch.Groups["clock"].Value, out var parsedClock)) return Fail("INVALID CLOCK TIME");
			clock = parsedClock;
			dateText = clockMatch.Groups["date"].Value.Trim();
			if (dateText.EndsWith(" at", StringComparison.OrdinalIgnoreCase)) dateText = dateText[..^3].TrimEnd();
			explicitParts |= DateExpressionComponents.Clock;
		}
		if (DateTime.TryParseExact(text, ["yyyy-MM-dd'T'HH:mm:ss", "yyyy-MM-dd'T'HH:mm", "ddd MMM dd HH:mm:ss yyyy"], English, DateTimeStyles.None, out var localIso))
			return ResolveLocal(localIso, zone, explicitParts | DateExpressionComponents.Date | DateExpressionComponents.Clock | DateExpressionComponents.Year);

		var relative = Match(dateText, @"^(?:in\s+)?(?<body>[+-]?.+?)(?:\s+from\s+now|\s+ago)?$");
		var body = relative.Groups["body"].Value;
		var quantity = Match(body, $@"^[+-]?\s*(?:{Number}\s*{Unit}\s*)+$");
		if (quantity.Success)
		{
			if (dateText.StartsWith("in ", StringComparison.OrdinalIgnoreCase) && dateText.EndsWith(" ago", StringComparison.OrdinalIgnoreCase))
				return Fail("IN AND AGO CONFLICT; GIVE ONE DIRECTION");
			var hasFutureDirection = dateText.StartsWith("in ", StringComparison.OrdinalIgnoreCase) || dateText.EndsWith(" from now", StringComparison.OrdinalIgnoreCase);
			var hasPastDirection = dateText.EndsWith(" ago", StringComparison.OrdinalIgnoreCase);
			if ((body.StartsWith('-') && (hasFutureDirection || hasPastDirection)) || (body.StartsWith('+') && hasPastDirection))
				return Fail("SIGNED OFFSET AND DIRECTION CONFLICT; GIVE ONE DIRECTION");
			var days = 0L;
			var seconds = 0L;
			var direction = dateText.EndsWith(" ago", StringComparison.OrdinalIgnoreCase) || body.StartsWith('-') ? -1 : 1;
			var tokens = Regex.Matches(body, $@"(?<number>{Number})\s*(?<unit>{Unit})", Options);
			if (tokens.Count > 16) return Fail("TOO MANY OFFSET COMPONENTS");
			foreach (Match token in tokens)
			{
				var n = ReadNumber(token.Groups["number"].Value) * direction;
				var unit = token.Groups["unit"].Value.ToLowerInvariant();
				if (unit.StartsWith('w')) days = checked(days + n * 7);
				else if (unit.StartsWith('d')) days = checked(days + n);
				else if (unit.StartsWith('f')) days = checked(days + n * 14);
				else seconds = checked(seconds + n * (unit.StartsWith('h') ? 3600 : unit.StartsWith('m') ? 60 : 1));
			}
			if (clock is not null && days == 0 && seconds != 0)
				return Fail("AN ELAPSED OFFSET AND A CLOCK CONFLICT; GIVE A CALENDAR DAY AND CLOCK");
			if (days == 0 && clock is null) return Resolved(reference.AddSeconds(seconds), zone, explicitParts | DateExpressionComponents.Relative);
			var target = localReference.AddDays(days);
			if (clock is { } c) target = target.Date.Add(c);
			return ResolveLocal(target, zone, explicitParts | DateExpressionComponents.Relative | DateExpressionComponents.Date) switch
			{
				ResolvedDateExpression resolved => Resolved(resolved.Instant.AddSeconds(seconds), zone, resolved.Explicit),
				DateExpressionFailure failure => failure
			};
		}

		DateTime? date = null;
		var lower = dateText.ToLowerInvariant();
		if (lower.Length == 0) date = localReference.Date;
		else if (lower is "today" or "tomorrow" or "yesterday" or "day after tomorrow" or "the day after tomorrow")
		{
			date = localReference.Date.AddDays(lower switch { "today" => 0, "tomorrow" => 1, "yesterday" => -1, _ => 2 });
			explicitParts |= DateExpressionComponents.Relative;
		}
		else if (Match(lower, $@"^(?:(?<mode>this|next|last|next week) )?(?<day>{Weekday})$") is { Success: true } weekday)
		{
			var targetDay = Day(weekday.Groups["day"].Value);
			var delta = ((int)targetDay - (int)localReference.DayOfWeek + 7) % 7;
			var mode = weekday.Groups["mode"].Value;
			date = mode switch
			{
				"this" => Monday(localReference).AddDays(((int)targetDay + 6) % 7),
				"next week" => Monday(localReference).AddDays(7 + ((int)targetDay + 6) % 7),
				"last" => localReference.Date.AddDays(delta == 0 ? -7 : delta - 7),
				"next" => localReference.Date.AddDays(delta == 0 ? 7 : delta),
				_ => localReference.Date.AddDays(delta == 0 && clock is { } c && localReference.TimeOfDay >= c ? 7 : delta)
			};
			explicitParts |= DateExpressionComponents.Relative;
		}
		else if (Match(lower, $@"^(?:a|one) week from (?<day>{Weekday})$") is { Success: true } weekFrom)
		{
			var delta = ((int)Day(weekFrom.Groups["day"].Value) - (int)localReference.DayOfWeek + 7) % 7;
			if (delta == 0 && clock is { } c && localReference.TimeOfDay >= c) delta = 7;
			date = localReference.Date.AddDays(delta + 7);
			explicitParts |= DateExpressionComponents.Relative;
		}
		else if (Match(lower, $@"^(?<ordinal>first|second|third|fourth|fifth|last) (?<day>{Weekday})(?: of)? (?<month>next month|[a-z]+)(?: (?<year>[0-9]{{4}}))?$") is { Success: true } ordinal)
		{
			var nextMonth = ordinal.Groups["month"].Value == "next month";
			if (nextMonth && ordinal.Groups["year"].Success)
				return Fail("NEXT MONTH AND AN EXPLICIT YEAR CONFLICT; USE A MONTH NAME AND YEAR", DateExpressionFailureKind.Ambiguous);
			if (nextMonth) explicitParts |= DateExpressionComponents.Relative;
			if (ordinal.Groups["year"].Success) explicitParts |= DateExpressionComponents.Year;
			var monthStart = nextMonth ? new DateTime(localReference.Year, localReference.Month, 1).AddMonths(1)
				: Month(ordinal.Groups["month"].Value) is var month && month != 0
					? new DateTime(ordinal.Groups["year"].Success ? int.Parse(ordinal.Groups["year"].Value, English) : localReference.Year, month, 1) : (DateTime?)null;
			if (monthStart is not { } start) return Fail("INVALID MONTH");
			var day = Day(ordinal.Groups["day"].Value);
			var which = ordinal.Groups["ordinal"].Value;
			if (which == "last")
			{
				var end = start.AddMonths(1).AddDays(-1);
				date = end.AddDays(-(((int)end.DayOfWeek - (int)day + 7) % 7));
			}
			else
			{
				var index = which switch { "first" => 0, "second" => 1, "third" => 2, "fourth" => 3, _ => 4 };
				var candidate = start.AddDays(((int)day - (int)start.DayOfWeek + 7) % 7 + index * 7);
				if (candidate.Month != start.Month) return Fail("THAT MONTH HAS NO SUCH WEEKDAY OCCURRENCE");
				date = candidate;
			}
		}
		else
		{
			var absolute = Regex.Replace(dateText, @"(?<=[0-9])(?:st|nd|rd|th)\b", "", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
			if (DateTime.TryParseExact(absolute, ["yyyy-MM-dd", "MMMM d yyyy", "MMM d yyyy", "d MMM yyyy", "d MMMM yyyy"], English, DateTimeStyles.None, out var parsed))
			{
				date = parsed;
				explicitParts |= DateExpressionComponents.Year;
			}
			else if (DateTime.TryParseExact($"{absolute} {localReference.Year}", ["MMMM d yyyy", "MMM d yyyy", "d MMM yyyy", "d MMMM yyyy"], English, DateTimeStyles.None, out parsed)) date = parsed;
		}
		if (date is null) return Fail("UNRECOGNIZED DATE EXPRESSION; USE A DATE AND EXACT CLOCK TIME");
		if (clock is null) return Fail("CLOCK TIME REQUIRED; ADD A TIME SUCH AS 19:00 OR NOON", DateExpressionFailureKind.Incomplete);
		if (dateText.Length != 0) explicitParts |= DateExpressionComponents.Date;
		return ResolveLocal(date.Value.Date.Add(clock.Value), zone, explicitParts);
	}

	private static DateTime Monday(DateTime date) => date.Date.AddDays(-(((int)date.DayOfWeek + 6) % 7));
	private static DayOfWeek Day(string text) => text[..3].ToLowerInvariant() switch
	{ "mon" => DayOfWeek.Monday, "tue" => DayOfWeek.Tuesday, "wed" => DayOfWeek.Wednesday, "thu" => DayOfWeek.Thursday, "fri" => DayOfWeek.Friday, "sat" => DayOfWeek.Saturday, _ => DayOfWeek.Sunday };
	private static int Month(string text)
	{
		for (var i = 1; i <= 12; i++)
			if (text.Equals(English.DateTimeFormat.GetMonthName(i), StringComparison.OrdinalIgnoreCase) || text.Equals(English.DateTimeFormat.GetAbbreviatedMonthName(i), StringComparison.OrdinalIgnoreCase)) return i;
		return 0;
	}
	private static long ReadNumber(string text) => text.ToLowerInvariant() switch
	{ "a" or "an" or "one" => 1, "two" => 2, "three" => 3, "four" => 4, "five" => 5, "six" => 6, "seven" => 7, "eight" => 8, "nine" => 9, "ten" => 10, "eleven" => 11, "twelve" => 12, _ => long.Parse(text, English) };
	private static bool TryClock(string text, out TimeSpan clock)
	{
		clock = default;
		if (text.Equals("noon", StringComparison.OrdinalIgnoreCase)) { clock = TimeSpan.FromHours(12); return true; }
		if (text.Equals("midnight", StringComparison.OrdinalIgnoreCase)) return true;
		if (!DateTime.TryParseExact(text.Replace(" ", "").ToUpperInvariant(), ["H:mm", "HH:mm", "H:mm:ss", "HH:mm:ss", "H:mm:ss.FFF", "HH:mm:ss.FFF", "htt", "hhtt", "h:mmtt", "hh:mmtt"], English, DateTimeStyles.None, out var parsed)) return false;
		clock = parsed.TimeOfDay;
		return true;
	}
	private static bool TryZone(string name, out TimeZoneInfo zone)
	{
		if (name.Equals("UTC", StringComparison.OrdinalIgnoreCase) || name.Equals("Z", StringComparison.OrdinalIgnoreCase)) { zone = TimeZoneInfo.Utc; return true; }
		if (Match(name, @"^[+-](?:0[0-9]|1[0-4]):[0-5][0-9]$").Success && TimeSpan.TryParse(name.TrimStart('+', '-'), English, out var offset))
		{
			if (name[0] == '-') offset = -offset;
			if (offset.Duration() <= TimeSpan.FromHours(14)) { zone = TimeZoneInfo.CreateCustomTimeZone(name, offset, name, name); return true; }
		}
		if (!name.Contains('/') && !name.Contains(' ')) { zone = TimeZoneInfo.Utc; return false; }
		return TimeZones.TryFind(name, out zone);
	}
	private static DateExpressionResult ResolveLocal(DateTime local, TimeZoneInfo zone, DateExpressionComponents explicitParts)
	{
		local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
		if (zone.IsInvalidTime(local)) return Fail("CLOCK TIME DOES NOT EXIST IN THIS ZONE (DST GAP); GIVE ANOTHER TIME OR EXPLICIT OFFSET", DateExpressionFailureKind.Invalid);
		if (zone.IsAmbiguousTime(local)) return Fail("CLOCK TIME OCCURS TWICE IN THIS ZONE (DST FOLD); GIVE AN EXPLICIT OFFSET", DateExpressionFailureKind.Ambiguous);
		return Resolved(new DateTimeOffset(local, zone.GetUtcOffset(local)), zone, explicitParts);
	}
	private static ResolvedDateExpression Resolved(DateTimeOffset instant, TimeZoneInfo zone, DateExpressionComponents explicitParts)
	{
		var local = TimeZoneInfo.ConvertTime(instant, zone);
		return new(instant, zone.Id, explicitParts, $"{local.ToString("yyyy-MM-dd HH:mm:ss zzz", English)} ({zone.Id})");
	}
}
