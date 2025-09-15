using SharpMUSH.Implementation.Definitions;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.ExpandedObjectData;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services;
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

	[SharpFunction(Name = "SECSCALC", MinArgs = 1, MaxArgs = int.MaxValue,
		Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> SecsCalc(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "starttime", MinArgs = 0, MaxArgs = 0, Flags = FunctionFlags.Regular)]
	public static async ValueTask<CallState> StartTime(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var data = await ObjectDataService!.GetExpandedServerDataAsync<UptimeData>();
		return data!.StartTime.ToString();
	}

	[SharpFunction(Name = "STRINGSECS", MinArgs = 1, MaxArgs = 1,
		Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> StringSecs(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "time", MinArgs = 0, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static async ValueTask<CallState> Time(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var arg0 = parser.CurrentState.Arguments.TryGetValue("0", out var arg0Value)
			? arg0Value.Message!.ToPlainText()
			: null;

		if (arg0 == null)
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

	[SharpFunction(Name = "TIMECALC", MinArgs = 1, MaxArgs = int.MaxValue,
		Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> TimeCalc(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "TIMEFMT", MinArgs = 1, MaxArgs = 3, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> TimeFmt(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "TIMESTRING", MinArgs = 1, MaxArgs = 2,
		Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> TimeString(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "uptime", MinArgs = 0, MaxArgs = 1, Flags = FunctionFlags.StripAnsi)]
	public static async ValueTask<CallState> Uptime(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var arg0 = parser.CurrentState.Arguments.TryGetValue("0", out var arg0Value)
			? arg0Value.Message!.ToPlainText()
			: null;

		var data = (await ObjectDataService!.GetExpandedServerDataAsync<UptimeData>())!;

		if (arg0 == null)
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

	[SharpFunction(Name = "ETIME", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> ETime(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "ETIMEFMT", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> ETimeFmt(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}
}