using Mediator;
using OneOf.Types;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
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
	IMediator mediator) : IConnectionAnnounceService
{
	/// <inheritdoc />
	public async ValueTask AnnounceConnectAsync(IMUSHCodeParser parser, AnySharpObject player, int connectionCount)
	{
		var isDark = await player.IsDark();
		var name = player.Object().Name;
		var wording = connectionCount > 1
			? ErrorMessages.Notifications.GameHasReconnected
			: ErrorMessages.Notifications.GameHasConnected;
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

		if (configuration.CurrentValue.Cosmetic.AnnounceConnects)
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
		}

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

	/// <inheritdoc />
	public ValueTask AnnounceDisconnectAsync(IMUSHCodeParser parser, AnySharpObject player, int remainingConnections)
		=> throw new NotImplementedException("Implemented in Task 5");

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
		var zoneRelation = await player.Object().Zone.WithCancellation(CancellationToken.None);
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
	/// Ports PennMUSH's queue_attribute_base plus the %0/%1 PE_REGS binding: runs
	/// <paramref name="attrName"/> on <paramref name="owner"/> (if set) as a fresh command-list
	/// evaluation, with %0 reserved (empty, matching ACONNECT/ADISCONNECT) and %1 the connection
	/// count. <paramref name="owner"/> is the hook's executor/%!; <paramref name="player"/> (the
	/// connecting/disconnecting player) is both enactor/%# and caller/%@.
	/// </summary>
	private async ValueTask QueueHookAsync(
		IMUSHCodeParser parser, AnySharpObject owner, AnySharpObject player, string attrName, string countArg)
	{
		var attrResult = await attributeService.GetAttributeAsync(
			player, owner, attrName, IAttributeService.AttributeMode.Execute, parent: true);

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
			LimitExceeded: isEmpty ? new LimitExceededFlag() : parser.CurrentState.LimitExceeded ?? new LimitExceededFlag()));

		var attributeText = attrResult.AsAttribute.Last().Value.ToPlainText();
		await evalParser.CommandListParse(MarkupText.Plain(attributeText));
	}
}
