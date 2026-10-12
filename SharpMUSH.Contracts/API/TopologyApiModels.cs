namespace SharpMUSH.Library.API;

/// <summary>Read-only room topology; object identities are dbrefs, names are labels.</summary>
public sealed record TopologyRoomDto(string Dbref, string Name);
public sealed record TopologyExitDto(string Dbref, string Name, string Source, string? Destination, string Status);
public sealed record TopologyDto(IReadOnlyList<TopologyRoomDto> Rooms, IReadOnlyList<TopologyExitDto> Exits, bool Truncated);
