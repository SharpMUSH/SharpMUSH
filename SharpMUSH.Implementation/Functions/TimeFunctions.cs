using SharpMUSH.Implementation.Definitions;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.ExpandedObjectData;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Functions;

public partial class Functions
{
	[SharpFunction(Name = "ctime", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static async ValueTask<CallState> CreationTime(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var targetArg = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var utc = parser.CurrentState.Arguments.ContainsKey("1") && parser.CurrentState.Arguments["1"].Message!.Truthy();

		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser, executor, executor, targetArg,
			LocateFlags.All,
			found => utc
				? found.Object().CreationTime.ToString()
				: DateTimeOffset
					.FromUnixTimeMilliseconds(found.Object().CreationTime)
					.ToLocalTime()
					.ToString());
	}

	[SharpFunction(Name = "isdaylight", MinArgs = 0, MaxArgs = 2, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> IsDaylight(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var secs = args.TryGetValue("0", out var value)
			? value.Message!.ToPlainText()
			: DateTimeOffset.Now.ToUnixTimeSeconds().ToString();
		var timezone = args.TryGetValue("1", out var value1) ? value1.Message!.ToPlainText() : TimeZoneInfo.Utc.Id;

		if (!TimeZoneInfo.TryFindSystemTimeZoneById(timezone, out var tz))
		{
			return new ValueTask<CallState>(Errors.ErrorNoSuchTimezone);
		}

		if (!long.TryParse(secs, out var secsInt))
		{
			return new ValueTask<CallState>(Errors.ErrorTimeInteger);
		}

		return ValueTask.FromResult<CallState>(tz.IsDaylightSavingTime(DateTimeOffset.FromUnixTimeMilliseconds(secsInt)));
	}

	[SharpFunction(Name = "mtime", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static async ValueTask<CallState> ModifiedTime(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var targetArg = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var utc = parser.CurrentState.Arguments.ContainsKey("1") && parser.CurrentState.Arguments["1"].Message!.Truthy();

		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser, executor, executor, targetArg,
			LocateFlags.All,
			found => utc
				? found.Object().CreationTime.ToString()
				: DateTimeOffset
					.FromUnixTimeMilliseconds(found.Object().ModifiedTime)
					.ToLocalTime()
					.ToString());
	}

	[SharpFunction(Name = "secs", MinArgs = 0, MaxArgs = 0, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> Secs(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ValueTask.FromResult<CallState>(DateTimeOffset.Now.ToLocalTime().ToUnixTimeSeconds().ToString());

	[SharpFunction(Name = "secscalc", MinArgs = 1, MaxArgs = int.MaxValue,
		Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> SecsCalc(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		
		// For now, implement basic duration string parsing like "1d2h3m4s"
		// The first argument should be the time string
		if (args.Count == 1)
		{
			var timeStr = args["0"].Message!.ToPlainText().Trim();
			
			// Parse time string which can be "1d 2h 3m 4s" or "1d2h3m4s"
			long totalSeconds = 0;
			var i = 0;
			
			while (i < timeStr.Length)
			{
				// Skip whitespace
				while (i < timeStr.Length && char.IsWhiteSpace(timeStr[i]))
				{
					i++;
				}
				
				if (i >= timeStr.Length) break;
				
				// Get the number part
				var numStr = "";
				while (i < timeStr.Length && (char.IsDigit(timeStr[i]) || timeStr[i] == '.' || timeStr[i] == '-'))
				{
					numStr += timeStr[i];
					i++;
				}
				
				if (string.IsNullOrEmpty(numStr))
				{
					return new ValueTask<CallState>(Errors.ErrorInteger);
				}
				
				if (!double.TryParse(numStr, out var value))
				{
					return new ValueTask<CallState>(Errors.ErrorInteger);
				}
				
				// Get the unit part
				var unit = "";
				while (i < timeStr.Length && !char.IsWhiteSpace(timeStr[i]) && !char.IsDigit(timeStr[i]) && timeStr[i] != '-')
				{
					unit += timeStr[i];
					i++;
				}
				
				unit = unit.ToLower();
				switch (unit)
				{
					case "y":
					case "years":
					case "year":
						totalSeconds += (long)(value * 365 * 24 * 3600);
						break;
					case "w":
					case "weeks":
					case "week":
						totalSeconds += (long)(value * 7 * 24 * 3600);
						break;
					case "d":
					case "days":
					case "day":
						totalSeconds += (long)(value * 24 * 3600);
						break;
					case "h":
					case "hours":
					case "hour":
						totalSeconds += (long)(value * 3600);
						break;
					case "m":
					case "minutes":
					case "minute":
						totalSeconds += (long)(value * 60);
						break;
					case "s":
					case "seconds":
					case "second":
					case "":
						totalSeconds += (long)value;
						break;
					default:
						return new ValueTask<CallState>(Errors.ErrorInteger);
				}
			}

			return ValueTask.FromResult<CallState>(totalSeconds.ToString());
		}
		
		// TODO: Handle more complex time calculations with modifiers
		return new ValueTask<CallState>(Errors.ErrorBadArgumentFormat.Replace("{0}", "SECSCALC"));
	}

	[SharpFunction(Name = "starttime", MinArgs = 0, MaxArgs = 0, Flags = FunctionFlags.Regular)]
	public static async ValueTask<CallState> StartTime(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var data = await ObjectDataService!.GetExpandedServerDataAsync<UptimeData>();
		return data!.StartTime.ToString();
	}

	[SharpFunction(Name = "stringsecs", MinArgs = 1, MaxArgs = 1,
		Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> StringSecs(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var timeStr = parser.CurrentState.Arguments["0"].Message!.ToPlainText().Trim();
		long totalSeconds = 0;

		// Parse strings like "5m 1s", "1d 2h 3m 4s", "3y 2m 7d 5h 23m", etc.
		// Split by whitespace and parse each component
		var parts = timeStr.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

		foreach (var part in parts)
		{
			if (string.IsNullOrWhiteSpace(part)) continue;

			// Extract number and unit
			var numStr = "";
			var unit = "";
			var i = 0;

			// Get the number part (including decimals)
			while (i < part.Length && (char.IsDigit(part[i]) || part[i] == '.' || part[i] == '-'))
			{
				numStr += part[i];
				i++;
			}

			// Get the unit part
			while (i < part.Length)
			{
				unit += part[i];
				i++;
			}

			if (string.IsNullOrEmpty(numStr)) continue;

			if (!double.TryParse(numStr, out var value))
			{
				return new ValueTask<CallState>(Errors.ErrorInteger);
			}

			// Convert based on unit
			unit = unit.ToLower();
			switch (unit)
			{
				case "y":
				case "years":
				case "year":
					totalSeconds += (long)(value * 365 * 24 * 3600);
					break;
				case "w":
				case "weeks":
				case "week":
					totalSeconds += (long)(value * 7 * 24 * 3600);
					break;
				case "d":
				case "days":
				case "day":
					totalSeconds += (long)(value * 24 * 3600);
					break;
				case "h":
				case "hours":
				case "hour":
					totalSeconds += (long)(value * 3600);
					break;
				case "m":
				case "minutes":
				case "minute":
					totalSeconds += (long)(value * 60);
					break;
				case "s":
				case "seconds":
				case "second":
				case "":
					totalSeconds += (long)value;
					break;
				default:
					return new ValueTask<CallState>(Errors.ErrorInteger);
			}
		}

		return ValueTask.FromResult<CallState>(totalSeconds.ToString());
	}

	[SharpFunction(Name = "time", MinArgs = 0, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static async ValueTask<CallState> Time(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var arg0 = parser.CurrentState.Arguments.TryGetValue("0", out var arg0Value)
			? arg0Value.Message!.ToPlainText()
			: null;

		if (arg0 is null)
		{
			return DateTimeOffset.Now.ToLocalTime().ToString();
		}

		if (TimeZoneInfo.TryFindSystemTimeZoneById(arg0, out var timeZone))
		{
			return DateTimeOffset.Now.ToOffset(timeZone.BaseUtcOffset).ToString();
		}

		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(
			parser, executor, executor, arg0, LocateFlags.All,
			async found =>
			{
				var attr = await AttributeService!.GetAttributeAsync(executor, found, "TZ",
					IAttributeService.AttributeMode.Read, false);
				if (!attr.IsAttribute)
				{
					return DateTimeOffset.Now.ToLocalTime().ToString();
				}

				var attrValue = attr.AsAttribute.Last().Value.ToPlainText();

				return TimeZoneInfo.TryFindSystemTimeZoneById(attrValue, out var dbTimeZone)
					? DateTimeOffset.Now.ToOffset(dbTimeZone.BaseUtcOffset).ToString()
					: DateTimeOffset.Now.ToLocalTime().ToString();
			});
	}

	[SharpFunction(Name = "timecalc", MinArgs = 1, MaxArgs = int.MaxValue,
		Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> TimeCalc(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		
		// Get the base time string
		var timeStr = args["0"].Message!.ToPlainText().Trim();
		
		// Parse the base time
		DateTimeOffset dt;
		
		if (timeStr.Equals("now", StringComparison.OrdinalIgnoreCase))
		{
			dt = DateTimeOffset.UtcNow;
		}
		else if (long.TryParse(timeStr, out var secs))
		{
			// Unix timestamp
			dt = DateTimeOffset.FromUnixTimeSeconds(secs);
		}
		else if (DateTime.TryParse(timeStr, out var parsed))
		{
			dt = new DateTimeOffset(parsed);
		}
		else
		{
			return new ValueTask<CallState>(Errors.ErrorBadArgumentFormat.Replace("{0}", "TIMECALC"));
		}

		// Apply modifiers
		for (var i = 1; i < args.Count; i++)
		{
			var modifier = args[i.ToString()].Message!.ToPlainText().Trim();
			
			if (modifier.Equals("unixepoch", StringComparison.OrdinalIgnoreCase))
			{
				// Already in Unix epoch format
				continue;
			}
			else if (modifier.Equals("localtime", StringComparison.OrdinalIgnoreCase))
			{
				dt = dt.ToLocalTime();
			}
			else if (modifier.Equals("utc", StringComparison.OrdinalIgnoreCase))
			{
				dt = dt.ToUniversalTime();
			}
			else if (modifier.StartsWith("start of ", StringComparison.OrdinalIgnoreCase))
			{
				var unit = modifier.Substring(9).ToLower();
				if (unit == "month")
				{
					dt = new DateTimeOffset(dt.Year, dt.Month, 1, 0, 0, 0, dt.Offset);
				}
				else if (unit == "year")
				{
					dt = new DateTimeOffset(dt.Year, 1, 1, 0, 0, 0, dt.Offset);
				}
				else if (unit == "day")
				{
					dt = new DateTimeOffset(dt.Year, dt.Month, dt.Day, 0, 0, 0, dt.Offset);
				}
			}
			else if (modifier.StartsWith("weekday ", StringComparison.OrdinalIgnoreCase))
			{
				if (int.TryParse(modifier.Substring(8), out var targetDay))
				{
					var currentDay = (int)dt.DayOfWeek;
					var daysToAdd = (targetDay - currentDay + 7) % 7;
					dt = dt.AddDays(daysToAdd);
				}
			}
			else
			{
				// Try to parse as time offset like "+100 years", "-5 days", etc.
				var parts = modifier.Split(' ', StringSplitOptions.RemoveEmptyEntries);
				if (parts.Length == 2 && double.TryParse(parts[0], out var amount))
				{
					var unit = parts[1].ToLower().TrimEnd('s');
					switch (unit)
					{
						case "year":
							dt = dt.AddYears((int)amount);
							break;
						case "month":
							dt = dt.AddMonths((int)amount);
							break;
						case "week":
							dt = dt.AddDays(amount * 7);
							break;
						case "day":
							dt = dt.AddDays(amount);
							break;
						case "hour":
							dt = dt.AddHours(amount);
							break;
						case "minute":
							dt = dt.AddMinutes(amount);
							break;
						case "second":
							dt = dt.AddSeconds(amount);
							break;
					}
				}
			}
		}

		// Return formatted time string
		return ValueTask.FromResult<CallState>(dt.ToString());
	}

	[SharpFunction(Name = "timefmt", MinArgs = 1, MaxArgs = 3, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> TimeFmt(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var format = args["0"].Message!.ToPlainText();
		
		// Get the time to format (default to current time)
		DateTimeOffset dt;
		if (args.TryGetValue("1", out var secsArg))
		{
			var secsStr = secsArg.Message!.ToPlainText();
			if (!long.TryParse(secsStr, out var secs))
			{
				return new ValueTask<CallState>(Errors.ErrorInteger);
			}
			dt = DateTimeOffset.FromUnixTimeSeconds(secs);
		}
		else
		{
			dt = DateTimeOffset.Now;
		}
		
		// Get timezone (default to local)
		if (args.TryGetValue("2", out var tzArg))
		{
			var tzStr = tzArg.Message!.ToPlainText();
			if (TimeZoneInfo.TryFindSystemTimeZoneById(tzStr, out var tz))
			{
				dt = TimeZoneInfo.ConvertTime(dt, tz);
			}
			else
			{
				return new ValueTask<CallState>(Errors.ErrorNoSuchTimezone);
			}
		}
		else
		{
			dt = dt.ToLocalTime();
		}

		// Process format string
		var result = "";
		for (var i = 0; i < format.Length; i++)
		{
			if (format[i] == '$' && i + 1 < format.Length)
			{
				var code = format[i + 1];
				switch (code)
				{
					case '$':
						result += '$';
						break;
					case 'a':
						result += dt.ToString("ddd");
						break;
					case 'A':
						result += dt.ToString("dddd");
						break;
					case 'b':
						result += dt.ToString("MMM");
						break;
					case 'B':
						result += dt.ToString("MMMM");
						break;
					case 'c':
						result += dt.ToString("f");
						break;
					case 'd':
						result += dt.ToString("dd");
						break;
					case 'H':
						result += dt.ToString("HH");
						break;
					case 'I':
						result += dt.ToString("hh");
						break;
					case 'j':
						result += dt.DayOfYear.ToString("D3");
						break;
					case 'm':
						result += dt.ToString("MM");
						break;
					case 'M':
						result += dt.ToString("mm");
						break;
					case 'p':
					case 'P':
						result += dt.ToString("tt");
						break;
					case 'S':
						result += dt.ToString("ss");
						break;
					case 'U':
						// Week of year from first Sunday
						var startOfYear = new DateTimeOffset(dt.Year, 1, 1, 0, 0, 0, dt.Offset);
						var daysOffset = (int)startOfYear.DayOfWeek;
						var firstSunday = daysOffset == 0 ? startOfYear : startOfYear.AddDays(7 - daysOffset);
						var weekNumber = dt < firstSunday ? 0 : (int)((dt - firstSunday).TotalDays / 7) + 1;
						result += weekNumber.ToString("D2");
						break;
					case 'w':
						result += ((int)dt.DayOfWeek).ToString();
						break;
					case 'W':
						// Week of year from first Monday
						var startOfYearW = new DateTimeOffset(dt.Year, 1, 1, 0, 0, 0, dt.Offset);
						var daysOffsetW = (int)startOfYearW.DayOfWeek;
						var firstMonday = daysOffsetW <= 1 ? startOfYearW.AddDays(1 - daysOffsetW) : startOfYearW.AddDays(8 - daysOffsetW);
						var weekNumberW = dt < firstMonday ? 0 : (int)((dt - firstMonday).TotalDays / 7) + 1;
						result += weekNumberW.ToString("D2");
						break;
					case 'x':
						result += dt.ToString("d");
						break;
					case 'X':
						result += dt.ToString("T");
						break;
					case 'y':
						result += dt.ToString("yy");
						break;
					case 'Y':
						result += dt.ToString("yyyy");
						break;
					case 'Z':
						result += dt.ToString("zzz");
						break;
					default:
						return new ValueTask<CallState>("#-1 INVALID ESCAPE CODE");
				}
				i++; // Skip the code character
			}
			else
			{
				result += format[i];
			}
		}

		return ValueTask.FromResult<CallState>(result);
	}

	[SharpFunction(Name = "timestring", MinArgs = 1, MaxArgs = 2,
		Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> TimeString(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var secsStr = args["0"].Message!.ToPlainText();
		var padFlag = args.TryGetValue("1", out var padArg)
			? padArg.Message!.ToPlainText()
			: "0";

		if (!long.TryParse(secsStr, out var totalSecs))
		{
			return new ValueTask<CallState>(Errors.ErrorInteger);
		}

		if (!int.TryParse(padFlag, out var pad))
		{
			pad = 0;
		}

		// Calculate time components
		var days = totalSecs / (24 * 3600);
		var remainder = totalSecs % (24 * 3600);
		var hours = remainder / 3600;
		remainder %= 3600;
		var minutes = remainder / 60;
		var seconds = remainder % 60;

		// Format based on pad flag
		if (pad == 2)
		{
			// All numbers are 2 digits
			return ValueTask.FromResult<CallState>($"{days:D2}d {hours:D2}h {minutes:D2}m {seconds:D2}s");
		}
		else if (pad == 1)
		{
			// All time periods used even if 0
			return ValueTask.FromResult<CallState>($"{days}d  {hours}h  {minutes}m  {seconds}s");
		}
		else
		{
			// Default: only show non-zero periods (but always show at least seconds)
			var parts = new List<string>();
			if (days > 0) parts.Add($"{days}d");
			if (hours > 0) parts.Add($"{hours}h");
			if (minutes > 0) parts.Add($"{minutes}m");
			parts.Add($"{seconds}s");
			return ValueTask.FromResult<CallState>(" " + string.Join("  ", parts));
		}
	}

	[SharpFunction(Name = "uptime", MinArgs = 0, MaxArgs = 1, Flags = FunctionFlags.StripAnsi)]
	public static async ValueTask<CallState> Uptime(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var arg0 = parser.CurrentState.Arguments.TryGetValue("0", out var arg0Value)
			? arg0Value.Message!.ToPlainText()
			: null;

		var data = (await ObjectDataService!.GetExpandedServerDataAsync<UptimeData>())!;

		if (arg0 is null)
		{
			return data.StartTime.ToUnixTimeSeconds().ToString();
		}

		return arg0.ToLower() switch
		{
			"reboot" => data.LastRebootTime.ToUnixTimeMilliseconds().ToString(),
			"save" => DateTimeOffset.Now.ToUnixTimeMilliseconds().ToString(),
			"nextsave" => DateTimeOffset.Now.ToUnixTimeMilliseconds().ToString(),
			"dbck" => DateTimeOffset.Now.ToUnixTimeMilliseconds().ToString(),
			"purge" => data.NextPurgeTime.ToUnixTimeMilliseconds().ToString(),
			"warnings" => data.NextWarningTime.ToUnixTimeMilliseconds().ToString(),
			_ => data.StartTime.ToUnixTimeMilliseconds().ToString()
		};
	}

	[SharpFunction(Name = "utctime", MinArgs = 0, MaxArgs = 0, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> CurrentCoordinatedUniversalTime(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ValueTask.FromResult<CallState>(DateTimeOffset.UtcNow.ToString());

	[SharpFunction(Name = "csecs", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static async ValueTask<CallState> CSecs(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var targetArg = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var utc = parser.CurrentState.Arguments.ContainsKey("1") && parser.CurrentState.Arguments["1"].Message!.Truthy();

		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser, executor, executor, targetArg,
			LocateFlags.All,
			found => utc
				? found.Object().CreationTime.ToString()
				: DateTimeOffset
					.FromUnixTimeMilliseconds(found.Object().CreationTime)
					.ToLocalTime()
					.ToUnixTimeMilliseconds()
					.ToString());
	}

	[SharpFunction(Name = "msecs", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static async ValueTask<CallState> ModifiedSecs(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var targetArg = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var utc = parser.CurrentState.Arguments.ContainsKey("1") && parser.CurrentState.Arguments["1"].Message!.Truthy();

		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser, executor, executor, targetArg,
			LocateFlags.All,
			found => utc
				? found.Object().CreationTime.ToString()
				: DateTimeOffset
					.FromUnixTimeMilliseconds(found.Object().ModifiedTime)
					.ToLocalTime()
					.ToUnixTimeMilliseconds()
					.ToString());
	}

	[SharpFunction(Name = "etime", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> ETime(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var secsStr = args["0"].Message!.ToPlainText();
		var width = args.TryGetValue("1", out var widthArg)
			? widthArg.Message!.ToPlainText()
			: null;

		if (!long.TryParse(secsStr, out var totalSecs))
		{
			return new ValueTask<CallState>(Errors.ErrorInteger);
		}

		var maxWidth = width != null && int.TryParse(width, out var w) ? w : int.MaxValue;

		// Calculate time components
		var years = totalSecs / (365 * 24 * 3600);
		var remainder = totalSecs % (365 * 24 * 3600);
		var weeks = remainder / (7 * 24 * 3600);
		remainder %= (7 * 24 * 3600);
		var days = remainder / (24 * 3600);
		remainder %= (24 * 3600);
		var hours = remainder / 3600;
		remainder %= 3600;
		var minutes = remainder / 60;
		var seconds = remainder % 60;

		// Build result string, showing non-zero fields
		var parts = new List<string>();

		if (years > 0) parts.Add($"{years}y");
		if (weeks > 0) parts.Add($"{weeks}w");
		if (days > 0) parts.Add($"{days}d");
		if (hours > 0) parts.Add($"{hours}h");
		if (minutes > 0) parts.Add($"{minutes}m");
		if (seconds > 0 || parts.Count == 0) parts.Add($"{seconds}s");

		// Join parts with two spaces and respect width limit
		var separator = "  ";
		var result = string.Join(separator, parts);
		
		if (result.Length > maxWidth && parts.Count > 1)
		{
			// Trim from the end until it fits
			while (parts.Count > 1 && string.Join(separator, parts).Length > maxWidth)
			{
				parts.RemoveAt(parts.Count - 1);
			}
			result = string.Join(separator, parts);
		}

		return ValueTask.FromResult<CallState>(result);
	}

	[SharpFunction(Name = "etimefmt", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> ETimeFmt(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var format = args["0"].Message!.ToPlainText();
		var secsStr = args["1"].Message!.ToPlainText();

		if (!long.TryParse(secsStr, out var totalSecs))
		{
			return new ValueTask<CallState>(Errors.ErrorInteger);
		}

		// Calculate time components
		var years = totalSecs / (365 * 24 * 3600);
		var remainder = totalSecs % (365 * 24 * 3600);
		var weeks = remainder / (7 * 24 * 3600);
		remainder %= (7 * 24 * 3600);
		var days = remainder / (24 * 3600);
		remainder %= (24 * 3600);
		var hours = remainder / 3600;
		remainder %= 3600;
		var minutes = remainder / 60;
		var seconds = remainder % 60;

		// Total values (for $t codes)
		var totalDays = totalSecs / (24 * 3600);
		var totalHours = totalSecs / 3600;
		var totalMinutes = totalSecs / 60;

		// Process format string
		var result = "";
		for (var i = 0; i < format.Length; i++)
		{
			if (format[i] == '$' && i + 1 < format.Length)
			{
				var width = 0;
				var padChar = ' ';
				var addSuffix = false;
				var skipZero = false;
				var useTotal = false;
				var j = i + 1;

				// Parse width
				while (j < format.Length && char.IsDigit(format[j]))
				{
					width = width * 10 + (format[j] - '0');
					j++;
				}

				// Parse flags (uppercase = pad with 0, x = add suffix, z = skip if zero, t = use total)
				while (j < format.Length && (format[j] == 'x' || format[j] == 'z' || format[j] == 't'))
				{
					if (format[j] == 'x') addSuffix = true;
					else if (format[j] == 'z') skipZero = true;
					else if (format[j] == 't') useTotal = true;
					j++;
				}

				if (j >= format.Length) break;

				var code = format[j];
				var isUpperCase = char.IsUpper(code);
				code = char.ToLower(code);

				if (isUpperCase && width > 0) padChar = '0';

				long value = 0;
				string suffix = "";

				switch (code)
				{
					case '$':
						result += '$';
						i = j;
						continue;
					case 'y':
						value = years;
						suffix = "y";
						break;
					case 'w':
						value = weeks;
						suffix = "w";
						break;
					case 'd':
						value = useTotal ? totalDays : days;
						suffix = "d";
						break;
					case 'h':
						value = useTotal ? totalHours : hours;
						suffix = "h";
						break;
					case 'm':
						value = useTotal ? totalMinutes : minutes;
						suffix = "m";
						break;
					case 's':
						value = useTotal ? totalSecs : seconds;
						suffix = "s";
						break;
					default:
						result += format[i];
						continue;
				}

				// Skip if zero and z flag is set
				if (skipZero && value == 0)
				{
					i = j;
					continue;
				}

				// Format the value
				var valueStr = width > 0
					? value.ToString().PadLeft(width, padChar)
					: value.ToString();

				result += valueStr;
				if (addSuffix) result += suffix;

				i = j;
			}
			else
			{
				result += format[i];
			}
		}

		// Trim leading spaces from space-padded values
		return ValueTask.FromResult<CallState>(result.TrimStart(' '));
	}
}