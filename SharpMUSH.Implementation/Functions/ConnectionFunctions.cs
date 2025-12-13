using System.Globalization;
using SharpMUSH.Implementation.Common;
using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using AsyncEnumerable = System.Linq.AsyncEnumerable;

namespace SharpMUSH.Implementation.Functions;

public partial class Functions
{
	[SharpFunction(Name = "addrlog", MinArgs = 2, MaxArgs = 4, Flags = FunctionFlags.Regular)]
	public static async ValueTask<CallState> AddressLog(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);

		if (!await executor.HasFlag("WIZARD") &&
		    !await executor.HasFlag("ROYALTY") &&
		    !await executor.HasPower("SEE_ALL"))
		{
			return new CallState(Errors.ErrorPerm);
		}

		if (!Configuration!.CurrentValue.Log.UseConnLog)
		{
			return new CallState("#-1");
		}

		var args = parser.CurrentState.Arguments;
		var argCount = args.Count;
		var isCount = false;
		var startArg = 0;

		if (argCount >= 3)
		{
			var firstArg = args["0"].Message!.ToPlainText().ToLower();
			if (firstArg == "count")
			{
				isCount = true;
				startArg = 1;
			}
		}

		var searchType = args[startArg.ToString()].Message!.ToPlainText().ToLower();
		var pattern = args[(startArg + 1).ToString()].Message!.ToPlainText();
		var osep = argCount > startArg + 2 ? args[(startArg + 2).ToString()].Message!.ToPlainText() : "|";

		if (searchType != "ip" && searchType != "hostname")
		{
			return new CallState("#-1 INVALID SEARCH TYPE");
		}

		var logs = Mediator!.CreateStream(new GetConnectionLogsQuery("Connection", 0, 1000));

		var results = new List<string>();
		var uniqueAddresses = new HashSet<string>();

		try
		{
			await foreach (var log in logs)
			{
				var addressValue = searchType switch
				{
					"ip" when log.Properties.TryGetValue("InternetProtocolAddress", out var ip) => ip,
					"hostname" when log.Properties.TryGetValue("HostName", out var host) => host,
					_ => null
				};

				if (addressValue != null
				    && MModule.isWildcardMatch2(MModule.single(addressValue), pattern)
				    && uniqueAddresses.Add(addressValue)
				    && !isCount)
				{
					results.Add(
						$"{log.Properties.GetValueOrDefault("InternetProtocolAddress", "UNKNOWN")} {log.Properties.GetValueOrDefault("HostName", "UNKNOWN")}");
				}
			}
		}
		catch
		{
			// If iteration fails, return partial results
		}

		return new CallState(isCount ? uniqueAddresses.Count.ToString() : string.Join(osep, results));
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
	public static async ValueTask<CallState> ConnectionLog(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		if (!Configuration!.CurrentValue.Log.UseConnLog)
		{
			return new CallState("#-1");
		}

		var args = parser.CurrentState.Arguments;
		var filter = args["0"].Message!.ToPlainText().ToLower();

		if (filter != "all" && filter != "logged in" && filter != "not logged in" && !filter.StartsWith("#"))
		{
			return new CallState("#-1 INVALID FILTER");
		}

		var osep = "|";
		var lastArgIndex = args.Count - 1;

		if (args.Count % 2 == 0)
		{
			osep = args[lastArgIndex.ToString()].Message!.ToPlainText();
			lastArgIndex--;
		}

		var specs = new List<(string type, string value)>();
		for (int i = 1; i <= lastArgIndex; i += 2)
		{
			if (i + 1 > lastArgIndex)
			{
				return new CallState("#-1 INVALID SPEC PAIR");
			}

			var specType = args[i.ToString()].Message!.ToPlainText().ToLower();
			var specValue = args[(i + 1).ToString()].Message!.ToPlainText();

			if (specType != "after" && specType != "before" && specType != "ip" &&
			    specType != "hostname" && specType != "count")
			{
				return new CallState("#-1 INVALID SPEC TYPE");
			}

			specs.Add((specType, specValue));
		}

		var isCount = specs.Any(s => s.type == "count");

		var logs = Mediator!.CreateStream(new GetConnectionLogsQuery("Connection", 0, 1000));

		var results = new List<string>();

		try
		{
			await foreach (var log in logs)
			{
				var matches = true;

				switch (filter)
				{
					case "logged in" when log.Properties.GetValueOrDefault("NewState") != "LoggedIn":
					case "not logged in" when log.Properties.GetValueOrDefault("NewState") == "LoggedIn":
						matches = false;
						break;
					default:
					{
						if (filter.StartsWith("#") && log.Properties.GetValueOrDefault("DBRef") != filter)
						{
							matches = false;
						}

						break;
					}
				}

				foreach (var (type, value) in specs.Where(s => s.type != "count"))
				{
					switch (type)
					{
						case "after" when long.TryParse(value, out var afterTime):
						{
							if (log.Timestamp <= DateTimeOffset.FromUnixTimeSeconds(afterTime).DateTime)
							{
								matches = false;
							}

							break;
						}
						case "before" when long.TryParse(value, out var beforeTime):
						{
							if (log.Timestamp >= DateTimeOffset.FromUnixTimeSeconds(beforeTime).DateTime)
							{
								matches = false;
							}

							break;
						}
						case "ip":
						{
							if (!MModule.isWildcardMatch2(
								    MModule.single(log.Properties.GetValueOrDefault("InternetProtocolAddress", "")),
								    value))
							{
								matches = false;
							}

							break;
						}
						case "hostname" when
							!MModule.isWildcardMatch2(MModule.single(log.Properties.GetValueOrDefault("HostName", "")), value):
							matches = false;
							break;
					}
				}

				switch (matches)
				{
					case true when !isCount:
						results.Add($"{log.Properties.GetValueOrDefault("DBRef", "null")} {log.Key}");
						break;
					case true:
						results.Add(log.Key);
						break;
				}
			}
		}
		catch
		{
			// If iteration fails, return partial results
		}

		return new CallState(isCount ? results.Count.ToString() : string.Join(osep, results));
	}

