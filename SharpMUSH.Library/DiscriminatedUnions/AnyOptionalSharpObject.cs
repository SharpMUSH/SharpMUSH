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

	public AnySharpObject Known => Value switch
	{
		SharpPlayer p => p,
		SharpRoom   r => r,
		SharpExit   e => e,
		SharpThing  t => t,
		_ => throw new ArgumentOutOfRangeException()
	};
}
