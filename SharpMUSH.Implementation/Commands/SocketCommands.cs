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
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Messaging.Messages;
using System.Text.RegularExpressions;

namespace SharpMUSH.Implementation.Commands;

public partial class Commands
{
	private static readonly Regex ConnectionPatternRegex = ConnectionPattern();

	[SharpCommand(Name = "WHO", Behavior = CommandBehavior.SOCKET | CommandBehavior.NoParse, MinArgs = 0, MaxArgs = 1, ParameterNames = [])]
	public static async ValueTask<Option<CallState>> Who(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		// WHO is CommandBehavior.SOCKET precisely so it answers at the connect screen, where nothing
		// is bound to the handle yet. KnownExecutorObject() throws on that None, so resolve the
		// executor optionally: an anonymous viewer is never a wizard and gets the mortal listing.
		var executorOption = await parser.CurrentState.ExecutorObject(Mediator!);
		var executor = executorOption.IsNone ? null : executorOption.Known();
		var isWizard = executor is not null && await executor.IsWizard();

		var everyone = ConnectionService!.GetAll();

		// PennMUSH dump_users formats (src/bsd.c): header widths and per-row layout are replicated verbatim.
		string header = isWizard
			? string.Format("{0,-16} {1,6} {2,9} {3,5} {4,5} {5,-4} {6}",
				"Player Name", "Loc #", "On For", "Idle", "Cmds", "Des", "Host")
			: string.Format("{0,-16} {1,10} {2,6}  {3}", "Player Name", "On For", "Idle", "Doing");

		var filteredPlayers = await everyone
			.Where(player => player.Ref.HasValue && (isWizard || player.PresenceClass != PresenceClasses.Portal))
			// PennMUSH dump_users skips a descriptor whose player is not a GoodObject. A handle can
			// outlive the object it is bound to — @nuke a connected player, or a stale entry recovered
			// from the state store — and Known throws on that None, taking the whole listing down with
			// an #-1 EXCEPTION rather than omitting one row.
			.Select(async (player, _, ct) => (Player: player,
				Object: await Mediator!.Send(new GetObjectNodeQuery(player.Ref!.Value), ct)))
			.Where(entry => !entry.Object.IsNone)
			.Select(async (entry, i, ct) =>
			{
				var player = entry.Player;
				var known = entry.Object.Known;
				var name = known.Object().Name;
				var namePadded = name.Length < 16 ? name.PadRight(16) : name;
				var onFor = TimeHelpers.TimeString(player.Connected ?? TimeSpan.Zero, accuracy: 3);
				var idle = TimeHelpers.TimeString(player.Idle ?? TimeSpan.Zero);
				var isDark = await known.HasFlag("DARK");

				string line;
				if (isWizard)
				{
					var location = known.IsContent
						? "#" + ((await known.AsContent.Location())?.Object().DBRef.Number.ToString() ?? "-1")
						: "#-1";
					// Host truncated + " (Dark)" for dark players, else truncated to 27 (PennMUSH).
					var host = isDark
						? (player.HostName.Length > 20 ? player.HostName[..20] : player.HostName) + " (Dark)"
						: (player.HostName.Length > 27 ? player.HostName[..27] : player.HostName);
					// "Des" is the descriptor (handle) plus connection-type flags: S=SSL, L=local, W=WebSocket.
					line = $"{namePadded} {location,6} {onFor,9} {idle,5}  {player.CommandCount,4} {player.Handle,3}{ConnType(player)} {host}";
				}
				else
				{
					// No executor at the connect screen: read DOING as the listed player reads it on
					// itself. @doing is public in PennMUSH, and it is the one column the connect-screen
					// listing exists to show.
					var doingText = await Commands.GetDoingText(executor ?? known, known);
					line = $"{namePadded} {onFor,10}   {idle,4}{(isDark ? 'D' : ' ')} {doingText}";
				}

				return (Line: line, Known: known);
			})
			// CanSee needs a viewer; an anonymous connect-screen viewer has none and is neither
			// privileged nor SEE_ALL, so it reduces to the DARK check CanSee would have made.
			.Where(async (player, _) => executor is null
				? !await player.Known.IsDark()
				: await PermissionService!.CanSee(executor, player.Known))
			.ToListAsync();

		var count = filteredPlayers.Count;
		var footer = count switch
		{
			0 => "There are no players connected.",
			1 => "There is one player connected.",
			_ => $"There are {count} players connected."
		};

		var message = $"{header}\n{string.Join('\n', filteredPlayers.Select(x => x.Line))}\n{footer}";

		await NotifyService!.Notify(handle: parser.CurrentState.Handle!.Value, what: message);

		return new None();

		// PennMUSH conntype: S (SSL) or L (local, non-SSL), optionally followed by W (WebSocket).
		static string ConnType(IConnectionService.ConnectionData c)
		{
			var flags = c.Metadata.GetValueOrDefault("SSL", "0") == "1"
				? "S"
				: c.InternetProtocolAddress is "127.0.0.1" or "::1" or "localhost" ? "L" : "";
			return c.ConnectionType == "websocket" ? flags + "W" : flags;
		}
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
	public static async ValueTask<Option<CallState>> Connect(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		if (ConnectionService!.Get(parser.CurrentState.Handle!.Value)?.Ref is not null)
		{
			await NotifyService!.Notify(parser.CurrentState.Handle!.Value, "Huh?  (Type \"help\" for help.)");
			return new CallState(ErrorMessages.Returns.AlreadyConnected);
		}

		var match = ConnectionPatternRegex.Match(parser.CurrentState.Arguments["0"].Message!.ToString());
		var username = match.Groups["User"].Value;
		var password = match.Groups["Password"].Value;

		var handle = parser.CurrentState.Handle!.Value;
		var connectionData = ConnectionService.Get(handle);
		var ipAddress = connectionData?.Metadata.TryGetValue("InternetProtocolAddress", out var ip) == true ? ip : "unknown";
		var hostName = connectionData?.HostName ?? ipAddress;

		// Task 15: sitelock gate for the whole connect surface (character login, OTT/token login,
		// AND guest login below all count as a "game connection"). Checked before any username/
		// credential parsing so a blocked site never learns whether a name it tried is valid.
		if (SitelockMatcher.IsBlocked(Configuration!.CurrentValue.SitelockRules.Rules, ipAddress, hostName, SitelockMatcher.ConnectFlag))
		{
			await NotifyService!.Notify(handle, "Access from your location is restricted.");
			return new CallState(ErrorMessages.Returns.PermissionDenied);
		}

		if (username.Equals("guest", StringComparison.OrdinalIgnoreCase))
		{
			return await HandleGuestLogin(parser, handle, ipAddress, hostName);
		}

		if (username.Equals("token", StringComparison.OrdinalIgnoreCase))
		{
			return await HandleTokenLogin(parser, handle, password, ipAddress);
		}

		var nameItems = ArgHelpers.NameList(username).ToList();

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
			return new CallState(ErrorMessages.Returns.PlayerNotFound);
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
			return new CallState(ErrorMessages.Returns.PlayerNotFound);
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
			return new CallState(ErrorMessages.Returns.InvalidPassword);
		}

		// Rehash legacy PennMUSH passwords to modern PBKDF2 format on successful login
		if (validPassword && PasswordService.NeedsRehash(foundDB.PasswordHash))
		{
			await PasswordService.RehashPasswordAsync(foundDB, password);
			Logger?.LogInformation("Rehashed legacy password for player #{Key}", foundDB.Object.Key);
		}

		if (!Configuration!.CurrentValue.Net.Logins
			&& !await new AnySharpObject(foundDB).IsWizard())
		{
			await NotifyService!.Notify(handle, "Logins are disabled.");
			return new CallState(ErrorMessages.Returns.PermissionDenied);
		}

		var playerDbRef = new DBRef(foundDB.Object.Key, foundDB.Object.CreationTime);
		await ConnectionService.Bind(parser.CurrentState.Handle!.Value, playerDbRef);

		await CompletePlayerLoginAsync(parser, parser.CurrentState.Handle!.Value, foundDB, playerDbRef);
		Logger?.LogDebug("Successful login and binding for {@person}", foundDB.Object);
		return new CallState(playerDbRef);
	}

