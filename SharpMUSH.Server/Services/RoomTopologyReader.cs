using Mediator;
using SharpMUSH.Library.API;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Reality;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Server.Services;

/// <summary>Bounded stored-relationship reads. No parser, movement resolver or write service is available here.</summary>
public sealed class RoomTopologyReader(IMediator mediator, IPermissionService permissions, IRealityPolicy reality, IAttributeService attributes)
{
	public const int MaxRooms = 100;
	public const int MaxExits = 500;
	public const int MaxCandidates = 1000;

	private async Task<bool> Readable(AnySharpObject actor, AnySharpObject target, CancellationToken ct) =>
		await reality.CanPerceiveAsync(actor.Object().DBRef, target.Object().DBRef, ct)
		&& await permissions.CanExamine(actor, target).AsTask().WaitAsync(ct);

	public async Task<TopologyDto> ReadAsync(AnySharpObject actor, IReadOnlyList<int> roomIds, CancellationToken ct)
	{
		if (roomIds.Count is < 1 or > MaxRooms || roomIds.Any(id => id < 0)) throw new ArgumentException("Select 1–100 non-negative room dbrefs.");
		var rooms = new List<TopologyRoomDto>();
		var readable = new List<AnySharpObject>();
		foreach (var id in roomIds.Distinct())
		{
			ct.ThrowIfCancellationRequested();
			if (await mediator.Send(new GetObjectNodeQuery(new DBRef(id)), ct) is not AnySharpObject target || !target.IsRoom) continue;
			if (!await Readable(actor, target, ct)) continue;
			readable.Add(target);
			rooms.Add(new($"#{id}", target.Object().Name));
		}
		var allowed = readable.ToDictionary(r => r.Object().DBRef.Number, r => r.Object().DBRef);
		var exits = new List<TopologyExitDto>();
		var candidates = 0;
		foreach (var source in readable)
		{
			await foreach (var exit in mediator.CreateStream(new GetExitsQuery(source.Object().DBRef), ct))
			{
				if (++candidates > MaxCandidates || exits.Count == MaxExits) return new(rooms, exits, true);
				var target = new AnySharpObject(exit);
				if (!await Readable(actor, target, ct)) continue;
				// A stale list entry must not leak an exit moved out of the readable source.
				if ((await exit.Location.WithCancellation(ct)).Object().DBRef != source.Object().DBRef) continue;
				var marker = await attributes.GetAttributeAsync(actor, target, "_LINKTYPE", IAttributeService.AttributeMode.Read, parent: false)
					.AsTask().WaitAsync(ct);
				var markerDenied = marker is Error<string>;
				var linkType = marker is SharpAttribute[] { Length: > 0 } chain ? chain[0].Value.ToPlainText().Trim().ToLowerInvariant() : null;
				var status = markerDenied ? "unavailable" : linkType switch { "variable" => "dynamic", "home" => "home", _ => "unlinked" };
				string? destinationId = null;
				if (!markerDenied && linkType is not ("variable" or "home")
					&& await exit.Home.WithCancellation(ct) is AnySharpContainer destination)
				{
					var objectTarget = destination.WithExitOption();
					if (!await Readable(actor, objectTarget, ct)) status = "unavailable";
					else if (!objectTarget.IsRoom) status = "non-room";
					else if (allowed.TryGetValue(objectTarget.Object().DBRef.Number, out var identity) && identity == objectTarget.Object().DBRef)
					{
						status = "linked";
						destinationId = $"#{identity.Number}";
					}
					else status = "outside-selection";
				}
				exits.Add(new($"#{exit.Object.DBRef.Number}", exit.Object.Name, $"#{source.Object().DBRef.Number}", destinationId, status));
			}
		}
		return new(rooms, exits, false);
	}
}
