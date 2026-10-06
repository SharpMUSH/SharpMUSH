using DotNext.Collections.Generic;
using System.Globalization;
using Microsoft.Extensions.Logging;
using SharpMUSH.Implementation.Common;
using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Commands.Database;
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
using SharpMUSH.Library.Common;

namespace SharpMUSH.Implementation.Commands;

public partial class Commands
{
	private static readonly Regex ConnectionPatternRegex = ConnectionPattern();

	[SharpCommand(Name = "WHO", Behavior = CommandBehavior.SOCKET | CommandBehavior.NoParse, MinArgs = 0, MaxArgs = 1, ParameterNames = [])]
	public async ValueTask<Option<CallState>> Who(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		// WHO is CommandBehavior.SOCKET precisely so it answers at the connect screen, where nothing
		// is bound to the handle yet. KnownExecutorObject() throws on that None, so resolve the
		// executor optionally: an anonymous viewer is never a wizard and gets the mortal listing.
		var executor = await parser.CurrentState.ExecutorObject(Mediator) is AnySharpObject found ? found : null;
		var isWizard = executor is not null && await executor.IsWizard();

		var everyone = ConnectionService.GetAll();

		// PennMUSH dump_users formats (src/bsd.c): header widths and per-row layout are replicated verbatim.
		string header = isWizard
			? string.Format("{0,-16} {1,6} {2,9} {3,5} {4,5} {5,-4} {6}",
				"Player Name", "Loc #", "On For", "Idle", "Cmds", "Des", "Host")
			: string.Format("{0,-16} {1,10} {2,6}  {3}", "Player Name", "On For", "Idle", "Doing");

		var lines = new List<string>();
		await foreach (var player in everyone
			.Where(player => player.Ref.HasValue && (isWizard || player.PresenceClass != PresenceClasses.Portal)))
		{
			// PennMUSH dump_users skips a descriptor whose player is not a GoodObject. A handle can
			// outlive the object it is bound to — @nuke a connected player, or a stale entry recovered
			// from the state store — so that descriptor is omitted rather than taking the whole listing
			// down with an #-1 EXCEPTION.
			if (await Mediator.Send(new GetObjectNodeQuery(player.Ref!.Value)) is not AnySharpObject known)
			{
				continue;
			}

			var name = known.Object().Name;
			var namePadded = name.PadToColumns(16);
			var onFor = TimeHelpers.TimeString(player.Connected ?? TimeSpan.Zero, accuracy: 3);
			var idle = TimeHelpers.TimeString(player.Idle ?? TimeSpan.Zero);
			var isDark = await known.HasFlag("DARK");
			// A row is treated as hidden-from-mortals either because the object itself is DARK,
			// or because this specific connection is Hidden (PennMUSH DESC.hide / @HIDE) — the
			// latter doesn't touch the object's flags, so it can't be seen via HasFlag("DARK").
			var isHiddenRow = isDark || player.IsHidden;

			string line;
			if (isWizard)
			{
				var location = known.IsContent
					? "#" + ((await known.AsContent.Location())?.Object().DBRef.Number.ToString() ?? "-1")
					: "#-1";
				// Host truncated + " (Dark)" for dark/hidden players, else truncated to 27 (PennMUSH).
				var host = isHiddenRow
					? (player.HostName.Length > 20 ? player.HostName[..20] : player.HostName) + " (Dark)"
					: (player.HostName.Length > 27 ? player.HostName[..27] : player.HostName);
				// "Des" is the descriptor (handle) plus connection-type flags: S=SSL, L=local, W=WebSocket.
				line = $"{namePadded} {location,6} {onFor,9} {idle,5}  {player.CommandCount,4} {player.Handle,3}{ConnType(player)} {host}";
			}
			else
			{
				// @doing is read without permission checks (get_doing, bsd.c:6251), so the connect
				// screen, which has no executor, shows the same column a logged-in viewer sees.
				var doingText = await GetDoingText(parser, executor ?? known, known);
				line = $"{namePadded} {onFor,10}   {idle,4}{(isHiddenRow ? 'D' : ' ')} {doingText}";
			}

			// CanSee(viewer, target) is `viewer.IsPriv() || viewer.IsSee_All() || !target.IsDark()`,
			// which only knows about the DARK flag. isHiddenRow folds in the per-connection Hidden
			// state too (see above), so the row is visible when either CanSee's own privilege
			// exemption applies, or the row isn't hidden by either mechanism. An anonymous
			// connect-screen viewer has no executor and is never privileged.
			var visible = executor is null
				? !isHiddenRow
				: !isHiddenRow || await executor.IsPriv() || await executor.IsSee_All();

			if (visible)
			{
				lines.Add(line);
			}
		}

		var count = lines.Count;
		var footer = count switch
		{
			0 => "There are no players connected.",
			1 => "There is one player connected.",
			_ => $"There are {count} players connected."
		};

		var message = $"{header}\n{string.Join('\n', lines)}\n{footer}";

		await NotifyService.Notify(handle: parser.CurrentState.Handle!.Value, what: message);

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

	/// <summary>
	/// PennMUSH's alternate login words (<c>bsd.c:4431-4497</c>): <c>cd</c> connects and forces the
	/// player's <c>DARK</c> flag on (and hides the connection if the player has permission), <c>cv</c>
	/// connects and forces <c>DARK</c> off, <c>ch</c> connects and hides the connection if permitted
	/// without touching <c>DARK</c>. Plain <c>connect</c> is <see cref="Normal"/> and touches neither.
	/// </summary>
	private enum ConnectMode
	{
		Normal,
		Dark,
		Visible,
		Hidden
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
	public ValueTask<Option<CallState>> Connect(IMUSHCodeParser parser, SharpCommandAttribute _2)
		=> ConnectCoreAsync(parser, ConnectMode.Normal);

	/// <summary>PennMUSH <c>cd</c>: connect and force the player's <c>DARK</c> flag on (<c>bsd.c:4431-4497</c>).</summary>
	[SharpCommand(Name = "CD", Behavior = CommandBehavior.SOCKET | CommandBehavior.NoParse, MinArgs = 1,
		MaxArgs = 2, ParameterNames = ["player", "password"])]
	public ValueTask<Option<CallState>> ConnectDark(IMUSHCodeParser parser, SharpCommandAttribute _2)
		=> ConnectCoreAsync(parser, ConnectMode.Dark);

	/// <summary>PennMUSH <c>cv</c>: connect and force the player's <c>DARK</c> flag off (<c>bsd.c:4431-4497</c>).</summary>
	[SharpCommand(Name = "CV", Behavior = CommandBehavior.SOCKET | CommandBehavior.NoParse, MinArgs = 1,
		MaxArgs = 2, ParameterNames = ["player", "password"])]
	public ValueTask<Option<CallState>> ConnectVisible(IMUSHCodeParser parser, SharpCommandAttribute _2)
		=> ConnectCoreAsync(parser, ConnectMode.Visible);

	/// <summary>PennMUSH <c>ch</c>: connect and hide the connection if permitted, <c>DARK</c> untouched (<c>bsd.c:4431-4497</c>).</summary>
	[SharpCommand(Name = "CH", Behavior = CommandBehavior.SOCKET | CommandBehavior.NoParse, MinArgs = 1,
		MaxArgs = 2, ParameterNames = ["player", "password"])]
	public ValueTask<Option<CallState>> ConnectHidden(IMUSHCodeParser parser, SharpCommandAttribute _2)
		=> ConnectCoreAsync(parser, ConnectMode.Hidden);

	private async ValueTask<Option<CallState>> ConnectCoreAsync(IMUSHCodeParser parser, ConnectMode mode)
	{
		if (ConnectionService.Get(parser.CurrentState.Handle!.Value)?.Ref is not null)
		{
			await NotifyService.Notify(parser.CurrentState.Handle!.Value, "Huh?  (Type \"help\" for help.)");
			return new CallState(ErrorMessages.Returns.AlreadyConnected);
		}

		var match = ConnectionPatternRegex.Match(parser.CurrentState.Arguments["0"].Message!.ToPlainText());
		var username = match.Groups["User"].Value;
		var password = match.Groups["Password"].Value;

		var handle = parser.CurrentState.Handle!.Value;
		var connectionData = ConnectionService.Get(handle);
		var ipAddress = connectionData?.Metadata.TryGetValue("InternetProtocolAddress", out var ip) == true ? ip : "unknown";
		var hostName = connectionData?.HostName ?? ipAddress;

		// Task 15: sitelock gate for the whole connect surface (character login, OTT/token login,
		// AND guest login below all count as a "game connection"). Checked before any username/
		// credential parsing so a blocked site never learns whether a name it tried is valid.
		if (SitelockMatcher.IsBlocked(Configuration.CurrentValue.SitelockRules.Rules, ipAddress, hostName, SitelockMatcher.ConnectFlag))
		{
			await NotifyService.Notify(handle, "Access from your location is restricted.");
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
			await EventService.TriggerEventAsync("SOCKET`LOGINFAIL",
				null, // System event
				handle.ToString(),
				ipAddress,
				"1", // count - simplified for now
				"invalid player name",
				"#-1", // no valid player
				username);

			await NotifyService.Notify(handle, "Could not find that player.");
			return new CallState(ErrorMessages.Returns.PlayerNotFound);
		}

		var nameItem = nameItems.First();

		var foundDB = nameItem switch
		{
			DBRef dbref => await Mediator.Send(new GetObjectNodeQuery(dbref)) is AnySharpObject and SharpPlayer player
				? player
				: null,
			string name => await Mediator.CreateStream(new GetPlayerQuery(name)).FirstOrDefaultAsync()
		};

		if (foundDB is null)
		{
			// Trigger SOCKET`LOGINFAIL for player not found
			// PennMUSH spec: socket`loginfail (descriptor, IP, count, reason, playerobjid, name)
			await EventService.TriggerEventAsync("SOCKET`LOGINFAIL",
				null, // System event
				handle.ToString(),
				ipAddress,
				"1", // count - simplified for now
				"player not found",
				"#-1", // no valid player
				username);

			await NotifyService.Notify(handle, "Could not find that player.");
			return new CallState(ErrorMessages.Returns.PlayerNotFound);
		}

		var validPassword = PasswordService.PasswordIsValid(password, foundDB.PasswordHash);

		if (!validPassword && !string.IsNullOrEmpty(foundDB.PasswordHash))
		{
			// Trigger SOCKET`LOGINFAIL for invalid password
			// PennMUSH spec: socket`loginfail (descriptor, IP, count, reason, playerobjid, name)
			await EventService.TriggerEventAsync("SOCKET`LOGINFAIL",
				null, // System event
				handle.ToString(),
				ipAddress,
				"1", // count - simplified for now
				"invalid password",
				$"#{foundDB.Object.Key}", // valid player objid
				foundDB.Object.Name);

			await NotifyService.Notify(handle, "Invalid Password.");
			return new CallState(ErrorMessages.Returns.InvalidPassword);
		}

		// Rehash legacy PennMUSH passwords to modern PBKDF2 format on successful login
		if (validPassword && PasswordService.NeedsRehash(foundDB.PasswordHash))
		{
			await PasswordService.RehashPasswordAsync(foundDB, password);
			Logger?.LogInformation("Rehashed legacy password for player #{Key}", foundDB.Object.Key);
		}

		if (await AccountRefusalAsync(foundDB.Object.DBRef) is { } refusal)
		{
			await EventService.TriggerEventAsync("SOCKET`LOGINFAIL", null,
				handle.ToString(), ipAddress, "1", "account not active", $"#{foundDB.Object.Key}", foundDB.Object.Name);
			await NotifyService.Notify(handle, refusal);
			return new CallState(ErrorMessages.Returns.PermissionDenied);
		}

		if (!Configuration.CurrentValue.Net.Logins
			&& !await new AnySharpObject(foundDB).IsWizard())
		{
			await NotifyService.Notify(handle, "Logins are disabled.");
			return new CallState(ErrorMessages.Returns.PermissionDenied);
		}

		var playerDbRef = new DBRef(foundDB.Object.Key, foundDB.Object.CreationTime);
		await ConnectionService.Bind(parser.CurrentState.Handle!.Value, playerDbRef);

		if (mode != ConnectMode.Normal)
		{
			var connectedPlayer = new AnySharpObject(foundDB);

			if (mode is ConnectMode.Dark or ConnectMode.Hidden && await connectedPlayer.CanHide())
			{
				ConnectionService.Update(parser.CurrentState.Handle!.Value, "Hidden", "1");
			}

			if (mode is ConnectMode.Dark or ConnectMode.Visible)
			{
				var darkFlag = await Mediator.Send(new GetObjectFlagQuery("DARK"));
				if (darkFlag is not null)
				{
					if (mode == ConnectMode.Dark)
					{
						// PennMUSH's set_flag special-cases DARK: only a Wizard or a player with the
						// Can_Dark power may set it on a living player (flags.c ~1793-1798) - cd must
						// respect the same gate rather than force DARK on unconditionally. cv (clearing
						// DARK) has no such special case in PennMUSH - clearing your own flag only needs
						// ordinary self-set permission, which every player already has - so it stays
						// ungated here.
						if (await connectedPlayer.CanDark())
						{
							await Mediator.Send(new SetObjectFlagCommand(connectedPlayer, darkFlag));
						}
					}
					else
					{
						await Mediator.Send(new UnsetObjectFlagCommand(connectedPlayer, darkFlag));
					}
				}
			}
		}

		await CompletePlayerLoginAsync(parser, parser.CurrentState.Handle!.Value, foundDB, playerDbRef);
		Logger?.LogDebug("Successful login and binding for {@person}", foundDB.Object);
		return new CallState(playerDbRef);
	}

	private async ValueTask<Option<CallState>> HandleTokenLogin(
		IMUSHCodeParser parser, long handle, string token, string ipAddress)
	{
		var playerDbRef = await OttStore.ValidateAndConsumeAsync(token);

		if (playerDbRef is null)
		{
			await EventService.TriggerEventAsync("SOCKET`LOGINFAIL", null,
				handle.ToString(), ipAddress, "1",
				"invalid or expired login token", "#-1", "token");
			await NotifyService.Notify(handle, "Invalid or expired login token.");
			Logger?.LogWarning("OTT login failed for handle {Handle} from {IP}: token invalid/expired", handle, ipAddress);
			return new CallState(ErrorMessages.Returns.InvalidPassword);
		}

		var playerNode = await Mediator.Send(new GetObjectNodeQuery(playerDbRef.Value));
		if (playerNode is not (AnySharpObject and SharpPlayer foundPlayer))
		{
			await NotifyService.Notify(handle, "Could not find that player.");
			return new CallState(ErrorMessages.Returns.PlayerNotFound);
		}

		// A login token outlives the session that minted it by up to its lifetime; a ban in between still holds.
		if (await AccountRefusalAsync(foundPlayer.Object.DBRef) is { } refusal)
		{
			await NotifyService.Notify(handle, refusal);
			return new CallState(ErrorMessages.Returns.PermissionDenied);
		}

		if (!Configuration.CurrentValue.Net.Logins
			&& !await new AnySharpObject(foundPlayer).IsWizard())
		{
			await NotifyService.Notify(handle, "Logins are disabled.");
			return new CallState(ErrorMessages.Returns.PermissionDenied);
		}

		await ConnectionService.Bind(handle, playerDbRef.Value);

		await CompletePlayerLoginAsync(parser, handle, foundPlayer, playerDbRef.Value);
		Logger?.LogInformation("OTT login succeeded for player {Name} (#{Key}) from {IP}",
			foundPlayer.Object.Name, foundPlayer.Object.Key, ipAddress);
		return new CallState(playerDbRef.Value);
	}

	/// <summary>
	/// What a character is told when its account may not sign in (disabled, banned or closed), or null when
	/// it may. A character with no account is not refused here.
	/// </summary>
	private async ValueTask<string?> AccountRefusalAsync(DBRef player)
		=> await AccountService.GetAccountForCharacterAsync(player) is { IsActive: false } account
			? (await AccountService.UnavailableAsync(account)).Message
			: null;

	private async ValueTask<Option<CallState>> HandleGuestLogin(IMUSHCodeParser parser, long handle, string ipAddress, string hostName)
	{
		// Task 15: guest-specific sitelock gate, on top of (not instead of) the !connect gate
		// already applied in Connect() above — a site can allow normal logins but disallow guests.
		if (SitelockMatcher.IsBlocked(Configuration.CurrentValue.SitelockRules.Rules, ipAddress, hostName, SitelockMatcher.GuestFlag))
		{
			await NotifyService.Notify(handle, "Access from your location is restricted.");
			return new CallState(ErrorMessages.Returns.PermissionDenied);
		}

		if (!Configuration.CurrentValue.Net.Logins)
		{
			await NotifyService.Notify(handle, "Logins are disabled.");
			return new CallState(ErrorMessages.Returns.PermissionDenied);
		}

		if (!Configuration.CurrentValue.Net.Guests)
		{
			await NotifyService.Notify(handle, "Guest logins are not enabled.");
			return new CallState(ErrorMessages.Returns.GuestLoginsDisabled);
		}

		var guestPlayers = await GuestCharacters.AllAsync(Mediator).ToListAsync();

		if (guestPlayers.Count == 0)
		{
			await EventService.TriggerEventAsync("SOCKET`LOGINFAIL",
				null,
				handle.ToString(),
				ipAddress,
				"1",
				"no guest characters available",
				"#-1",
				"guest");

			await NotifyService.Notify(handle, "Sorry, there are no guest characters available.");
			return new CallState(ErrorMessages.Returns.NoGuestCharacters);
		}

		var maxGuests = Configuration.CurrentValue.Limit.MaxGuests;

		SharpPlayer? selectedGuest = null;

		if (maxGuests == -1)
		{
			selectedGuest = await guestPlayers.ToAsyncEnumerable()
				.FirstOrDefaultAsync(async (guest, ct) =>
				{
					var guestDbRef = new DBRef(guest.Object.Key, guest.Object.CreationTime);
					return !await ConnectionService.Get(guestDbRef).AnyAsync(ct);
				});

			if (selectedGuest == null)
			{
				await EventService.TriggerEventAsync("SOCKET`LOGINFAIL",
					null,
					handle.ToString(),
					ipAddress,
					"1",
					"all guest characters in use",
					"#-1",
					"guest");

				await NotifyService.Notify(handle, "Sorry, all guest characters are currently in use.");
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
					return ConnectionService.Get(guestDbRef).CountAsync(ct);
				})
				.SumAsync();

			if (totalGuestConnections >= maxGuests)
			{
				await EventService.TriggerEventAsync("SOCKET`LOGINFAIL",
					null,
					handle.ToString(),
					ipAddress,
					"1",
					"maximum guest connections reached",
					"#-1",
					"guest");

				await NotifyService.Notify(handle, "Sorry, the maximum number of guest connections has been reached.");
				return new CallState(ErrorMessages.Returns.MaxGuestsReached);
			}

			SharpPlayer? leastUsedGuest = null;
			var minConnections = int.MaxValue;

			foreach (var guest in guestPlayers)
			{
				var guestDbRef = new DBRef(guest.Object.Key, guest.Object.CreationTime);
				var guestConnections = await ConnectionService.Get(guestDbRef).CountAsync();

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
			await EventService.TriggerEventAsync("SOCKET`LOGINFAIL",
				null,
				handle.ToString(),
				ipAddress,
				"1",
				"unexpected guest selection failure",
				"#-1",
				"guest");

			await NotifyService.Notify(handle, "Sorry, there are no guest characters available.");
			return new CallState(ErrorMessages.Returns.GuestSelectionFailed);
		}

		var playerDbRef = new DBRef(selectedGuest.Object.Key, selectedGuest.Object.CreationTime);
		await ConnectionService.Bind(handle, playerDbRef);

		await CompletePlayerLoginAsync(parser, handle, selectedGuest, playerDbRef, isGuest: true);
		Logger?.LogDebug("Successful guest login for {@guest}", selectedGuest.Object);
		return new CallState(playerDbRef);
	}

	[SharpCommand(Name = "QUIT", Behavior = CommandBehavior.SOCKET | CommandBehavior.NoParse, MinArgs = 0, MaxArgs = 0, ParameterNames = [])]
	public async ValueTask<Option<CallState>> Quit(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var handle = parser.CurrentState.Handle!.Value;

		// QUIT is a SOCKET command: it must also work at the connect screen, where the handle has
		// no executor bound and KnownExecutorObject() would throw. Fall back to notifying the
		// socket directly in that case.
		var executor = await parser.CurrentState.ExecutorObject(Mediator) is AnySharpObject found ? found : null;

		await NotifyQuitAsync(MarkupText.Plain("GOODBYE."));

		var quitText = await ReadMessageFileAsync(Configuration.CurrentValue.Message.QuitFile);
		if (!string.IsNullOrWhiteSpace(quitText))
		{
			await NotifyQuitAsync(MarkupText.Plain(quitText));
		}

		await ConnectionService.Disconnect(handle);

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
				await NotifyService.Notify(handle, what);
			}
			else
			{
				await NotifyService.Notify(executor, what, executor);
			}
		}
	}