	private static async ValueTask<Option<CallState>> HandleTokenLogin(
		IMUSHCodeParser parser, long handle, string token, string ipAddress)
	{
		var playerDbRef = await OttStore!.ValidateAndConsumeAsync(token);

		if (playerDbRef is null)
		{
			await EventService!.TriggerEventAsync(
				parser, "SOCKET`LOGINFAIL", null,
				handle.ToString(), ipAddress, "1",
				"invalid or expired login token", "#-1", "token");
			await NotifyService!.Notify(handle, "Invalid or expired login token.");
			Logger?.LogWarning("OTT login failed for handle {Handle} from {IP}: token invalid/expired", handle, ipAddress);
			return new CallState(ErrorMessages.Returns.InvalidPassword);
		}

		var playerNode = await Mediator!.Send(new GetObjectNodeQuery(playerDbRef.Value));
		if (!playerNode.IsPlayer)
		{
			await NotifyService!.Notify(handle, "Could not find that player.");
			return new CallState(ErrorMessages.Returns.PlayerNotFound);
		}
		var foundPlayer = playerNode.AsPlayer;

		if (!Configuration!.CurrentValue.Net.Logins
			&& !await new AnySharpObject(foundPlayer).IsWizard())
		{
			await NotifyService!.Notify(handle, "Logins are disabled.");
			return new CallState(ErrorMessages.Returns.PermissionDenied);
		}

		await ConnectionService!.Bind(handle, playerDbRef.Value);

		await CompletePlayerLoginAsync(parser, handle, foundPlayer, playerDbRef.Value);
		Logger?.LogInformation("OTT login succeeded for player {Name} (#{Key}) from {IP}",
			foundPlayer.Object.Name, foundPlayer.Object.Key, ipAddress);
		return new CallState(playerDbRef.Value);
	}