	[SharpFunction(Name = "connrecord", MinArgs = 1, MaxArgs = 2,
		Flags = FunctionFlags.Regular | FunctionFlags.WizardOnly)]
	public static async ValueTask<CallState> ConnectionRecord(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		if (!Configuration!.CurrentValue.Log.UseConnLog)
		{
			return new CallState("#-1");
		}

		var args = parser.CurrentState.Arguments;
		var connectionId = args["0"].Message!.ToPlainText();
		var osep = (args.TryGetValue("1", out var osepArg) && osepArg?.Message != null)
			? osepArg.Message.ToPlainText()
			: " ";
		if (string.IsNullOrWhiteSpace(connectionId))
		{
			return new CallState("#-1 INVALID CONNECTION ID");
		}

		var logs = Mediator!.CreateStream(new GetConnectionLogsQuery("Connection", 0, 1000));

		try
		{
			await foreach (var log in logs)
			{
				if (log.Key != connectionId)
				{
					continue;
				}

				// Format: DBREF NAME IPADDR HOSTNAME CONNECTION-TIME DISCONNECTION-TIME DISCONNECTION-REASON SSL WEBSOCKET
				var fields = new List<string>
				{
					log.Properties.GetValueOrDefault("DBRef", "#-1"),
					"Unknown", // Name - would need to look up from DBRef
					log.Properties.GetValueOrDefault("InternetProtocolAddress", "UNKNOWN"),
					log.Properties.GetValueOrDefault("HostName", "UNKNOWN"),
					log.Timestamp.ToUnixTimeSeconds().ToString(),
					log.Properties.GetValueOrDefault("DisconnectTime", "0"),
					log.Properties.GetValueOrDefault("DisconnectReason", ""),
					log.Properties.GetValueOrDefault("SSL", "0"),
					log.Properties.GetValueOrDefault("WebSocket", "0")
				};

				return new CallState(string.Join(osep, fields));
			}
		}
		catch
		{
			// If iteration fails, return not found
		}

		return new CallState("#-1 CONNECTION NOT FOUND");
	}