	/// <summary>
	/// Query player flags and send output preferences to ConnectionServer
	/// </summary>
	private async Task SyncPlayerOutputPreferences(long handle, SharpObject player)
	{
		var ansiEnabled = await player.HasFlag("ANSI");
		var colorEnabled = await player.HasFlag("COLOR");
		var xterm256Enabled = await player.HasFlag("XTERM256");
		var truecolorEnabled = await player.HasFlag("TRUECOLOR");

		if (MessageBus != null)
		{
			await MessageBus.Publish(new UpdatePlayerPreferencesMessage(
				handle,
				ansiEnabled,
				colorEnabled,
				xterm256Enabled,
				truecolorEnabled
			));

			Logger?.LogDebug("Synced output preferences for handle {Handle}: ANSI={Ansi}, COLOR={Color}, XTERM256={Xterm}, TRUECOLOR={Truecolor}",
				handle, ansiEnabled, colorEnabled, xterm256Enabled, truecolorEnabled);
		}
	}

	/// <summary>
	/// Reads a message file by path, returning its content or null if the file does not exist.
	/// </summary>
	private async Task<string?> ReadMessageFileAsync(string? filePath)
	{
		if (string.IsNullOrEmpty(filePath)) return null;
		if (!File.Exists(filePath)) return null;
		return await File.ReadAllTextAsync(filePath);
	}

