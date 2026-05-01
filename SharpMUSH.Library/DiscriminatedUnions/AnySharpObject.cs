using Mediator;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Library.DiscriminatedUnions;

/// <summary>
/// A union of SharpPlayer, SharpRoom, SharpExit, SharpThing.
/// Replaces AnySharpObject : OneOfBase&lt;SharpPlayer, SharpRoom, SharpExit, SharpThing&gt;.
/// </summary>
public union AnySharpObject(SharpPlayer, SharpRoom, SharpExit, SharpThing)
{
	public bool Equals(AnySharpObject other) =>
		Value is null ? other.Value is null :
		other.Value is not null && this.Object().DBRef == other.Object().DBRef;
	public override bool Equals(object? o) => o is AnySharpObject other && Equals(other);
	public override int GetHashCode() => Value is null ? 0 : this.Object().DBRef.GetHashCode();
	public static bool operator ==(AnySharpObject a, AnySharpObject b) => a.Equals(b);
	public static bool operator !=(AnySharpObject a, AnySharpObject b) => !a.Equals(b);

	public bool IsPlayer => Value is SharpPlayer;
	public bool IsRoom   => Value is SharpRoom;
	public bool IsExit   => Value is SharpExit;
	public bool IsThing  => Value is SharpThing;

	public bool IsContent   => IsPlayer || IsExit || IsThing;
	public bool IsContainer => IsPlayer || IsRoom || IsThing;

	public SharpPlayer AsPlayer => (SharpPlayer)Value!;
	public SharpRoom   AsRoom   => (SharpRoom)Value!;
	public SharpExit   AsExit   => (SharpExit)Value!;
	public SharpThing  AsThing  => (SharpThing)Value!;

	public string[] Aliases => (Value switch
	{
		SharpPlayer p => p.Aliases,
		SharpRoom   r => r.Aliases,
		SharpExit   e => e.Aliases,
		SharpThing  t => t.Aliases,
		_ => null
	}) ?? [];

	public async ValueTask<AnySharpContainer> Where() => Value switch
	{
		SharpPlayer p => await p.Location.WithCancellation(CancellationToken.None),
		SharpRoom   r => r,
		SharpExit   e => await e.Location.WithCancellation(CancellationToken.None),
		SharpThing  t => await t.Location.WithCancellation(CancellationToken.None),
		_ => throw new InvalidOperationException("Invalid AnySharpObject state")
	};

	public async ValueTask<AnySharpContainer> OutermostWhere()
	{
		var where = await Where();
		for (DBRef? tmpWhere = null; where.Object().DBRef != tmpWhere;)
		{
			tmpWhere = where.Object().DBRef;
			where = await where.Location();
		}
		return where;
	}

	public AnySharpContainer MinusExit() => Value switch
	{
		SharpPlayer p => p,
		SharpRoom   r => r,
		SharpExit   => throw new ArgumentException("Cannot convert an exit to a non-exit."),
		SharpThing  t => t,
		_ => throw new InvalidOperationException()
	};

	public AnySharpContent MinusRoom() => Value switch
	{
		SharpPlayer p => p,
		SharpRoom   => throw new ArgumentException("Cannot convert a room to a non-room."),
		SharpExit   e => e,
		SharpThing  t => t,
		_ => throw new InvalidOperationException()
	};

	public AnySharpContent AsContent => Value switch
	{
		SharpPlayer p => p,
		SharpRoom   => throw new ArgumentException("Cannot convert a room to content."),
		SharpExit   e => e,
		SharpThing  t => t,
		_ => throw new InvalidOperationException()
	};

	public AnySharpContainer AsContainer => Value switch
	{
		SharpPlayer p => p,
		SharpRoom   r => r,
		SharpExit   => throw new ArgumentException("Cannot convert an exit to container."),
		SharpThing  t => t,
		_ => throw new InvalidOperationException()
	};
}
