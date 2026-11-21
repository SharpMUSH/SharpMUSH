namespace SharpMUSH.Library.Models;

/// <summary>
/// One attribute of a batched write: its path, its value, the player it belongs to, and the flags its
/// leaf takes on top of those its attribute entry gives it.
/// </summary>
public record AttributeWrite(string[] Path, MString Value, SharpPlayer Owner, IReadOnlyList<SharpAttributeFlag> Flags)
{
	/// <summary>
	/// Whether <see cref="Flags"/> are all the leaf has, as a loaded database stores them: the attribute entry's
	/// default flags are not added, and a branch the write creates on the way has only <c>branch</c> (PennMUSH's
	/// <c>atr_new_add</c> while it loads a dump).
	/// </summary>
	public bool StoredFlags { get; init; }
}
