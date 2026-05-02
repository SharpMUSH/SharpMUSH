using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.DiscriminatedUnions;

/// <summary>
/// A union of SharpPlayer, SharpRoom, SharpExit, SharpThing, and None.
/// Replaces AnyOptionalSharpObject : OneOfBase&lt;SharpPlayer, SharpRoom, SharpExit, SharpThing, None&gt;.
/// </summary>
public union AnyOptionalSharpObject(SharpPlayer, SharpRoom, SharpExit, SharpThing, None)
{
	public bool IsPlayer => Value is SharpPlayer;
	public bool IsRoom   => Value is SharpRoom;
	public bool IsExit   => Value is SharpExit;
	public bool IsThing  => Value is SharpThing;
	public bool IsNone   => Value is null or None;

	public SharpPlayer AsPlayer => (SharpPlayer)Value!;
	public SharpRoom   AsRoom   => (SharpRoom)Value!;
	public SharpExit   AsExit   => (SharpExit)Value!;
	public SharpThing  AsThing  => (SharpThing)Value!;

	public SharpObject? Object => Value switch
	{
		SharpPlayer p => p.Object,
		SharpRoom   r => r.Object,
		SharpExit   e => e.Object,
		SharpThing  t => t.Object,
		_ => null
	};

	public string? Id => Value switch
	{
		SharpPlayer p => p.Id,
		SharpRoom   r => r.Id,
		SharpExit   e => e.Id,
		SharpThing  t => t.Id,
		_ => null
	};

	public AnySharpObject Known => Value switch
	{
		SharpPlayer p => p,
		SharpRoom   r => r,
		SharpExit   e => e,
		SharpThing  t => t,
		_ => throw new ArgumentOutOfRangeException()
	};

	/// <summary>
	/// Converts Player, Room, or Thing to an <see cref="AnyOptionalSharpContainer"/>.
	/// Exit and None both map to <see cref="None"/>.
	/// </summary>
	public AnyOptionalSharpContainer AsOptionalContainer => Value switch
	{
		SharpPlayer p => (AnyOptionalSharpContainer)p,
		SharpRoom   r => (AnyOptionalSharpContainer)r,
		SharpThing  t => (AnyOptionalSharpContainer)t,
		_ => new None()
	};
}
