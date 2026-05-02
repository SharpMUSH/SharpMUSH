using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.DiscriminatedUnions;

/// <summary>
/// A union of SharpPlayer, SharpRoom, SharpExit, SharpThing, None, and SharpError.
/// Replaces AnyOptionalSharpObjectOrError.
/// </summary>
public union AnyOptionalSharpObjectOrError(SharpPlayer, SharpRoom, SharpExit, SharpThing, None, SharpError)
{
	public bool IsPlayer => Value is SharpPlayer;
	public bool IsRoom   => Value is SharpRoom;
	public bool IsExit   => Value is SharpExit;
	public bool IsThing  => Value is SharpThing;
	public bool IsNone   => Value is null or None;
	public bool IsError  => Value is SharpError;

	public bool IsAnyObject => !IsNone && !IsError;

	public SharpPlayer AsPlayer => (SharpPlayer)Value!;
	public SharpRoom   AsRoom   => (SharpRoom)Value!;
	public SharpExit   AsExit   => (SharpExit)Value!;
	public SharpThing  AsThing  => (SharpThing)Value!;
	public None        AsNone   => (None)Value!;
	public SharpError  AsError  => (SharpError)Value!;

	public AnySharpObject AsAnyObject => Value switch
	{
		SharpPlayer p => p,
		SharpRoom   r => r,
		SharpExit   e => e,
		SharpThing  t => t,
		_ => throw new ArgumentOutOfRangeException()
	};

	public bool IsPlayer_OrRoom => IsPlayer || IsRoom;
}