	/// <summary>
	/// The shared post-login sequence run after a handle is bound to <paramref name="player"/>:
	/// syncs output preferences, shows login messages, announces the connection, queues the connect
	/// events and hooks, and performs auto-look. Identical across
	/// CONNECT (name/password and OTT token) and the account-mode MAKE/PLAY commands; guest logins
	/// share the same sequence but additionally show the guest file, hence <paramref name="isGuest"/>.
	/// </summary>
	private async ValueTask CompletePlayerLoginAsync(
		IMUSHCodeParser parser, long handle, SharpPlayer player, DBRef playerRef, bool isGuest = false)
	{
		// PennMUSH check_connect (src/bsd.c:4369-4377): announce_connect queues PLAYER`CONNECT and the
		// ACONNECT hooks, then the look runs in place. The events and hooks below are queue entries of
		// their own, each with its own time limit, so none of them can run the login out of time; they
		// run after the look.
		await SyncPlayerOutputPreferences(handle, player.Object);
		await ShowPostLoginMessages(handle, new AnySharpObject(player), isGuest);

		// Trigger PLAYER`CONNECT event - PennMUSH compatible
		// PennMUSH spec: player`connect (objid, number of connections, descriptor)
		var connectionCount = await ConnectionService.Get(playerRef).CountAsync();
		await EventService.TriggerEventAsync("PLAYER`CONNECT",
			playerRef,
			$"#{player.Object.Key}",
			connectionCount.ToString(),
			handle.ToString());

		await ConnectionAnnounceService.AnnounceConnectAsync(
			new AnySharpObject(player), connectionCount, ConnectionService.Get(handle)?.IsHidden ?? false);
		await CheckLastAsync(handle, player, isGuest);

		// The player's own channel list, which a fresh connection has not been sent yet.
		await EventService.TriggerEventAsync(SharpEvents.PlayerChannels,
			playerRef,
			player.Object.DBRef.ToString(),
			"connect",
			string.Empty);

		// Refresh everyone in the room the player just appeared in.
		var connectRoomContainer = await player.Location.WithCancellation(CancellationToken.None);
		await EventService.TriggerEventAsync(SharpEvents.RoomContents,
			playerRef,
			connectRoomContainer.Object().DBRef.ToString(),
			"connect");

		await LookAfterLoginAsync(parser, handle, player);
	}

