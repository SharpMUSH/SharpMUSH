using System.Text.RegularExpressions;
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
	// Generated regex for parsing duration strings
	// Matches: number + optional unit (y/year/years, w/week/weeks, d/day/days, h/hour/hours, m/minute/minutes, s/second/seconds)
	[GeneratedRegex(@"(?<number>[-+]?\d+(?:\.\d+)?)\s*(?<unit>y(?:ears?)?|w(?:eeks?)?|d(?:ays?)?|h(?:ours?)?|m(?:inutes?)?|s(?:econds?)?)", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
	private static partial Regex DurationPattern();
	
	// Generated regex for parsing etimefmt format codes
	// Matches: $ + optional width + optional flags (x,z,t) + code letter
	[GeneratedRegex(@"\$(?<width>\d*)(?<flags>[xzt]*)(?<code>[yYwWdDhHmMsS$])", RegexOptions.Compiled)]
	private static partial Regex ETimeFmtPattern();
	
	// Generated regex for parsing timefmt format codes
	// Matches: $ + format code letter
	[GeneratedRegex(@"\$(?<code>.)", RegexOptions.Compiled)]
	private static partial Regex TimeFmtPattern();

	[SharpFunction(Name = "ctime", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["seconds"])]
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

	[SharpFunction(Name = "isdaylight", MinArgs = 0, MaxArgs = 2, Flags = FunctionFlags.Regular, ParameterNames = ["seconds"])]
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

	[SharpFunction(Name = "mtime", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
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

	[SharpFunction(Name = "secs", MinArgs = 0, MaxArgs = 0, Flags = FunctionFlags.Regular, ParameterNames = [])]
	public static ValueTask<CallState> Secs(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ValueTask.FromResult<CallState>(DateTimeOffset.Now.ToLocalTime().ToUnixTimeSeconds().ToString());

	[SharpFunction(Name = "secscalc", MinArgs = 1, MaxArgs = int.MaxValue,
		Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["timestring"])]
	public static ValueTask<CallState> SecsCalc(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var timeStr = args["0"].Message!.ToPlainText().Trim();
		
		// Parse the initial timestring
		DateTimeOffset baseTime;
		bool isUnixEpoch = false;
		
		// Handle "now" keyword
		if (timeStr.Equals("now", StringComparison.OrdinalIgnoreCase))
		{
			baseTime = DateTimeOffset.UtcNow;
		}
		// Try to parse as Julian day (just a number)
		else if (long.TryParse(timeStr, out var julianOrEpoch))
		{
			// If it's a large number, treat as seconds since epoch (will be modified by unixepoch modifier if needed)
			baseTime = DateTimeOffset.FromUnixTimeSeconds(julianOrEpoch);
			isUnixEpoch = true;
		}
		// Try various datetime formats
		else if (DateTimeOffset.TryParse(timeStr, out var parsedTime))
		{
			baseTime = parsedTime;
		}
		else
		{
			// If we can't parse it, try simple duration parsing for backward compatibility
			var matches = DurationPattern().Matches(timeStr);
			if (matches.Count > 0)
			{
				long totalSeconds = 0;
				foreach (Match match in matches)
				{
					if (!double.TryParse(match.Groups["number"].Value, out var value))
					{
						return new ValueTask<CallState>(Errors.ErrorInteger);
					}
					
					var unit = match.Groups["unit"].Value.ToLower();
					totalSeconds += unit switch
					{
						['y', ..] => (long)(value * 365 * 24 * 3600),
						['w', ..] => (long)(value * 7 * 24 * 3600),
						['d', ..] => (long)(value * 24 * 3600),
						['h', ..] => (long)(value * 3600),
						['m', ..] => (long)(value * 60),
						['s', ..] => (long)value,
						"" => (long)value,
						_ => 0
					};
				}
				return ValueTask.FromResult<CallState>(totalSeconds.ToString());
			}
			
			return new ValueTask<CallState>(Errors.ErrorBadArgumentFormat.Replace("{0}", "SECSCALC"));
		}
		
		// Apply modifiers
		for (int i = 1; i < args.Count; i++)
		{
			var modifier = args[i.ToString()].Message!.ToPlainText().Trim().ToLower();
			
			// Check for "unixepoch" modifier
			if (modifier == "unixepoch")
			{
				// If the original number was treated as julian day, now treat it as unix epoch
				if (isUnixEpoch)
				{
					// Already treated as epoch, no change needed
				}
				continue;
			}
			
			// Check for "localtime" modifier
			if (modifier == "localtime")
			{
				baseTime = baseTime.ToLocalTime();
				continue;
			}
			
			// Check for "utc" modifier
			if (modifier == "utc")
			{
				baseTime = baseTime.ToUniversalTime();
				continue;
			}
			
			// Check for "start of" modifiers
			if (modifier.StartsWith("start of "))
			{
				var unit = modifier.AsSpan(9).Trim().ToString();
				baseTime = unit switch
				{
					"day" => new DateTimeOffset(baseTime.Year, baseTime.Month, baseTime.Day, 0, 0, 0, baseTime.Offset),
					"month" => new DateTimeOffset(baseTime.Year, baseTime.Month, 1, 0, 0, 0, baseTime.Offset),
					"year" => new DateTimeOffset(baseTime.Year, 1, 1, 0, 0, 0, baseTime.Offset),
					_ => baseTime
				};
				continue;
			}
			
			// Check for "weekday N" modifier
			if (modifier.StartsWith("weekday "))
			{
				if (int.TryParse(modifier.AsSpan(8).Trim(), out var targetWeekday))
				{
					var currentWeekday = (int)baseTime.DayOfWeek;
					var daysToAdd = (targetWeekday - currentWeekday + 7) % 7;
					baseTime = baseTime.AddDays(daysToAdd);
				}
				continue;
			}
			
			// Parse numeric modifiers like "5 days", "3 hours", etc.
			var parts = modifier.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
			if (parts.Length == 2)
			{
				if (double.TryParse(parts[0], out var value))
				{
					var unit = parts[1];
					baseTime = unit switch
					{
						"years" or "year" => baseTime.AddYears((int)value),
						"months" or "month" => baseTime.AddMonths((int)value),
						"days" or "day" => baseTime.AddDays(value),
						"hours" or "hour" => baseTime.AddHours(value),
						"minutes" or "minute" => baseTime.AddMinutes(value),
						"seconds" or "second" => baseTime.AddSeconds(value),
						_ => baseTime
					};
				}
			}
		}
		
		return ValueTask.FromResult<CallState>(baseTime.ToUnixTimeSeconds().ToString());
	}

	[SharpFunction(Name = "starttime", MinArgs = 0, MaxArgs = 0, Flags = FunctionFlags.Regular, ParameterNames = [])]
	public static async ValueTask<CallState> StartTime(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var data = await ObjectDataService!.GetExpandedServerDataAsync<UptimeData>();
		return data!.StartTime.ToString();
	}

	[SharpFunction(Name = "stringsecs", MinArgs = 1, MaxArgs = 1,
		Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["seconds"])]
	public static ValueTask<CallState> StringSecs(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var timeStr = parser.CurrentState.Arguments["0"].Message!.ToPlainText().Trim();
		
		// Parse strings like "5m 1s", "1d 2h 3m 4s", "3y 2m 7d 5h 23m", etc.
		// Use generated regex to extract all number-unit pairs
		var matches = DurationPattern().Matches(timeStr);
		
		if (matches.Count == 0)
		{
			return new ValueTask<CallState>(Errors.ErrorInteger);
		}
		
		long totalSeconds = 0;
		foreach (Match match in matches)
		{
			if (!double.TryParse(match.Groups["number"].Value, out var value))
			{
				return new ValueTask<CallState>(Errors.ErrorInteger);
			}
			
			var unit = match.Groups["unit"].Value.ToLower();
			// Use array pattern matching for first character
			totalSeconds += unit switch
			{
				['y', ..] => (long)(value * 365 * 24 * 3600),
				['w', ..] => (long)(value * 7 * 24 * 3600),
				['d', ..] => (long)(value * 24 * 3600),
				['h', ..] => (long)(value * 3600),
				['m', ..] => (long)(value * 60),
				['s', ..] => (long)value,
				"" => (long)value, // Empty unit defaults to seconds
				_ => 0 // Unknown unit returns 0 instead of throwing
			};
		}

		return ValueTask.FromResult<CallState>(totalSeconds.ToString());
	}

	[SharpFunction(Name = "time", MinArgs = 0, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = [])]
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
		Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["timestring"])]
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

		// Apply modifiers using Aggregate
		dt = Enumerable.Range(1, args.Count - 1)
			.Aggregate(dt, (currentDt, i) =>
			{
				var modifier = args[i.ToString()].Message!.ToPlainText().Trim();
				
				if (modifier.Equals("unixepoch", StringComparison.OrdinalIgnoreCase))
				{
					// Already in Unix epoch format
					return currentDt;
				}
				else if (modifier.Equals("localtime", StringComparison.OrdinalIgnoreCase))
				{
					return currentDt.ToLocalTime();
				}
				else if (modifier.Equals("utc", StringComparison.OrdinalIgnoreCase))
				{
					return currentDt.ToUniversalTime();
				}
				else if (modifier.StartsWith("start of ", StringComparison.OrdinalIgnoreCase))
				{
					var unit = modifier.AsSpan(9).ToString().ToLower();
					return unit switch
					{
						"month" => new DateTimeOffset(currentDt.Year, currentDt.Month, 1, 0, 0, 0, currentDt.Offset),
						"year" => new DateTimeOffset(currentDt.Year, 1, 1, 0, 0, 0, currentDt.Offset),
						"day" => new DateTimeOffset(currentDt.Year, currentDt.Month, currentDt.Day, 0, 0, 0, currentDt.Offset),
						_ => currentDt
					};
				}
				else if (modifier.StartsWith("weekday ", StringComparison.OrdinalIgnoreCase))
				{
					if (int.TryParse(modifier.AsSpan(8), out var targetDay))
					{
						var currentDay = (int)currentDt.DayOfWeek;
						var daysToAdd = (targetDay - currentDay + 7) % 7;
						return currentDt.AddDays(daysToAdd);
					}
					return currentDt;
				}
				else
				{
					// Try to parse as time offset like "+100 years", "-5 days", etc.
					var parts = modifier.Split(' ', StringSplitOptions.RemoveEmptyEntries);
					if (parts.Length == 2 && double.TryParse(parts[0], out var amount))
					{
						var unit = parts[1].ToLower().TrimEnd('s');
						return unit switch
						{
							"year" => currentDt.AddYears((int)amount),
							"month" => currentDt.AddMonths((int)amount),
							"week" => currentDt.AddDays(amount * 7),
							"day" => currentDt.AddDays(amount),
							"hour" => currentDt.AddHours(amount),
							"minute" => currentDt.AddMinutes(amount),
							"second" => currentDt.AddSeconds(amount),
							_ => currentDt
						};
					}
					return currentDt;
				}
			});

		// Return formatted time string
		return ValueTask.FromResult<CallState>(dt.ToString());
	}

	[SharpFunction(Name = "timefmt", MinArgs = 1, MaxArgs = 3, Flags = FunctionFlags.Regular, ParameterNames = ["format", "seconds"])]
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

		// Process format string using generated regex with MatchEvaluator
		var result = TimeFmtPattern().Replace(format, match =>
		{
			var code = match.Groups["code"].Value[0];
			return code switch
			{
				'$' => "$",
				'a' => dt.ToString("ddd"),
				'A' => dt.ToString("dddd"),
				'b' => dt.ToString("MMM"),
				'B' => dt.ToString("MMMM"),
				'c' => dt.ToString("f"),
				'd' => dt.ToString("dd"),
				'H' => dt.ToString("HH"),
				'I' => dt.ToString("hh"),
				'j' => dt.DayOfYear.ToString("D3"),
				'm' => dt.ToString("MM"),
				'M' => dt.ToString("mm"),
				'p' or 'P' => dt.ToString("tt"),
				'S' => dt.ToString("ss"),
				'U' => CalculateWeekOfYearFromSunday(dt),
				'w' => ((int)dt.DayOfWeek).ToString(),
				'W' => CalculateWeekOfYearFromMonday(dt),
				'x' => dt.ToString("d"),
				'X' => dt.ToString("T"),
				'y' => dt.ToString("yy"),
				'Y' => dt.ToString("yyyy"),
				'Z' => dt.ToString("zzz"),
				_ => "#-1 INVALID ESCAPE CODE"
			};
		});

		return ValueTask.FromResult<CallState>(result);
	}
	
	private static string CalculateWeekOfYearFromSunday(DateTimeOffset dt)
	{
		var startOfYear = new DateTimeOffset(dt.Year, 1, 1, 0, 0, 0, dt.Offset);
		var daysOffset = (int)startOfYear.DayOfWeek;
		var firstSunday = daysOffset == 0 ? startOfYear : startOfYear.AddDays(7 - daysOffset);
		var weekNumber = dt < firstSunday ? 0 : (int)((dt - firstSunday).TotalDays / 7) + 1;
		return weekNumber.ToString("D2");
	}
	
	private static string CalculateWeekOfYearFromMonday(DateTimeOffset dt)
	{
		var startOfYear = new DateTimeOffset(dt.Year, 1, 1, 0, 0, 0, dt.Offset);
		var daysOffset = (int)startOfYear.DayOfWeek;
		var firstMonday = daysOffset <= 1 ? startOfYear.AddDays(1 - daysOffset) : startOfYear.AddDays(8 - daysOffset);
		var weekNumber = dt < firstMonday ? 0 : (int)((dt - firstMonday).TotalDays / 7) + 1;
		return weekNumber.ToString("D2");
	}

	[SharpFunction(Name = "timestring", MinArgs = 1, MaxArgs = 2,
		Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["seconds"])]
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

		// Calculate time components using TimeSpan
		var timeSpan = TimeSpan.FromSeconds(totalSecs);
		var days = (long)timeSpan.TotalDays;
		var hours = timeSpan.Hours;
		var minutes = timeSpan.Minutes;
		var seconds = timeSpan.Seconds;

		// Format based on pad flag
		return pad switch
		{
			2 => ValueTask.FromResult<CallState>($"{days:D2}d {hours:D2}h {minutes:D2}m {seconds:D2}s"),
			1 => ValueTask.FromResult<CallState>($"{days}d  {hours}h  {minutes}m  {seconds}s"),
			_ => ValueTask.FromResult<CallState>(" " + string.Join("  ", 
				new[] { 
					days > 0 ? $"{days}d" : null,
					hours > 0 ? $"{hours}h" : null,
					minutes > 0 ? $"{minutes}m" : null,
					$"{seconds}s"
				}.Where(s => s != null)))
		};
	}

	[SharpFunction(Name = "uptime", MinArgs = 0, MaxArgs = 1, Flags = FunctionFlags.StripAnsi, ParameterNames = [])]
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

	[SharpFunction(Name = "utctime", MinArgs = 0, MaxArgs = 0, Flags = FunctionFlags.Regular, ParameterNames = [])]
	public static ValueTask<CallState> CurrentCoordinatedUniversalTime(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ValueTask.FromResult<CallState>(DateTimeOffset.UtcNow.ToString());

	[SharpFunction(Name = "csecs", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = [])]
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

	[SharpFunction(Name = "msecs", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = [])]
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

	[SharpFunction(Name = "etime", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular, ParameterNames = ["seconds"])]
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
		var timeSpan = TimeSpan.FromSeconds(totalSecs);
		var years = (long)(timeSpan.TotalDays / 365);
		var remainingDays = (long)timeSpan.TotalDays % 365;
		var weeks = remainingDays / 7;
		var days = remainingDays % 7;
		var hours = timeSpan.Hours;
		var minutes = timeSpan.Minutes;
		var seconds = timeSpan.Seconds;

		// Build result string, showing non-zero fields
		var parts = new List<string>();

		if (years > 0) parts.Add($"{years}y");
		if (weeks > 0) parts.Add($"{weeks}w");
		if (days > 0) parts.Add($"{days}d");
		if (hours > 0) parts.Add($"{hours}h");
		if (minutes > 0) parts.Add($"{minutes}m");
		if (seconds > 0 || parts.Count == 0) parts.Add($"{seconds}s");

		// Join parts with two spaces and respect width limit
		const string separator = "  ";
		var result = string.Join(separator, parts);
		
		// Trim from the end until it fits width
		if (result.Length > maxWidth && parts.Count > 1)
		{
			parts = parts.TakeWhile((_, index) => 
				string.Join(separator, parts.Take(index + 1)).Length <= maxWidth || index == 0
			).ToList();
			result = string.Join(separator, parts);
		}

		return ValueTask.FromResult<CallState>(result);
	}

	[SharpFunction(Name = "etimefmt", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular, ParameterNames = ["format", "seconds"])]
	public static ValueTask<CallState> ETimeFmt(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var format = args["0"].Message!.ToPlainText();
		var secsStr = args["1"].Message!.ToPlainText();

		if (!long.TryParse(secsStr, out var totalSecs))
		{
			return new ValueTask<CallState>(Errors.ErrorInteger);
		}

		// Calculate time components using TimeSpan
		var timeSpan = TimeSpan.FromSeconds(totalSecs);
		var years = (long)(timeSpan.TotalDays / 365);
		var remainingDays = (long)timeSpan.TotalDays % 365;
		var weeks = remainingDays / 7;
		var days = remainingDays % 7;
		var hours = timeSpan.Hours;
		var minutes = timeSpan.Minutes;
		var seconds = timeSpan.Seconds;

		// Total values (for $t codes)
		var totalDays = (long)timeSpan.TotalDays;
		var totalHours = (long)timeSpan.TotalHours;
		var totalMinutes = (long)timeSpan.TotalMinutes;

		// Process format string using generated regex with MatchEvaluator
		var result = ETimeFmtPattern().Replace(format, match =>
		{
			var widthStr = match.Groups["width"].Value;
			var flags = match.Groups["flags"].Value.ToLower();
			var codeChar = match.Groups["code"].Value;
			
			var width = string.IsNullOrEmpty(widthStr) ? 0 : int.Parse(widthStr);
			var addSuffix = flags.Contains('x');
			var skipZero = flags.Contains('z');
			var useTotal = flags.Contains('t');
			var isUpperCase = char.IsUpper(codeChar[0]);
			var code = char.ToLower(codeChar[0]);
			var padChar = isUpperCase && width > 0 ? '0' : ' ';

			if (code == '$') return "$";

			var (value, suffix) = code switch
			{
				'y' => (years, "y"),
				'w' => (weeks, "w"),
				'd' => (useTotal ? totalDays : days, "d"),
				'h' => (useTotal ? totalHours : hours, "h"),
				'm' => (useTotal ? totalMinutes : minutes, "m"),
				's' => (useTotal ? totalSecs : seconds, "s"),
				_ => (0L, "")
			};

			if (skipZero && value == 0) return "";

			var valueStr = width > 0 ? value.ToString().PadLeft(width, padChar) : value.ToString();
			return addSuffix ? valueStr + suffix : valueStr;
		});

		// Trim leading spaces from space-padded values
		return ValueTask.FromResult<CallState>(result.TrimStart(' '));
	}

	[SharpFunction(Name = "CONVSECS", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular, 
		ParameterNames = ["seconds", "timezone"])]
	public static ValueTask<CallState> ConvSecs(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		// Convert seconds since epoch to time string
		var secsStr = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var timezone = parser.CurrentState.Arguments.TryGetValue("1", out var tzArg)
			? tzArg.Message!.ToPlainText()
			: null;

		if (!long.TryParse(secsStr, out var seconds))
		{
			return ValueTask.FromResult<CallState>("#-1 INVALID SECONDS");
		}

		var dateTime = DateTimeOffset.FromUnixTimeSeconds(seconds);

		if (timezone != null)
		{
			if (timezone.Equals("utc", StringComparison.OrdinalIgnoreCase))
			{
				return ValueTask.FromResult<CallState>(dateTime.UtcDateTime.ToString("ddd MMM dd HH:mm:ss yyyy"));
			}
			else if (TimeZoneInfo.TryFindSystemTimeZoneById(timezone, out var tz))
			{
				var converted = TimeZoneInfo.ConvertTime(dateTime, tz);
				return ValueTask.FromResult<CallState>(converted.ToString("ddd MMM dd HH:mm:ss yyyy"));
			}
			else
			{
				return ValueTask.FromResult<CallState>("#-1 INVALID TIMEZONE");
			}
		}

		// Local time by default
		return ValueTask.FromResult<CallState>(dateTime.ToLocalTime().ToString("ddd MMM dd HH:mm:ss yyyy"));
	}

	[SharpFunction(Name = "CONVTIME", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular, 
		ParameterNames = ["time-string", "timezone"])]
	public static ValueTask<CallState> ConvTime(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		// Convert time string to seconds since epoch
		var timeStr = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var timezone = parser.CurrentState.Arguments.TryGetValue("1", out var tzArg)
			? tzArg.Message!.ToPlainText()
			: null;

		// Try to parse the time string
		// Format: Ddd MMM DD HH:MM:SS YYYY
		if (!DateTimeOffset.TryParse(timeStr, out var dateTime))
		{
			// Try different formats for extended convtime support
			if (!DateTime.TryParse(timeStr, out var dt))
			{
				return ValueTask.FromResult<CallState>("#-1");
			}
			dateTime = new DateTimeOffset(dt, TimeSpan.Zero);
		}

		if (timezone != null && timezone.Equals("utc", StringComparison.OrdinalIgnoreCase))
		{
			return ValueTask.FromResult<CallState>(dateTime.ToUnixTimeSeconds().ToString());
		}

		// Assume local time if no timezone specified
		return ValueTask.FromResult<CallState>(dateTime.ToUnixTimeSeconds().ToString());
	}
}