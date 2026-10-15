using System.Globalization;
using System.Text.RegularExpressions;
using Mediator;
using SharpMUSH.Library.API;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Reality;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Server.Services;

/// <summary>Bounded stored-relationship reads. No parser, movement resolver or write service is available here.</summary>
public sealed partial class RoomTopologyReader(IMediator mediator, IPermissionService permissions, IRealityPolicy reality,
	IAttributeService attributes, IWarningService warnings)
{
	public const int MaxRooms = 100;
	public const int MaxExits = 500;
	public const int MaxCandidates = 1000;

	/// <summary>The coordinate attributes gmcp-room reads by default, and so the ones a builder is most likely to have set.</summary>
	public static readonly IReadOnlyList<string> DefaultCoordinates = ["X", "Y", "Z"];

	/// <summary>
	/// The checks a room is linted with. One-way and duplicate exits are left to the caller, which sees the graph: the
	/// engine's check counts the return exits in the destination, which may be a room the reader cannot examine.
	/// </summary>
	private const WarningType RoomChecks = WarningType.RoomDesc | WarningType.LockProbs;

	/// <summary>The checks an exit is linted with. Linking is checked only where the reader resolves the destination itself.</summary>
	private const WarningType ExitChecks = WarningType.ExitDesc | WarningType.ExitMsgs | WarningType.LockProbs;

	[GeneratedRegex("^[A-Za-z0-9_`-]{1,64}$")]
	private static partial Regex CoordinateAttributeName();

	/// <summary>Whether <paramref name="names"/> is three attribute names a reader may be asked to read coordinates from.</summary>
	public static bool ValidCoordinates(IReadOnlyList<string> names)
		=> names.Count == 3 && names.All(name => CoordinateAttributeName().IsMatch(name));

	private async Task<bool> Readable(AnySharpObject actor, AnySharpObject target, CancellationToken ct) =>
		await reality.CanPerceiveAsync(actor.Object().DBRef, target.Object().DBRef, ct)
		&& await permissions.CanExamine(actor, target).AsTask().WaitAsync(ct);

	public async Task<TopologyDto> ReadAsync(AnySharpObject actor, IReadOnlyList<int> roomIds, CancellationToken ct,
		IReadOnlyList<string>? coordinates = null)
		=> (await ScanAsync(actor, roomIds, coordinates ?? DefaultCoordinates, describe: false, ct)).Topology;

	/// <summary>
	/// The readable rooms among <paramref name="roomIds"/> and their exits as a <see cref="RoomDocumentDto"/>, in dbref
	/// order. A truncated scan is refused rather than exported, since a document missing exits would read as a world
	/// without them.
	/// </summary>
	public async Task<Result<RoomDocumentDto>> ExportAsync(AnySharpObject actor, IReadOnlyList<int> roomIds,
		IReadOnlyList<string> coordinates, CancellationToken ct)
	{
		var scan = await ScanAsync(actor, roomIds, coordinates, describe: true, ct);
		if (scan.Topology.Truncated)
		{
			return new Error<string>($"These rooms have more than {MaxExits} exits. Export fewer rooms at a time.");
		}

		var rooms = scan.Topology.Rooms
			.OrderBy(room => Number(room.Dbref))
			.Select(room => new RoomDocumentRoomDto(room.Dbref, room.Name, scan.Descriptions[room.Dbref], room.Coordinates,
				[
					.. scan.Topology.Exits
						.Where(exit => exit.Source == room.Dbref)
						.OrderBy(exit => Number(exit.Dbref))
						.Select(exit => new RoomDocumentExitDto(exit.Dbref, scan.ExitNames[exit.Dbref], scan.ExitTargets[exit.Dbref],
							scan.Descriptions[exit.Dbref]))
				]))
			.ToList();
		return new RoomDocumentDto(RoomDocumentDto.FormatName, RoomDocumentDto.CurrentVersion, coordinates, rooms);
	}

	private static int Number(string dbref) => int.Parse(dbref.AsSpan(1), CultureInfo.InvariantCulture);

	/// <summary>What one pass over the rooms found: the topology, and what an export adds to it.</summary>
	private sealed record Scan(
		TopologyDto Topology,
		Dictionary<string, string> Descriptions,
		Dictionary<string, string> ExitNames,
		Dictionary<string, string> ExitTargets);

	private async Task<Scan> ScanAsync(AnySharpObject actor, IReadOnlyList<int> roomIds, IReadOnlyList<string> coordinates,
		bool describe, CancellationToken ct)
	{
		if (roomIds.Count is < 1 or > MaxRooms || roomIds.Any(id => id < 0)) throw new ArgumentException("Select 1–100 non-negative room dbrefs.");
		if (!ValidCoordinates(coordinates)) throw new ArgumentException("Name three coordinate attributes.");
		var descriptions = new Dictionary<string, string>();
		var exitNames = new Dictionary<string, string>();
		var exitTargets = new Dictionary<string, string>();
		var rooms = new List<TopologyRoomDto>();
		var readable = new List<AnySharpObject>();
		foreach (var id in roomIds.Distinct())
		{
			ct.ThrowIfCancellationRequested();
			if (await mediator.Send(new GetObjectNodeQuery(new DBRef(id)), ct) is not AnySharpObject target || !target.IsRoom) continue;
			if (!await Readable(actor, target, ct)) continue;
			readable.Add(target);
			rooms.Add(new($"#{id}", target.Object().Name, await CoordinatesAsync(actor, target, coordinates, ct),
				await WarningsAsync(actor, target, RoomChecks, ct)));
			if (describe) descriptions[$"#{id}"] = await DescriptionAsync(actor, target, ct);
		}
		var allowed = readable.ToDictionary(r => r.Object().DBRef.Number, r => r.Object().DBRef);
		var exits = new List<TopologyExitDto>();
		var candidates = 0;
		foreach (var source in readable)
		{
			await foreach (var exit in mediator.CreateStream(new GetExitsQuery(source.Object().DBRef), ct))
			{
				if (++candidates > MaxCandidates || exits.Count == MaxExits)
					return new(new(rooms, exits, true), descriptions, exitNames, exitTargets);
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
				string? readableDestination = null;
				if (!markerDenied && linkType is not ("variable" or "home")
					&& await exit.Home.WithCancellation(ct) is AnySharpContainer destination)
				{
					var objectTarget = destination.WithExitOption();
					if (!await Readable(actor, objectTarget, ct)) status = "unavailable";
					else
					{
						readableDestination = $"#{objectTarget.Object().DBRef.Number}";
						if (!objectTarget.IsRoom) status = "non-room";
						else if (allowed.TryGetValue(objectTarget.Object().DBRef.Number, out var identity) && identity == objectTarget.Object().DBRef)
						{
							status = "linked";
							destinationId = $"#{identity.Number}";
						}
						else status = "outside-selection";
					}
				}
				// The link checks resolve the destination edge, which this reader leaves alone for a variable, HOME or
				// unreadable link.
				var checks = status is "dynamic" or "home" or "unavailable" ? ExitChecks : ExitChecks | WarningType.ExitUnlinked;
				var dbref = $"#{exit.Object.DBRef.Number}";
				exits.Add(new(dbref, exit.Object.Name, $"#{source.Object().DBRef.Number}", destinationId, status,
					await WarningsAsync(actor, target, checks, ct)));
				if (!describe) continue;
				descriptions[dbref] = await DescriptionAsync(actor, target, ct);
				exitNames[dbref] = string.Join(';', [exit.Object.Name, .. target.Aliases ?? []]);
				// A player or thing is no room to draw or build to, so only its kind is named.
				exitTargets[dbref] = status == "non-room" ? "non-room" : readableDestination ?? status switch
				{
					"dynamic" => "variable",
					"home" => "home",
					"unlinked" => "unlinked",
					_ => "hidden"
				};
			}
		}
		return new(new(rooms, exits, false), descriptions, exitNames, exitTargets);
	}

	private async Task<IReadOnlyList<TopologyWarningDto>> WarningsAsync(AnySharpObject actor, AnySharpObject target,
		WarningType checks, CancellationToken ct)
		=> await warnings.FindWarningsAsync(actor, target, checks)
			.Select(warning => new TopologyWarningDto(warning.Name, warning.Message))
			.ToListAsync(ct);

	/// <summary>The value of <paramref name="name"/> stored on <paramref name="target"/> itself, as plain text; empty when unset or unreadable.</summary>
	private async Task<string> OwnValueAsync(AnySharpObject actor, AnySharpObject target, string name, CancellationToken ct)
		=> await attributes.GetAttributeAsync(actor, target, name, IAttributeService.AttributeMode.Read, parent: false)
			.AsTask().WaitAsync(ct) is SharpAttribute[] { Length: > 0 } chain
			? chain[^1].Value.ToPlainText()
			: string.Empty;

	private Task<string> DescriptionAsync(AnySharpObject actor, AnySharpObject target, CancellationToken ct)
		=> OwnValueAsync(actor, target, "DESCRIBE", ct);

	/// <summary>
	/// x, y and z as gmcp-room reads them: a missing axis is 0, and a room with none set, or with one that is not a
	/// number, has no coordinates.
	/// </summary>
	private async Task<double[]?> CoordinatesAsync(AnySharpObject actor, AnySharpObject room, IReadOnlyList<string> names,
		CancellationToken ct)
	{
		var values = new string[names.Count];
		for (var i = 0; i < names.Count; i++) values[i] = (await OwnValueAsync(actor, room, names[i], ct)).Trim();
		if (values.All(string.IsNullOrEmpty)) return null;
		var axes = new double[values.Length];
		for (var i = 0; i < values.Length; i++)
		{
			if (values[i].Length == 0) continue;
			if (!double.TryParse(values[i], NumberStyles.Float, CultureInfo.InvariantCulture, out axes[i]) || !double.IsFinite(axes[i])) return null;
		}
		return axes;
	}
}
