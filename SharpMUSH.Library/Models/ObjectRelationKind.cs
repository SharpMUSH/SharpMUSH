namespace SharpMUSH.Library.Models;

/// <summary>
/// A single-valued edge from one object to another, as <see cref="INavigationStore.GetRelationRefAsync"/> reads it.
/// </summary>
public enum ObjectRelationKind
{
	/// <summary>The player that owns the object.</summary>
	Owner,

	/// <summary>The object's parent, if any.</summary>
	Parent,

	/// <summary>The object's zone, if any.</summary>
	Zone,

	/// <summary>
	/// The home edge: a player's or thing's home, a room's drop-to and an exit's destination are all this one
	/// edge, read the same way.
	/// </summary>
	Home
}
