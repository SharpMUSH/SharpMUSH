using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Extensions;

public static class OneOfExtensions
{
	public static SharpObject? Object(this AnyOptionalSharpObject union) => union.Value switch
	{
		SharpPlayer p => p.Object,
		SharpRoom   r => r.Object,
		SharpExit   e => e.Object,
		SharpThing  t => t.Object,
		_ => null
	};

	public static SharpObject Object(this AnySharpContainer union) => union.Value switch
	{
		SharpPlayer p => p.Object,
		SharpRoom   r => r.Object,
		SharpThing  t => t.Object,
		_ => throw new InvalidOperationException()
	};

	public static SharpObject Object(this AnySharpContent union) => union.Value switch
	{
		SharpPlayer p => p.Object,
		SharpExit   e => e.Object,
		SharpThing  t => t.Object,
		_ => throw new InvalidOperationException()
	};

	public static SharpObject? Object(this AnyOptionalSharpContainer union) => union.Value switch
	{
		SharpPlayer p => p.Object,
		SharpRoom   r => r.Object,
		SharpThing  t => t.Object,
		_ => null
	};

	public static SharpObject Object(this AnySharpObject union) => union.Value switch
	{
		SharpPlayer p => p.Object,
		SharpRoom   r => r.Object,
		SharpExit   e => e.Object,
		SharpThing  t => t.Object,
		_ => throw new InvalidOperationException()
	};

	public static AnySharpObject WithExitOption(this AnySharpContainer union) => union.Value switch
	{
		SharpPlayer p => p,
		SharpRoom   r => r,
		SharpThing  t => t,
		_ => throw new InvalidOperationException()
	};

	public static AnySharpObject WithRoomOption(this AnySharpContainer union) => union.Value switch
	{
		SharpPlayer p => p,
		SharpRoom   r => r,
		SharpThing  t => t,
		_ => throw new InvalidOperationException()
	};

	public static AnyOptionalSharpObject WithNoneOption(this AnySharpObject union) => union.Value switch
	{
		SharpPlayer p => p,
		SharpRoom   r => r,
		SharpExit   e => e,
		SharpThing  t => t,
		_ => throw new InvalidOperationException()
	};

	public static AnyOptionalSharpObjectOrError WithErrorOption(this AnyOptionalSharpObject union) => union.Value switch
	{
		SharpPlayer p => p,
		SharpRoom   r => r,
		SharpExit   e => e,
		SharpThing  t => t,
		None n => n,
		_ => new None()
	};

	public static AnySharpObject WithoutNone(this AnyOptionalSharpObject union) => union.Value switch
	{
		SharpPlayer p => p,
		SharpRoom   r => r,
		SharpExit   e => e,
		SharpThing  t => t,
		_ => throw new ArgumentException("Cannot convert a None to a non-None value.")
	};

	public static AnyOptionalSharpObject WithoutError(this AnyOptionalSharpObjectOrError union) => union.Value switch
	{
		SharpPlayer p => p,
		SharpRoom   r => r,
		SharpExit   e => e,
		SharpThing  t => t,
		None n => n,
		SharpError => throw new ArgumentException("Cannot convert an Error to a non-Error value."),
		_ => new None()
	};

	public static async ValueTask<AnySharpContainer> Home(this AnySharpContent thing) => thing.Value switch
	{
		SharpPlayer p => await p.Home.WithCancellation(CancellationToken.None),
		SharpExit   e => await e.Location.WithCancellation(CancellationToken.None),
		SharpThing  t => await t.Home.WithCancellation(CancellationToken.None),
		_ => throw new InvalidOperationException()
	};

	public static async ValueTask<AnySharpContainer> Location(this AnySharpContent thing) => thing.Value switch
	{
		SharpPlayer p => await p.Location.WithCancellation(CancellationToken.None),
		SharpExit   e => await e.Home.WithCancellation(CancellationToken.None),
		SharpThing  t => await t.Location.WithCancellation(CancellationToken.None),
		_ => throw new InvalidOperationException()
	};

	public static AnySharpObject Known(this AnyOptionalSharpObject union) => union.Value switch
	{
		SharpPlayer p => p,
		SharpRoom   r => r,
		SharpExit   e => e,
		SharpThing  t => t,
		_ => throw new ArgumentNullException(nameof(union))
	};

	public static Option<SharpObject> ObjectOption(this AnyOptionalSharpObject union) => union.Value switch
	{
		SharpPlayer p => Option<SharpObject>.FromOption(p.Object),
		SharpRoom   r => Option<SharpObject>.FromOption(r.Object),
		SharpExit   e => Option<SharpObject>.FromOption(e.Object),
		SharpThing  t => Option<SharpObject>.FromOption(t.Object),
		_ => new None()
	};

	public static string? Id(this AnyOptionalSharpObject union) => union.Value switch
	{
		SharpPlayer p => p.Id,
		SharpRoom   r => r.Id,
		SharpExit   e => e.Id,
		SharpThing  t => t.Id,
		_ => null
	};

	public static string? Id(this AnyOptionalSharpContainer union) => union.Value switch
	{
		SharpPlayer p => p.Id,
		SharpRoom   r => r.Id,
		SharpThing  t => t.Id,
		_ => null
	};

	public static string? Id(this AnySharpObject union) => union.Value switch
	{
		SharpPlayer p => p.Id,
		SharpRoom   r => r.Id,
		SharpExit   e => e.Id,
		SharpThing  t => t.Id,
		_ => null
	};

	public static bool IsNone(this AnyOptionalSharpObject union)   => union.IsNone;
	public static bool IsNone(this AnyOptionalSharpContainer union) => union.IsNone;
	public static bool IsNone(this AnyOptionalSharpObjectOrError union) => union.IsNone;
	public static bool IsError(this AnyOptionalSharpObjectOrError union) => union.IsError;
	public static bool IsValid(this AnyOptionalSharpObjectOrError union) => !(union.IsNone || union.IsError);
	public static bool IsPlayer(this AnyOptionalSharpObjectOrError union) => union.IsPlayer;
}
