namespace SharpMUSH.Library.Models;

/// <summary>
/// One attribute of a batched write: its path, its value, the player it belongs to, and the flags its
/// leaf takes on top of those its attribute entry gives it.
/// </summary>
public record AttributeWrite(string[] Path, MString Value, SharpPlayer Owner, IReadOnlyList<SharpAttributeFlag> Flags);
