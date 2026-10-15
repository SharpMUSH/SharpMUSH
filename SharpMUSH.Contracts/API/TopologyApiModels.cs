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

/// <param name="Id">The room's dbref, <c>#10</c>.</param>
/// <param name="Description">The room's own DESCRIBE, as plain text; empty when it has none.</param>
public sealed record RoomDocumentRoomDto(string Id, string Name, string Description, double[]? Coordinates, IReadOnlyList<RoomDocumentExitDto> Exits);

/// <param name="Id">The exit's dbref, <c>#11</c>.</param>
/// <param name="Name">The exit's name followed by its aliases, <c>North;n</c>, as <c>@name</c> and <c>@open</c> take it.</param>
/// <param name="To">
/// Where the exit leads: a room's dbref, <c>home</c>, <c>variable</c> (a destination worked out when it is used),
/// <c>unlinked</c>, <c>non-room</c> when it leads to a player or thing, or <c>hidden</c> when the reader may not see
/// its destination.
/// </param>
public sealed record RoomDocumentExitDto(string Id, string Name, string To, string Description);