	private static async ValueTask<Option<CallState>> HandleGuestLogin(IMUSHCodeParser parser, long handle, string ipAddress, string hostName)
	{
		// Task 15: guest-specific sitelock gate, on top of (not instead of) the !connect gate
		// already applied in Connect() above — a site can allow normal logins but disallow guests.
		if (SitelockMatcher.IsBlocked(Configuration!.CurrentValue.SitelockRules.Rules, ipAddress, hostName, SitelockMatcher.GuestFlag))
		{
			await NotifyService!.Notify(handle, "Access from your location is restricted.");
			return new CallState(ErrorMessages.Returns.PermissionDenied);
		}

		if (!Configuration!.CurrentValue.Net.Logins)
		{
			await NotifyService!.Notify(handle, "Logins are disabled.");
			return new CallState(ErrorMessages.Returns.PermissionDenied);
		}

		if (!Configuration!.CurrentValue.Net.Guests)
		{
			await NotifyService!.Notify(handle, "Guest logins are not enabled.");
			return new CallState(ErrorMessages.Returns.GuestLoginsDisabled);
		}

		var guestPlayers = await Mediator!.CreateStream(new GetAllPlayersQuery())
			.Where(async (player, _) => await player.Object.HasPower("Guest"))
			.ToListAsync();

		if (guestPlayers.Count == 0)
		{
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
			return new CallState(ErrorMessages.Returns.NoGuestCharacters);
		}

		var maxGuests = Configuration!.CurrentValue.Limit.MaxGuests;

		SharpPlayer? selectedGuest = null;

		if (maxGuests == -1)
		{
			selectedGuest = await guestPlayers.ToAsyncEnumerable()
				.FirstOrDefaultAsync(async (guest, ct) =>
				{
					var guestDbRef = new DBRef(guest.Object.Key, guest.Object.CreationTime);
					return await ConnectionService!.Get(guestDbRef).CountAsync(ct) == 0;
				});

			if (selectedGuest == null)
			{
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
				return new CallState(ErrorMessages.Returns.AllGuestsInUse);
			}
		}
		else if (maxGuests == 0)
		{
			// No limit - use any guest (prefer first)
			selectedGuest = guestPlayers.First();
		}
		else
		{
			var totalGuestConnections = await guestPlayers.ToAsyncEnumerable()
				.Select((SharpPlayer guest, CancellationToken ct) =>
				{
					var guestDbRef = new DBRef(guest.Object.Key, guest.Object.CreationTime);
					return ConnectionService!.Get(guestDbRef).CountAsync(ct);
				})
				.SumAsync();

			if (totalGuestConnections >= maxGuests)
			{
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
				return new CallState(ErrorMessages.Returns.MaxGuestsReached);
			}

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
			return new CallState(ErrorMessages.Returns.GuestSelectionFailed);
		}

		var playerDbRef = new DBRef(selectedGuest.Object.Key, selectedGuest.Object.CreationTime);
		await ConnectionService!.Bind(handle, playerDbRef);

		await CompletePlayerLoginAsync(parser, handle, selectedGuest, playerDbRef, isGuest: true);
		Logger?.LogDebug("Successful guest login for {@guest}", selectedGuest.Object);
		return new CallState(playerDbRef);
	}

