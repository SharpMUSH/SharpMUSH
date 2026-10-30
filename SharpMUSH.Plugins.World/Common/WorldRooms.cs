using Mediator;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Plugins.World.Storage;

namespace SharpMUSH.Plugins.World.Common;

/// <summary>
/// The rooms that stand on a stored map's places. A place with no room of its own is lent one when someone goes
/// there: a spare room when there is one, else a new one. A lent room that has held nothing for
/// <see cref="Grace"/> goes back and waits as a spare for any map; spares past <see cref="SpareKeep"/>, or waiting
/// longer than <see cref="SpareMaxIdle"/>, are destroyed the ordinary way. Rooms are never destroyed in between,
/// so the recover list and the dbref allocator see a lent room only once it is no longer wanted.
/// </summary>
public sealed class WorldRooms(
	IMediator mediator,
	WorldStore store,
	IAttributeService attributes,
	IObjectDestructionService destruction,
	IEnumerable<IFreedObjectCleanup> cleanups,
	TimeProvider time)
{
	/// <summary>The attribute every room standing on a place holds: the place's id.</summary>
	public const string PlaceAttribute = "MAP`PLACE";

	/// <summary>The attribute on a map naming the parent of the rooms it is lent.</summary>
	public const string CellParentAttribute = "MAP`CELLPARENT";

	/// <summary>The name a spare room waits under.</summary>
	public const string SpareName = "Spare room";

	/// <summary>How long a lent room stays empty before it goes back.</summary>
	public static readonly TimeSpan Grace = TimeSpan.FromMinutes(5);

	/// <summary>How many spare rooms wait at most.</summary>
	public const int SpareKeep = 50;

	/// <summary>How long a spare room waits at most.</summary>
	public static readonly TimeSpan SpareMaxIdle = TimeSpan.FromDays(7);

	private static readonly string[] PlacePath = PlaceAttribute.Split('`');

	/// <summary>
	/// The room standing at <paramref name="at"/> on <paramref name="map"/>: the one placed or lent there, else one
	/// lent there now. An error when nothing is painted there.
	/// </summary>
	public async ValueTask<Result<DBRef>> RoomAsync(AnySharpObject map, WorldMapRecord record, WorldPosition at)
	{
		var dbref = map.Object().DBRef;
		if (await StandingAsync(dbref, at) is { } standing) return standing;
		if (record.Type(store.Cell(dbref, at.X, at.Y, at.Z).Terrain) is not { } type)
			return new Error<string>("#-1 NOTHING THERE");

		var owner = await map.Object().Owner.WithCancellation(CancellationToken.None);
		var name = store.Place(dbref, at.X, at.Y, at.Z)?.Name
			?? $"{(await new WorldLegend(attributes, map).TerrainAsync(type)).Label} ({at})";
		var room = await SpareAsync(owner.Object.DBRef) ?? await mediator.Send(new CreateRoomCommand(name, owner));
		if (await store.LendAsync(dbref, at.X, at.Y, at.Z, room) is not { } lent) return new Error<string>("#-1 NOT A WORLD MAP");
		if (lent.Number != room.Number)
		{
			// Someone else lent the place a room first: this one waits for the next.
			await store.AddSpareAsync(room, owner.Object.DBRef, time.GetUtcNow().ToUnixTimeMilliseconds());
			return lent;
		}

		if (await mediator.Send(new GetObjectNodeQuery(room)) is not AnySharpObject lentRoom)
			return new Error<string>("#-1 NOTHING THERE");
		await mediator.Send(new SetNameCommand(lentRoom, MarkupText.Plain(name)));
		await mediator.Send(new SetObjectZoneCommand(lentRoom, map));
		await mediator.Send(await CellParentAsync(map) is { } parent
			? new SetObjectParentCommand(lentRoom, parent)
			: new UnsetObjectParentCommand(lentRoom));
		if (await mediator.Send(new GetObjectFlagQuery("FLOATING")) is { } floating)
			await mediator.Send(new SetObjectFlagCommand(lentRoom, floating));
		await MarkAsync(lentRoom, owner, map, at);
		return room;
	}

	/// <summary>The room standing at <paramref name="at"/>, when it still exists; a record for one that does not is dropped.</summary>
	public async ValueTask<DBRef?> StandingAsync(DBRef map, WorldPosition at)
	{
		if (store.StandingAt(map, at.X, at.Y, at.Z) is not { } standing) return null;
		if (await mediator.Send(new GetObjectNodeQuery(standing)) is AnySharpObject) return standing;
		await store.UnstandAsync(standing, release: false);
		return null;
	}

	/// <summary>Writes the place's id on a room standing on it.</summary>
	public async ValueTask MarkAsync(AnySharpObject room, SharpPlayer owner, AnySharpObject map, WorldPosition at) =>
		await mediator.Send(new SetAttributeCommand(room.Object().DBRef, PlacePath,
			MarkupText.Plain(WorldPlaceId.Of(map.Object().DBRef, at.X, at.Y, at.Z)), owner));

	/// <summary>Takes the place's id off a room that no longer stands on it.</summary>
	public async ValueTask UnmarkAsync(DBRef room) => await mediator.Send(new ClearAttributeCommand(room, PlacePath));

	/// <summary>
	/// A spare room of <paramref name="owner"/>'s that still exists, taken off the list; null when there is none. A spare
	/// keeps whatever was put on it, so it is lent again only to its owner's maps, never handed to another player.
	/// </summary>
	private async ValueTask<DBRef?> SpareAsync(DBRef owner)
	{
		while (await store.TakeSpareAsync(owner) is { } spare)
		{
			if (await mediator.Send(new GetObjectNodeQuery(spare)) is AnySharpObject and { IsRoom: true }) return spare;
		}
		return null;
	}

	private async ValueTask<AnySharpObject?> CellParentAsync(AnySharpObject map) =>
		await attributes.GetAttributeAsync(map, map, CellParentAttribute, IAttributeService.AttributeMode.Read, parent: false)
			is SharpAttribute[] { Length: > 0 } chain
		&& DBRef.TryParse(chain[^1].Value.ToPlainText().Trim(), out var parentRef)
		&& parentRef is { } found
		&& await mediator.Send(new GetObjectNodeQuery(found)) is AnySharpObject parent
			? parent
			: null;

	/// <summary>
	/// One pass over the rooms: a lent room empty for <paramref name="grace"/> (<see cref="Grace"/> unless given) goes
	/// back, a room let go is tidied, and spares past what is kept are destroyed. With no grace, a room goes back as
	/// soon as it is empty. Answers how many rooms went back and how many spares were destroyed.
	/// </summary>
	public async ValueTask<(int Returned, int Destroyed)> SweepAsync(IMUSHCodeParser parser, TimeSpan? grace = null,
		CancellationToken cancellationToken = default)
	{
		var now = time.GetUtcNow().ToUnixTimeMilliseconds();
		var wait = (long)(grace ?? Grace).TotalMilliseconds;
		var returned = 0;
		foreach (var (room, record) in store.Rooms())
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (await mediator.Send(new GetObjectNodeQuery(room), cancellationToken) is not AnySharpObject)
			{
				await store.UnstandAsync(room, release: false);
				continue;
			}
			if (record.Permanent) continue;
			if (await HoldsAnythingAsync(room, cancellationToken))
			{
				if (record.EmptySince is not null) await store.SetEmptySinceAsync(room, null);
				continue;
			}
			if ((record.EmptySince ?? (wait == 0 ? now : null)) is not { } since) await store.SetEmptySinceAsync(room, now);
			else if (now - since >= wait && await store.UnstandAsync(room, release: true) is not null) returned++;
		}

		foreach (var (room, release) in store.Releases())
		{
			cancellationToken.ThrowIfCancellationRequested();
			// A lent room let go while it holds anyone, as when its map is destroyed, waits until it is empty.
			if (release.Lent && await HoldsAnythingAsync(room, cancellationToken)) continue;
			if (await TidyAsync(room, release.Lent, cancellationToken) is { } owner) await store.AddSpareAsync(room, owner, now);
			await store.DropReleaseAsync(room);
		}

		var destroyed = 0;
		foreach (var room in Surplus(store.Spares(), now))
		{
			cancellationToken.ThrowIfCancellationRequested();
			await store.DropSpareAsync(room);
			if (await mediator.Send(new GetObjectNodeQuery(room), cancellationToken) is AnySharpObject gone
				&& await destruction.FreeObjectAsync(parser, gone, cancellationToken))
				destroyed++;
		}
		return (returned, destroyed);
	}

	/// <summary>
	/// The spares to destroy, from <paramref name="spares"/> listed longest waiting first: those past the newest
	/// <see cref="SpareKeep"/>, and any that waited <see cref="SpareMaxIdle"/>.
	/// </summary>
	public static IReadOnlyList<DBRef> Surplus(IReadOnlyList<(DBRef Room, WorldSpareRecord Record)> spares, long now) =>
	[
		.. spares
			.Where((spare, index) => spares.Count - index > SpareKeep || now - spare.Record.Since >= (long)SpareMaxIdle.TotalMilliseconds)
			.Select(spare => spare.Room),
	];

	/// <summary>Whether <paramref name="room"/> holds anyone or anything.</summary>
	public async ValueTask<bool> HoldsAnythingAsync(DBRef room, CancellationToken cancellationToken = default)
	{
		await foreach (var _ in mediator.CreateStream(new GetContentsQuery(room), cancellationToken)) return true;
		return false;
	}

	/// <summary>
	/// A room let go of its place: its id comes off and, when it was lent, it is made spare: renamed, out of the map's
	/// zone and parent, and let go by everything that keeps rooms by number (a scene held there), as if freed. Answers
	/// its owner when it is to wait as a spare, else null.
	/// </summary>
	private async ValueTask<DBRef?> TidyAsync(DBRef room, bool lent, CancellationToken cancellationToken)
	{
		if (await mediator.Send(new GetObjectNodeQuery(room), cancellationToken) is not AnySharpObject found) return null;
		await UnmarkAsync(room);
		if (!lent || !found.IsRoom) return null;
		await mediator.Send(new SetNameCommand(found, MarkupText.Plain(SpareName)), cancellationToken);
		await mediator.Send(new UnsetObjectZoneCommand(found), cancellationToken);
		await mediator.Send(new UnsetObjectParentCommand(found), cancellationToken);
		foreach (var cleanup in cleanups.Where(cleanup => cleanup is not WorldStore))
		{
			await cleanup.ObjectsFreedAsync([room], cancellationToken);
		}
		return (await found.Object().Owner.WithCancellation(cancellationToken)).Object.DBRef;
	}
}