	/// <summary>
	/// The look a login ends with, the one evaluation it does (the room's and player's formats), under a
	/// <c>queue_entry_cpu_time</c> limit of its own: the login line itself is not timed (see
	/// <c>TaskScheduler.RunsSoftcode</c>), so the server's login work before it cannot spend the player's
	/// limit. Running out tells the player "CPU usage exceeded.", unless QUIET, as a queue entry's would.
	/// </summary>
	private async ValueTask LookAfterLoginAsync(IMUSHCodeParser parser, long handle, SharpPlayer player)
	{
		using var budget = ExecutionBudget.FromMilliseconds(Configuration.CurrentValue.Limit.QueueEntryCpuTime,
			ExecutionBudget.CurrentToken);
		using (budget.Enter())
		{
			try
			{
				await parser.FromState(parser.CurrentState with { ExecutionBudget = budget })
					.CommandParse(handle, ConnectionService, MarkupText.Plain("look"));
			}
			catch (OperationCanceledException) when (budget.IsExpired) { }
		}

		if (budget.IsExpired && !await player.Object.HasFlag("QUIET"))
		{
			await NotifyService.NotifyLocalized(player.Object.DBRef, nameof(ErrorMessages.Notifications.CpuUsageExceeded), null);
		}
	}

	/// <summary>
	/// PennMUSH <c>check_last</c> (<c>src/player.c:651-692</c>), run right after announce_connect: tells a
	/// non-guest (on every connection, as Penn's notify_format does) where and when they last connected, and
	/// where their last failed connect came from, then
	/// records this connect in <c>LAST</c>, <c>LASTSITE</c> and <c>LASTIP</c> and clears <c>LASTFAILED</c>.
	/// The writes are God's, as Penn's <c>atr_add(..., GOD, 0)</c> are: the attributes are wizard-flagged.
	/// The paycheck Penn gives on the first connect of a day is not ported.
	/// </summary>
	private async ValueTask CheckLastAsync(long handle, SharpPlayer player, bool isGuest)
	{
		var playerRef = player.Object.DBRef;
		var last = await PlainAttributeAsync(playerRef, "LAST");
		if (!isGuest && last is not null)
		{
			if (await PlainAttributeAsync(playerRef, "LASTSITE") is { } lastSite)
			{
				await NotifyService.NotifyLocalized(playerRef, nameof(ErrorMessages.Notifications.LastConnectFormat), null,
					lastSite, last);
			}

			if (await PlainAttributeAsync(playerRef, "LASTFAILED") is { Length: > 2 } lastFailed)
			{
				await NotifyService.NotifyLocalized(playerRef, nameof(ErrorMessages.Notifications.LastFailedConnectFormat), null,
					lastFailed);
			}
		}

		if (await HelperFunctions.GetGod(Mediator) is not SharpPlayer god) return;
		var connection = ConnectionService.Get(handle);
		var now = DateTimeOffset.UtcNow.ToLocalTime().ToString("ddd MMM dd HH:mm:ss yyyy", CultureInfo.InvariantCulture);
		await Mediator.Send(new SetAttributeCommand(playerRef, ["LAST"], MarkupText.Plain(now), god));
		await Mediator.Send(new SetAttributeCommand(playerRef, ["LASTSITE"],
			MarkupText.Plain(connection?.HostName ?? string.Empty), god));
		await Mediator.Send(new SetAttributeCommand(playerRef, ["LASTIP"],
			MarkupText.Plain(connection?.InternetProtocolAddress ?? string.Empty), god));
		await Mediator.Send(new SetAttributeCommand(playerRef, ["LASTFAILED"], MarkupText.Plain(" "), god));
	}