	[SharpCommand(Name = "QUIT", Behavior = CommandBehavior.SOCKET | CommandBehavior.NoParse, MinArgs = 0, MaxArgs = 0, ParameterNames = [])]
	public static async ValueTask<Option<CallState>> Quit(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var handle = parser.CurrentState.Handle!.Value;

		// QUIT is a SOCKET command: it must also work at the connect screen, where the handle has
		// no executor bound and KnownExecutorObject() would throw. Fall back to notifying the
		// socket directly in that case.
		var executorOption = await parser.CurrentState.ExecutorObject(Mediator!);
		var executor = executorOption.IsNone ? null : executorOption.Known();

		await NotifyQuitAsync(MModule.single("GOODBYE."));

		var quitText = await ReadMessageFileAsync(Configuration!.CurrentValue.Message.QuitFile);
		if (!string.IsNullOrWhiteSpace(quitText))
		{
			await NotifyQuitAsync(MModule.single(quitText));
		}

		await ConnectionService!.Disconnect(handle);

		// Tell ConnectionServer to close the actual socket connection
		if (MessageBus != null)
		{
			await MessageBus.Publish(new DisconnectConnectionMessage(handle, "QUIT"));
		}

		return new None();

		async ValueTask NotifyQuitAsync(MString what)
		{
			if (executor is null)
			{
				await NotifyService!.Notify(handle, what);
			}
			else
			{
				await NotifyService!.Notify(executor, what, executor);
			}
		}
	}

	/// <summary>
	/// Query player flags and send output preferences to ConnectionServer
	/// </summary>
	private static async Task SyncPlayerOutputPreferences(long handle, SharpObject player)
	{
		var ansiEnabled = await player.Flags.Value.AnyAsync(f =>
			string.Equals(f.Name, "ANSI", StringComparison.OrdinalIgnoreCase));
		var colorEnabled = await player.Flags.Value.AnyAsync(f =>
			string.Equals(f.Name, "COLOR", StringComparison.OrdinalIgnoreCase));
		var xterm256Enabled = await player.Flags.Value.AnyAsync(f =>
			string.Equals(f.Name, "XTERM256", StringComparison.OrdinalIgnoreCase));

		if (MessageBus != null)
		{
			await MessageBus.Publish(new UpdatePlayerPreferencesMessage(
				handle,
				ansiEnabled,
				colorEnabled,
				xterm256Enabled
			));

			Logger?.LogDebug("Synced output preferences for handle {Handle}: ANSI={Ansi}, COLOR={Color}, XTERM256={Xterm}",
				handle, ansiEnabled, colorEnabled, xterm256Enabled);
		}
	}

