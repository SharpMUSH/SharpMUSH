using System.Globalization;
using Mediator;
using Microsoft.Extensions.Logging;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Notifications;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Services;

/// <summary>
/// Ports PennMUSH's announce_connect/announce_disconnect (src/bsd.c:5906-6017): room/inventory
/// broadcasts, a SUSPECT-&gt;WIZARD broadcast, the HEAR_CONNECT-flagged broadcast, and ACONNECT/
/// ADISCONNECT attribute-hook dispatch to the player, their room, their zone, and the master room.
/// </summary>
public class ConnectionAnnounceService(
	ICommunicationService communicationService,
	IGameBroadcastService gameBroadcastService,
	IAttributeService attributeService,
	IOptionsWrapper<SharpMUSHOptions> configuration,
	IMediator mediator,
	ILogger<ConnectionAnnounceService> logger) : IConnectionAnnounceService
{
	/// <inheritdoc />
	public async ValueTask AnnounceConnectAsync(IMUSHCodeParser parser, AnySharpObject player, int connectionCount, bool isHiddenConnection)
	{
		try
		{
			var isDark = await player.IsDark();
			var name = player.Object().Name;
			var wording = (isHiddenConnection, connectionCount > 1) switch
			{
				(true, true) => ErrorMessages.Notifications.GameHasHiddenReconnected,
				(true, false) => ErrorMessages.Notifications.GameHasHiddenConnected,
				(false, true) => ErrorMessages.Notifications.GameHasReconnected,
				(false, false) => ErrorMessages.Notifications.GameHasConnected,
			};
			var fullMessage = $"{name} {wording}";

			if (await player.HasFlag("SUSPECT"))
			{
				await gameBroadcastService.BroadcastToFlagAsync(
					"WIZARD", string.Format(ErrorMessages.Notifications.GameSuspectActivity, fullMessage));
			}

			var gameLine = $"GAME: {fullMessage}";
			if (isDark)
			{
				await gameBroadcastService.BroadcastToFlagAsync(["ROYALTY", "WIZARD"], "HEAR_CONNECT", gameLine);
			}
			else
			{
				await gameBroadcastService.BroadcastToFlagAsync(null, "HEAR_CONNECT", gameLine);
			}

			await BroadcastAnnouncementAsync(player, fullMessage, isDark, isHiddenConnection);

			await QueueHookAsync(parser, player, player, "ACONNECT", connectionCount.ToString());

			if (configuration.CurrentValue.Attribute.RoomConnects)
			{
				var loc = await player.Where();
				var locObj = loc.WithExitOption();
				if (locObj.IsRoom || locObj.IsThing)
				{
					await QueueHookAsync(parser, locObj, player, "ACONNECT", connectionCount.ToString());
				}
			}

			await DispatchZoneAndMasterRoomHooksAsync(parser, player, "ACONNECT", connectionCount.ToString());
		}
		catch (Exception ex)
		{
			// Log error but don't propagate - a broken ACONNECT hook (or a transient failure
			// resolving the player's zone/master room) shouldn't skip whatever the caller runs
			// after this, most concretely the room-contents refresh other players depend on.
			logger.LogError(ex, "Error announcing connect for player {Player}", player.Object().DBRef);
		}
	}

	/// <inheritdoc />
	public async ValueTask AnnounceDisconnectAsync(IMUSHCodeParser parser, AnySharpObject player, int remainingConnections, bool isHiddenConnection)
	{
		try
		{
			var isDark = await player.IsDark();
			var name = player.Object().Name;
			var wording = (isHiddenConnection, remainingConnections > 0) switch
			{
				(true, true) => ErrorMessages.Notifications.GameHasPartiallyHiddenDisconnected,
				(true, false) => ErrorMessages.Notifications.GameHasHiddenDisconnected,
				(false, true) => ErrorMessages.Notifications.GameHasPartiallyDisconnected,
				(false, false) => ErrorMessages.Notifications.GameHasDisconnected,
			};
			var fullMessage = $"{name} {wording}";

			if (await player.HasFlag("SUSPECT"))
			{
				await gameBroadcastService.BroadcastToFlagAsync(
					"WIZARD", string.Format(ErrorMessages.Notifications.GameSuspectActivity, fullMessage));
			}

			var gameLine = $"GAME: {fullMessage}";
			if (isDark)
			{
				await gameBroadcastService.BroadcastToFlagAsync(["ROYALTY", "WIZARD"], "HEAR_CONNECT", gameLine);
			}
			else
			{
				await gameBroadcastService.BroadcastToFlagAsync(null, "HEAR_CONNECT", gameLine);
			}

			await BroadcastAnnouncementAsync(player, fullMessage, isDark, isHiddenConnection);

			await QueueHookAsync(parser, player, player, "ADISCONNECT", remainingConnections.ToString());

			if (configuration.CurrentValue.Attribute.RoomConnects)
			{
				var loc = await player.Where();
				var locObj = loc.WithExitOption();
				if (locObj.IsRoom || locObj.IsThing)
				{
					await QueueHookAsync(parser, locObj, player, "ADISCONNECT", remainingConnections.ToString());
				}
			}

			await DispatchZoneAndMasterRoomHooksAsync(parser, player, "ADISCONNECT", remainingConnections.ToString());

			if (remainingConnections == 0)
			{
				var lastLogout = DateTimeOffset.UtcNow.ToLocalTime()
					.ToString("ddd MMM dd HH:mm:ss yyyy", CultureInfo.InvariantCulture);

				// LASTLOGOUT is engine-maintained bookkeeping, not a player-authored write - PennMUSH
				// stamps it with GOD's ownership (src/bsd.c:6162: atr_add(player, "LASTLOGOUT", ..., GOD,
				// 0)), matching this codebase's convention for other automatic attribute writes (see e.g.
				// GeneralCommands.cs's SetAttributeCommand call sites, all stamped with #1/God). Going
				// through AttributeService.SetAttributeAsync's permission-gated wrapper is wrong here:
				// LASTLOGOUT is seeded wizard-flagged (AttributeEntrySeed.cs), so once the attribute exists
				// (created by the player's first-ever disconnect), PermissionService.CanSetInternal denies
				// every subsequent write from a mortal player - freezing LASTLOGOUT after one update.
				// Sending SetAttributeCommand directly bypasses that permission gate entirely, the same
				// way the engine's other automatic bookkeeping writes do.
				var god = await HelperFunctions.GetGod(mediator);
				await mediator.Send(new SetAttributeCommand(
					player.Object().DBRef, ["LASTLOGOUT"], MarkupText.Plain(lastLogout), god.AsPlayer));
			}
		}
		catch (Exception ex)
		{
			// Log error but don't propagate - a broken ADISCONNECT hook (or a transient failure
			// resolving the player's zone/master room) shouldn't skip whatever the caller runs
			// after this, most concretely the room-contents refresh other players depend on.
			logger.LogError(ex, "Error announcing disconnect for player {Player}", player.Object().DBRef);
		}
	}

	/// <summary>
	/// Isolates the room/inventory/channel broadcast (gated on <c>Cosmetic.AnnounceConnects</c>) with
	/// its own try/catch, mirroring <see cref="QueueHookAsync"/>'s. Without this, a broadcast failure
	/// (a bad room reference, a permission-check exception, any other transient failure inside
	/// <see cref="ICommunicationService.SendToRoomAsync"/> or <see cref="AnnounceOnChannelsAsync"/>)
	/// would jump straight to the caller's outer catch, skipping the ACONNECT/ADISCONNECT hook
	/// dispatch and (on disconnect) LASTLOGOUT that are meant to run regardless. Shared between
	/// <see cref="AnnounceConnectAsync"/> and <see cref="AnnounceDisconnectAsync"/>, whose broadcast
	/// sections are otherwise identical.
	/// </summary>
	private async ValueTask BroadcastAnnouncementAsync(
		AnySharpObject player, string fullMessage, bool isDark, bool isHiddenConnection)
	{
		if (!configuration.CurrentValue.Cosmetic.AnnounceConnects)
		{
			return;
		}

		try
		{
			await communicationService.SendToRoomAsync(
				player, player.AsContainer, _ => fullMessage, INotifyService.NotificationType.Announce);

			if (!isDark)
			{
				var loc = await player.Where();
				await communicationService.SendToRoomAsync(
					player, loc, _ => fullMessage, INotifyService.NotificationType.Announce,
					excludeObjects: [player]);
			}

			await AnnounceOnChannelsAsync(player, fullMessage, isHiddenConnection);
		}
		catch (Exception ex)
		{
			// Log error but don't propagate - a broadcast failure must not prevent the caller from
			// reaching the hook dispatch (and, on disconnect, LASTLOGOUT) that follow it.
			logger.LogError(ex, "Error broadcasting connect/disconnect announcement for player {Player}", player.Object().DBRef);
		}
	}

	/// <summary>
	/// Ports the zone (src/bsd.c:5994-6011) and master-room (src/bsd.c:6012-6015) traversal from
	/// announce_connect; announce_disconnect (:6085-6127) does the same walk. If the player's zone is
	/// set and is a Thing, the hook runs once on the zone itself; if it's a Room, the hook runs on
	/// every object in the zone's contents. The hook then always runs on every object in the master
	/// room's contents.
	/// </summary>
	private async ValueTask DispatchZoneAndMasterRoomHooksAsync(
		IMUSHCodeParser parser, AnySharpObject player, string attrName, string countArg)
	{
		// PennMUSH zones the player's LOCATION, not the player itself (bsd.c:5992, loc = Location(player)) -
		// zones are attached to rooms, so reading player.Object().Zone directly was dead code in practice.
		var loc = await player.Where();
		var zoneRelation = await loc.Object().Zone.WithCancellation(CancellationToken.None);
		if (!zoneRelation.IsNone)
		{
			var zone = zoneRelation.Known;
			if (zone.IsThing)
			{
				await QueueHookAsync(parser, zone, player, attrName, countArg);
			}
			else if (zone.IsRoom)
			{
				await foreach (var content in zone.AsContainer.Content(mediator))
				{
					await QueueHookAsync(parser, content.WithRoomOption(), player, attrName, countArg);
				}
			}
		}

		var masterRoomDbref = new DBRef(Convert.ToInt32(configuration.CurrentValue.Database.MasterRoom));
		var masterRoomResult = await mediator.Send(new GetObjectNodeQuery(masterRoomDbref));
		if (!masterRoomResult.IsNone)
		{
			await foreach (var content in masterRoomResult.Known.AsContainer.Content(mediator))
			{
				await QueueHookAsync(parser, content.WithRoomOption(), player, attrName, countArg);
			}
		}
	}

	/// <summary>
	/// Ports the channel-broadcast portion of chat_player_announce (src/extchat.c:3187-3205): the
	/// connect/disconnect line is published to every channel the player belongs to, skipping channels
	/// with the "Quiet" privilege. A line from a hidden connection - or from a member who is hidden on
	/// that particular channel - goes out CB_SEEALL, so only See_All members (and the player
	/// themselves) receive it; that is PennMUSH's
	/// <c>if (Chanuser_Hide(up) || (desc_player-&gt;hide == 1))</c> at :3190. Per-viewer
	/// CHATFORMAT/combine formatting remains out of scope for this port.
	/// </summary>
	private async ValueTask AnnounceOnChannelsAsync(
		AnySharpObject player, string fullMessage, bool isHiddenConnection)
	{
		var playerNumber = player.Object().DBRef.Number;

		await foreach (var channel in mediator.CreateStream(new GetOnChannelQuery(player)))
		{
			if (channel.HasPriv("Quiet"))
			{
				continue;
			}

			var hiddenOnChannel = isHiddenConnection;
			if (!hiddenOnChannel)
			{
				var membership = await channel.Members.Value
					.FirstOrDefaultAsync(x => x.Member.Object().DBRef.Number == playerNumber);
				hiddenOnChannel = membership?.Status.Hide ?? false;
			}

			await mediator.Publish(new ChannelMessageNotification(
				channel,
				player.WithNoneOption(),
				INotifyService.NotificationType.Announce,
				MarkupText.Plain(fullMessage),
				MarkupText.Empty,
				MarkupText.Plain(player.Object().Name),
				MarkupText.Empty,
				[],
				SeeAllOnly: hiddenOnChannel,
				// CB_CHECKQUIET (src/extchat.c:3234): connect and disconnect lines are exactly what
				// @channel/mute exists to suppress.
				CheckQuiet: true));
		}
	}

	/// <summary>
	/// Ports PennMUSH's queue_attribute_base plus the %0/%1 PE_REGS binding: runs
	/// <paramref name="attrName"/> on <paramref name="owner"/> (if set) as a fresh command-list
	/// evaluation, with %0 reserved (empty, matching ACONNECT/ADISCONNECT) and %1 the connection
	/// count. <paramref name="owner"/> is the hook's executor/%!; <paramref name="player"/> (the
	/// connecting/disconnecting player) is both enactor/%# and caller/%@.
	/// </summary>
	/// <remarks>
	/// PennMUSH queues each hook independently, so one broken global object (a bad ACONNECT on a
	/// zone or master-room object) can't take out the others. Catching here - rather than only in
	/// the outer <c>AnnounceConnectAsync</c>/<c>AnnounceDisconnectAsync</c> try/catch - means a
	/// throwing hook never stops the caller from reaching its later hooks or (on disconnect)
	/// LASTLOGOUT.
	/// </remarks>
	private async ValueTask QueueHookAsync(
		IMUSHCodeParser parser, AnySharpObject owner, AnySharpObject player, string attrName, string countArg)
	{
		try
		{
			// The read/execute permission check is whether OWNER may run its own attribute, not whether
			// the connecting/disconnecting PLAYER may - queue_attribute_base (src/bsd.c) is an automatic,
			// system-triggered execution that runs with the hook owner's own authority. Passing `player`
			// here as the executor made this check PermissionService.CanEvalAttr(player, owner, ...), which
			// fails whenever owner is a WIZARD/ROYALTY-flagged object (common for master-room utility
			// objects) and player is an ordinary mortal - silently skipping the hook for the single most
			// common real-world configuration. Self-evaluation (owner reading its own attribute) always
			// satisfies PermissionService.CanEval regardless of owner's own privilege level.
			var attrResult = await attributeService.GetAttributeAsync(
				owner, owner, attrName, IAttributeService.AttributeMode.Execute, parent: true);

			if (!attrResult.IsAttribute || attrResult.AsAttribute.Length == 0)
			{
				return;
			}

			// %0 is reserved (PennMUSH leaves it unset for ACONNECT/ADISCONNECT), %1 is the connection count.
			var argsDict = new Dictionary<string, CallState> { ["0"] = new(string.Empty), ["1"] = new(countArg) };

			var ownerRef = owner.Object().DBRef;
			var playerRef = player.Object().DBRef;
			var isEmpty = parser.State.IsEmpty;

			var evalParser = parser.Push(new ParserState(
				Registers: new([[]]),
				IterationRegisters: [],
				RegexRegisters: [],
				SwitchStack: [],
				ExecutionStack: [],
				EnvironmentRegisters: argsDict,
				CurrentEvaluation: null,
				ParserFunctionDepth: 0,
				Function: null,
				Command: null,
				CommandInvoker: isEmpty
					? _ => ValueTask.FromResult(new Option<CallState>(new None()))
					: parser.CurrentState.CommandInvoker,
				Switches: [],
				Arguments: argsDict,
				Executor: ownerRef,
				Enactor: playerRef,
				Caller: playerRef,
				Handle: isEmpty ? null : parser.CurrentState.Handle,
				CallDepth: isEmpty ? new InvocationCounter() : parser.CurrentState.CallDepth ?? new InvocationCounter(),
				FunctionRecursionDepths: isEmpty
					? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
					: parser.CurrentState.FunctionRecursionDepths ?? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
				TotalInvocations: isEmpty ? new InvocationCounter() : parser.CurrentState.TotalInvocations ?? new InvocationCounter(),
				LimitExceeded: isEmpty ? new LimitExceededFlag() : parser.CurrentState.LimitExceeded ?? new LimitExceededFlag())
			{
				MoveDepth = isEmpty ? new InvocationCounter() : parser.CurrentState.MoveDepth ?? new InvocationCounter()
			});

			var attributeText = attrResult.AsAttribute.Last().Value.ToPlainText();
			await evalParser.CommandListParse(MarkupText.Plain(attributeText));
		}
		catch (Exception ex)
		{
			// Log error but don't propagate - a broken hook on one object (player/room/zone/master
			// room object) must not prevent the caller from queuing the next hook or, on disconnect,
			// writing LASTLOGOUT.
			logger.LogError(ex, "Error running {AttrName} hook on {Owner} for player {Player}",
				attrName, owner.Object().DBRef, player.Object().DBRef);
		}
	}
}
