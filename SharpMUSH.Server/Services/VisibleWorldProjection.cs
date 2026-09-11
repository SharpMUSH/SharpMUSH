using Mediator;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Portal;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Reality;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Server.Services;

public interface IVisibleWorldProjection
{
	ValueTask<SharpPlayer?> ResolveCharacterAsync(CapabilityActor actor, CancellationToken ct = default);
	ValueTask<bool> CanObserveRoomAsync(CapabilityActor actor, DBRef room, CancellationToken ct = default);
	ValueTask<bool> CanSubscribeRoomAsync(CapabilityActor actor, DBRef room, CancellationToken ct = default);
	ValueTask<bool> CanReceiveRoomEventAsync(CapabilityActor actor, DBRef room, DBRef source, RoomEventType type, CancellationToken ct = default);
	ValueTask<EngineStateResponse?> GetStateAsync(CapabilityActor actor, CancellationToken ct = default);
}

/// <summary>Fresh authenticated identity and shared game visibility for web reads and event delivery.</summary>
public sealed class VisibleWorldProjection(IAdministrativeCapabilityService capabilities, IMediator mediator,
	IPermissionService permissions, IRealityPolicy reality, IConnectionService connections) : IVisibleWorldProjection
{
	public const int MaxVisibleObjects = 1000;

	public async ValueTask<SharpPlayer?> ResolveCharacterAsync(CapabilityActor actor, CancellationToken ct = default)
	{
		if (actor.ActiveCharacter is not { IsObjid: true } character || actor.Executor != character
			|| await capabilities.GetGameActorAsync(character, ct) != actor) return null;
		return await mediator.Send(new GetObjectNodeQuery(character), ct) is AnySharpObject and SharpPlayer player
			&& player.Object.DBRef == character
				? player
				: null;
	}

	private async ValueTask<AnySharpContainer?> CurrentLocationAsync(SharpPlayer player, CancellationToken ct)
	{
		return await mediator.Send(new GetLocationQuery(player.Object.DBRef), ct) is AnySharpContainer location ? location : null;
	}

	private async ValueTask<AnySharpContainer?> VisibleLocationAsync(SharpPlayer player, CancellationToken ct)
		=> await CurrentLocationAsync(player, ct) is { } room
			&& await reality.CanPerceiveAsync(player.Object.DBRef, room.Object().DBRef, ct) ? room : null;

	public async ValueTask<bool> CanObserveRoomAsync(CapabilityActor actor, DBRef room, CancellationToken ct = default)
	{
		if (!room.IsObjid || await ResolveCharacterAsync(actor, ct) is not { } player) return false;
		return await VisibleLocationAsync(player, ct) is { } location && location.Object().DBRef == room;
	}

	public async ValueTask<bool> CanSubscribeRoomAsync(CapabilityActor actor, DBRef room, CancellationToken ct = default)
	{
		if (!room.IsObjid || await ResolveCharacterAsync(actor, ct) is not { } player) return false;
		return await CurrentLocationAsync(player, ct) is { } location && location.Object().DBRef == room;
	}

	public async ValueTask<bool> CanReceiveRoomEventAsync(CapabilityActor actor, DBRef room, DBRef source,
		RoomEventType type, CancellationToken ct = default)
	{
		if (!room.IsObjid || !source.IsObjid || !Enum.IsDefined(type)
			|| await ResolveCharacterAsync(actor, ct) is not { } player) return false;
		if (await CurrentLocationAsync(player, ct) is not { } location || location.Object().DBRef != room) return false;
		if (await mediator.Send(new GetObjectNodeQuery(source), ct) is not AnySharpObject sender || sender.Object().DBRef != source
			|| !await reality.CanPerceiveAsync(player.Object.DBRef, source, ct)) return false;
		// Legacy permission implementations have no token parameter. Give built-in reads the
		// dispatch lifetime and bound this read-only await for implementations without cancellation.
		using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(ct, ExecutionBudget.CurrentToken);
		using var budget = ExecutionBudget.FromMilliseconds(0, cancellation.Token);
		using var scope = budget.Enter();
		budget.ThrowIfExceeded();
		// Hearing is directed from sender to receiver; visual interaction is viewer to target.
		var interaction = type is RoomEventType.Say or RoomEventType.Pose
			? permissions.CanInteract(sender, new AnySharpObject(player), IPermissionService.InteractType.Hear)
			: permissions.CanInteract(player, sender, IPermissionService.InteractType.See);
		return await interaction.AsTask().WaitAsync(budget.Token);
	}

	public async ValueTask<EngineStateResponse?> GetStateAsync(CapabilityActor actor, CancellationToken ct = default)
	{
		if (await ResolveCharacterAsync(actor, ct) is not { } player
			|| await VisibleLocationAsync(player, ct) is not { } room) return null;
		var visible = new List<string>();
		var truncated = false;
		var canSeeContent = await WorldVisibility.CreateScanAsync(player, room.WithExitOption(), reality, connections, ct);
		await foreach (var item in mediator.CreateStream(new GetContentsQuery(room), ct))
		{
			ct.ThrowIfCancellationRequested();
			if (!await canSeeContent(item, ct)) continue;
			if (visible.Count == MaxVisibleObjects) { truncated = true; break; }
			visible.Add(item.Object().DBRef.ToString());
		}
		// Do not publish a snapshot for an account unlinked or a character moved during the scan.
		if (!await CanObserveRoomAsync(actor, room.Object().DBRef, ct)) return null;
		return new(player.Object.DBRef.ToString(), room.Object().DBRef.ToString(), room.Object().Name, visible, truncated);
	}
}
