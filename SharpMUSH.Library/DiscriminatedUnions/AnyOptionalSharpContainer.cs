using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.DiscriminatedUnions;

/// <summary>
/// A union of SharpPlayer, SharpRoom, SharpThing, and None.
/// Replaces AnyOptionalSharpContainer : OneOfBase&lt;SharpPlayer, SharpRoom, SharpThing, None&gt;.
/// </summary>
public union AnyOptionalSharpContainer(SharpPlayer, SharpRoom, SharpThing, None)
{
	public bool IsPlayer => Value is SharpPlayer;
	public bool IsRoom   => Value is SharpRoom;
	public bool IsThing  => Value is SharpThing;
	public bool IsNone   => Value is null or None;

	public SharpPlayer AsPlayer => (SharpPlayer)Value!;
	public SharpRoom   AsRoom   => (SharpRoom)Value!;
	public SharpThing  AsThing  => (SharpThing)Value!;

	public AnyOptionalSharpObject WithExitOption() => Value switch
	{
		SharpPlayer p => p,
		SharpRoom   r => r,
		SharpThing  t => t,
		None n => n,
		_ => new None()
	};

	public AnySharpContainer WithoutNone() => Value switch
	{
		SharpPlayer p => p,
		SharpRoom   r => r,
		SharpThing  t => t,
		_ => throw new InvalidOperationException("Cannot convert None to a valid object.")
	};
}
