using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.DiscriminatedUnions;

/// <summary>
/// A union of SharpPlayer, SharpExit, SharpThing (things that live inside containers).
/// Replaces AnySharpContent : OneOfBase&lt;SharpPlayer, SharpExit, SharpThing&gt;.
/// </summary>
public union AnySharpContent(SharpPlayer, SharpExit, SharpThing)
{
	public bool IsPlayer => Value is SharpPlayer;
	public bool IsExit   => Value is SharpExit;
	public bool IsThing  => Value is SharpThing;

	public SharpPlayer AsPlayer => (SharpPlayer)Value!;
	public SharpExit   AsExit   => (SharpExit)Value!;
	public SharpThing  AsThing  => (SharpThing)Value!;

	public string Id => Value switch
	{
		SharpPlayer p => p.Id,
		SharpExit   e => e.Id,
		SharpThing  t => t.Id,
		_ => throw new InvalidOperationException()
	} ?? throw new InvalidOperationException("Id was null");

	public AnySharpObject WithRoomOption() => Value switch
	{
		SharpPlayer p => p,
		SharpExit   e => e,
		SharpThing  t => t,
		_ => throw new InvalidOperationException()
	};

	public AnyOptionalSharpContent WithNoneOption() => Value switch
	{
		SharpPlayer p => p,
		SharpExit   e => e,
		SharpThing  t => t,
		_ => throw new InvalidOperationException()
	};

	public async ValueTask<AnySharpContainer> Location() => Value switch
	{
		SharpPlayer p => await p.Location.WithCancellation(CancellationToken.None),
		SharpExit   e => await e.Location.WithCancellation(CancellationToken.None),
		SharpThing  t => await t.Location.WithCancellation(CancellationToken.None),
		_ => throw new InvalidOperationException()
	};

	public async ValueTask<AnySharpContainer> Home() => Value switch
	{
		SharpPlayer p => await p.Home.WithCancellation(CancellationToken.None),
		SharpExit   e => await e.Home.WithCancellation(CancellationToken.None),
		SharpThing  t => await t.Home.WithCancellation(CancellationToken.None),
		_ => throw new InvalidOperationException()
	};
}
