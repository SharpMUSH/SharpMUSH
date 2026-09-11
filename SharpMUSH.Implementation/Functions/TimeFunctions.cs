using SharpMUSH.Implementation.Definitions;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.ExpandedObjectData;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.Time;
using System.Globalization;
using System.Text.RegularExpressions;

namespace SharpMUSH.Implementation.Functions;

public partial class Functions
{
	// Generated regex for parsing duration strings
	// Matches: number + optional unit (y/year/years, w/week/weeks, d/day/days, h/hour/hours, m/minute/minutes, s/second/seconds)
	[GeneratedRegex(@"(?<number>[-+]?\d+(?:\.\d+)?)\s*(?<unit>y(?:ears?)?|w(?:eeks?)?|d(?:ays?)?|h(?:ours?)?|m(?:inutes?)?|s(?:econds?)?)?", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
	private static partial Regex DurationPattern();

	// Generated regex for parsing etimefmt format codes
	// Matches: $ + optional width + optional flags (x,z,t) + code letter
	[GeneratedRegex(@"\$(?<width>\d*)(?<flags>[xzt]*)(?<code>[yYwWdDhHmMsS$])", RegexOptions.Compiled)]
	private static partial Regex ETimeFmtPattern();

	// Generated regex for parsing timefmt format codes
	// Matches: $ + format code letter
	[GeneratedRegex(@"\$(?<code>.)", RegexOptions.Compiled)]
	private static partial Regex TimeFmtPattern();

	/// <summary>
	/// Reads the trailing precision argument. Absent means <see cref="TimePrecision.Seconds"/>, so
	/// every PennMUSH call site keeps PennMUSH's unit without saying so.
	/// </summary>
	private static bool TryPrecision(IReadOnlyDictionary<string, CallState> args, string key,
		out TimePrecision precision)
		=> TimePrecisions.TryParse(
			args.TryGetValue(key, out var arg) ? arg.Message?.ToPlainText() : null,
			out precision);

	/// <summary>
	/// PennMUSH's time-string format, shared by time(), ctime(), mtime(), convsecs(), starttime()
	/// and restarttime() so they cannot drift apart.
	/// </summary>
	private const string PennTimeFormat = "ddd MMM dd HH:mm:ss yyyy";

	[SharpFunction(Name = "ctime", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		ParameterNames = ["object", "utc"])]
	public async ValueTask<CallState> CreationTime(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var targetArg = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var utc = parser.CurrentState.Arguments.TryGetValue("1", out var utcArgument) && utcArgument.Message.Truthy(parser);

		return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(parser, executor, executor, targetArg,
			LocateFlags.All,
			found => FormatInstant(found.Object().CreationTime, utc));
	}

	// PennMUSH renders a time string from both branches of ctime()/mtime(); <utc> chooses the zone.
	private static string FormatInstant(long milliseconds, bool utc)
	{
		var instant = DateTimeOffset.FromUnixTimeMilliseconds(milliseconds);
		return (utc ? instant.UtcDateTime : instant.ToLocalTime().DateTime)
			.ToString(PennTimeFormat, CultureInfo.InvariantCulture);
	}

	[SharpFunction(Name = "isdaylight", MinArgs = 0, MaxArgs = 2, Flags = FunctionFlags.Regular,
		ParameterNames = ["seconds", "timezone"])]
	public ValueTask<CallState> IsDaylight(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var secs = args.TryGetValue("0", out var value) ? value.Message?.ToPlainText() : null;
		var timezone = args.TryGetValue("1", out var value1) ? value1.Message!.ToPlainText() : TimeZoneInfo.Utc.Id;

		if (!TimeZoneInfo.TryFindSystemTimeZoneById(timezone, out var tz))
		{
			return new ValueTask<CallState>(ErrorMessages.Returns.NoSuchTimezone);
		}

		if (string.IsNullOrWhiteSpace(secs))
		{
			return ValueTask.FromResult<CallState>(tz.IsDaylightSavingTime(DateTimeOffset.UtcNow));
		}

		if (!TimePrecisions.TryParseInstant(secs, out var instant))
		{
			return new ValueTask<CallState>(ErrorMessages.Returns.TimeInteger);
		}

		return ValueTask.FromResult<CallState>(tz.IsDaylightSavingTime(instant));
	}

	[SharpFunction(Name = "mtime", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		ParameterNames = ["object", "utc"])]
	public async ValueTask<CallState> ModifiedTime(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var targetArg = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var utc = parser.CurrentState.Arguments.TryGetValue("1", out var utcArgument) && utcArgument.Message.Truthy(parser);

		return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(parser, executor, executor, targetArg,
			LocateFlags.All,
			found => FormatInstant(found.Object().ModifiedTime, utc));
	}

	[SharpFunction(Name = "secs", MinArgs = 0, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		ParameterNames = ["precision"])]
	public ValueTask<CallState> Secs(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ValueTask.FromResult<CallState>(
			TryPrecision(parser.CurrentState.Arguments, "0", out var precision)
				? TimePrecisions.Format(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), precision)
				: ErrorMessages.Returns.InvalidPrecision);

	[SharpFunction(Name = "secscalc", MinArgs = 1, MaxArgs = int.MaxValue,
		Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["timestring"])]
	public ValueTask<CallState> SecsCalc(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var timeStr = args["0"].Message!.ToPlainText().Trim();

		DateTimeOffset baseTime;
		bool isUnixEpoch = false;

		if (timeStr.Equals("now", StringComparison.OrdinalIgnoreCase))
		{
			baseTime = DateTimeOffset.UtcNow;
		}
		else if (long.TryParse(timeStr, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var julianOrEpoch))
		{
			baseTime = DateTimeOffset.FromUnixTimeSeconds(julianOrEpoch);
			isUnixEpoch = true;
		}
		else if (DateTimeOffset.TryParse(timeStr, CultureInfo.InvariantCulture, out var parsedTime))
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
					if (!double.TryParse(match.Groups["number"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
					{
						return new ValueTask<CallState>(ErrorMessages.Returns.Integer);
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

			return new ValueTask<CallState>(ErrorMessages.Returns.BadArgumentFormat.Replace("{0}", "SECSCALC"));
		}

		for (int i = 1; i < args.Count; i++)
		{
			var modifier = args[i.ToString()].Message!.ToPlainText().Trim().ToLower();

			if (modifier == "unixepoch")
			{
				if (isUnixEpoch)
				{
					// Already treated as epoch, no change needed
				}
				continue;
			}

			if (modifier == "localtime")
			{
				baseTime = baseTime.ToLocalTime();
				continue;
			}

			if (modifier == "utc")
			{
				baseTime = baseTime.ToUniversalTime();
				continue;
			}

			if (modifier.StartsWith("start of "))
			{
				var unit = modifier.Substring(9).Trim();
				baseTime = unit switch
				{
					"day" => new DateTimeOffset(baseTime.Year, baseTime.Month, baseTime.Day, 0, 0, 0, baseTime.Offset),
					"month" => new DateTimeOffset(baseTime.Year, baseTime.Month, 1, 0, 0, 0, baseTime.Offset),
					"year" => new DateTimeOffset(baseTime.Year, 1, 1, 0, 0, 0, baseTime.Offset),
					_ => baseTime
				};
				continue;
			}

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
			var parts = modifier.Split(' ', StringSplitOptions.RemoveEmptyEntries);
			if (parts.Length == 2)
			{
				if (double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
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
	public async ValueTask<CallState> StartTime(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var data = await ObjectDataService.GetExpandedServerDataAsync<UptimeData>();
		return data!.StartTime.ToLocalTime().ToString(PennTimeFormat, CultureInfo.InvariantCulture);
	}

	[SharpFunction(Name = "stringsecs", MinArgs = 1, MaxArgs = 2,
		Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["timestring", "precision"])]
	public ValueTask<CallState> StringSecs(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		if (!TryPrecision(parser.CurrentState.Arguments, "1", out var precision))
		{
			return new ValueTask<CallState>(ErrorMessages.Returns.InvalidPrecision);
		}

		var timeStr = parser.CurrentState.Arguments["0"].Message!.ToPlainText().Trim();

		if (!TryParseDurationMilliseconds(timeStr, out var milliseconds, out var error))
		{
			return new ValueTask<CallState>(error!);
		}

		return ValueTask.FromResult<CallState>(TimePrecisions.Format(milliseconds, precision));
	}

	/// <summary>
	/// Parses a duration such as "1d 2h 3m 4s" into milliseconds, accumulating in decimal so a
	/// fractional component ("1.5s") survives to the millisecond rather than being truncated at
	/// each term.
	/// </summary>
	private static bool TryParseDurationMilliseconds(string timeStr, out long milliseconds, out string? error)
	{
		milliseconds = 0;
		error = null;

		var matches = DurationPattern().Matches(timeStr);
		if (matches.Count == 0)
		{
			error = ErrorMessages.Returns.InvalidTimestring;
			return false;
		}

		// The whole accumulation sits inside the guard, not just the final cast: each term is a
		// user-supplied decimal scaled by up to 31,536,000,000, so the multiplication and the running
		// sum can both overflow, and softcode can reach either.
		try
		{
			decimal total = 0;
			foreach (Match match in matches)
			{
				if (!decimal.TryParse(match.Groups["number"].Value,
							NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
							CultureInfo.InvariantCulture, out var value))
				{
					error = ErrorMessages.Returns.Integer;
					return false;
				}

				total += match.Groups["unit"].Value.ToLowerInvariant() switch
				{
					['y', ..] => value * 365 * 24 * 3600 * 1000,
					['w', ..] => value * 7 * 24 * 3600 * 1000,
					['d', ..] => value * 24 * 3600 * 1000,
					['h', ..] => value * 3600 * 1000,
					['m', ..] => value * 60 * 1000,
					['s', ..] => value * 1000,
					"" => value * 1000, // Empty unit defaults to seconds
					_ => 0 // Unknown unit returns 0 instead of throwing
				};
			}

			milliseconds = (long)decimal.Round(total, MidpointRounding.AwayFromZero);
			return true;
		}
		catch (OverflowException)
		{
			error = ErrorMessages.Returns.InvalidTimestring;
			return false;
		}
	}

	[SharpFunction(Name = "time", MinArgs = 0, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = [])]
	public async ValueTask<CallState> Time(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var arg0 = parser.CurrentState.Arguments.TryGetValue("0", out var arg0Value)
			? arg0Value.Message!.ToPlainText()
			: null;

		if (string.IsNullOrEmpty(arg0))
		{
			return DateTimeOffset.Now.ToLocalTime().ToString(PennTimeFormat, CultureInfo.InvariantCulture);
		}

		if (TimeZoneInfo.TryFindSystemTimeZoneById(arg0, out var timeZone))
		{
			return DateTimeOffset.Now.ToOffset(timeZone.BaseUtcOffset).ToString(PennTimeFormat, CultureInfo.InvariantCulture);
		}

		return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(
			parser, executor, executor, arg0, LocateFlags.All,
			async found =>
			{
				var attr = await AttributeService.GetAttributeAsync(executor, found, "TZ",
					IAttributeService.AttributeMode.Read, false);
				if (attr is not SharpAttribute[] tz)
				{
					return DateTimeOffset.Now.ToLocalTime().ToString(PennTimeFormat, CultureInfo.InvariantCulture);
				}

				var attrValue = tz.Last().Value.ToPlainText();

				return TimeZoneInfo.TryFindSystemTimeZoneById(attrValue, out var dbTimeZone)
					? DateTimeOffset.Now.ToOffset(dbTimeZone.BaseUtcOffset).ToString(PennTimeFormat, CultureInfo.InvariantCulture)
					: DateTimeOffset.Now.ToLocalTime().ToString(PennTimeFormat, CultureInfo.InvariantCulture);
			});
	}

	[SharpFunction(Name = "timecalc", MinArgs = 1, MaxArgs = int.MaxValue,
		Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["timestring"])]
	public ValueTask<CallState> TimeCalc(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;

		var timeStr = args["0"].Message!.ToPlainText().Trim();

		DateTimeOffset dt;

		if (timeStr.Equals("now", StringComparison.OrdinalIgnoreCase))
		{
			dt = DateTimeOffset.UtcNow;
		}
		else if (long.TryParse(timeStr, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var secs))
		{
			dt = DateTimeOffset.FromUnixTimeSeconds(secs);
		}
		else if (DateTime.TryParse(timeStr, CultureInfo.InvariantCulture, out var parsed))
		{
			dt = new DateTimeOffset(parsed);
		}
		else
		{
			return new ValueTask<CallState>(ErrorMessages.Returns.BadArgumentFormat.Replace("{0}", "TIMECALC"));
		}

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
					var unit = modifier.Substring(9).ToLower();
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
					if (parts.Length == 2 && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var amount))
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

		// PennMUSH: "timecalc() returns a time in the same format as time()".
		return ValueTask.FromResult<CallState>(dt.ToString(PennTimeFormat, CultureInfo.InvariantCulture));
	}

	[SharpFunction(Name = "timefmt", MinArgs = 1, MaxArgs = 3, Flags = FunctionFlags.Regular,
		ParameterNames = ["format", "seconds", "timezone"])]
	public ValueTask<CallState> TimeFmt(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var format = args["0"].Message!.ToPlainText();

		DateTimeOffset dt;
		if (args.TryGetValue("1", out var secsArg))
		{
			var secsStr = secsArg.Message!.ToPlainText();
			if (!TimePrecisions.TryParseInstant(secsStr, out dt))
			{
				return new ValueTask<CallState>(ErrorMessages.Returns.TimeInteger);
			}
		}
		else
		{
			dt = DateTimeOffset.Now;
		}

		if (args.TryGetValue("2", out var tzArg))
		{
			var tzStr = tzArg.Message!.ToPlainText();
			if (TimeZoneInfo.TryFindSystemTimeZoneById(tzStr, out var tz))
			{
				dt = TimeZoneInfo.ConvertTime(dt, tz);
			}
			else
			{
				return new ValueTask<CallState>(ErrorMessages.Returns.NoSuchTimezone);
			}
		}
		else
		{
			dt = dt.ToLocalTime();
		}

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
				_ => ErrorMessages.Returns.InvalidEscapeCode
			};
		});

		return ValueTask.FromResult<CallState>(result);
	}

	private string CalculateWeekOfYearFromSunday(DateTimeOffset dt)
	{
		var startOfYear = new DateTimeOffset(dt.Year, 1, 1, 0, 0, 0, dt.Offset);
		var daysOffset = (int)startOfYear.DayOfWeek;
		var firstSunday = daysOffset == 0 ? startOfYear : startOfYear.AddDays(7 - daysOffset);
		var weekNumber = dt < firstSunday ? 0 : (int)((dt - firstSunday).TotalDays / 7) + 1;
		return weekNumber.ToString("D2");
	}

	private string CalculateWeekOfYearFromMonday(DateTimeOffset dt)
	{
		var startOfYear = new DateTimeOffset(dt.Year, 1, 1, 0, 0, 0, dt.Offset);
		var daysOffset = (int)startOfYear.DayOfWeek;
		var firstMonday = daysOffset <= 1 ? startOfYear.AddDays(1 - daysOffset) : startOfYear.AddDays(8 - daysOffset);
		var weekNumber = dt < firstMonday ? 0 : (int)((dt - firstMonday).TotalDays / 7) + 1;
		return weekNumber.ToString("D2");
	}

	[SharpFunction(Name = "timestring", MinArgs = 1, MaxArgs = 3,
		Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["seconds", "pad", "precision"])]
	public ValueTask<CallState> TimeString(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;

		if (!TryPrecision(args, "2", out var precision))
		{
			return new ValueTask<CallState>(ErrorMessages.Returns.InvalidPrecision);
		}

		var secsStr = args["0"].Message!.ToPlainText();
		var padFlag = args.TryGetValue("1", out var padArg)
			? padArg.Message!.ToPlainText()
			: "0";

		if (!TimePrecisions.TryParseSecondsParts(secsStr, out var totalSecs, out var fractionMs))
		{
			return new ValueTask<CallState>(ErrorMessages.Returns.Integer);
		}

		// Both parts carry the sign, so a sub-second negative — where the whole part is 0 — is only
		// visible in the remainder.
		if (totalSecs < 0 || fractionMs < 0)
		{
			return new ValueTask<CallState>(ErrorMessages.Returns.SecondsMustNotBeNegative);
		}

		if (!int.TryParse(padFlag, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var pad))
		{
			pad = 0;
		}

		// Divided out rather than handed to TimeSpan, which overflows past ~9.2e11 seconds and made
		// the function return EMPTY — the one answer a caller cannot tell from a real one. Integer
		// division covers the whole non-negative long and cannot throw.
		//
		// PennMUSH stops well short of that: fun_timestring reads its seconds into an "unsigned int"
		// (src/funtime.c:484-501), so it refuses anything past 32 bits. That ceiling is a property of
		// the C type rather than of timestring, and SharpMUSH is 64-bit throughout, so it is not
		// reproduced here.
		var days = totalSecs / 86400;
		var hours = totalSecs % 86400 / 3600;
		var minutes = totalSecs % 3600 / 60;
		var secondsField = TimePrecisions.FormatSecondsField(totalSecs % 60, fractionMs, precision);

		// fun_timestring (src/funtime.c:509-519) prints each field with printf's "%2u" — right-aligned
		// in two columns — and separates fields with ONE space. Two-digit values therefore close up
		// ("timestring(281)" is " 4m 41s", not " 4m  41s"), and only the leading field of the days
		// form is unpadded. Joining single-digit fields with two spaces happens to agree; anything
		// that reaches ten does not, which is what made every rendered age in the game an hour into
		// its life read wrong.
		if (pad == 2)
		{
			return ValueTask.FromResult<CallState>(
				$"{days:D2}d {hours:D2}h {minutes:D2}m {PadSecondsField(secondsField, 2)}s");
		}

		var seconds = AlignSecondsField(secondsField, 2);

		// "pad || days > 0" in Penn: any non-zero pad, or a span that reached a day, shows every unit.
		if (pad != 0 || days > 0)
		{
			return ValueTask.FromResult<CallState>(
				$"{days}d {hours,2}h {minutes,2}m {seconds}");
		}

		return ValueTask.FromResult<CallState>(hours > 0
			? $"{hours,2}h {minutes,2}m {seconds}"
			: minutes > 0
				? $"{minutes,2}m {seconds}"
				: seconds);
	}

	/// <summary>
	/// Right-aligns the whole-seconds part of a rendered seconds field to <paramref name="width"/>
	/// with spaces and appends the unit, so a fraction (which widens the field on its own) is never
	/// counted towards the alignment.
	/// </summary>
	private static string AlignSecondsField(string text, int width)
	{
		var point = text.IndexOf('.');
		return point < 0
			? text.PadLeft(width) + "s"
			: text[..point].PadLeft(width) + text[point..] + "s";
	}

	/// <summary>
	/// Zero-pads the whole-seconds part of a rendered seconds field, leaving any fraction alone.
	/// PennMUSH's pad flag widens the number to two digits, so 1.5s pads to "01.500s" — padding the
	/// whole rendered field instead would give "1.500s" no padding at all, since it is already wide.
	/// </summary>
	private static string PadSecondsField(string text, int width)
	{
		var point = text.IndexOf('.');
		return point < 0
			? text.PadLeft(width, '0')
			: text[..point].PadLeft(width, '0') + text[point..];
	}

	/// <remarks>
	/// PennMUSH's uptime() is seconds for every type, and -1 for an event that has not happened or
	/// is disabled. SharpMUSH has no save or dbck scheduler, so those report -1 rather than a
	/// plausible "now" that softcode would treat as a real timestamp.
	/// </remarks>
	[SharpFunction(Name = "uptime", MinArgs = 0, MaxArgs = 2, Flags = FunctionFlags.StripAnsi,
		ParameterNames = ["type", "precision"])]
	public async ValueTask<CallState> Uptime(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		if (!TryPrecision(parser.CurrentState.Arguments, "1", out var precision))
		{
			return ErrorMessages.Returns.InvalidPrecision;
		}

		var arg0 = parser.CurrentState.Arguments.TryGetValue("0", out var arg0Value)
			? arg0Value.Message!.ToPlainText()
			: null;

		var data = (await ObjectDataService.GetExpandedServerDataAsync<UptimeData>())!;

		long? milliseconds = arg0?.ToLowerInvariant() switch
		{
			null or "" or "upsince" => data.StartTime.ToUnixTimeMilliseconds(),
			"reboot" => data.LastRebootTime.ToUnixTimeMilliseconds(),
			"purge" => data.NextPurgeTime.ToUnixTimeMilliseconds(),
			"warnings" => data.NextWarningTime.ToUnixTimeMilliseconds(),
			"save" or "nextsave" or "dbck" => null,
			_ => data.StartTime.ToUnixTimeMilliseconds()
		};

		return milliseconds is null
			? "-1"
			: TimePrecisions.Format(milliseconds.Value, precision);
	}

	[SharpFunction(Name = "utctime", MinArgs = 0, MaxArgs = 0, Flags = FunctionFlags.Regular, ParameterNames = [])]
	public ValueTask<CallState> CurrentCoordinatedUniversalTime(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ValueTask.FromResult<CallState>(
			DateTimeOffset.UtcNow.ToString(PennTimeFormat, CultureInfo.InvariantCulture));

	/// <summary>
	/// An object's creation time, in PennMUSH's unit — seconds since the epoch — unless a precision
	/// argument asks otherwise.
	/// </summary>
	/// <remarks>
	/// <para>PennMUSH has no &lt;utc&gt; argument here and none is possible: a Unix epoch value names
	/// one instant, with no timezone to vary by. Slot 2 is the precision argument instead.</para>
	/// <para>SharpMUSH's objid carries milliseconds, so unlike PennMUSH,
	/// <c>[num(%0)]:[csecs(%0)]</c> does not reconstruct one. <c>csecs(&lt;object&gt;,ms)</c> is the
	/// field objid actually holds.</para>
	/// </remarks>
	[SharpFunction(Name = "csecs", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		ParameterNames = ["object", "precision"])]
	public async ValueTask<CallState> CSecs(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		if (!TryPrecision(parser.CurrentState.Arguments, "1", out var precision))
		{
			return ErrorMessages.Returns.InvalidPrecision;
		}

		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var targetArg = parser.CurrentState.Arguments["0"].Message!.ToPlainText();

		return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(parser, executor, executor, targetArg,
			LocateFlags.All,
			found => TimePrecisions.Format(found.Object().CreationTime, precision));
	}

	/// <remarks>See <see cref="CSecs"/> on why slot 2 is precision rather than &lt;utc&gt;.</remarks>
	[SharpFunction(Name = "msecs", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		ParameterNames = ["object", "precision"])]
	public async ValueTask<CallState> ModifiedSecs(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		if (!TryPrecision(parser.CurrentState.Arguments, "1", out var precision))
		{
			return ErrorMessages.Returns.InvalidPrecision;
		}

		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var targetArg = parser.CurrentState.Arguments["0"].Message!.ToPlainText();

		return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(parser, executor, executor, targetArg,
			LocateFlags.All,
			found => TimePrecisions.Format(found.Object().ModifiedTime, precision));
	}

	[SharpFunction(Name = "etime", MinArgs = 1, MaxArgs = 3, Flags = FunctionFlags.Regular,
		ParameterNames = ["seconds", "width", "precision"])]
	public ValueTask<CallState> ETime(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;

		if (!TryPrecision(args, "2", out var precision))
		{
			return new ValueTask<CallState>(ErrorMessages.Returns.InvalidPrecision);
		}

		var secsStr = args["0"].Message!.ToPlainText();
		var width = args.TryGetValue("1", out var widthArg)
			? widthArg.Message!.ToPlainText()
			: null;

		if (!TimePrecisions.TryParseSecondsParts(secsStr, out var totalSecs, out var fractionMs))
		{
			return new ValueTask<CallState>(ErrorMessages.Returns.Integer);
		}

		// PennMUSH's fun_etime rejects a negative (src/funtime.c: "secs < 0" -> e_range). Both parts
		// carry the sign, so a sub-second negative shows up only in the remainder.
		if (totalSecs < 0 || fractionMs < 0)
		{
			return new ValueTask<CallState>(ErrorMessages.Returns.SecondsMustNotBeNegative);
		}

		// An empty width argument is an omitted one. It is reachable whenever a caller skips the
		// slot to reach precision — etime(<secs>,,ms) — and treating it as a malformed width would
		// make the third argument unreachable without inventing a width.
		var maxWidth = string.IsNullOrWhiteSpace(width)
			? int.MaxValue
			: int.TryParse(width, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var w) ? w : -1;

		if (maxWidth < 0)
		{
			return new ValueTask<CallState>(ErrorMessages.Returns.WidthMustBeANumber);
		}

		var years = totalSecs / (365L * 86400L);
		var remainingDays = totalSecs / 86400L % 365;
		var weeks = remainingDays / 7;
		var days = remainingDays % 7;
		var hours = totalSecs % 86400L / 3600L;
		var minutes = totalSecs % 3600L / 60L;
		var seconds = totalSecs % 60L;

		var parts = new List<string>();

		if (years > 0) parts.Add($"{years}y");
		if (weeks > 0) parts.Add($"{weeks}w");
		if (days > 0) parts.Add($"{days}d");
		if (hours > 0) parts.Add($"{hours}h");
		if (minutes > 0) parts.Add($"{minutes}m");
		if (seconds > 0 || fractionMs > 0 || parts.Count == 0)
			parts.Add($"{TimePrecisions.FormatSecondsField(seconds, fractionMs, precision)}s");

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

	[SharpFunction(Name = "etimefmt", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular,
		ParameterNames = ["format", "seconds", "precision"])]
	public ValueTask<CallState> ETimeFmt(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;

		if (!TryPrecision(args, "2", out var precision))
		{
			return new ValueTask<CallState>(ErrorMessages.Returns.InvalidPrecision);
		}

		var format = args["0"].Message!.ToPlainText();
		var secsStr = args["1"].Message!.ToPlainText();

		if (!TimePrecisions.TryParseSecondsParts(secsStr, out var totalSecs, out var fractionMs))
		{
			return new ValueTask<CallState>(ErrorMessages.Returns.Integer);
		}

		// PennMUSH: negative seconds return error. Both parts carry the sign, so a sub-second
		// negative shows up only in the remainder.
		if (totalSecs < 0 || fractionMs < 0)
		{
			return new ValueTask<CallState>(ErrorMessages.Returns.SecondsMustNotBeNegative);
		}

		var years = totalSecs / (365L * 86400L);
		var remainingDays = totalSecs / 86400L % 365;
		var weeks = remainingDays / 7;
		var days = remainingDays % 7;
		var hours = totalSecs % 86400L / 3600L;
		var minutes = totalSecs % 3600L / 60L;
		var seconds = totalSecs % 60L;

		// Total values (for $t codes)
		var totalDays = totalSecs / 86400L;
		var totalHours = totalSecs / 3600L;
		var totalMinutes = totalSecs / 60L;

		var result = ETimeFmtPattern().Replace(format, match =>
		{
			var widthStr = match.Groups["width"].Value;
			var flags = match.Groups["flags"].Value.ToLowerInvariant();
			var codeChar = match.Groups["code"].Value;

			// The pattern's width group is \d* with no length bound, so "$99999999999s" overflows
			// Int32. A width nobody could render falls back to no padding.
			var width = int.TryParse(widthStr, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedWidth)
				? parsedWidth
				: 0;
			var addSuffix = flags.Contains('x');
			var skipZero = flags.Contains('z');
			var useTotal = flags.Contains('t');
			var isUpperCase = char.IsUpper(codeChar[0]);
			var code = char.ToLowerInvariant(codeChar[0]);
			var padChar = isUpperCase && width > 0 ? '0' : ' ';

			if (code == '$') return "$";

			// The seconds field is the only one precision reaches: every larger unit is whole by
			// construction, so rendering a fraction anywhere else would invent digits.
			if (code == 's')
			{
				var wholeSeconds = useTotal ? totalSecs : seconds;
				if (skipZero && wholeSeconds == 0 && fractionMs == 0) return "";

				var secondsText = TimePrecisions.FormatSecondsField(wholeSeconds, fractionMs, precision);
				var padded = width > 0
					? (padChar == '0' ? PadSecondsField(secondsText, width) : secondsText.PadLeft(width, padChar))
					: secondsText;
				return addSuffix ? padded + "s" : padded;
			}

			var (value, suffix) = code switch
			{
				'y' => (years, "y"),
				'w' => (weeks, "w"),
				'd' => (useTotal ? totalDays : days, "d"),
				'h' => (useTotal ? totalHours : hours, "h"),
				'm' => (useTotal ? totalMinutes : minutes, "m"),
				_ => (0L, "")
			};

			if (skipZero && value == 0) return "";

			var valueStr = width > 0
				? value.ToString(CultureInfo.InvariantCulture).PadLeft(width, padChar)
				: value.ToString(CultureInfo.InvariantCulture);
			return addSuffix ? valueStr + suffix : valueStr;
		});

		return ValueTask.FromResult<CallState>(result);
	}

	[SharpFunction(Name = "CONVSECS", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular,
		ParameterNames = ["seconds", "timezone"])]
	public ValueTask<CallState> ConvSecs(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var secsStr = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var timezone = parser.CurrentState.Arguments.TryGetValue("1", out var tzArg)
			? tzArg.Message!.ToPlainText()
			: null;

		if (!TimePrecisions.TryParseInstant(secsStr, out var dateTime))
		{
			return ValueTask.FromResult<CallState>(ErrorMessages.Returns.InvalidSeconds);
		}

		if (timezone != null)
		{
			if (timezone.Equals("utc", StringComparison.OrdinalIgnoreCase))
			{
				return ValueTask.FromResult<CallState>(
					dateTime.UtcDateTime.ToString(PennTimeFormat, CultureInfo.InvariantCulture));
			}

			if (TimeZoneInfo.TryFindSystemTimeZoneById(timezone, out var tz))
			{
				var converted = TimeZoneInfo.ConvertTime(dateTime, tz);
				return ValueTask.FromResult<CallState>(converted.ToString(PennTimeFormat, CultureInfo.InvariantCulture));
			}

			return ValueTask.FromResult<CallState>(ErrorMessages.Returns.InvalidTimezone);
		}

		return ValueTask.FromResult<CallState>(
			dateTime.ToLocalTime().ToString(PennTimeFormat, CultureInfo.InvariantCulture));
	}

	[SharpFunction(Name = "CONVTIME", MinArgs = 1, MaxArgs = 3, Flags = FunctionFlags.Regular,
		ParameterNames = ["time-string", "timezone", "precision"])]
	public ValueTask<CallState> ConvTime(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		if (!TryPrecision(parser.CurrentState.Arguments, "2", out var precision))
		{
			return new ValueTask<CallState>(ErrorMessages.Returns.InvalidPrecision);
		}

		var timeStr = parser.CurrentState.Arguments["0"].Message!.ToPlainText();

		// Format: Ddd MMM DD HH:MM:SS YYYY
		if (!DateTimeOffset.TryParse(timeStr, CultureInfo.InvariantCulture, out var dateTime))
		{
			if (!DateTime.TryParse(timeStr, CultureInfo.InvariantCulture, out var dt))
			{
				return ValueTask.FromResult<CallState>("#-1");
			}
			dateTime = new DateTimeOffset(dt, TimeSpan.Zero);
		}

		return ValueTask.FromResult<CallState>(
			TimePrecisions.Format(dateTime.ToUnixTimeMilliseconds(), precision));
	}

	[SharpFunction(Name = "CONVUTCSECS", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular,
		ParameterNames = ["seconds"])]
	public ValueTask<CallState> ConvUtcSecs(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var secsStr = parser.CurrentState.Arguments["0"].Message!.ToPlainText();

		if (!TimePrecisions.TryParseInstant(secsStr, out var dateTime))
		{
			return ValueTask.FromResult<CallState>(ErrorMessages.Returns.InvalidSeconds);
		}

		return ValueTask.FromResult<CallState>(
			dateTime.UtcDateTime.ToString(PennTimeFormat, CultureInfo.InvariantCulture));
	}

	[SharpFunction(Name = "CONVUTCTIME", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular,
		ParameterNames = ["time-string", "precision"])]
	public ValueTask<CallState> ConvUtcTime(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		if (!TryPrecision(parser.CurrentState.Arguments, "1", out var precision))
		{
			return new ValueTask<CallState>(ErrorMessages.Returns.InvalidPrecision);
		}

		var timeStr = parser.CurrentState.Arguments["0"].Message!.ToPlainText();

		// Try standard format first: Ddd Mmm DD HH:MM:SS YYYY
		if (DateTimeOffset.TryParseExact(timeStr, PennTimeFormat, CultureInfo.InvariantCulture,
			DateTimeStyles.AssumeUniversal, out var dateTime))
		{
			return ValueTask.FromResult<CallState>(
				TimePrecisions.Format(dateTime.ToUnixTimeMilliseconds(), precision));
		}

		// Fallback: try generic UTC parse
		if (DateTimeOffset.TryParse(timeStr, CultureInfo.InvariantCulture,
			DateTimeStyles.AssumeUniversal, out dateTime))
		{
			return ValueTask.FromResult<CallState>(
				TimePrecisions.Format(dateTime.ToUnixTimeMilliseconds(), precision));
		}

		return ValueTask.FromResult<CallState>("#-1");
	}
}