	[SharpFunction(Name = "doing", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static async ValueTask<CallState> Doing(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var arg0 = parser.CurrentState.Arguments["0"].Message!.ToPlainText();

		if (long.TryParse(arg0, out var port))
		{
			var data = ConnectionService!.Get(port);
			if (data is null || data.Ref is null)
			{
				return new CallState(string.Empty);
			}

			var player = await Mediator!.Send(new GetObjectNodeQuery(data.Ref.Value));

			var maybeAttr = await AttributeService!.GetAttributeAsync(
				executor,
				player.Known,
				"DOING",
				mode: IAttributeService.AttributeMode.Read,
				parent: false);

			return maybeAttr switch
			{
				{ IsError: true } or { IsNone: true } => new CallState(string.Empty),
				_ => new CallState(maybeAttr.AsAttribute.Last().Value)
			};
		}

		var maybeLocate = await LocateService!.LocatePlayerAndNotifyIfInvalid(parser, executor, executor, arg0);
		if (maybeLocate.IsNone || maybeLocate.IsError)
		{
			return new CallState(string.Empty);
		}

		var located = maybeLocate.AsPlayer;

		var doingAttr = await AttributeService!.GetAttributeAsync(
			executor,
			located,
			"DOING",
			mode: IAttributeService.AttributeMode.Read,
			parent: false);

		return doingAttr switch
		{
			{ IsError: true } or { IsNone: true } => new CallState(string.Empty),
			_ => new CallState(doingAttr.AsAttribute.Last().Value)
		};
	}

	[SharpFunction(Name = "host", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static async ValueTask<CallState> HostName(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var arg0 = parser.CurrentState.Arguments["0"].Message!.ToPlainText();

		if (long.TryParse(arg0, out var port))
		{
			var data = ConnectionService!.Get(port);
			if (data is null)
			{
				return new CallState("#-1");
			}

			if (!await CanAccessConnectionData(executor, data.Ref))
			{
				return new CallState(Errors.ErrorPerm);
			}

			return new CallState(data.HostName);
		}

		var maybeLocate = await LocateService!.LocatePlayerAndNotifyIfInvalid(parser, executor, executor, arg0);
		if (maybeLocate.IsNone || maybeLocate.IsError)
		{
			return new CallState(maybeLocate.IsNone ? "#-1" : maybeLocate.AsError.Value);
		}

		var located = maybeLocate.AsPlayer;

		if (!await CanAccessConnectionData(executor, located.Object.DBRef))
		{
			return new CallState(Errors.ErrorPerm);
		}

		var connectionData = await ConnectionService!.Get(located.Object.DBRef).FirstOrDefaultAsync();
		if (connectionData is null)
		{
			return new CallState("#-1");
		}

		return new CallState(connectionData.HostName);
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
	public static async ValueTask<CallState> IpAddress(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var arg0 = parser.CurrentState.Arguments["0"].Message!.ToPlainText();

		if (long.TryParse(arg0, out var port))
		{
			var data = ConnectionService!.Get(port);
			if (data is null)
			{
				return new CallState("#-1");
			}

			if (!await CanAccessConnectionData(executor, data.Ref))
			{
				return new CallState(Errors.ErrorPerm);
			}

			return new CallState(data.InternetProtocolAddress);
		}

		var maybeLocate = await LocateService!.LocatePlayerAndNotifyIfInvalid(parser, executor, executor, arg0);
		if (maybeLocate.IsNone || maybeLocate.IsError)
		{
			return new CallState(maybeLocate.IsNone ? "#-1" : maybeLocate.AsError.Value);
		}

		var located = maybeLocate.AsPlayer;

		if (!await CanAccessConnectionData(executor, located.Object.DBRef))
		{
			return new CallState(Errors.ErrorPerm);
		}

		var connectionData = await ConnectionService!.Get(located.Object.DBRef).FirstOrDefaultAsync();
		
		return connectionData is null 
			? new CallState("#-1") 
			: new CallState(connectionData.InternetProtocolAddress);
	}

	[SharpFunction(Name = "lports", MinArgs = 0, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static async ValueTask<CallState> ListPorts(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);

		if (!await executor.HasPower("SEE_ALL"))
		{
			return new CallState(Errors.ErrorPerm);
		}

		var viewer = executor;
		var status = "online"; // default status

		if (args.ContainsKey("0"))
		{
			var arg0 = args["0"].Message!.ToPlainText();
			if (!string.IsNullOrWhiteSpace(arg0))
			{
				var maybeLocate = await LocateService!.LocatePlayerAndNotifyIfInvalid(parser, executor, executor, arg0);
				if (maybeLocate.IsNone || maybeLocate.IsError)
				{
					return new CallState(maybeLocate.IsNone ? "#-1" : maybeLocate.AsError.Value);
				}

				viewer = maybeLocate.AsPlayer;
			}
		}

		if (args.ContainsKey("1"))
		{
			status = args["1"].Message!.ToPlainText().ToLower();
			if (status != "all" && status != "online" && status != "offline")
			{
				return new CallState("#-1 INVALID SECOND ARGUMENT");
			}
		}

		var allConnections = ConnectionService!.GetAll()
			.Where(x => status == "all" ||
			            (status == "online" && x.State == IConnectionService.ConnectionState.LoggedIn) ||
			            (status == "offline" && x.State != IConnectionService.ConnectionState.LoggedIn));

		// Filter connections viewer can see
		var visibleConnections = new List<long>();
		await foreach (var conn in allConnections)
		{
			if (conn.Ref is null)
			{
				if (await viewer.HasPower("SEE_ALL"))
				{
					visibleConnections.Add(conn.Handle);
				}
			}
			else
			{
				var connectedPlayer = await Mediator!.Send(new GetObjectNodeQuery(conn.Ref.Value));
				if (await PermissionService!.CanSee(viewer, connectedPlayer.Known))
				{
					visibleConnections.Add(conn.Handle);
				}
			}
		}

		return new CallState(string.Join(" ", visibleConnections));
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

		// TODO NEEDED: 'Get All Players'.
		var allConnectionsDbRefs = ConnectionService!
			.GetAll()
			.Where(x => x.Ref is not null)
			.Select(async (x, ct) => (await Mediator!.Send(new GetObjectNodeQuery(x.Ref!.Value), ct)).Known)
			.Where(async (x, _) => await PermissionService!.CanSee(looker, x))
			.Select(player => $"#{player.Object().DBRef.Number}");

		return new CallState(string.Join(" ", await allConnectionsDbRefs.ToArrayAsync()));
	}

	[SharpFunction(Name = "lwhoid", MinArgs = 0, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static async ValueTask<CallState> ListWhoObjectIds(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var looker = executor;

		if (args.ContainsKey("0"))
		{
			var arg0 = args["0"].Message!.ToPlainText();
			if (!string.IsNullOrWhiteSpace(arg0))
			{
				var maybeLocate =
					await LocateService!.LocatePlayerAndNotifyIfInvalidWithCallState(parser, executor, executor, arg0);
				if (maybeLocate.IsError)
				{
					return maybeLocate.AsError;
				}

				looker = maybeLocate.AsSharpObject;
			}
		}

		var allConnectionsObjIds = ConnectionService!
			.GetAll()
			.Where(x => x.Ref is not null && x.State == IConnectionService.ConnectionState.LoggedIn)
			.Select(async (x, ct) => (await Mediator!.Send(new GetObjectNodeQuery(x.Ref!.Value), ct)).Known)
			.Where(async (x, _) => await PermissionService!.CanSee(looker, x))
			.Select(x => x.Object().DBRef);

		return new CallState(string.Join(" ", allConnectionsObjIds));
	}

	[SharpFunction(Name = "mwho", MinArgs = 0, MaxArgs = 0, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static async ValueTask<CallState> MortalWho(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		// TODO: Create a "mortal" viewer context - can't see hidden players
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);


		var nonHiddenConnections = ConnectionService!
			.GetAll()
			.Where(x => x.Ref is not null && x.State == IConnectionService.ConnectionState.LoggedIn)
			.Select(async (x, ct) => (await Mediator!.Send(new GetObjectNodeQuery(x.Ref!.Value), ct)).Known)
			.Where(async (x, _) => !await x.HasFlag("DARK"))
			.Select(player => $"#{player.Object().DBRef.Number}");
		
		return new CallState(string.Join(" ", await nonHiddenConnections.ToArrayAsync()));
	}

	[SharpFunction(Name = "mwhoid", MinArgs = 0, MaxArgs = 0, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static async ValueTask<CallState> MortalWhoObjectIds(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var nonHiddenConnectionsObjIds = ConnectionService!
			.GetAll()
			.Where(x => x.Ref is not null && x.State == IConnectionService.ConnectionState.LoggedIn)
			.Select(async (x, ct) => (await Mediator!.Send(new GetObjectNodeQuery(x.Ref!.Value), ct)).Known)
			.Where(async (x, _) => !await x.HasFlag("DARK"))
			.Select(x => x.Object().DBRef);

		return new CallState(string.Join(" ", await nonHiddenConnectionsObjIds.ToArrayAsync()));
	}

	[SharpFunction(Name = "nmwho", MinArgs = 0, MaxArgs = 0, Flags = FunctionFlags.Regular)]
	public static async ValueTask<CallState> NumberMortalWho(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		// Count all connected players that are not hidden
		var count = await ConnectionService!
			.GetAll()
			.Where(x => x.Ref is not null && x.State == IConnectionService.ConnectionState.LoggedIn)
			.Select(async (x, ct) => (await Mediator!.Send(new GetObjectNodeQuery(x.Ref!.Value), ct)).Known)
			.Where(async (x, _) => !await x.HasFlag("DARK"))
			.CountAsync();

		return new CallState(count.ToString(CultureInfo.InvariantCulture));
	}

	[SharpFunction(Name = "nwho", MinArgs = 0, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static async ValueTask<CallState> NumberWho(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var looker = executor;

		if (args.TryGetValue("0", out var arg0))
		{
			var arg0Text = arg0.Message!.ToPlainText();
			if (!string.IsNullOrWhiteSpace(arg0Text))
			{
				var maybeLocate =
					await LocateService!.LocatePlayerAndNotifyIfInvalidWithCallState(parser, executor, executor, arg0Text);

				if (maybeLocate.IsError)
				{
					return maybeLocate.AsError;
				}

				looker = maybeLocate.AsSharpObject;
			}
		}

		var count = await ConnectionService!
			.GetAll()
			.Where(x => x.Ref is not null && x.State == IConnectionService.ConnectionState.LoggedIn)
			.Select(async (x, ct) => (await Mediator!.Send(new GetObjectNodeQuery(x.Ref!.Value), ct)).Known)
			.Where(async (x, _) => await PermissionService!.CanSee(looker, x))
			.CountAsync();

		return new CallState(count.ToString(CultureInfo.InvariantCulture));
	}

	[SharpFunction(Name = "pueblo", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static async ValueTask<CallState> Pueblo(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> await ArgHelpers.ForHandleOrPlayer(parser, Mediator!, ConnectionService!, LocateService!,
			parser.CurrentState.Arguments["0"],
			(_, cd) => ValueTask.FromResult<CallState>(cd.Metadata.GetValueOrDefault("PUEBLO", "0")),
			(_, cd) => ValueTask.FromResult<CallState>(cd.Metadata.GetValueOrDefault("PUEBLO", "0"))
		);

	[SharpFunction(Name = "recv", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static async ValueTask<CallState> Received(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var arg0 = parser.CurrentState.Arguments["0"].Message!.ToPlainText();

		if (long.TryParse(arg0, out var port))
		{
			var data = ConnectionService!.Get(port);
			if (data is null)
			{
				return new CallState("#-1");
			}

			if (!await CanAccessConnectionData(executor, data.Ref))
			{
				return new CallState(Errors.ErrorPerm);
			}

			return new CallState(data.Metadata.GetValueOrDefault("RECV", "0"));
		}

		var maybeLocate = await LocateService!.LocatePlayerAndNotifyIfInvalid(parser, executor, executor, arg0);
		if (maybeLocate.IsNone || maybeLocate.IsError)
		{
			return new CallState(maybeLocate.IsNone ? "#-1" : maybeLocate.AsError.Value);
		}

		var located = maybeLocate.AsPlayer;

		if (!await CanAccessConnectionData(executor, located.Object.DBRef))
		{
			return new CallState(Errors.ErrorPerm);
		}

		var connectionData = await ConnectionService!.Get(located.Object.DBRef).FirstOrDefaultAsync();
		return connectionData is null
			? new CallState("#-1")
			: new CallState(connectionData.Metadata.GetValueOrDefault("RECV", "0"));
	}

	[SharpFunction(Name = "sent", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static async ValueTask<CallState> Sent(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var arg0 = parser.CurrentState.Arguments["0"].Message!.ToPlainText();

		if (long.TryParse(arg0, out var port))
		{
			var data = ConnectionService!.Get(port);
			if (data is null)
			{
				return new CallState("#-1");
			}

			if (!await CanAccessConnectionData(executor, data.Ref))
			{
				return new CallState(Errors.ErrorPerm);
			}

			return new CallState(data.Metadata.GetValueOrDefault("SENT", "0"));
		}

		var maybeLocate = await LocateService!.LocatePlayerAndNotifyIfInvalid(parser, executor, executor, arg0);
		if (maybeLocate.IsNone || maybeLocate.IsError)
		{
			return new CallState(maybeLocate.IsNone ? "#-1" : maybeLocate.AsError.Value);
		}

		var located = maybeLocate.AsPlayer;

		if (!await CanAccessConnectionData(executor, located.Object.DBRef))
		{
			return new CallState(Errors.ErrorPerm);
		}

		var connectionData = await ConnectionService!.Get(located.Object.DBRef).FirstOrDefaultAsync();
		return connectionData is null
			? new CallState("#-1")
			: new CallState(connectionData.Metadata.GetValueOrDefault("SENT", "0"));
	}

	[SharpFunction(Name = "ssl", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static async ValueTask<CallState> SecureSocketLayer(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var arg0 = parser.CurrentState.Arguments["0"].Message!.ToPlainText();

		if (long.TryParse(arg0, out var port))
		{
			var data = ConnectionService!.Get(port);
			if (data is null)
			{
				return new CallState("0");
			}

			if (data.Ref != executor.Object().DBRef)
			{
				if (!await executor.HasPower("SEE_ALL"))
				{
					return new CallState(Errors.ErrorPerm);
				}
			}

			var ssl = data.Metadata.GetValueOrDefault("SSL", "0");
			return new CallState(ssl);
		}

		var maybeLocate = await LocateService!.LocatePlayerAndNotifyIfInvalid(parser, executor, executor, arg0);
		if (maybeLocate.IsNone || maybeLocate.IsError)
		{
			return new CallState(maybeLocate.IsNone ? "0" : maybeLocate.AsError.Value);
		}

		var located = maybeLocate.AsPlayer;

		if (located.Object.DBRef != executor.Object().DBRef && !await executor.HasPower("SEE_ALL"))
		{
			return new CallState(Errors.ErrorPerm);
		}

		var connectionData = await ConnectionService!.Get(located.Object.DBRef).FirstOrDefaultAsync();
		return connectionData is null
			? new CallState("0")
			: new CallState(connectionData.Metadata.GetValueOrDefault("SSL", "0"));
	}

	[SharpFunction(Name = "terminfo", MinArgs = 1, MaxArgs = 1,
		Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static async ValueTask<CallState> TerminalInformation(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var arg0 = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var hasSeeAll = await executor.HasPower("SEE_ALL");

		if (long.TryParse(arg0, out var port))
		{
			var data = ConnectionService!.Get(port);
			if (data is null)
			{
				return new CallState("unknown");
			}

			var isSelf = data.Ref == executor.Object().DBRef;

			if (!hasSeeAll && !isSelf)
			{
				return new CallState("unknown");
			}

			return new CallState(BuildTermInfo(data.Metadata, hasSeeAll || isSelf));
		}

		var maybeLocate = await LocateService!.LocatePlayerAndNotifyIfInvalid(parser, executor, executor, arg0);
		if (maybeLocate.IsNone || maybeLocate.IsError)
		{
			return new CallState("unknown");
		}

		var located = maybeLocate.AsPlayer;
		var isSelfPlayer = located.Object.DBRef == executor.Object().DBRef;

		if (!hasSeeAll && !isSelfPlayer)
		{
			return new CallState("unknown");
		}

		var connectionData = await ConnectionService!.Get(located.Object.DBRef).FirstOrDefaultAsync();
		return connectionData is null
			? new CallState("unknown")
			: new CallState(BuildTermInfo(connectionData.Metadata, hasSeeAll || isSelfPlayer));
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
	public static async ValueTask<CallState> NumberRangeMortalWhoObjectId(IMUSHCodeParser parser,
		SharpFunctionAttribute _2)
	{
		var arg0 = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var arg1 = parser.CurrentState.Arguments["1"].Message!.ToPlainText();

		if (!int.TryParse(arg0, out var start) || !int.TryParse(arg1, out var count))
		{
			return new CallState(Errors.ErrorIntegers);
		}

		if (start < 1 || count < 0)
		{
			return new CallState(Errors.ErrorArgRange);
		}

		var allObjIds = ConnectionService!
			.GetAll()
			.Where(x => x.Ref is not null && x.State == IConnectionService.ConnectionState.LoggedIn)
			.Select(async (x, ct) => (await Mediator!.Send(new GetObjectNodeQuery(x.Ref!.Value), ct)).Known)
			.Where(async (x, _) => !await x.HasFlag("DARK"))
			.Select(x => x.Object().DBRef);

		var result = allObjIds.Skip(start - 1).Take(count);
		return new CallState(string.Join(" ", await result.ToArrayAsync()));
	}

	[SharpFunction(Name = "xwho", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static async ValueTask<CallState> NumberRangeWho(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var looker = executor;

		int start, count;

		if (args.Count == 3)
		{
			// xwho(<looker>, <start>, <count>)
			var arg0 = args["0"].Message!.ToPlainText();
			if (!string.IsNullOrWhiteSpace(arg0))
			{
				var maybeLocate =
					await LocateService!.LocatePlayerAndNotifyIfInvalidWithCallState(parser, executor, executor, arg0);
				if (maybeLocate.IsError)
				{
					return maybeLocate.AsError;
				}

				looker = maybeLocate.AsSharpObject;
			}

			if (!int.TryParse(args["1"].Message!.ToPlainText(), out start) ||
			    !int.TryParse(args["2"].Message!.ToPlainText(), out count))
			{
				return new CallState(Errors.ErrorIntegers);
			}
		}
		else
		{
			// xwho(<start>, <count>)
			if (!int.TryParse(args["0"].Message!.ToPlainText(), out start) ||
			    !int.TryParse(args["1"].Message!.ToPlainText(), out count))
			{
				return new CallState(Errors.ErrorIntegers);
			}
		}

		if (start < 1 || count < 0)
		{
			return new CallState(Errors.ErrorArgRange);
		}

		var allDbrefs = ConnectionService!
			.GetAll()
			.Where(x => x.Ref is not null && x.State == IConnectionService.ConnectionState.LoggedIn)
			.Select(async (x, ct) => (await Mediator!.Send(new GetObjectNodeQuery(x.Ref!.Value), ct)).Known)
			.Where(async (x, _) => await PermissionService!.CanSee(looker, x))
			.Select(x => $"#{x.Object().DBRef.Number}");

		var result = allDbrefs.Skip(start - 1).Take(count);
		return new CallState(string.Join(" ", await result.ToArrayAsync()));
	}

	[SharpFunction(Name = "xwhoid", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static async ValueTask<CallState> NumberRangeWhoObjectId(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var looker = executor;

		int start, count;

		if (args.Count == 3)
		{
			// xwhoid(<looker>, <start>, <count>)
			var arg0 = args["0"].Message!.ToPlainText();
			if (!string.IsNullOrWhiteSpace(arg0))
			{
				var maybeLocate =
					await LocateService!.LocatePlayerAndNotifyIfInvalidWithCallState(parser, executor, executor, arg0);
				if (maybeLocate.IsError)
				{
					return maybeLocate.AsError;
				}

				looker = maybeLocate.AsSharpObject;
			}

			if (!int.TryParse(args["1"].Message!.ToPlainText(), out start) ||
			    !int.TryParse(args["2"].Message!.ToPlainText(), out count))
			{
				return new CallState(Errors.ErrorIntegers);
			}
		}
		else
		{
			// xwhoid(<start>, <count>)
			if (!int.TryParse(args["0"].Message!.ToPlainText(), out start) ||
			    !int.TryParse(args["1"].Message!.ToPlainText(), out count))
			{
				return new CallState(Errors.ErrorIntegers);
			}
		}

		if (start < 1 || count < 0)
		{
			return new CallState(Errors.ErrorArgRange);
		}

		var allObjIds = ConnectionService!
			.GetAll()
			.Where(x => x.Ref is not null && x.State == IConnectionService.ConnectionState.LoggedIn)
			.Select(async (x, ct) => (await Mediator!.Send(new GetObjectNodeQuery(x.Ref!.Value), ct)).Known)
			.Where(async (x, _) => await PermissionService!.CanSee(looker, x))
			.Select(x => x.Object().DBRef);

		var result = allObjIds.Skip(start - 1).Take(count);
		return new CallState(string.Join(" ", await result.ToArrayAsync()));
	}

	[SharpFunction(Name = "zmwho", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static async ValueTask<CallState> ZoneMortalWho(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var arg0 = parser.CurrentState.Arguments["0"].Message!.ToPlainText();

		var maybeZone = await LocateService!.LocateAndNotifyIfInvalid(parser, executor, executor, arg0, LocateFlags.All);
		if (maybeZone.IsNone || maybeZone.IsError)
		{
			return new CallState(maybeZone.IsNone ? "#-1" : maybeZone.AsError.Value);
		}

		var zone = maybeZone.AsAnyObject;

		var hasSeeAll = await executor.HasPower("SEE_ALL");
		if (!hasSeeAll)
		{
			// Check if executor passes the zone lock
			if (!LockService!.Evaluate(LockType.Zone, zone, executor))
			{
				return new CallState(Errors.ErrorPerm);
			}
		}

		// Get all players in rooms that are in this zone (excluding dark/hidden players)
		var playersInZone = new List<string>();
		var allPlayers = Mediator!.CreateStream(new GetAllPlayersQuery())!;
		
		await foreach (var player in allPlayers)
		{
			// Skip dark/hidden players unless executor has SEE_ALL
			if (!hasSeeAll)
			{
				AnySharpObject playerObj = player;
				var isDark = await playerObj.HasFlag("DARK");
				if (isDark)
				{
					continue;
				}
			}
			
			// Get player's location
			var playerLocation = await player.Location.WithCancellation(CancellationToken.None);
			
			// Check if the location's zone matches
			var locationObj = playerLocation.WithExitOption();
			var locationZone = await locationObj.Object().Zone.WithCancellation(CancellationToken.None);
			
			if (!locationZone.IsNone)
			{
				if (locationZone.Known.Object().DBRef.Number == zone.Object().DBRef.Number)
				{
					playersInZone.Add(player.Object.DBRef.ToString());
				}
			}
		}

		// Default: space-separated list
		return new CallState(string.Join(" ", playersInZone));
	}

	[SharpFunction(Name = "zwho", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static async ValueTask<CallState> ZoneWho(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var arg0 = args["0"].Message!.ToPlainText();

		var maybeZone = await LocateService!.LocateAndNotifyIfInvalid(parser, executor, executor, arg0, LocateFlags.All);
		if (maybeZone.IsNone || maybeZone.IsError)
		{
			return new CallState(maybeZone.IsNone ? "#-1" : maybeZone.AsError.Value);
		}

		var zone = maybeZone.AsAnyObject;

		var hasSeeAll = await executor.HasPower("SEE_ALL");
		if (!hasSeeAll)
		{
			// Check if executor passes the zone lock
			if (!LockService!.Evaluate(LockType.Zone, zone, executor))
			{
				return new CallState(Errors.ErrorPerm);
			}
		}

		// Get all players in rooms that are in this zone
		var playersInZone = new List<string>();
		var allPlayers = Mediator!.CreateStream(new GetAllPlayersQuery())!;
		
		await foreach (var player in allPlayers)
		{
			// Get player's location
			var playerLocation = await player.Location.WithCancellation(CancellationToken.None);
			
			// Check if the location's zone matches
			var locationObj = playerLocation.WithExitOption();
			var locationZone = await locationObj.Object().Zone.WithCancellation(CancellationToken.None);
			
			if (!locationZone.IsNone)
			{
				if (locationZone.Known.Object().DBRef.Number == zone.Object().DBRef.Number)
				{
					playersInZone.Add(player.Object.DBRef.ToString());
				}
			}
		}

		// Handle output format parameter if provided
		if (args.TryGetValue("1", out var arg1Value))
		{
			var format = arg1Value.Message!.ToPlainText();
			if (!string.IsNullOrWhiteSpace(format))
			{
				// Format output with specified delimiter
				return new CallState(string.Join(format, playersInZone));
			}
		}

		// Default: space-separated list
		return new CallState(string.Join(" ", playersInZone));
	}

	[SharpFunction(Name = "poll", MinArgs = 0, MaxArgs = 0, Flags = FunctionFlags.Regular)]
	public static async ValueTask<CallState> Poll(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		await ValueTask.CompletedTask;
		// TODO: Get the current @poll value from configuration or game state
		// For now, return a default empty value since @poll infrastructure isn't implemented
		return new CallState(string.Empty);
	}

	[SharpFunction(Name = "ports", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static async ValueTask<CallState> Ports(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var arg0 = parser.CurrentState.Arguments["0"].Message!.ToPlainText();

		var maybeLocate = await LocateService!.LocatePlayerAndNotifyIfInvalid(parser, executor, executor, arg0);
		if (maybeLocate.IsNone || maybeLocate.IsError)
		{
			return new CallState(maybeLocate.IsNone ? "#-1" : maybeLocate.AsError.Value);
		}

		var target = maybeLocate.AsPlayer;

		if (target.Object.DBRef != executor.Object().DBRef)
		{
			if (!await executor.HasPower("SEE_ALL"))
			{
				return new CallState(Errors.ErrorPerm);
			}
		}

		var handles = ConnectionService!
			.Get(target.Object.DBRef)
			.Select(x => x.Handle);

		return new CallState(string.Join(" ", await handles.ToArrayAsync()));
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

		if (!isHandle)
		{
			return await LocateService!.LocatePlayerAndNotifyIfInvalidWithCallStateFunction(parser, executor, executor,
				playerOrDescriptor,
				async found =>
				{
					var fod = await ConnectionService!.Get(found.Object.DBRef).FirstOrDefaultAsync();
					return fod?.Metadata["HEIGHT"] ?? defaultArg.ToPlainText();
				});
		}

		var data = ConnectionService!.Get(port);
		if (data is null)
		{
			return new CallState("#-1");
		}

		return data.Metadata.TryGetValue("HEIGHT", out var height)
			? height
			: defaultArg;
	}

	[SharpFunction(Name = "hidden", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static async ValueTask<CallState> Hidden(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var arg0 = parser.CurrentState.Arguments["0"].Message!.ToPlainText();

		var canSeeHidden = await executor.HasFlag("WIZARD") || await executor.HasFlag("ROYALTY") ||
		                   await executor.HasPower("SEE_ALL");

		if (!canSeeHidden)
		{
			return new CallState("#-1");
		}

		if (long.TryParse(arg0, out var port))
		{
			var data = ConnectionService!.Get(port);
			if (data is null || data.Ref is null)
			{
				return new CallState("#-1");
			}

			var player = await Mediator!.Send(new GetObjectNodeQuery(data.Ref.Value));
			var isHidden = await player.Known.HasFlag("DARK");
			return new CallState(isHidden ? "1" : "0");
		}

		var maybeLocate = await LocateService!.LocatePlayerAndNotifyIfInvalid(parser, executor, executor, arg0);
		if (maybeLocate.IsNone || maybeLocate.IsError)
		{
			return new CallState("#-1");
		}

		var located = maybeLocate.AsPlayer;
		var isHiddenPlayer = await new AnySharpObject(located).HasFlag("DARK");
		return new CallState(isHiddenPlayer ? "1" : "0");
	}

	/// <summary>
	/// Checks if the executor has permission to access connection data for another player.
	/// </summary>
	private static async ValueTask<bool> CanAccessConnectionData(AnySharpObject executor, DBRef? targetDbRef)
	{
		if (targetDbRef == executor.Object().DBRef)
		{
			return true;
		}

		return await executor.HasFlag("WIZARD") ||
		       await executor.HasFlag("ROYALTY") ||
		       await executor.HasPower("SEE_ALL");
	}

	/// <summary>
	/// Builds terminal information string from connection metadata.
	/// </summary>
	private static string BuildTermInfo(IReadOnlyDictionary<string, string> metadata, bool includeDetails)
	{
		var terminfo = new List<string>
		{
			metadata.GetValueOrDefault("CLIENT", "unknown")
		};

		if (!includeDetails)
		{
			return string.Join(" ", terminfo);
		}

		if (metadata.GetValueOrDefault("PUEBLO", "0") == "1")
		{
			terminfo.Add("pueblo");
		}

		if (metadata.GetValueOrDefault("TELNET", "0") == "1")
		{
			terminfo.Add("telnet");
		}

		if (metadata.GetValueOrDefault("GMCP", "0") == "1")
		{
			terminfo.Add("gmcp");
		}

		if (metadata.GetValueOrDefault("SSL", "0") == "1")
		{
			terminfo.Add("ssl");
		}

		if (metadata.GetValueOrDefault("PROMPT_NEWLINES", "0") == "1")
		{
			terminfo.Add("prompt_newlines");
		}

		if (metadata.GetValueOrDefault("STRIPACCENTS", "0") == "1")
		{
			terminfo.Add("stripaccents");
		}

		var colorStyle = metadata.GetValueOrDefault("COLORSTYLE", "");
		if (!string.IsNullOrEmpty(colorStyle))
		{
			terminfo.Add(colorStyle);
		}

		return string.Join(" ", terminfo);
	}
}