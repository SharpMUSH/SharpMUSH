using SharpMUSH.Implementation.Common;
using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ExpandedObjectData;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.Time;
using SharpMUSH.Library.Utilities;
using System.Globalization;
using System.Runtime.CompilerServices;
using SharpMUSH.Library.Markup;

namespace SharpMUSH.Implementation.Functions;

public partial class Functions
{
	[SharpFunction(Name = "addrlog", MinArgs = 2, MaxArgs = 4, Flags = FunctionFlags.Regular, ParameterNames = ["object"])]
	public async ValueTask<CallState> AddressLog(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);

		if (!await executor.IsWizard() &&
				!await executor.IsRoyalty() &&
				!await executor.IsSee_All())
		{
			return new CallState(ErrorMessages.Returns.PermissionDenied);
		}

		if (!Configuration.CurrentValue.Log.UseConnLog)
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
			return new CallState(ErrorMessages.Returns.InvalidSearchType);
		}

		var logs = Mediator.CreateStream(new GetConnectionLogsQuery("Connection", 0, 1000));

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
						&& MushText.IsWildcardMatch(MarkupText.Plain(addressValue), pattern)
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
		}

		return new CallState(isCount ? uniqueAddresses.Count.ToString() : string.Join(osep, results));
	}

	/// <summary>
	/// PennMUSH <c>fun_cmds</c> (src/bsd.c): the command count off the target's descriptor, and "-1"
	/// for every failure — no descriptor, or one the caller may not read. It carries no restriction of
	/// its own, because a player asking about their own connection is allowed and the See_All check
	/// inside covers everyone else; declaring it admin-only refused that reading outright.
	/// <para>
	/// The count lives under "CommandCount", which is the key
	/// <see cref="SharpMUSHParserVisitor"/> increments and <c>WHO</c> reports. Reading a "CMDS" key
	/// nothing writes — through an indexer, so a miss threw rather than returned — meant this
	/// function could not answer at all.
	/// </para>
	/// </summary>
	[SharpFunction(Name = "cmds", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		ParameterNames = ["object"])]
	public async ValueTask<CallState> Commands(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var arg0 = parser.CurrentState.Arguments["0"].Message!.ToPlainText();

		IConnectionService.ConnectionData? connection;

		if (long.TryParse(arg0, out var port))
		{
			connection = ConnectionService.Get(port);
		}
		else
		{
			var maybeLocate = await LocateService.LocateConnectionTarget(parser, executor, executor, arg0);
			connection = maybeLocate is AnySharpObject and SharpPlayer player
				? await LeastIdleConnectionAsync(player.Object.DBRef)
				: null;
		}

		if (connection is null || !await CanAccessConnectionData(executor, connection.Ref))
		{
			return new CallState("-1");
		}

		return new CallState(connection.CommandCount.ToString(CultureInfo.InvariantCulture));
	}

	[SharpFunction(Name = "conn", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public async ValueTask<CallState> ConnectedSeconds(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var arg0 = parser.CurrentState.Arguments["0"].Message!.ToPlainText();

		if (long.TryParse(arg0, out var port))
		{
			var data2 = ConnectionService.Get(port);
			if (data2 is null || data2.Ref is null)
			{
				return new CallState("-1");
			}

			if (await Mediator.Send(new GetObjectNodeQuery(data2.Ref.Value)) is not AnySharpObject connectedPlayer
					|| !await PermissionService.CanSee(executor, connectedPlayer))
			{
				return new CallState("-1");
			}

			return new CallState(data2.Connected?.TotalSeconds.ToString(CultureInfo.InvariantCulture) ?? "-1");
		}

		// fun_conn answers every failure with "-1" — a name that matches nothing, a match that is not
		// connected, and a descriptor the caller may not see are one outcome, not three.
		var maybeLocate = await LocateService.LocateConnectionTarget(parser, executor, executor, arg0);
		if (maybeLocate is not (AnySharpObject and SharpPlayer located))
		{
			return new CallState("-1");
		}

		if (!await PermissionService.CanSee(executor, located.Object))
		{
			return new CallState("-1");
		}

		var data = await LeastIdleConnectionAsync(located.Object.DBRef);
		return new CallState(data?.Connected?.TotalSeconds.ToString(CultureInfo.InvariantCulture) ?? "-1");
	}

	[SharpFunction(Name = "connlog", MinArgs = 3, MaxArgs = int.MaxValue,
		Flags = FunctionFlags.Regular | FunctionFlags.WizardOnly, ParameterNames = ["object"])]
	public async ValueTask<CallState> ConnectionLog(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		if (!Configuration.CurrentValue.Log.UseConnLog)
		{
			return new CallState("#-1");
		}

		var args = parser.CurrentState.Arguments;
		var filter = args["0"].Message!.ToPlainText().ToLower();

		if (filter != "all" && filter != "logged in" && filter != "not logged in" && !filter.StartsWith("#"))
		{
			return new CallState(ErrorMessages.Returns.InvalidFilter);
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
				return new CallState(ErrorMessages.Returns.InvalidSpecPair);
			}

			var specType = args[i.ToString()].Message!.ToPlainText().ToLower();
			var specValue = args[(i + 1).ToString()].Message!.ToPlainText();

			if (specType != "after" && specType != "before" && specType != "ip" &&
					specType != "hostname" && specType != "count")
			{
				return new CallState(ErrorMessages.Returns.InvalidSpecType);
			}

			specs.Add((specType, specValue));
		}

		var isCount = specs.Any(s => s.type == "count");

		var logs = Mediator.CreateStream(new GetConnectionLogsQuery("Connection", 0, 1000));

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
								if (!MushText.IsWildcardMatch(MarkupText.Plain(log.Properties.GetValueOrDefault("InternetProtocolAddress", "")), value))
								{
									matches = false;
								}

								break;
							}
						case "hostname" when
							!MushText.IsWildcardMatch(MarkupText.Plain(log.Properties.GetValueOrDefault("HostName", "")), value):
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
		}

		return new CallState(isCount ? results.Count.ToString() : string.Join(osep, results));
	}

	[SharpFunction(Name = "connrecord", MinArgs = 1, MaxArgs = 2,
		Flags = FunctionFlags.Regular | FunctionFlags.WizardOnly, ParameterNames = ["object"])]
	public async ValueTask<CallState> ConnectionRecord(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		if (!Configuration.CurrentValue.Log.UseConnLog)
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
			return new CallState(ErrorMessages.Returns.InvalidConnectionId);
		}

		var logs = Mediator.CreateStream(new GetConnectionLogsQuery("Connection", 0, 1000));

		try
		{
			await foreach (var log in logs)
			{
				if (log.Key != connectionId)
				{
					continue;
				}

				var fields = new List<string>
				{
					log.Properties.GetValueOrDefault("DBRef", "#-1"),
					"Unknown",
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
		}

		return new CallState(ErrorMessages.Returns.ConnectionNotFound);
	}

	[SharpFunction(Name = "doing", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public async ValueTask<CallState> Doing(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var arg0 = parser.CurrentState.Arguments["0"].Message!.ToPlainText();

		if (long.TryParse(arg0, out var port))
		{
			var data = ConnectionService.Get(port);
			if (data is null || data.Ref is null)
			{
				return new CallState(string.Empty);
			}

			if (await Mediator.Send(new GetObjectNodeQuery(data.Ref.Value)) is not AnySharpObject player)
			{
				return new CallState(string.Empty);
			}

			var maybeAttr = await AttributeService.GetAttributeAsync(
				executor,
				player,
				"DOING",
				mode: IAttributeService.AttributeMode.Read,
				parent: false);

			return maybeAttr switch
			{
				SharpAttribute[] chain => new CallState(chain.Last().Value),
				None or Error<string> => new CallState(string.Empty)
			};
		}

		var maybeLocate = await LocateService.LocateConnectionTarget(parser, executor, executor, arg0);
		if (maybeLocate is not (AnySharpObject and SharpPlayer located))
		{
			return new CallState(string.Empty);
		}

		var doingAttr = await AttributeService.GetAttributeAsync(
			executor,
			located,
			"DOING",
			mode: IAttributeService.AttributeMode.Read,
			parent: false);

		return doingAttr switch
		{
			SharpAttribute[] chain => new CallState(chain.Last().Value),
			None or Error<string> => new CallState(string.Empty)
		};
	}

	[SharpFunction(Name = "host", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public async ValueTask<CallState> HostName(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var arg0 = parser.CurrentState.Arguments["0"].Message!.ToPlainText();

		if (long.TryParse(arg0, out var port))
		{
			var data = ConnectionService.Get(port);
			if (data is null)
			{
				return new CallState("#-1");
			}

			// fun_hostname folds "no descriptor" and "not yours to see" into one "#-1": whether the
			// descriptor exists is itself the thing a caller without See_All must not learn.
			if (!await CanAccessConnectionData(executor, data.Ref))
			{
				return new CallState("#-1");
			}

			return new CallState(data.HostName);
		}

		var maybeLocate = await LocateService.LocateConnectionTarget(parser, executor, executor, arg0);
		if (maybeLocate is not (AnySharpObject and SharpPlayer located))
		{
			return new CallState(maybeLocate is Error<string> error ? error.Value : "#-1");
		}

		if (!await CanAccessConnectionData(executor, located.Object.DBRef))
		{
			return new CallState("#-1");
		}

		var connectionData = await LeastIdleConnectionAsync(located.Object.DBRef);
		if (connectionData is null)
		{
			return new CallState("#-1");
		}

		return new CallState(connectionData.HostName);
	}

	/// <summary>
	/// Renders an idle duration. A player who is not connected, or whom the executor cannot see, is
	/// -1 — PennMUSH's sentinel, and not a duration, so precision does not apply to it.
	/// </summary>
	private static string FormatIdle(TimeSpan? idle, TimePrecision precision)
		=> idle is null
			? "-1"
			: TimePrecisions.Format((long)idle.Value.TotalMilliseconds, precision);

	/// <remarks>
	/// PennMUSH's idle() is a whole number of seconds, and idlesecs() is an alias for it, so the two
	/// must render the same value.
	/// </remarks>
	[SharpFunction(Name = "idle", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		ParameterNames = ["object", "precision"])]
	public async ValueTask<CallState> IdleSeconds(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		if (!TimePrecisions.TryParse(
					parser.CurrentState.Arguments.TryGetValue("1", out var precisionArg)
						? precisionArg.Message?.ToPlainText()
						: null,
					out var precision))
		{
			return new CallState(ErrorMessages.Returns.InvalidPrecision);
		}

		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var arg0 = parser.CurrentState.Arguments["0"].Message!.ToPlainText();

		if (long.TryParse(arg0, out var port))
		{
			var data2 = ConnectionService.Get(port);
			if (data2 is null || data2.Ref is null)
			{
				return new CallState("-1");
			}

			if (await Mediator.Send(new GetObjectNodeQuery(data2.Ref.Value)) is not AnySharpObject connectedPlayer
					|| !await PermissionService.CanSee(executor, connectedPlayer))
			{
				return new CallState("-1");
			}

			return new CallState(FormatIdle(data2.Idle, precision));
		}

		var maybeLocate = await LocateService.LocateConnectionTarget(parser, executor, executor, arg0);
		if (maybeLocate is not (AnySharpObject and SharpPlayer locate))
		{
			return new CallState(maybeLocate is Error<string> error ? error.Value : "-1");
		}

		if (!await PermissionService.CanSee(executor, locate.Object))
		{
			return new CallState("-1");
		}

		var connectionData = await LeastIdleConnectionAsync(locate.Object.DBRef);
		return new CallState(FormatIdle(connectionData?.Idle, precision));
	}

	[SharpFunction(Name = "ipaddr", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public async ValueTask<CallState> IpAddress(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var arg0 = parser.CurrentState.Arguments["0"].Message!.ToPlainText();

		if (long.TryParse(arg0, out var port))
		{
			var data = ConnectionService.Get(port);
			if (data is null)
			{
				return new CallState("#-1");
			}

			// Same "#-1" for both failures as fun_hostname, and for the same reason.
			if (!await CanAccessConnectionData(executor, data.Ref))
			{
				return new CallState("#-1");
			}

			return new CallState(data.InternetProtocolAddress);
		}

		var maybeLocate = await LocateService.LocateConnectionTarget(parser, executor, executor, arg0);
		if (maybeLocate is not (AnySharpObject and SharpPlayer located))
		{
			return new CallState(maybeLocate is Error<string> error ? error.Value : "#-1");
		}

		if (!await CanAccessConnectionData(executor, located.Object.DBRef))
		{
			return new CallState("#-1");
		}

		var connectionData = await LeastIdleConnectionAsync(located.Object.DBRef);

		return connectionData is null
			? new CallState("#-1")
			: new CallState(connectionData.InternetProtocolAddress);
	}

	[SharpFunction(Name = "lports", MinArgs = 0, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public async ValueTask<CallState> ListPorts(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);

		if (!await executor.IsSee_All())
		{
			return new CallState(ErrorMessages.Returns.PermissionDenied);
		}

		var viewer = executor;
		var status = "online";

		if (args.ContainsKey("0"))
		{
			var arg0 = args["0"].Message!.ToPlainText();
			if (!string.IsNullOrWhiteSpace(arg0))
			{
				var maybeLocate = await LocateService.LocatePlayerAndNotifyIfInvalid(parser, executor, executor, arg0);
				if (maybeLocate is not (AnySharpObject and SharpPlayer located))
				{
					return new CallState(maybeLocate is Error<string> error ? error.Value : "#-1");
				}

				viewer = located;
			}
		}

		if (args.ContainsKey("1"))
		{
			status = args["1"].Message!.ToPlainText().ToLower();
			if (status != "all" && status != "online" && status != "offline")
			{
				return new CallState(ErrorMessages.Returns.InvalidSecondArgument);
			}
		}

		var viewerSeesAll = await viewer.IsSee_All();

		var visibleConnections = ConnectionService.GetAll()
			.Where(x => status == "all" ||
									(status == "online" && x.State == IConnectionService.ConnectionState.LoggedIn) ||
									(status == "offline" && x.State != IConnectionService.ConnectionState.LoggedIn))
			.Where(async (conn, _) => conn.Ref is null
				? viewerSeesAll
				: await Mediator.Send(new GetObjectNodeQuery(conn.Ref.Value)) is AnySharpObject connected
					&& await PermissionService.CanSee(viewer, connected))
			.Select(conn => conn.Handle);

		return new CallState(string.Join(" ", await visibleConnections.ToArrayAsync()));
	}

	[SharpFunction(Name = "lwho", MinArgs = 0, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["flag"])]
	public async ValueTask<CallState> ListWho(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> await ListWhoCore(parser, objectIds: false);

	[SharpFunction(Name = "lwhoid", MinArgs = 0, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["flags"])]
	public async ValueTask<CallState> ListWhoObjectIds(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> await ListWhoCore(parser, objectIds: true);

	/// <summary>
	/// lwho() and lwhoid(), which differ only in whether each entry is a bare <c>#N</c> or a full
	/// objid - PennMUSH runs both through one fun_lwho, keyed on <c>strchr(called_as, 'D')</c>
	/// (bsd.c:6528).
	/// </summary>
	private async ValueTask<CallState> ListWhoCore(IMUSHCodeParser parser, bool objectIds)
	{
		var args = parser.CurrentState.Arguments;
		// Arguments["0"] is always present (DefaultIfEmpty(CallState.Empty)) even for 0-arg calls, so it
		// arrives here as an empty string rather than as an absent key; ResolveWhoLookerAsync treats
		// blank as "no viewer named", which is the same thing.
		var arg0Raw = args.TryGetValue("0", out var arg0) ? arg0.Message!.ToPlainText() : null;
		// Same for the status argument: PennMUSH's `if (nargs > 1 && args[1] && *args[1])` (bsd.c:6548)
		// treats an explicitly empty <status> as absent, so lwho(<viewer>,) means the "online" default
		// rather than "#-1 INVALID SECOND ARGUMENT".
		var arg1Raw = args.TryGetValue("1", out var arg1Value) ? arg1Value.Message!.ToPlainText() : null;
		var arg1 = string.IsNullOrEmpty(arg1Raw)
			? ["online"]
			: arg1Raw.ToLower().Split(" ");

		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var (looker, powered, lookerError) = await ResolveWhoLookerAsync(parser, executor, arg0Raw);
		if (lookerError is not null)
		{
			return lookerError;
		}

		if (arg1.Length > 1)
		{
			return ErrorMessages.Returns.InvalidSecondArgument;
		}

		var status = arg1[0];
		if (status is not ("online" or "offline" or "all"))
		{
			return ErrorMessages.Returns.InvalidSecondArgument;
		}

		// PennMUSH bsd.c:6561: only a powered caller may ask about anything but the online list.
		if (status != "online" && !powered)
		{
			return new CallState(ErrorMessages.Returns.PermissionDenied);
		}

		if (status == "online")
		{
			// The online list is exactly what the rest of the caller-scoped family already computes,
			// @hide gate included - lwho() and lwhoid() were the two members of it that still filtered
			// on the DARK flag alone, so a @hide'd (but undarkened) player stayed listed.
			var online = VisibleWhoPlayers(looker, powered)
				.Select(x => objectIds ? x.Object().DBRef.ToString() : $"#{x.Object().DBRef.Number}");

			return new CallState(string.Join(" ", await online.ToArrayAsync()));
		}

		// "offline"/"all" reach the player table rather than the connection list, and are powered-only
		// (checked above), so no per-connection Hidden gate applies to them.
		//
		// Keyed on DBRef.Number, not on the whole DBRef: the bound reference is a bare #N on some login
		// paths and a full #N:creation objid on others, and those two compare unequal - so a player
		// bound the objid way counted as not connected, and "offline" listed them (see the same note on
		// MortalWhoPlayers).
		var connectedDbRefs = await ConnectionService
			.GetAll()
			.Where(x => x.Ref is not null)
			.Select(x => x.Ref!.Value.Number)
			.ToHashSetAsync();

		var result = Mediator.CreateStream(new GetAllPlayersQuery())
			.Where(player => status != "offline" || !connectedDbRefs.Contains(player.Object.DBRef.Number))
			.Where(async (player, _) => await PermissionService.CanSee(looker, player))
			.Select(player => objectIds ? player.Object.DBRef.ToString() : $"#{player.Object.DBRef.Number}");

		return new CallState(string.Join(" ", await result.ToArrayAsync()));
	}

	/// <summary>
	/// PennMUSH's shared victim/privilege resolution for the caller-scoped WHO family (fun_lwho
	/// bsd.c:6532-6546, fun_nwho :6494-6506, fun_xwho :6438-6450): the optional first argument names
	/// the <em>viewer</em> whose visibility the answer is computed for, an unprivileged caller may
	/// only name itself, and the answer is then capped at the lower of the caller's and the viewer's
	/// Priv_Who.
	/// </summary>
	/// <remarks>
	/// The "may only name itself" half is what stops <c>lwho(&lt;wizard&gt;)</c> / <c>nwho(&lt;wizard&gt;)</c>
	/// from handing a mortal the privileged WHO list - @hide'd connections included - simply by naming
	/// someone privileged as the viewer.
	/// </remarks>
	private async ValueTask<(AnySharpObject Looker, bool Powered, CallState? Error)> ResolveWhoLookerAsync(
		IMUSHCodeParser parser, AnySharpObject executor, string? arg0Text)
	{
		var powered = await executor.IsSee_All();

		if (string.IsNullOrWhiteSpace(arg0Text))
		{
			return (executor, powered, null);
		}

		var maybeLocate =
			await LocateService.LocatePlayerAndNotifyIfInvalidWithCallState(parser, executor, executor, arg0Text);

		return maybeLocate switch
		{
			Error<CallState> error => (executor, powered, error.Value),
			AnySharpObject looker => await NamedLooker(looker)
		};

		async ValueTask<(AnySharpObject Looker, bool Powered, CallState? Error)> NamedLooker(AnySharpObject looker)
		{
			if (!powered && looker.Object().DBRef.Number != executor.Object().DBRef.Number)
			{
				return (executor, false, new CallState(ErrorMessages.Returns.PermissionDenied));
			}

			return (looker, powered && await looker.IsSee_All(), null);
		}
	}

	/// <summary>
	/// The mortal (public) WHO list, as <em>players</em>: never DARK, never portal-class, and
	/// independent of the caller.
	/// </summary>
	/// <remarks>
	/// The distinct step is load-bearing. <see cref="IConnectionService.GetAll"/> yields one row per
	/// open socket, so a player connected twice was listed twice — but help mwho defines mwho() as
	/// "exactly the same as lwho() used by a mortal", and lwho() walks the player table, so the
	/// connection-shaped answer disagreed with both its own documentation and its privileged twin.
	/// Everything downstream inherited the duplicate: nmwho() over-counted, and the portal's
	/// "Players online" tile — which reads this through the profile-handler's GET`ONLINE route —
	/// counted sockets while claiming to count people. Deduplicating before the object lookup also
	/// costs one query per player rather than one per connection. The per-connection view is what
	/// ports()/lports() and WHO are for.
	/// <para>
	/// On <see cref="DBRef.Number"/> and not on the whole <see cref="DBRef"/>: the bound reference
	/// is a bare <c>#N</c> on some login paths and a full <c>#N:creation</c> objid on others, and
	/// those two compare unequal, so a player holding one connection of each kind would still have
	/// been listed twice. Two live connections sharing a dbref number are the same player — the
	/// number cannot be reused while an object still holds it.
	/// </para>
	/// </remarks>
	private IAsyncEnumerable<AnySharpObject> MortalWhoPlayers() =>
		ExistingObjects(ConnectionService
				.GetAll()
				.Where(x => x.Ref is not null && x.State == IConnectionService.ConnectionState.LoggedIn
					&& x.PresenceClass != PresenceClasses.Portal
					// @hide (per-connection Hidden, distinct from the DARK flag) must exclude a player from
					// this whole WHO family exactly as DARK always did - see WHO's own row filtering in
					// SocketCommands.cs for the same isHiddenRow = isDark || connection.IsHidden pattern.
					&& !x.IsHidden)
				.Select(x => x.Ref!.Value)
				.DistinctBy(x => x.Number))
			.Where(async (x, _) => !await x.HasFlag("DARK"));

	/// <summary>
	/// The caller-visible WHO list, as <em>players</em>: everyone logged in whom
	/// <paramref name="looker"/> can see. The counterpart to <see cref="MortalWhoPlayers"/> for the
	/// nwho()/xwho() family, and distinct for the same reason — help nwho documents it as
	/// <c>words(lwho(&lt;viewer&gt;))</c>, and lwho() lists each player once.
	/// </summary>
	private IAsyncEnumerable<AnySharpObject> VisibleWhoPlayers(AnySharpObject looker, bool powered) =>
		ExistingObjects(ConnectionService
				.GetAll()
				.Where(x => x.Ref is not null && x.State == IConnectionService.ConnectionState.LoggedIn)
				// @hide (per-connection Hidden, distinct from the DARK flag) must exclude a player from
				// this whole WHO family exactly as DARK always did, unless the caller is privileged -
				// PennMUSH's fun_nwho/fun_xwho: `if (!Hidden(d) || powered)` (bsd.c:6438,6503). `powered`
				// is the *pair's* Priv_Who, not the looker's alone (see ResolveWhoLookerAsync). See WHO's
				// own row filtering in SocketCommands.cs for the same
				// isHiddenRow = isDark || connection.IsHidden pattern.
				.Where(x => !x.IsHidden || powered)
				.Select(x => x.Ref!.Value)
				.DistinctBy(x => x.Number))
			.Where(async (x, _) => await PermissionService.CanSee(looker, x));

	/// <summary>
	/// The objects <paramref name="refs"/> name, skipping any that no longer exist: a connection can
	/// outlive the player behind it for the moment it takes to be dropped.
	/// </summary>
	private async IAsyncEnumerable<AnySharpObject> ExistingObjects(IAsyncEnumerable<DBRef> refs,
		[EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		await foreach (var dbref in refs.WithCancellation(cancellationToken))
		{
			if (await Mediator.Send(new GetObjectNodeQuery(dbref), cancellationToken) is AnySharpObject found)
			{
				yield return found;
			}
		}
	}

	[SharpFunction(Name = "mwho", MinArgs = 0, MaxArgs = 0, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = [])]
	public async ValueTask<CallState> MortalWho(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var nonHiddenPlayers = MortalWhoPlayers().Select(player => $"#{player.Object().DBRef.Number}");

		return new CallState(string.Join(" ", await nonHiddenPlayers.ToArrayAsync()));
	}

	[SharpFunction(Name = "mwhoid", MinArgs = 0, MaxArgs = 0, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = [])]
	public async ValueTask<CallState> MortalWhoObjectIds(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var nonHiddenPlayerObjIds = MortalWhoPlayers().Select(x => x.Object().DBRef);

		return new CallState(string.Join(" ", await nonHiddenPlayerObjIds.ToArrayAsync()));
	}

	[SharpFunction(Name = "nmwho", MinArgs = 0, MaxArgs = 0, Flags = FunctionFlags.Regular, ParameterNames = [])]
	public async ValueTask<CallState> NumberMortalWho(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var count = await MortalWhoPlayers().CountAsync();

		return new CallState(count.ToString(CultureInfo.InvariantCulture));
	}

	[SharpFunction(Name = "nwho", MinArgs = 0, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["flags"])]
	public async ValueTask<CallState> NumberWho(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);

		var (looker, powered, lookerError) = await ResolveWhoLookerAsync(
			parser, executor, args.TryGetValue("0", out var arg0) ? arg0.Message!.ToPlainText() : null);
		if (lookerError is not null)
		{
			return lookerError;
		}

		var count = await VisibleWhoPlayers(looker, powered).CountAsync();

		return new CallState(count.ToString(CultureInfo.InvariantCulture));
	}

	[SharpFunction(Name = "pueblo", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public async ValueTask<CallState> Pueblo(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> await ArgHelpers.ForHandleOrPlayer(parser, Mediator, ConnectionService, LocateService,
			parser.CurrentState.Arguments["0"],
			(_, cd) => ValueTask.FromResult<CallState>(cd.Metadata.GetValueOrDefault("PUEBLO", "0")),
			(_, cd) => ValueTask.FromResult<CallState>(cd.Metadata.GetValueOrDefault("PUEBLO", "0"))
		);

	[SharpFunction(Name = "recv", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public async ValueTask<CallState> Received(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var arg0 = parser.CurrentState.Arguments["0"].Message!.ToPlainText();

		if (long.TryParse(arg0, out var port))
		{
			var data = ConnectionService.Get(port);
			if (data is null)
			{
				return new CallState("-1");
			}

			// fun_recv counts as an integer and fails as one: "-1" for a descriptor that is not there and
			// for one the caller may not read, with no "#-1" anywhere in it.
			if (!await CanAccessConnectionData(executor, data.Ref))
			{
				return new CallState("-1");
			}

			return new CallState(data.Metadata.GetValueOrDefault("RECV", "0"));
		}

		var maybeLocate = await LocateService.LocateConnectionTarget(parser, executor, executor, arg0);
		if (maybeLocate is not (AnySharpObject and SharpPlayer located))
		{
			return new CallState("-1");
		}

		if (!await CanAccessConnectionData(executor, located.Object.DBRef))
		{
			return new CallState("-1");
		}

		var connectionData = await LeastIdleConnectionAsync(located.Object.DBRef);
		return connectionData is null
			? new CallState("-1")
			: new CallState(connectionData.Metadata.GetValueOrDefault("RECV", "0"));
	}

	[SharpFunction(Name = "sent", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public async ValueTask<CallState> Sent(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var arg0 = parser.CurrentState.Arguments["0"].Message!.ToPlainText();

		if (long.TryParse(arg0, out var port))
		{
			var data = ConnectionService.Get(port);
			if (data is null)
			{
				return new CallState("-1");
			}

			// fun_sent counts as an integer and fails as one: "-1" for a descriptor that is not there and
			// for one the caller may not read, with no "#-1" anywhere in it.
			if (!await CanAccessConnectionData(executor, data.Ref))
			{
				return new CallState("-1");
			}

			return new CallState(data.Metadata.GetValueOrDefault("SENT", "0"));
		}

		var maybeLocate = await LocateService.LocateConnectionTarget(parser, executor, executor, arg0);
		if (maybeLocate is not (AnySharpObject and SharpPlayer located))
		{
			return new CallState("-1");
		}

		if (!await CanAccessConnectionData(executor, located.Object.DBRef))
		{
			return new CallState("-1");
		}

		var connectionData = await LeastIdleConnectionAsync(located.Object.DBRef);
		return connectionData is null
			? new CallState("-1")
			: new CallState(connectionData.Metadata.GetValueOrDefault("SENT", "0"));
	}

	[SharpFunction(Name = "ssl", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public async ValueTask<CallState> SecureSocketLayer(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var arg0 = parser.CurrentState.Arguments["0"].Message!.ToPlainText();

		if (long.TryParse(arg0, out var port))
		{
			var data = ConnectionService.Get(port);
			if (data is null)
			{
				// fun_ssl separates the two failures where fun_hostname folds them: no descriptor is
				// "#-1 NOT CONNECTED", and a descriptor you may not read is a permission error. The
				// boolean 0 means "connected, and not over TLS", which is a different fact.
				return new CallState(ErrorMessages.Returns.NotConnected);
			}

			if (data.Ref != executor.Object().DBRef)
			{
				if (!await executor.IsSee_All())
				{
					return new CallState(ErrorMessages.Returns.PermissionDenied);
				}
			}

			var ssl = data.Metadata.GetValueOrDefault("SSL", "0");
			return new CallState(ssl);
		}

		var maybeLocate = await LocateService.LocateConnectionTarget(parser, executor, executor, arg0);
		if (maybeLocate is not (AnySharpObject and SharpPlayer located))
		{
			return new CallState(ErrorMessages.Returns.NotConnected);
		}

		var connectionData = await LeastIdleConnectionAsync(located.Object.DBRef);

		if (connectionData is null)
		{
			return new CallState(ErrorMessages.Returns.NotConnected);
		}

		// lookup_desc first, permission second: a name that matches a player who is not online is "not
		// connected" whoever asks, and only a descriptor that exists can be refused.
		if (located.Object.DBRef != executor.Object().DBRef && !await executor.IsSee_All())
		{
			return new CallState(ErrorMessages.Returns.PermissionDenied);
		}

		return new CallState(connectionData.Metadata.GetValueOrDefault("SSL", "0"));
	}

	[SharpFunction(Name = "terminfo", MinArgs = 1, MaxArgs = 1,
		Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["player"])]
	public async ValueTask<CallState> TerminalInformation(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var arg0 = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var hasSeeAll = await executor.IsSee_All();

		// fun_terminfo checks the argument before it looks anything up, and says so rather than
		// answering "unknown" — which would claim the descriptor exists and has no terminal type.
		if (string.IsNullOrEmpty(arg0))
		{
			return new CallState(ErrorMessages.Returns.FunctionRequiresOneArgument);
		}

		if (long.TryParse(arg0, out var port))
		{
			var data = ConnectionService.Get(port);

			return data is null
				? new CallState(ErrorMessages.Returns.NotConnected)
				: new CallState(BuildTermInfo(data.Metadata, hasSeeAll || data.Ref == executor.Object().DBRef,
					await ArgHelpers.ColorFlagsOfAsync(Mediator, data.Ref)));
		}

		var maybeLocate = await LocateService.LocateConnectionTarget(parser, executor, executor, arg0);
		if (maybeLocate is not (AnySharpObject and SharpPlayer located))
		{
			return new CallState(ErrorMessages.Returns.NotConnected);
		}

		var connectionData = await LeastIdleConnectionAsync(located.Object.DBRef);

		// "unknown" is default_ttype — what a connected client with no terminal type is called. A
		// target that is not connected at all is a different answer.
		return connectionData is null
			? new CallState(ErrorMessages.Returns.NotConnected)
			: new CallState(BuildTermInfo(connectionData.Metadata,
				hasSeeAll || located.Object.DBRef == executor.Object().DBRef,
				await ArgHelpers.ColorFlagsOfAsync(Mediator, connectionData.Ref)));
	}

	[SharpFunction(Name = "width", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public async ValueTask<CallState> Width(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> await DescriptorDimensionAsync(parser, "WIDTH",
			ArgHelpers.NoParseDefaultNoParseArgument(parser.CurrentState.ArgumentsOrdered, 1, "78"));

	[SharpFunction(Name = "xmwho", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["start", "count"])]
	public async ValueTask<CallState> NumberRangeMortalWho(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var arg0 = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var arg1 = parser.CurrentState.Arguments["1"].Message!.ToPlainText();

		if (!int.TryParse(arg0, out var start) || !int.TryParse(arg1, out var count))
		{
			return new CallState(ErrorMessages.Returns.Integers);
		}

		if (start < 1 || count < 0)
		{
			return new CallState(ErrorMessages.Returns.ArgRange);
		}

		var allDbrefs = MortalWhoPlayers().Select(x => $"#{x.Object().DBRef.Number}");

		var result = allDbrefs.Skip(start - 1).Take(count);
		return new CallState(string.Join(" ", await result.ToArrayAsync()));
	}

	[SharpFunction(Name = "xmwhoid", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["flags"])]
	public async ValueTask<CallState> NumberRangeMortalWhoObjectId(IMUSHCodeParser parser,
		SharpFunctionAttribute _2)
	{
		var arg0 = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var arg1 = parser.CurrentState.Arguments["1"].Message!.ToPlainText();

		if (!int.TryParse(arg0, out var start) || !int.TryParse(arg1, out var count))
		{
			return new CallState(ErrorMessages.Returns.Integers);
		}

		if (start < 1 || count < 0)
		{
			return new CallState(ErrorMessages.Returns.ArgRange);
		}

		var allObjIds = MortalWhoPlayers().Select(x => x.Object().DBRef);

		var result = allObjIds.Skip(start - 1).Take(count);
		return new CallState(string.Join(" ", await result.ToArrayAsync()));
	}

	[SharpFunction(Name = "xwho", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["flags"])]
	public async ValueTask<CallState> NumberRangeWho(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var looker = executor;
		var powered = await executor.IsSee_All();

		int start, count;

		if (args.Count == 3)
		{
			var (resolved, resolvedPowered, lookerError) =
				await ResolveWhoLookerAsync(parser, executor, args["0"].Message!.ToPlainText());
			if (lookerError is not null)
			{
				return lookerError;
			}

			(looker, powered) = (resolved, resolvedPowered);

			if (!int.TryParse(args["1"].Message!.ToPlainText(), out start) ||
					!int.TryParse(args["2"].Message!.ToPlainText(), out count))
			{
				return new CallState(ErrorMessages.Returns.Integers);
			}
		}
		else
		{
			if (!int.TryParse(args["0"].Message!.ToPlainText(), out start) ||
					!int.TryParse(args["1"].Message!.ToPlainText(), out count))
			{
				return new CallState(ErrorMessages.Returns.Integers);
			}
		}

		if (start < 1 || count < 0)
		{
			return new CallState(ErrorMessages.Returns.ArgRange);
		}

		var allDbrefs = VisibleWhoPlayers(looker, powered).Select(x => $"#{x.Object().DBRef.Number}");

		var result = allDbrefs.Skip(start - 1).Take(count);
		return new CallState(string.Join(" ", await result.ToArrayAsync()));
	}

	[SharpFunction(Name = "xwhoid", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["flags"])]
	public async ValueTask<CallState> NumberRangeWhoObjectId(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var looker = executor;
		var powered = await executor.IsSee_All();

		int start, count;

		if (args.Count == 3)
		{
			var (resolved, resolvedPowered, lookerError) =
				await ResolveWhoLookerAsync(parser, executor, args["0"].Message!.ToPlainText());
			if (lookerError is not null)
			{
				return lookerError;
			}

			(looker, powered) = (resolved, resolvedPowered);

			if (!int.TryParse(args["1"].Message!.ToPlainText(), out start) ||
					!int.TryParse(args["2"].Message!.ToPlainText(), out count))
			{
				return new CallState(ErrorMessages.Returns.Integers);
			}
		}
		else
		{
			if (!int.TryParse(args["0"].Message!.ToPlainText(), out start) ||
					!int.TryParse(args["1"].Message!.ToPlainText(), out count))
			{
				return new CallState(ErrorMessages.Returns.Integers);
			}
		}

		if (start < 1 || count < 0)
		{
			return new CallState(ErrorMessages.Returns.ArgRange);
		}

		var allObjIds = VisibleWhoPlayers(looker, powered).Select(x => x.Object().DBRef);

		var result = allObjIds.Skip(start - 1).Take(count);
		return new CallState(string.Join(" ", await result.ToArrayAsync()));
	}

	[SharpFunction(Name = "zmwho", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["zone"])]
	public async ValueTask<CallState> ZoneMortalWho(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> await ZoneWhoCore(parser, mortal: true);

	[SharpFunction(Name = "zwho", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["zone", "viewer"])]
	public async ValueTask<CallState> ZoneWho(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> await ZoneWhoCore(parser, mortal: false);

	/// <summary>
	/// zwho() and zmwho(), which PennMUSH runs through one fun_zwho (bsd.c:6810) keyed on
	/// <c>called_as</c>: the connected players in locations @chzone'd to the named zone, gated by the
	/// viewer's visibility. zmwho() is the mortal-audience twin and is never powered, so it always
	/// drops @hide'd connections; zwho() drops them too unless the pair is privileged.
	/// </summary>
	/// <remarks>
	/// Both used to walk the whole player table and answer for players who were not connected at all -
	/// zmwho() gated only on the DARK flag and zwho() on nothing whatsoever, so an @hide'd player was
	/// listed by both. Routing them through <see cref="VisibleWhoPlayers"/> makes the connection list
	/// the source of truth, exactly as it already was for the mwho()/nwho()/xwho() families, and gives
	/// zwho() the <c>&lt;viewer&gt;</c> second argument that <c>help zwho</c> has always documented -
	/// it was being read as an output separator instead.
	/// </remarks>
	private async ValueTask<CallState> ZoneWhoCore(IMUSHCodeParser parser, bool mortal)
	{
		var args = parser.CurrentState.Arguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var arg0 = args["0"].Message!.ToPlainText();

		var maybeZone = await LocateService.LocateAndNotifyIfInvalid(parser, executor, executor, arg0, LocateFlags.All);
		if (maybeZone is not AnySharpObject zone)
		{
			return new CallState(maybeZone is Error<string> error ? error.Value : "#-1");
		}

		var executorHasSeeAll = await executor.IsSee_All();
		// PennMUSH bsd.c:6815 - `powered = (strcmp(called_as, "ZMWHO") && Priv_Who(executor))`.
		var powered = !mortal && executorHasSeeAll;
		var viewer = executor;

		if (args.TryGetValue("1", out var arg1) && !string.IsNullOrWhiteSpace(arg1.Message!.ToPlainText()))
		{
			// Only a powered caller may compute the answer for someone else (bsd.c:6822-6830), and the
			// answer is then capped at that viewer's own privilege (:6847).
			if (!powered)
			{
				return new CallState(ErrorMessages.Returns.PermissionDenied);
			}

			var maybeViewer = await LocateService.LocatePlayerAndNotifyIfInvalidWithCallState(
				parser, executor, executor, arg1.Message!.ToPlainText());
			switch (maybeViewer)
			{
				case Error<CallState> error:
					return error.Value;
				case AnySharpObject named:
					viewer = named;
					break;
			}

			powered = await viewer.IsSee_All();
		}

		// The zone lock is waived for a privileged caller and otherwise evaluated as the viewer, not as
		// the caller (bsd.c:6832-6839).
		if (!executorHasSeeAll && !await LockService.Evaluate(LockType.Zone, zone, viewer))
		{
			return new CallState(ErrorMessages.Returns.PermissionDenied);
		}

		var zoneNumber = zone.Object().DBRef.Number;

		var playersInZone = VisibleWhoPlayers(viewer, powered)
			.Where(async (player, _) =>
			{
				var location = await player.Where();
				return await location.Object().Zone.WithCancellation(CancellationToken.None) is AnySharpObject locationZone
					&& locationZone.Object().DBRef.Number == zoneNumber;
			})
			.Select(player => $"#{player.Object().DBRef.Number}");

		return new CallState(string.Join(" ", await playersInZone.ToArrayAsync()));
	}

	[SharpFunction(Name = "zfind", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["zone", "flags"])]
	public async ValueTask<CallState> ZoneFind(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var arg0 = args["0"].Message!.ToPlainText();

		var maybeZone = await LocateService.LocateAndNotifyIfInvalid(parser, executor, executor, arg0, LocateFlags.All);
		if (maybeZone is not AnySharpObject zone)
		{
			return new CallState(maybeZone is Error<string> error ? error.Value : "#-1");
		}

		var hasSeeAll = await executor.IsSee_All();
		if (!hasSeeAll)
		{
			if (!await LockService.Evaluate(LockType.Zone, zone, executor))
			{
				return new CallState(ErrorMessages.Returns.PermissionDenied);
			}
		}

		var objectList = await Mediator.CreateStream(new GetObjectsByZoneQuery(zone))
			.Where(async (obj, _) =>
			{
				return await Mediator.Send(new GetObjectNodeQuery(new DBRef(obj.Key))) is AnySharpObject fullObj
					&& (hasSeeAll || await PermissionService.CanExamine(executor, fullObj));
			})
			.Select(obj => $"#{obj.Key}")
			.ToArrayAsync();

		var separator = args.TryGetValue("1", out var arg1Value) && arg1Value.Message!.ToPlainText() is { } format
			&& !string.IsNullOrWhiteSpace(format)
				? format
				: " ";

		return new CallState(string.Join(separator, objectList));
	}

	[SharpFunction(Name = "poll", MinArgs = 0, MaxArgs = 0, Flags = FunctionFlags.Regular, ParameterNames = [])]
	public async ValueTask<CallState> Poll(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var pollData = await ObjectDataService.GetExpandedServerDataAsync<PollData>();
		return new CallState(pollData?.Message ?? string.Empty);
	}

	[SharpFunction(Name = "ports", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public async ValueTask<CallState> Ports(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var arg0 = parser.CurrentState.Arguments["0"].Message!.ToPlainText();

		// fun_ports resolves its target exactly as lookup_desc's player half does — lookup_player, then
		// MAT_ABSOLUTE | MAT_PLAYER | MAT_ME | MAT_TYPE — so "me" works here too, and a name that
		// matches nothing is answered with an empty list rather than an error string or a message.
		var maybeLocate = await LocateService.LocateConnectionTarget(parser, executor, executor, arg0);
		if (maybeLocate is not (AnySharpObject and SharpPlayer target))
		{
			return CallState.Empty;
		}

		// The one thing fun_ports does say out loud, and it still returns nothing rather than "#-1":
		// reading someone else's descriptors needs Priv_Who.
		if (target.Object.DBRef != executor.Object().DBRef && !await executor.IsSee_All())
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PermissionDenied), executor);
			return CallState.Empty;
		}

		var handles = ConnectionService
			.Get(target.Object.DBRef)
			.Select(x => x.Handle);

		return new CallState(string.Join(" ", await handles.ToArrayAsync()));
	}

	[SharpFunction(Name = "player", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["descriptor"])]
	public async ValueTask<CallState> Player(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var portString = parser.CurrentState.Arguments["0"].Message!.ToPlainText()!;

		if (!long.TryParse(portString, out var port))
		{
			return new CallState(ErrorMessages.Returns.InvalidPort);
		}

		var data = ConnectionService.Get(port);

		if (data?.Ref == executor.Object().DBRef)
		{
			return new CallState($"#{executor.Object().DBRef.Number}");
		}

		if (await executor.IsWizard() || await executor.IsRoyalty() || await executor.IsSee_All())
		{
			return data is null
				? new CallState(ErrorMessages.Returns.InvalidPort)
				: new CallState($"#{data.Ref?.Number}");
		}

		return new CallState(ErrorMessages.Returns.PermissionDenied);
	}

	[SharpFunction(Name = "height", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public async ValueTask<CallState> Height(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		// 24, not 78: fun_height's fallback is a screen's worth of rows, and only fun_width's is 78.
		=> await DescriptorDimensionAsync(parser, "HEIGHT",
			ArgHelpers.NoParseDefaultNoParseArgument(parser.CurrentState.ArgumentsOrdered, 1, "24"));

	[SharpFunction(Name = "hidden", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public async ValueTask<CallState> Hidden(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var arg0 = parser.CurrentState.Arguments["0"].Message!.ToPlainText();

		var canSeeHidden = await executor.IsWizard() || await executor.IsRoyalty() ||
											 await executor.IsSee_All();

		// The exception in this family: fun_hidden answers "#-1" and says why, for all three of its
		// failures. Being told a descriptor could not be found costs nothing, because only a caller who
		// already has See_All gets this far.
		if (!canSeeHidden)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PermissionDenied), executor);
			return new CallState("#-1");
		}

		if (long.TryParse(arg0, out var port))
		{
			var data = ConnectionService.Get(port);
			if (data is null || data.Ref is null)
			{
				await NotifyService.NotifyLocalized(executor,
					nameof(ErrorMessages.Notifications.CouldNotFindDescriptor), executor);
				return new CallState("#-1");
			}

			var isHidden = data.IsHidden
				|| (await Mediator.Send(new GetObjectNodeQuery(data.Ref.Value)) is AnySharpObject player && await player.HasFlag("DARK"));
			return new CallState(isHidden ? "1" : "0");
		}

		var maybeLocate = await LocateService.LocateConnectionTarget(parser, executor, executor, arg0);
		if (maybeLocate is not (AnySharpObject and SharpPlayer located))
		{
			await NotifyService.NotifyLocalized(executor,
				nameof(ErrorMessages.Notifications.CouldNotFindPlayer), executor);
			return new CallState("#-1");
		}

		var isHiddenPlayer = await ConnectionService.IsPlayerHiddenAsync(located.Object.DBRef)
			|| await new AnySharpObject(located).HasFlag("DARK");
		return new CallState(isHiddenPlayer ? "1" : "0");
	}

	/// <summary>
	/// The descriptor PennMUSH's <c>lookup_desc()</c> (src/bsd.c) settles on for a player: it walks the
	/// whole connected list and keeps the one with the greatest <c>last_time</c> — the least idle. A
	/// player with two clients open therefore answers <c>idle()</c>, <c>terminfo()</c>, <c>width()</c>
	/// and the rest about the one they are actually using, which taking whichever connection came out
	/// of the dictionary first did only by luck.
	/// </summary>
	private ValueTask<IConnectionService.ConnectionData?> LeastIdleConnectionAsync(DBRef who)
		=> ConnectionService.Get(who).MinByAsync(connection => connection.Idle ?? TimeSpan.MaxValue);

	/// <summary>
	/// Checks if the executor has permission to access connection data for another player.
	/// </summary>
	private async ValueTask<bool> CanAccessConnectionData(AnySharpObject executor, DBRef? targetDbRef)
	{
		if (targetDbRef == executor.Object().DBRef)
		{
			return true;
		}

		return await executor.IsWizard() ||
					 await executor.IsRoyalty() ||
					 await executor.IsSee_All();
	}

	/// <summary>
	/// PennMUSH's <c>fun_width</c> / <c>fun_height</c> (src/bsd.c), which are one function apart from
	/// the key they read and the fallback they end on:
	/// <code>
	/// if (!*args[0])                                    "#-1 FUNCTION REQUIRES ONE ARGUMENT"
	/// else if (lookup_desc(...) &amp;&amp; match->width > 0)     the dimension
	/// else if (args[1])                                 the caller's default
	/// else                                              78 / 24
	/// </code>
	/// Every failure after the argument check lands on the default: a name that matches nobody, a
	/// match who is not connected, and a descriptor whose dimension was never negotiated are one
	/// outcome. Answering "#-1 NO MATCH" instead — which is what routing this through the notifying
	/// locate produced — turns a formatting helper into an error string in the middle of a line, and
	/// said "I can't see that here." to the caller on the way.
	/// </summary>
	private async ValueTask<CallState> DescriptorDimensionAsync(
		IMUSHCodeParser parser, string key, MString defaultArg)
	{
		var target = parser.CurrentState.Arguments["0"].Message!.ToPlainText();

		if (string.IsNullOrEmpty(target))
		{
			return new CallState(ErrorMessages.Returns.FunctionRequiresOneArgument);
		}

		var connection = long.TryParse(target, out var port)
			? ConnectionService.Get(port)
			: await LocatedConnectionAsync(parser, target);

		// PennMUSH's "&& match->width > 0": a dimension of zero is one nobody has reported, so it takes
		// the default rather than being sent as a width of nothing.
		return connection?.Metadata.GetValueOrDefault(key) is { Length: > 0 } dimension && dimension != "0"
			? new CallState(dimension)
			: new CallState(defaultArg);

		async ValueTask<IConnectionService.ConnectionData?> LocatedConnectionAsync(
			IMUSHCodeParser inner, string name)
		{
			var executor = await inner.CurrentState.KnownExecutorObject(Mediator);
			var maybeLocate = await LocateService.LocateConnectionTarget(inner, executor, executor, name);

			return maybeLocate is AnySharpObject and SharpPlayer player
				? await LeastIdleConnectionAsync(player.Object.DBRef)
				: null;
		}
	}

	/// <summary>
	/// Builds terminal information string from connection metadata.
	/// </summary>
	/// <summary>
	/// PennMUSH <c>fun_terminfo</c> (src/bsd.c), whose privilege split is finer than "all or nothing":
	/// the client's own name, <c>telnet</c>, <c>gmcp</c>, <c>ssl</c>, <c>websocket</c> and
	/// <c>prompt_newlines</c> are behind <c>has_privs</c>, while <c>pueblo</c>, <c>stripaccents</c> and
	/// the colour style are emitted for anyone. An unprivileged caller gets <c>default_ttype</c> — the
	/// literal "unknown" here — in place of the name and the rest of the unprivileged tokens after it,
	/// rather than the bare word on its own.
	/// </summary>
	/// <param name="includeDetails">
	/// PennMUSH's <c>has_privs</c>: the caller is looking at their own descriptor, or has See_All.
	/// </param>
	private string BuildTermInfo(IReadOnlyDictionary<string, string> metadata, bool includeDetails,
		PlayerColorFlags? flags)
	{
		// The RFC 1091 terminal type, which is where PennMUSH gets the client name too — set by TTYPE
		// negotiation, by MSDP's TERMINAL_TYPE, or by hand with "@sockset terminaltype". The same key
		// SocketOptions reads, so SOCKSET and terminfo() cannot disagree about who the client is.
		var terminfo = new List<string>
		{
			includeDetails ? metadata.GetValueOrDefault("TerminalType", "unknown") : "unknown"
		};

		if (metadata.GetValueOrDefault("PUEBLO", "0") == "1")
		{
			terminfo.Add("pueblo");
		}

		// Not a PennMUSH token — PennMUSH has no MXP — but it says the same kind of thing about the
		// markup the connection renders as "pueblo" does, so it keeps that one's visibility.
		if (metadata.GetValueOrDefault("OUTPUT_FORMAT", "ansi") == "mxp")
		{
			terminfo.Add("mxp");
		}

		if (includeDetails)
		{
			// Set once the client genuinely answers a telnet option, not merely because it arrived on
			// the telnet port — PennMUSH's CONN_TELNET means the same, and a raw socket cannot claim it.
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

			if (metadata.GetValueOrDefault("ConnectionType", "") == "websocket")
			{
				terminfo.Add("websocket");
			}

			if (metadata.GetValueOrDefault("PresenceClass", PresenceClasses.Play) == PresenceClasses.Portal)
			{
				terminfo.Add(PresenceClasses.Portal);
			}

			if (metadata.GetValueOrDefault("PROMPT_NEWLINES", "0") == "1")
			{
				terminfo.Add("prompt_newlines");
			}
		}

		if (metadata.GetValueOrDefault("STRIPACCENTS", "0") == "1")
		{
			terminfo.Add("stripaccents");
		}

		// "One of the color styles shown in [colorstyle] will also be included" — always one, so a
		// client that pinned nothing still reports what it is being rendered at. An explicit
		// "SOCKSET colorstyle" wins; otherwise it is read back out of the client's own MTTS claims.
		terminfo.Add(TerminalCapabilityReader.ColorStyleFor(metadata, flags));

		return string.Join(" ", terminfo);
	}

	[SharpFunction(Name = "IDLESECS", MinArgs = 0, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		ParameterNames = ["player", "precision"])]
	public async ValueTask<CallState> IdleSecs(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		// Note: the parser always injects Arguments["0"] = CallState.Empty via DefaultIfEmpty,
		// so ContainsKey("0") is always true. Check for a non-empty value instead.
		var arg0 = parser.CurrentState.Arguments.TryGetValue("0", out var arg0State)
			? arg0State.Message?.ToPlainText()
			: null;

		if (!string.IsNullOrEmpty(arg0))
		{
			return await IdleSeconds(parser, _2);
		}

		if (!TimePrecisions.TryParse(
					parser.CurrentState.Arguments.TryGetValue("1", out var precisionArg)
						? precisionArg.Message?.ToPlainText()
						: null,
					out var precision))
		{
			return new CallState(ErrorMessages.Returns.InvalidPrecision);
		}

		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);

		// Connections with no known idle time are filtered out rather than mapped to -1: mixed in,
		// the sentinel wins the Min() and one unavailable connection hides every real idle time.
		var data = ConnectionService.Get(executor.Object().DBRef);
		var idleMilliseconds = await data
			.Where(x => x.Idle is not null)
			.Select(x => (long)x.Idle!.Value.TotalMilliseconds)
			.DefaultIfEmpty(-1L)
			.MinAsync();

		return new CallState(idleMilliseconds < 0
			? "-1"
			: TimePrecisions.Format(idleMilliseconds, precision));
	}
}