using System.Text.RegularExpressions;
using DotNext.Collections.Generic;
using Microsoft.Extensions.Logging;
using OneOf.Types;
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

namespace SharpMUSH.Implementation.Commands;

public partial class Commands
{
	private static readonly Regex ConnectionPatternRegex = ConnectionPattern();

	[SharpCommand(Name = "WHO", Behavior = CommandBehavior.SOCKET | CommandBehavior.NoParse, MinArgs = 0, MaxArgs = 1, ParameterNames = [])]
	public static async ValueTask<Option<CallState>> Who(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);

		var everyone = ConnectionService!.GetAll();
		const string fmt = "{0,-18} {1,10} {2,6}  {3,-32}";
		var header = string.Format(fmt, "Player Name", "On For", "Idle", "Doing");
		
		var filteredPlayers = await everyone
			.Where(player => player.Ref.HasValue)
			.Select(async (player,i,ct) =>
			{
				var obj = await Mediator!.Send(new GetObjectNodeQuery(player.Ref!.Value), ct);
				var doingText = await Commands.GetDoingText(executor, obj.Known);
				
				return (string.Format(
					fmt,
					obj.Known.Object().Name,
					TimeHelpers.TimeString(player.Connected ?? TimeSpan.Zero, accuracy: 3),
					TimeHelpers.TimeString(player.Idle ?? TimeSpan.Zero),
					doingText), obj.Known);
			})
			.Where(async (player, _) => await PermissionService!.CanSee(executor, player.Known))
			.ToListAsync();

		var sortedPlayers = filteredPlayers.Select(x => x.Item1).ToArray();
		
		var footer = $"{sortedPlayers.Length} players logged in.";

		var message = $"{header}\n{string.Join('\n', sortedPlayers)}\n{footer}";

		await NotifyService!.Notify(handle: parser.CurrentState.Handle!.Value, what: message);

