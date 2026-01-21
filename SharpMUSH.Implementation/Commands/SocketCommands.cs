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
	public async ValueTask<Option<CallState>> Who(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(_mediator);

		var everyone = _connectionService.GetAll();
		const string fmt = "{0,-18} {1,10} {2,6}  {3,-32}";
		var header = string.Format(fmt, "Player Name", "On For", "Idle", "Doing");
		
		var filteredPlayers = await everyone
			.Where(player => player.Ref.HasValue)
			.Select(async (player,i,ct) =>
			{
				var obj = await _mediator.Send(new GetObjectNodeQuery(player.Ref!.Value), ct);
				var doingText = await GetDoingText(executor, obj.Known);
				
				return (string.Format(
					fmt,
					obj.Known.Object().Name,
					TimeHelpers.TimeString(player.Connected ?? TimeSpan.Zero, accuracy: 3),
					TimeHelpers.TimeString(player.Idle ?? TimeSpan.Zero),
					doingText), obj.Known);
			})
			.Where(async (player, _) => await _permissionService.CanSee(executor, player.Known))
			.ToListAsync();

		var sortedPlayers = filteredPlayers.Select(x => x.Item1).ToArray();
		
		var footer = $"{sortedPlayers.Length} players logged in.";

		var message = $"{header}\n{string.Join('\n', sortedPlayers)}\n{footer}";

		await _notifyService.Notify(handle: parser.CurrentState.Handle!.Value, what: message);

		return new None();
	}

	/// <example>
	/// connect "person with long name" password
	/// connect person password
	/// connect PersonWithoutAPassword
	/// connect "person without a password"
	/// connect guest
	/// </example>
	[SharpCommand(Name = "CONNECT", Behavior = CommandBehavior.SOCKET | CommandBehavior.NoParse, MinArgs = 1,
		MaxArgs = 2, ParameterNames = ["player", "password"])]
	public async ValueTask<Option<CallState>> Connect(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		// Early HUH if already logged in.
		if (_connectionService.Get(parser.CurrentState.Handle!.Value)?.Ref is not null)
		{
			await NotifyService!.Notify(parser.CurrentState.Handle!.Value, "Huh?  (Type \"help\" for help.)");
			return new CallState("#-1 ALREADY CONNECTED");
		}

		var match = ConnectionPatternRegex.Match(parser.CurrentState.Arguments["0"].Message!.ToString());
		var username = match.Groups["User"].Value;
		var password = match.Groups["Password"].Value;

		var handle = parser.CurrentState.Handle!.Value;
		var connectionData = _connectionService.Get(handle);
		var ipAddress = connectionData?.Metadata.TryGetValue("InternetProtocolAddress", out var ip) == true ? ip : "unknown";

		// Check if this is a guest login attempt
		if (username.Equals("guest", StringComparison.OrdinalIgnoreCase))
		{
			return await HandleGuestLogin(parser, handle, ipAddress);
		}

		var nameItems = ArgHelpers.NameList(username).ToList();

		if (nameItems.Count != 1)
		{
			// Trigger SOCKET`LOGINFAIL for invalid player name
			// PennMUSH spec: socket`loginfail (descriptor, IP, count, reason, playerobjid, name)
			await _eventService.TriggerEventAsync(
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
			return new CallState("#-1 PLAYER NOT FOUND");
		}

		var nameItem = nameItems.First();
		
		var foundDB = await nameItem.Match(
			async dbref => (await _mediator.Send(new GetObjectNodeQuery(dbref))).TryPickT0(out var player, out _)
				? player
				: null,
			async name => await (_mediator.CreateStream(new GetPlayerQuery(name))).FirstOrDefaultAsync());

		if (foundDB is null)
		{
			// Trigger SOCKET`LOGINFAIL for player not found
			// PennMUSH spec: socket`loginfail (descriptor, IP, count, reason, playerobjid, name)
			await _eventService.TriggerEventAsync(
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
			return new CallState("#-1 PLAYER NOT FOUND");
		}

		var validPassword = _passwordService.PasswordIsValid($"#{foundDB.Object.Key}:{foundDB.Object.CreationTime}",
			password, foundDB.PasswordHash);

		if (!validPassword && !string.IsNullOrEmpty(foundDB.PasswordHash))
		{
			// Trigger SOCKET`LOGINFAIL for invalid password
			// PennMUSH spec: socket`loginfail (descriptor, IP, count, reason, playerobjid, name)
			await _eventService.TriggerEventAsync(
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
			return new CallState("#-1 INVALID PASSWORD");
		}

		// Rehash legacy PennMUSH passwords to modern PBKDF2 format on successful login
		if (validPassword && _passwordService.NeedsRehash(foundDB.PasswordHash))
		{
			await _passwordService.RehashPasswordAsync(foundDB, password);
			_logger?.LogInformation("Rehashed legacy password for player #{Key}", foundDB.Object.Key);
		}

		// Future feature: Site lock checking would go here
		var playerDbRef = new DBRef(foundDB.Object.Key, foundDB.Object.CreationTime);
		await _connectionService.Bind(parser.CurrentState.Handle!.Value, playerDbRef);

		// Trigger PLAYER`CONNECT event - PennMUSH compatible
		// PennMUSH spec: player`connect (objid, number of connections, descriptor)
		var connectionCount = await _connectionService.Get(playerDbRef).CountAsync();
		await _eventService.TriggerEventAsync(
			parser,
			"PLAYER`CONNECT",
			playerDbRef, // Enactor is the player who connected
			$"#{foundDB.Object.Key}",
			connectionCount.ToString(),
			parser.CurrentState.Handle!.Value.ToString());

		await NotifyService!.Notify(parser.CurrentState.Handle!.Value, "Connected!");
		Logger?.LogDebug("Successful login and binding for {@person}", foundDB.Object);
		return new CallState(playerDbRef);
	}

	private static async ValueTask<Option<CallState>> HandleGuestLogin(IMUSHCodeParser parser, long handle, string ipAddress)
	{
		// Check if guest logins are enabled
		if (!Configuration!.CurrentValue.Net.Guests)
		{
			await NotifyService!.Notify(handle, "Guest logins are not enabled.");
			return new CallState("#-1 GUEST LOGINS DISABLED");
		}

		// Get all players and filter for those with Guest power
		var guestPlayers = await Mediator!.CreateStream(new GetAllPlayersQuery())
			.Where(async (player, _) => await player.Object.HasPower("Guest"))
			.ToListAsync();

		if (guestPlayers.Count == 0)
		{
			// Trigger SOCKET`LOGINFAIL for no guest characters
			await EventService!.TriggerEventAsync(
				parser,
				"SOCKET`LOGINFAIL",
				null,
				handle.ToString(),
				ipAddress,
				"1",
				"no guest characters available",
				"#-1",
				"guest");

			await NotifyService!.Notify(handle, "Sorry, there are no guest characters available.");
			return new CallState("#-1 NO GUEST CHARACTERS");
		}

		// Get max_guests configuration
		var maxGuests = Configuration!.CurrentValue.Limit.MaxGuests;

		// Find an appropriate guest character based on max_guests policy
		SharpPlayer? selectedGuest = null;

		if (maxGuests == -1)
		{
			// Find a guest that's not currently connected
			foreach (var guest in guestPlayers)
			{
				var guestDbRef = new DBRef(guest.Object.Key, guest.Object.CreationTime);
				var guestConnectionCount = await ConnectionService!.Get(guestDbRef).CountAsync();
				
				if (guestConnectionCount == 0)
				{
					selectedGuest = guest;
					break;
				}
			}

			if (selectedGuest == null)
			{
				// Trigger SOCKET`LOGINFAIL for max guests reached
				await EventService!.TriggerEventAsync(
					parser,
					"SOCKET`LOGINFAIL",
					null,
					handle.ToString(),
					ipAddress,
					"1",
					"all guest characters in use",
					"#-1",
					"guest");

				await NotifyService!.Notify(handle, "Sorry, all guest characters are currently in use.");
				return new CallState("#-1 ALL GUESTS IN USE");
			}
		}
		else if (maxGuests == 0)
		{
			// No limit - use any guest (prefer first)
			selectedGuest = guestPlayers.First();
		}
		else
		{
			// Limited to maxGuests total connections
			// Count total guest connections
			var totalGuestConnections = 0;
			foreach (var guest in guestPlayers)
			{
				var guestDbRef = new DBRef(guest.Object.Key, guest.Object.CreationTime);
				totalGuestConnections += await ConnectionService!.Get(guestDbRef).CountAsync();
			}

			if (totalGuestConnections >= maxGuests)
			{
				// Trigger SOCKET`LOGINFAIL for max guests reached
				await EventService!.TriggerEventAsync(
					parser,
					"SOCKET`LOGINFAIL",
					null,
					handle.ToString(),
					ipAddress,
					"1",
					"maximum guest connections reached",
					"#-1",
					"guest");

				await NotifyService!.Notify(handle, "Sorry, the maximum number of guest connections has been reached.");
				return new CallState("#-1 MAX GUESTS REACHED");
			}

			// Find the guest with fewest connections
			SharpPlayer? leastUsedGuest = null;
			var minConnections = int.MaxValue;

			foreach (var guest in guestPlayers)
			{
				var guestDbRef = new DBRef(guest.Object.Key, guest.Object.CreationTime);
				var guestConnections = await ConnectionService!.Get(guestDbRef).CountAsync();
				
				if (guestConnections < minConnections)
				{
					minConnections = guestConnections;
					leastUsedGuest = guest;
				}
			}

			selectedGuest = leastUsedGuest;
		}

		if (selectedGuest == null)
		{
			// This shouldn't happen, but handle it just in case
			// Trigger SOCKET`LOGINFAIL for unexpected guest selection failure
			await EventService!.TriggerEventAsync(
				parser,
				"SOCKET`LOGINFAIL",
				null,
				handle.ToString(),
				ipAddress,
				"1",
				"unexpected guest selection failure",
				"#-1",
				"guest");

			await NotifyService!.Notify(handle, "Sorry, there are no guest characters available.");
			return new CallState("#-1 GUEST SELECTION FAILED");
		}

		// Bind the connection to the selected guest
		var playerDbRef = new DBRef(selectedGuest.Object.Key, selectedGuest.Object.CreationTime);
		await ConnectionService!.Bind(handle, playerDbRef);

		// Trigger PLAYER`CONNECT event
		var connectionCount = await ConnectionService.Get(playerDbRef).CountAsync();
		await EventService!.TriggerEventAsync(
			parser,
			"PLAYER`CONNECT",
			playerDbRef,
			$"#{selectedGuest.Object.Key}",
			connectionCount.ToString(),
			handle.ToString());

		await NotifyService!.Notify(handle, "Connected!");
		Logger?.LogDebug("Successful guest login for {@guest}", selectedGuest.Object);
		return new CallState(playerDbRef);
	}

	[SharpCommand(Name = "QUIT", Behavior = CommandBehavior.SOCKET | CommandBehavior.NoParse, MinArgs = 0, MaxArgs = 0, ParameterNames = [])]
	public async ValueTask<Option<CallState>> Quit(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(_mediator);
		await _notifyService.Notify(executor, MModule.single("GOODBYE."));
		
		// Display Disconnect Banner (DownMotd) if configured
		var motdData = await _objectDataService.GetExpandedServerDataAsync<MotdData>();
		if (!string.IsNullOrWhiteSpace(motdData?.DownMotd))
		{
			await _notifyService.Notify(executor, motdData.DownMotd);
		}
		
		await _connectionService.Disconnect(parser.CurrentState.Handle!.Value);
		return new None();
	}

	[GeneratedRegex("^(?<User>\"(?:.+?)\"|(?:.+?))(?:\\s+(?<Password>\\S+))?$")]
	private static partial Regex ConnectionPattern();
}
