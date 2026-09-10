using System.Globalization;
using Quartz;

namespace SharpMUSH.Library.Services.RecurringJobs;

/// <summary>Bounded Unix-style fields adapted to the pinned Quartz grammar; timezone mapping is explicit.</summary>
public sealed class FiveFieldSchedule
{
	private readonly CronExpression[] _expressions;
	private readonly TimeZoneInfo _zone;
	public FiveFieldSchedule(string expression, string timeZone)
	{
		if (expression is null || expression.Length > 256 || string.IsNullOrWhiteSpace(timeZone) || timeZone.Length > 128) throw new ArgumentException("Provide a five-field schedule and timezone.");
		var fields = expression.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
		if (fields.Length != 5) throw new ArgumentException("Schedules have five fields: minute hour day-of-month month day-of-week.");
		_zone = TimeZoneInfo.FindSystemTimeZoneById(timeZone);
		var minute = Parse(fields[0], 0, 59);
		var hour = Parse(fields[1], 0, 23);
		var day = Parse(fields[2], 1, 31);
		var month = Parse(fields[3], 1, 12);
		var week = Parse(fields[4], 0, 7).Select(n => n % 7 + 1).Distinct().Order().ToArray();
		string Join(int[] values) => string.Join(',', values);
		string Cron(string dom, string dow) => $"0 {Join(minute)} {Join(hour)} {dom} {Join(month)} {dow}";
		// Restricted day fields form a union. A literal '*' leaves that field unrestricted.
		var expressions = fields[2] == "*" ? new[] { Cron("?", Join(week)) }
			: fields[4] == "*" ? new[] { Cron(Join(day), "?") }
			: new[] { Cron(Join(day), "?"), Cron("?", Join(week)) };
		_expressions = expressions.Select(value => new CronExpression(value) { TimeZone = TimeZoneInfo.Utc }).ToArray();
	}

	public DateTimeOffset? Next(DateTimeOffset after)
	{
		var local = DateTime.SpecifyKind(TimeZoneInfo.ConvertTime(after, _zone).DateTime, DateTimeKind.Utc);
		var cursor = new DateTimeOffset(local);
		// Search is bounded even for nonexistent dates or future timezone rule changes.
		for (var attempt = 0; attempt < 4096; attempt++)
		{
			var candidates = _expressions.Select(e => e.GetNextValidTimeAfter(cursor)).Where(v => v.HasValue).Select(v => v!.Value).ToArray();
			if (candidates.Length == 0) return null;
			cursor = candidates.Min();
			var wall = DateTime.SpecifyKind(cursor.DateTime, DateTimeKind.Unspecified);
			if (_zone.IsInvalidTime(wall)) continue;
			var offset = _zone.IsAmbiguousTime(wall) ? _zone.GetAmbiguousTimeOffsets(wall).Max() : _zone.GetUtcOffset(wall);
			var candidate = new DateTimeOffset(wall, offset).ToUniversalTime();
			if (candidate > after) return candidate;
		}
		return null;
	}

	private static int[] Parse(string field, int min, int max)
	{
		var values = new SortedSet<int>();
		foreach (var part in field.Split(','))
		{
			var stepParts = part.Split('/');
			if (stepParts.Length > 2) throw new ArgumentException("Invalid schedule step.");
			var step = stepParts.Length == 2 ? Number(stepParts[1], 1, max - min + 1) : 1;
			var range = stepParts[0].Split('-');
			int start, end;
			if (stepParts[0] == "*") { start = min; end = max; }
			else if (range.Length == 2) { start = Number(range[0], min, max); end = Number(range[1], start, max); }
			else if (range.Length == 1) { start = Number(range[0], min, max); end = stepParts.Length == 2 ? max : start; }
			else throw new ArgumentException("Invalid schedule range.");
			for (var value = start; value <= end; value += step) values.Add(value);
		}
		return values.ToArray();
	}
	private static int Number(string value, int min, int max) => value.Length > 0 && value.All(c => c is >= '0' and <= '9') &&
		int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) && parsed >= min && parsed <= max
		? parsed : throw new ArgumentException("A schedule number is outside its field range.");
}