		return new None();
	}

	/// <example>
	/// connect "person with long name" password
	/// connect person password
	/// connect PersonWithoutAPassword
	/// connect "person without a password"
	/// </example>
	[SharpCommand(Name = "CONNECT", Behavior = CommandBehavior.SOCKET | CommandBehavior.NoParse, MinArgs = 1,
		MaxArgs = 2, ParameterNames = ["player", "password"])]
	public static async ValueTask<Option<CallState>> Connect(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		// Early HUH if already logged in.
		if (ConnectionService!.Get(parser.CurrentState.Handle!.Value)?.Ref is not null)
		{
			await NotifyService!.Notify(parser.CurrentState.Handle!.Value, "Huh?  (Type \"help\" for help.)");
			return new None();
		}

		var match = ConnectionPatternRegex.Match(parser.CurrentState.Arguments["0"].Message!.ToString());
		var username = match.Groups["User"].Value;
		var password = match.Groups["Password"].Value;

		var nameItems = ArgHelpers.NameList(username).ToList();
		var handle = parser.CurrentState.Handle!.Value;
		var connectionData = ConnectionService.Get(handle);
		var ipAddress = connectionData?.Metadata.TryGetValue("InternetProtocolAddress", out var ip) == true ? ip : "unknown";

		if (nameItems.Count != 1)
		{
			// Trigger SOCKET`LOGINFAIL for invalid player name
			// PennMUSH spec: socket`loginfail (descriptor, IP, count, reason, playerobjid, name)
			await EventService!.TriggerEventAsync(
				parser,
				"SOCKET`LOGINFAIL",
				null, // System event
				handle.ToString(),
				ipAddress,
				"1", // count - simplified for now
				"invalid player name",
				"#-1", // no valid player
				username);
			
			await NotifyService!.Notify(handle, "Could not find that player.");
			return new None();
		}

		var nameItem = nameItems.First();
		
		var foundDB = await nameItem.Match(
			async dbref => (await Mediator!.Send(new GetObjectNodeQuery(dbref))).TryPickT0(out var player, out _)
				? player
				: null,
			async name => await (Mediator!.CreateStream(new GetPlayerQuery(name))).FirstOrDefaultAsync());

		if (foundDB is null)
		{
			// Trigger SOCKET`LOGINFAIL for player not found
			// PennMUSH spec: socket`loginfail (descriptor, IP, count, reason, playerobjid, name)
			await EventService!.TriggerEventAsync(
				parser,
				"SOCKET`LOGINFAIL",
				null, // System event
				handle.ToString(),
				ipAddress,
				"1", // count - simplified for now
				"player not found",
				"#-1", // no valid player
				username);
			
			await NotifyService!.Notify(handle, "Could not find that player.");
			return new None();
		}

		var validPassword = PasswordService!.PasswordIsValid($"#{foundDB.Object.Key}:{foundDB.Object.CreationTime}",
			password, foundDB.PasswordHash);

		if (!validPassword && !string.IsNullOrEmpty(foundDB.PasswordHash))
		{
			// Trigger SOCKET`LOGINFAIL for invalid password
			// PennMUSH spec: socket`loginfail (descriptor, IP, count, reason, playerobjid, name)
			await EventService!.TriggerEventAsync(
				parser,
				"SOCKET`LOGINFAIL",
				null, // System event
				handle.ToString(),
				ipAddress,
				"1", // count - simplified for now
				"invalid password",
				$"#{foundDB.Object.Key}", // valid player objid
				foundDB.Object.Name);
			
			await NotifyService!.Notify(handle, "Invalid Password.");
			return new None();
		}

		// Rehash legacy PennMUSH passwords to modern PBKDF2 format on successful login
		if (validPassword && PasswordService.NeedsRehash(foundDB.PasswordHash))
		{
			await PasswordService.RehashPasswordAsync(foundDB, password);
			Logger?.LogInformation("Rehashed legacy password for player #{Key}", foundDB.Object.Key);
		}

		// Future feature: Site lock checking would go here
		var playerDbRef = new DBRef(foundDB.Object.Key, foundDB.Object.CreationTime);
		await ConnectionService.Bind(parser.CurrentState.Handle!.Value, playerDbRef);

		// Trigger PLAYER`CONNECT event - PennMUSH compatible
		// PennMUSH spec: player`connect (objid, number of connections, descriptor)
		var connectionCount = await ConnectionService.Get(playerDbRef).CountAsync();
		await EventService!.TriggerEventAsync(
			parser,
			"PLAYER`CONNECT",
			playerDbRef, // Enactor is the player who connected
			$"#{foundDB.Object.Key}",
			connectionCount.ToString(),
			parser.CurrentState.Handle!.Value.ToString());

		await NotifyService!.Notify(parser.CurrentState.Handle!.Value, "Connected!");
		Serilog.Log.Logger.Debug("Successful login and binding for {@person}", foundDB.Object);
		return new None();
	}

	[SharpCommand(Name = "QUIT", Behavior = CommandBehavior.SOCKET | CommandBehavior.NoParse, MinArgs = 0, MaxArgs = 0, ParameterNames = [])]
	public static async ValueTask<Option<CallState>> Quit(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		await NotifyService!.Notify(executor, MModule.single("GOODBYE."));
		
		// Display Disconnect Banner (DownMotd) if configured
		var motdData = await ObjectDataService!.GetExpandedServerDataAsync<MotdData>();
		if (!string.IsNullOrWhiteSpace(motdData?.DownMotd))
		{
			await NotifyService!.Notify(executor, motdData.DownMotd);
		}
		
		await ConnectionService!.Disconnect(parser.CurrentState.Handle!.Value);
		return new None();
	}

	[GeneratedRegex("^(?<User>\"(?:.+?)\"|(?:.+?))(?:\\s+(?<Password>\\S+))?$")]
	private static partial Regex ConnectionPattern();
}
