using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.DiscriminatedUnions;

/// <summary>
/// A union of SharpPlayer, SharpExit, SharpThing, and None.
/// Replaces AnyOptionalSharpContent : OneOfBase&lt;SharpPlayer, SharpExit, SharpThing, None&gt;.
/// </summary>
public union AnyOptionalSharpContent(SharpPlayer, SharpExit, SharpThing, None)
{
	public bool IsPlayer => Value is SharpPlayer;
	public bool IsExit   => Value is SharpExit;
	public bool IsThing  => Value is SharpThing;
	public bool IsNone   => Value is null or None;

	public SharpPlayer AsPlayer => (SharpPlayer)Value!;
	public SharpExit   AsExit   => (SharpExit)Value!;
	public SharpThing  AsThing  => (SharpThing)Value!;

	public AnyOptionalSharpObject WithRoomOption() => Value switch
	{
		SharpPlayer p => p,
		SharpExit   e => e,
		SharpThing  t => t,
		None n => n,
		_ => new None()
	};

	public AnySharpContent WithoutNone() => Value switch
	{
		SharpPlayer p => p,
		SharpExit   e => e,
		SharpThing  t => t,
		_ => throw new InvalidOperationException("Cannot convert None to a valid object.")
	};
}
