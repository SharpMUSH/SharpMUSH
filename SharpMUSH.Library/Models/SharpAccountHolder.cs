namespace SharpMUSH.Library.Models;

/// <summary>
/// The holder of an account's attributes (<see cref="AccountHolders"/>): a thing that is in no room, owned by God,
/// with no flags, powers, locks, parent or zone, whose only use is to carry attributes. It is never stored as an
/// object, and is found only by the commands and functions that read and write attributes.
/// </summary>
public sealed class SharpAccountHolder : SharpThing
{
	/// <summary>The account whose attributes these are, as <c>node_accounts/&lt;key&gt;</c>.</summary>
	public required string AccountId { get; init; }

	/// <summary>The dbref numbers of the characters on the account.</summary>
	public required AsyncRelation<IReadOnlySet<int>> Characters { get; init; }
}
