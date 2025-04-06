namespace SharpMUSH.Library.ExpandedObjectData;

/// <summary>
/// Expanded Mail Data for a Player.
/// </summary>
/// <param name="Folders">All of a Player's Folders.</param>
/// <param name="ActiveFolder">The active Folder.</param>
public record ExpandedMailData(string[]? Folders = null, string? ActiveFolder = null) : AbstractExpandedData;