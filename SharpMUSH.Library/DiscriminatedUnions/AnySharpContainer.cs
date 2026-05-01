using Mediator;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Library.DiscriminatedUnions;

/// <summary>
/// A union of SharpPlayer, SharpRoom, SharpThing (things that can contain other objects).
/// Replaces AnySharpContainer : OneOfBase&lt;SharpPlayer, SharpRoom, SharpThing&gt;.
/// </summary>
public union AnySharpContainer(SharpPlayer, SharpRoom, SharpThing)
{
	public bool IsPlayer => Value is SharpPlayer;
	public bool IsRoom   => Value is SharpRoom;
	public bool IsThing  => Value is SharpThing;

	public SharpPlayer AsPlayer => (SharpPlayer)Value!;
	public SharpRoom   AsRoom   => (SharpRoom)Value!;
	public SharpThing  AsThing  => (SharpThing)Value!;

	public string Id => Value switch
	{
		SharpPlayer p => p.Id!,
		SharpRoom   r => r.Id!,
		SharpThing  t => t.Id!,
		_ => throw new InvalidOperationException()
	};

	public AnySharpObject WithExitOption() => Value switch
	{
		SharpPlayer p => p,
		SharpRoom   r => r,
		SharpThing  t => t,
		_ => throw new InvalidOperationException()
	};

	public AnyOptionalSharpContainer WithNoneOption() => Value switch
	{
		SharpPlayer p => p,
		SharpRoom   r => r,
		SharpThing  t => t,
		_ => throw new InvalidOperationException()
	};

	public async ValueTask<AnySharpContainer> Location() => Value switch
	{
		SharpPlayer p => await p.Location.WithCancellation(CancellationToken.None),
		SharpRoom   r => r,
		SharpThing  t => await t.Location.WithCancellation(CancellationToken.None),
		_ => throw new InvalidOperationException()
	};

	public IAsyncEnumerable<AnySharpContent> Content(IMediator mediator) =>
		mediator.CreateStream(new GetContentsQuery(this));
}
