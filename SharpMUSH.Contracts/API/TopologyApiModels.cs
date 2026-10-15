namespace SharpMUSH.Library.API;

/// <summary>Read-only room topology; object identities are dbrefs, names are labels.</summary>
/// <param name="Coordinates">x, y and z from the room's own coordinate attributes, a missing axis as 0; null when the room has none of them, or one is not a number.</param>
/// <param name="Warnings">What <c>@wcheck</c> would say about the room, whatever the reader's own <c>@warnings</c>.</param>
public sealed record TopologyRoomDto(string Dbref, string Name, double[]? Coordinates = null, IReadOnlyList<TopologyWarningDto>? Warnings = null);
public sealed record TopologyExitDto(string Dbref, string Name, string Source, string? Destination, string Status, IReadOnlyList<TopologyWarningDto>? Warnings = null);
public sealed record TopologyDto(IReadOnlyList<TopologyRoomDto> Rooms, IReadOnlyList<TopologyExitDto> Exits, bool Truncated);

/// <summary>One warning: its name as <c>@warnings</c> spells it, and what is wrong.</summary>
public sealed record TopologyWarningDto(string Name, string Message);

/// <summary>
/// A set of rooms as text a builder can keep, diff and edit: <c>format</c> <see cref="RoomDocumentDto.FormatName"/>,
/// version <see cref="RoomDocumentDto.CurrentVersion"/>. Rooms and exits are in dbref order, so two exports of an
/// unchanged world are identical.
/// </summary>
/// <param name="Coordinates">The three attributes the rooms' coordinates are read from and written to.</param>
public sealed record RoomDocumentDto(string Format, int Version, IReadOnlyList<string> Coordinates, IReadOnlyList<RoomDocumentRoomDto> Rooms)
{
	public const string FormatName = "sharpmush-rooms";
	public const int CurrentVersion = 1;
}

/// <remarks>
/// Read back for an import, a field left out (or null) leaves what is there alone, and only the rooms and exits a
/// document lists are compared. An import never destroys anything.
/// </remarks>
/// <param name="Id">The room's dbref, <c>#10</c>; in an import, a name of the builder's own (<c>shed</c>) digs a new room.</param>
/// <param name="Description">The room's own DESCRIBE, as plain text; empty when it has none.</param>
/// <param name="Coordinates">x, y and z; null when the room has none. In an import an empty list removes them.</param>
public sealed record RoomDocumentRoomDto(string Id, string? Name, string? Description, double[]? Coordinates, IReadOnlyList<RoomDocumentExitDto>? Exits);

/// <param name="Id">The exit's dbref, <c>#11</c>; in an import, a name of the builder's own opens a new exit.</param>
/// <param name="Name">The exit's name followed by its aliases, <c>North;n</c>, as <c>@name</c> and <c>@open</c> take it.</param>
/// <param name="To">
/// Where the exit leads: a room's dbref, <c>home</c>, <c>variable</c> (a destination worked out when it is used),
/// <c>unlinked</c>, <c>non-room</c> when it leads to a player or thing, or <c>hidden</c> when the reader may not see
/// its destination.
/// </param>
public sealed record RoomDocumentExitDto(string Id, string? Name, string? To, string? Description);

/// <summary>What importing a room document would change, in the order it would be done.</summary>
/// <param name="Problems">Why the document cannot be imported as it is; an import with any is refused.</param>
/// <param name="Notes">What the import leaves alone that the builder may not expect, such as an exit the document does not list.</param>
/// <param name="Fingerprint">Names this exact list of changes; an apply is refused when the changes it works out differ.</param>
public sealed record RoomPlanDto(IReadOnlyList<RoomPlanStepDto> Steps, IReadOnlyList<string> Problems, IReadOnlyList<string> Notes, string Fingerprint);

/// <param name="Action"><c>dig</c>, <c>rename</c>, <c>describe</c>, <c>place</c>, <c>open</c>, <c>link</c> or <c>unlink</c>.</param>
/// <param name="Target">The dbref, or the document's own name for something the import makes.</param>
/// <param name="Summary">The change in a sentence.</param>
public sealed record RoomPlanStepDto(string Action, string Target, string Summary);

/// <param name="Fingerprint">The <see cref="RoomPlanDto.Fingerprint"/> of the plan the builder confirmed.</param>
public sealed record RoomApplyRequest(RoomDocumentDto Document, string Fingerprint);

/// <summary>What an import did. It stops at the first change the game refuses; the changes before it stay made.</summary>
/// <param name="Done">The summaries of the changes made, in order.</param>
/// <param name="Failed">The change that was refused and why, or null when every change was made.</param>
/// <param name="Created">The dbref of each room and exit the import made, by the document's name for it.</param>
public sealed record RoomApplyDto(IReadOnlyList<string> Done, string? Failed, IReadOnlyDictionary<string, string> Created);