	/// <summary>
	/// Reads a message file by path, returning its content or null if the file does not exist.
	/// </summary>
	private static async Task<string?> ReadMessageFileAsync(string? filePath)
	{
		if (string.IsNullOrEmpty(filePath)) return null;
		if (!File.Exists(filePath)) return null;
		return await File.ReadAllTextAsync(filePath);
	}

	/// <summary>
	/// The shared post-login sequence run after a handle is bound to <paramref name="player"/>:
	/// triggers <c>PLAYER`CONNECT</c>, refreshes the login room's contents, syncs the connection's
	/// output preferences, and shows post-login messages (MOTD/auto-look). Identical across
	/// CONNECT (name/password and OTT token) and the account-mode MAKE/PLAY commands; guest logins
	/// share the same sequence but additionally show the guest file, hence <paramref name="isGuest"/>.
	/// </summary>
	private static async ValueTask CompletePlayerLoginAsync(
		IMUSHCodeParser parser, long handle, SharpPlayer player, DBRef playerRef, bool isGuest = false)
	{
		// Trigger PLAYER`CONNECT event - PennMUSH compatible
		// PennMUSH spec: player`connect (objid, number of connections, descriptor)
		var connectionCount = await ConnectionService!.Get(playerRef).CountAsync();
		await EventService!.TriggerEventAsync(
			parser,
			"PLAYER`CONNECT",
			playerRef,
			$"#{player.Object.Key}",
			connectionCount.ToString(),
			handle.ToString());

		// Refresh everyone in the room the player just appeared in.
		var connectRoomContainer = await player.Location.WithCancellation(CancellationToken.None);
		await EventService.TriggerEventAsync(
			parser,
			SharpEvents.RoomContents,
			playerRef,
			connectRoomContainer.Object().DBRef.ToString(),
			"connect");

		await SyncPlayerOutputPreferences(handle, player.Object);

		// Show post-login messages and auto-look (PennMUSH-compatible login experience)
		await ShowPostLoginMessages(parser, handle, new AnySharpObject(player), isGuest);
	}

	/// <summary>
	/// Shows post-login messages (MOTD, wizard MOTD, guest file) and performs an auto-look.
	/// Matches PennMUSH login experience.
	/// </summary>
	private static async Task ShowPostLoginMessages(IMUSHCodeParser parser, long handle, AnySharpObject player, bool isGuest = false)
	{
		var motdData = await ObjectDataService!.GetExpandedServerDataAsync<MotdData>();

		var motdText = motdData?.ConnectMotd;
		if (string.IsNullOrWhiteSpace(motdText))
		{
			motdText = await ReadMessageFileAsync(Configuration!.CurrentValue.Message.MessageOfTheDayFile);
		}
		if (!string.IsNullOrWhiteSpace(motdText))
		{
			await NotifyService!.Notify(handle, motdText);
		}

		if (await player.IsWizard() || await player.IsRoyalty())
		{
			var wizmotdText = motdData?.WizardMotd;
			if (string.IsNullOrWhiteSpace(wizmotdText))
			{
				wizmotdText = await ReadMessageFileAsync(Configuration!.CurrentValue.Message.WizMessageOfTheDayFile);
			}
			if (!string.IsNullOrWhiteSpace(wizmotdText))
			{
				await NotifyService!.Notify(handle, wizmotdText);
			}
		}

		if (isGuest)
		{
			var guestText = await ReadMessageFileAsync(Configuration!.CurrentValue.Message.GuestFile);
			if (!string.IsNullOrWhiteSpace(guestText))
			{
				await NotifyService!.Notify(handle, guestText);
			}
		}

		await parser.CommandParse(handle, ConnectionService!, MModule.single("look"));
	}

	[GeneratedRegex("^(?<User>\"(?:.+?)\"|(?:.+?))(?:\\s+(?<Password>\\S+))?$")]
	private static partial Regex ConnectionPattern();
}
