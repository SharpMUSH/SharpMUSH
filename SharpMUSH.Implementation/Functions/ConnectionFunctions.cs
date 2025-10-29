using System.Globalization;
using SharpMUSH.Implementation.Common;
using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using AsyncEnumerable = System.Linq.AsyncEnumerable;

namespace SharpMUSH.Implementation.Functions;

public partial class Functions
{
	[SharpFunction(Name = "addrlog", MinArgs = 2, MaxArgs = 4, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> AddressLog(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "cmds", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		Restrict = ["admin", "power:see_all"])]
	public static async ValueTask<CallState> Commands(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> await ArgHelpers.ForHandleOrPlayer(parser, Mediator!, ConnectionService!, LocateService!,
			parser.CurrentState.Arguments["0"],
			(_, cd) => ValueTask.FromResult<CallState>(cd.Metadata["CMDS"]),
			(_, cd) => ValueTask.FromResult<CallState>(cd.Metadata["CMDS"])
		);

	[SharpFunction(Name = "conn", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static async ValueTask<CallState> ConnectedSeconds(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var arg0 = parser.CurrentState.Arguments["0"].Message!.ToPlainText();

		if (int.TryParse(arg0, out var port))
		{
			// TODO: CanSee in case of Dark.

			var data2 = ConnectionService!.Get(port);
			return new CallState(data2?.Connected?.TotalSeconds.ToString(CultureInfo.InvariantCulture) ?? "-1");
		}

		var maybeLocate = await LocateService!.LocatePlayerAndNotifyIfInvalid(parser, executor, executor,
			arg0);
		if (maybeLocate.IsNone || maybeLocate.IsError)
		{
			return new CallState(maybeLocate.IsNone ? Errors.ErrorCantSeeThat : maybeLocate.AsError.Value);
		}

		var located = maybeLocate.AsPlayer;

		// TODO: CanSee in case of Dark.
		var data = await ConnectionService!.Get(located.Object.DBRef).FirstAsync();
		return new CallState(data.Connected?.TotalSeconds.ToString(CultureInfo.InvariantCulture) ?? "-1");
	}

	[SharpFunction(Name = "connlog", MinArgs = 3, MaxArgs = int.MaxValue,
		Flags = FunctionFlags.Regular | FunctionFlags.WizardOnly)]
	public static ValueTask<CallState> ConnectionLog(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "connrecord", MinArgs = 1, MaxArgs = 2,
		Flags = FunctionFlags.Regular | FunctionFlags.WizardOnly)]
	public static ValueTask<CallState> ConnectionRecord(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "doing", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> Doing(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "host", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> HostName(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "idle", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static async ValueTask<CallState> IdleSeconds(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var arg0 = parser.CurrentState.Arguments["0"].Message!.ToPlainText();

		if (int.TryParse(arg0, out var port))
		{
			// TODO: CanSee in case of Dark.

			var data2 = ConnectionService!.Get(port);
			return new CallState(data2?.Idle?.TotalSeconds.ToString(CultureInfo.InvariantCulture) ?? "-1");
		}

		var maybeLocate = await LocateService!.LocatePlayerAndNotifyIfInvalid(parser, executor, executor,
			arg0);
		if (maybeLocate.IsNone || maybeLocate.IsError)
		{
			return new CallState(maybeLocate.IsNone ? "-1" : maybeLocate.AsError.Value);
		}

		var locate = maybeLocate.AsPlayer;
		var data = ConnectionService!.Get(locate.Object.DBRef);

		// TODO: CanSee in case of Dark.
		return new CallState(await data
			.Select(x => x.Idle?.TotalSeconds ?? -1)
			.MinAsync());
	}

	[SharpFunction(Name = "ipaddr", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> IpAddress(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "lports", MinArgs = 0, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> ListPorts(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "lwho", MinArgs = 0, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static async ValueTask<CallState> ListWho(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var arg0 = args.ContainsKey("0") ? parser.CurrentState.Arguments["0"].Message!.ToPlainText() : null;
		var arg1 = args.ContainsKey("1")
			? parser.CurrentState.Arguments["1"].Message!.ToPlainText().ToLower().Split(" ")
			: ["offline"];

		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var looker = executor;

		if (arg0 != null)
		{
			var maybeLocate =
				await LocateService!.LocatePlayerAndNotifyIfInvalidWithCallState(parser, executor, executor, arg0);
			if (maybeLocate.IsError)
			{
				return maybeLocate.AsError;
			}

			looker = maybeLocate.AsSharpObject;
		}

		if (arg1.Length > 1)
		{
			return "#-1 INVALID SECOND ARGUMENT";
		}

		if (!((string[])["online", "offline", "all"]).Contains(arg1.First()))
		{
			return "#-1 INVALID SECOND ARGUMENT";
		}

		// NEEDED: 'Get All Players'.
		var allConnectionsDbRefs = await AsyncEnumerable.ToArrayAsync(ConnectionService!
			.GetAll()
			.Where(x => x.Ref is not null)
			.Select(async (x, ct) => (await Mediator!.Send(new GetObjectNodeQuery(x.Ref!.Value), ct)).Known)
			.Where(async (x, _) => await PermissionService!.CanSee(looker, x))
			.Select(x => $"#{x.Object().DBRef.Number}"));

		return new CallState(string.Join(" ", allConnectionsDbRefs));
	}

	[SharpFunction(Name = "lwhoid", MinArgs = 0, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> ListWhoObjectIds(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "mwho", MinArgs = 0, MaxArgs = 0, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> MortalWho(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "mwhoid", MinArgs = 0, MaxArgs = 0, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> MortalWhoObjectIds(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "nmwho", MinArgs = 0, MaxArgs = 0, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> NumberMortalWho(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "nwho", MinArgs = 0, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> NumberWho(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "pueblo", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> Pueblo(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "recv", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> Received(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "sent", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> Sent(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "ssl", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> SecureSocketLayer(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "terminfo", MinArgs = 1, MaxArgs = 1,
		Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> TerminalInformation(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "width", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static async ValueTask<CallState> Width(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var playerOrDescriptor = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var defaultArg = ArgHelpers.NoParseDefaultNoParseArgument(parser.CurrentState.ArgumentsOrdered, 1, "78");
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);

		var isHandle = long.TryParse(playerOrDescriptor, out var port);

		if (isHandle)
		{
			var data = ConnectionService!.Get(port);
			if (data is null)
			{
				return new CallState("#-1");
			}

			return data.Metadata.TryGetValue("WIDTH", out var height)
				? height
				: defaultArg;
		}

		return await LocateService!.LocatePlayerAndNotifyIfInvalidWithCallStateFunction(parser, executor, executor,
			playerOrDescriptor,
			async found =>
			{
				var fod = await ConnectionService!.Get(found.Object.DBRef).FirstOrDefaultAsync();
				return fod?.Metadata["WIDTH"] ?? defaultArg.ToPlainText();
			});
	}

	[SharpFunction(Name = "xmwhoid", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> NumberRangeMortalWhoObjectId(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "xwho", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> NumberRangeWho(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "xwhoid", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> NumberRangeWhoObjectId(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "zmwho", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> ZoneMortalWho(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "zwho", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> ZoneWho(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "poll", MinArgs = 0, MaxArgs = 0, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> Poll(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "ports", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> Ports(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "player", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static async ValueTask<CallState> Player(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var portString = parser.CurrentState.Arguments["0"].Message!.ToPlainText()!;

		if (!long.TryParse(portString, out var port))
		{
			return new CallState("#-1 INVALID PORT");
		}

		var data = ConnectionService!.Get(port);

		if (data?.Ref == executor.Object().DBRef)
		{
			return new CallState($"#{executor.Object().DBRef.Number}");
		}

		if (await executor.HasFlag("WIZARD") || await executor.HasFlag("ROYALTY") || await executor.HasPower("SEE_ALL"))
		{
			return data is null
				? new CallState("#-1 INVALID PORT")
				: new CallState($"#{data.Ref?.Number}");
		}

		return new CallState(Errors.ErrorPerm);
	}

	[SharpFunction(Name = "height", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static async ValueTask<CallState> Height(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var playerOrDescriptor = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var defaultArg = ArgHelpers.NoParseDefaultNoParseArgument(parser.CurrentState.ArgumentsOrdered, 1, "78");
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);

		var isHandle = long.TryParse(playerOrDescriptor, out var port);

		if (isHandle)
		{
			var data = ConnectionService!.Get(port);
			if (data is null)
			{
				return new CallState("#-1");
			}

			return data.Metadata.TryGetValue("HEIGHT", out var height)
				? height
				: defaultArg;
		}

		return await LocateService!.LocatePlayerAndNotifyIfInvalidWithCallStateFunction(parser, executor, executor,
			playerOrDescriptor,
			async found =>
			{
				var fod = await ConnectionService!.Get(found.Object.DBRef).FirstOrDefaultAsync();
				return fod?.Metadata["HEIGHT"] ?? defaultArg.ToPlainText();
			});
	}

	[SharpFunction(Name = "hidden", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> Hidden(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}
}