	/// <summary>An attribute's plain value on the object itself (Penn's <c>atr_get_noparent</c>), or null.</summary>
	private async ValueTask<string?> PlainAttributeAsync(DBRef dbref, string name)
		=> await Mediator.CreateStream(new GetAttributeQuery(dbref, [name])).LastOrDefaultAsync() is { } attribute
			? attribute.Value.ToPlainText()
			: null;

	/// <summary>
	/// Shows required post-login messages (MOTD, wizard MOTD, guest file).
	/// Matches PennMUSH login experience.
	/// </summary>
	private async Task ShowPostLoginMessages(long handle, AnySharpObject player, bool isGuest = false)
	{
		var motdData = await ObjectDataService.GetExpandedServerDataAsync<MotdData>();

		var motdText = motdData?.ConnectMotd;
		if (string.IsNullOrWhiteSpace(motdText))
		{
			motdText = await ReadMessageFileAsync(Configuration.CurrentValue.Message.MessageOfTheDayFile);
		}
		if (!string.IsNullOrWhiteSpace(motdText))
		{
			await NotifyService.Notify(handle, motdText);
		}

		if (await player.IsWizard() || await player.IsRoyalty())
		{
			var wizmotdText = motdData?.WizardMotd;
			if (string.IsNullOrWhiteSpace(wizmotdText))
			{
				wizmotdText = await ReadMessageFileAsync(Configuration.CurrentValue.Message.WizMessageOfTheDayFile);
			}
			if (!string.IsNullOrWhiteSpace(wizmotdText))
			{
				await NotifyService.Notify(handle, wizmotdText);
			}
		}

		if (isGuest)
		{
			var guestText = await ReadMessageFileAsync(Configuration.CurrentValue.Message.GuestFile);
			if (!string.IsNullOrWhiteSpace(guestText))
			{
				await NotifyService.Notify(handle, guestText);
			}
		}
	}

	[GeneratedRegex("^(?<User>\"(?:.+?)\"|(?:.+?))(?:\\s+(?<Password>\\S+))?$")]
	private static partial Regex ConnectionPattern();
}
