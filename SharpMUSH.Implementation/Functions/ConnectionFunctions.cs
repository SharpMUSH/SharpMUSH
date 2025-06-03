using System.Globalization;
using OneOf.Types;
using SharpMUSH.Implementation.Definitions;
using SharpMUSH.Library;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Implementation.Functions;

public partial class Functions
{
	[SharpFunction(Name = "ADDRLOG", MinArgs = 2, MaxArgs = 4, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> AddressLog(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "CMDS", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> Commands(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "CONN", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static async ValueTask<CallState> ConnectedSeconds(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(parser.Mediator);
		var arg0 = parser.CurrentState.Arguments["0"].Message!.ToPlainText();

		if (int.TryParse(arg0, out var port))
		{
			// TODO: CanSee in case of Dark.
			
			var data2 = parser.ConnectionService.Get(port);
			return new CallState(data2?.Connected?.TotalSeconds.ToString(CultureInfo.InvariantCulture) ?? "-1");
		}
		
		var maybeLocate = await parser.LocateService.LocatePlayerAndNotifyIfInvalid(parser, executor, executor,
			arg0);
		if (maybeLocate.IsNone || maybeLocate.IsError)
		{
			return new CallState(maybeLocate.IsNone ? Errors.ErrorCantSeeThat : maybeLocate.AsError.Value);
		}

		var located = maybeLocate.AsPlayer;
		
		// TODO: CanSee in case of Dark.
		var data = parser.ConnectionService.Get(located.Object.DBRef).ToArray().First();
		return new CallState(data.Connected?.TotalSeconds.ToString(CultureInfo.InvariantCulture) ?? "-1");
	}

	[SharpFunction(Name = "CONNLOG", MinArgs = 3, MaxArgs = int.MaxValue,
		Flags = FunctionFlags.Regular | FunctionFlags.WizardOnly)]
	public static ValueTask<CallState> ConnectionLog(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "CONNRECORD", MinArgs = 1, MaxArgs = 2,
		Flags = FunctionFlags.Regular | FunctionFlags.WizardOnly)]
	public static ValueTask<CallState> ConnectionRecord(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "DOING", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> Doing(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "HOST", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> HostName(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "idle", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static async ValueTask<CallState> IdleSeconds(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(parser.Mediator);
		var arg0 = parser.CurrentState.Arguments["0"].Message!.ToPlainText();

		if (int.TryParse(arg0, out var port))
		{
			// TODO: CanSee in case of Dark.
			
			var data2 = parser.ConnectionService.Get(port);
			return new CallState(data2?.Idle?.TotalSeconds.ToString(CultureInfo.InvariantCulture) ?? "-1");
		}
		
		var maybeLocate = await parser.LocateService.LocatePlayerAndNotifyIfInvalid(parser, executor, executor,
			arg0);
		if (maybeLocate.IsNone || maybeLocate.IsError)
		{
			return new CallState(maybeLocate.IsNone ? "-1" : maybeLocate.AsError.Value);
		}

		var locate = maybeLocate.AsPlayer;
		var data = parser.ConnectionService.Get(locate.Object.DBRef).ToArray();

		// TODO: CanSee in case of Dark.
		return new CallState(data.Min(x => x.Idle?.TotalSeconds).ToString() ?? "-1");
	}

	[SharpFunction(Name = "IPADDR", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> IpAddress(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "LPORTS", MinArgs = 0, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> ListPorts(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "lwho", MinArgs = 0, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static async ValueTask<CallState> ListWho(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(parser.Mediator);
		var looker = executor;
		await ValueTask.CompletedTask;
		
		if(parser.CurrentState.Arguments.Count > 0)
		{
			// var arg0 = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
			// `-> looker
			// var arg1 = parser.CurrentState.Arguments["1"].Message!.ToPlainText();
			// `-> 'online', 'offline', 'all'
		}

		var allConnectionsDbRefs = parser.ConnectionService
			.GetAll()
			.Where(x => x.Ref is not null)
			.Where(x => true) // TODO: Looker CanSee
			.Select(x => x.Ref!.Value)
			.Select(x => $"#{x.Number}");
		return new CallState(string.Join(" ", allConnectionsDbRefs));
	}

	[SharpFunction(Name = "LWHOID", MinArgs = 0, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> ListWhoObjectIds(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "MWHO", MinArgs = 0, MaxArgs = 0, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> MortalWho(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "MWHOID", MinArgs = 0, MaxArgs = 0, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> MortalWhoObjectIds(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "NMWHO", MinArgs = 0, MaxArgs = 0, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> NumberMortalWho(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "NWHO", MinArgs = 0, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> NumberWho(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "PUEBLO", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> Pueblo(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "RECV", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> Received(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "SENT", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> Sent(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "SSL", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> SecureSocketLayer(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "TERMINFO", MinArgs = 1, MaxArgs = 1,
		Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> TerminalInformation(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "WIDTH", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> Width(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "XMWHOID", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> NumberRangeMortalWhoObjectId(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "XWHO", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> NumberRangeWho(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "XWHOID", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> NumberRangeWhoObjectId(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "ZMWHO", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> ZoneMortalWho(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "ZWHO", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> ZoneWho(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "POLL", MinArgs = 0, MaxArgs = 0, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> Poll(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "PORTS", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> Ports(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "PLAYER", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static async ValueTask<CallState> Player(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(parser.Mediator);
		var portString = parser.CurrentState.Arguments["0"].Message!.ToPlainText()!;

		if (!long.TryParse(portString, out var port))
		{
			return new CallState("#-1 INVALID PORT");
		}
		
		var data = parser.ConnectionService.Get(port);

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

	[SharpFunction(Name = "HEIGHT", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static async ValueTask<CallState> Height(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(parser.Mediator);

		// TODO: Look up the player object
		
		var data = parser.ConnectionService.Get(executor.Object().DBRef).ToArray();
		if (data.Length == 0)
		{
			return new CallState("#-1");
		}

		var mostActive = data.OrderBy(x => x.Idle).First();
		var height = mostActive.Metadata["HEIGHT"];

		return new CallState(height);
	}

	[SharpFunction(Name = "HIDDEN", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> Hidden(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}
}