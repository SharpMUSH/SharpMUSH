using System.Runtime.CompilerServices;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.DiscriminatedUnions;

[Union]
public sealed class AnySharpObject : IUnion, IObjectShaped<AnySharpObject>
{
	public AnySharpObject(SharpPlayer value) => Value = value;
	public AnySharpObject(SharpRoom value) => Value = value;
	public AnySharpObject(SharpExit value) => Value = value;
	public AnySharpObject(SharpThing value) => Value = value;

	public object? Value { get; }

	/// <summary>
	/// Equal when both hold the same model instance. The object node cache hands out one instance per
	/// object, so in practice that means the same object.
	/// </summary>
	public override bool Equals(object? obj) => obj is AnySharpObject other && Equals(Value, other.Value);

	public override int GetHashCode() => Object().DBRef.GetHashCode();

	public async ValueTask<AnySharpContainer> Where() => this switch
	{
		SharpPlayer player => await player.Location.WithCancellation(CancellationToken.None),
		SharpRoom room => room,
		SharpExit exit => await exit.Location.WithCancellation(CancellationToken.None),
		SharpThing thing => await thing.Location.WithCancellation(CancellationToken.None)
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

	public string[] Aliases => this switch
	{
		SharpPlayer player => player.Aliases,
		SharpRoom room => room.Aliases,
		SharpExit exit => exit.Aliases,
		SharpThing thing => thing.Aliases
	} ?? [];

	public AnySharpContainer MinusExit() => this switch
	{
		SharpPlayer player => player,
		SharpRoom room => room,
		SharpExit => throw new ArgumentException("Cannot convert an exit to a non-exit."),
		SharpThing thing => thing
	};

	public AnySharpContent MinusRoom() => this switch
	{
		SharpPlayer player => player,
		SharpRoom => throw new ArgumentException("Cannot convert an room to a non-room."),
		SharpExit exit => exit,
		SharpThing thing => thing
	};

	public bool IsPlayer => Value is SharpPlayer;
	public bool IsRoom => Value is SharpRoom;
	public bool IsExit => Value is SharpExit;
	public bool IsThing => Value is SharpThing;

	public bool IsContent => IsPlayer || IsExit || IsThing;

	public AnySharpContent AsContent => this switch
	{
		SharpPlayer player => player,
		SharpRoom => throw new ArgumentException("Cannot convert a room to content."),
		SharpExit exit => exit,
		SharpThing thing => thing
	};

	public AnySharpContainer AsContainer => this switch
	{
		SharpPlayer player => player,
		SharpRoom room => room,
		SharpExit => throw new ArgumentException("Cannot convert an exit to container."),
		SharpThing thing => thing
	};

	public bool IsContainer => IsPlayer || IsRoom || IsThing;

	public SharpPlayer AsPlayer => Value as SharpPlayer ?? throw UnionCase.Mismatch<SharpPlayer>(Value);
	public SharpRoom AsRoom => Value as SharpRoom ?? throw UnionCase.Mismatch<SharpRoom>(Value);
	public SharpExit AsExit => Value as SharpExit ?? throw UnionCase.Mismatch<SharpExit>(Value);
	public SharpThing AsThing => Value as SharpThing ?? throw UnionCase.Mismatch<SharpThing>(Value);

	public SharpObject Object() => this switch
	{
		SharpPlayer player => player.Object,
		SharpRoom room => room.Object,
		SharpExit exit => exit.Object,
		SharpThing thing => thing.Object
	};

	public string? Id() => this switch
	{
		SharpPlayer player => player.Id,
		SharpRoom room => room.Id,
		SharpExit exit => exit.Id,
		SharpThing thing => thing.Id
	};

	public AnyOptionalSharpObject WithNoneOption() => this switch
	{
		SharpPlayer player => player,
		SharpRoom room => room,
		SharpExit exit => exit,
		SharpThing thing => thing
	};

	public static DBRef? RefOf(AnySharpObject value) => value.Object().DBRef;

	public static bool TryFromNode(AnyOptionalSharpObject node, out AnySharpObject value)
	{
		value = node.IsNone ? null! : node.Known;
		return !node.IsNone;
	}
}
