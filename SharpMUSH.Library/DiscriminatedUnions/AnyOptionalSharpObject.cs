using System.Runtime.CompilerServices;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.DiscriminatedUnions;

[Union]
public sealed partial class AnyOptionalSharpObject : IUnion, IObjectShaped<AnyOptionalSharpObject>
{
	public AnyOptionalSharpObject(SharpPlayer value) => Value = value;
	public AnyOptionalSharpObject(SharpRoom value) => Value = value;
	public AnyOptionalSharpObject(SharpExit value) => Value = value;
	public AnyOptionalSharpObject(SharpThing value) => Value = value;
	public AnyOptionalSharpObject(None value) => Value = value;

	public object? Value { get; }

	public override bool Equals(object? obj) => obj is AnyOptionalSharpObject other && Equals(Value, other.Value);

	public override int GetHashCode() => Value?.GetHashCode() ?? 0;

	public bool IsPlayer => Value is SharpPlayer;
	public bool IsRoom => Value is SharpRoom;
	public bool IsExit => Value is SharpExit;
	public bool IsThing => Value is SharpThing;
	public bool IsNone => Value is None;

	public SharpPlayer AsPlayer => Value as SharpPlayer ?? throw UnionCase.Mismatch<SharpPlayer>(Value);
	public SharpRoom AsRoom => Value as SharpRoom ?? throw UnionCase.Mismatch<SharpRoom>(Value);
	public SharpExit AsExit => Value as SharpExit ?? throw UnionCase.Mismatch<SharpExit>(Value);
	public SharpThing AsThing => Value as SharpThing ?? throw UnionCase.Mismatch<SharpThing>(Value);

	public AnySharpObject Known => this switch
	{
		SharpPlayer player => player,
		SharpRoom room => room,
		SharpExit exit => exit,
		SharpThing thing => thing,
		None => throw new ArgumentOutOfRangeException()
	};

	public SharpObject? Object() => this switch
	{
		SharpPlayer player => player.Object,
		SharpRoom room => room.Object,
		SharpExit exit => exit.Object,
		SharpThing thing => thing.Object,
		None => null
	};

	public string? Id() => this switch
	{
		SharpPlayer player => player.Id,
		SharpRoom room => room.Id,
		SharpExit exit => exit.Id,
		SharpThing thing => thing.Id,
		None => null
	};

	public AnyOptionalSharpObjectOrError WithErrorOption() => this switch
	{
		SharpPlayer player => player,
		SharpRoom room => room,
		SharpExit exit => exit,
		SharpThing thing => thing,
		None none => none
	};

	public AnySharpObject WithoutNone() => this switch
	{
		SharpPlayer player => player,
		SharpRoom room => room,
		SharpExit exit => exit,
		SharpThing thing => thing,
		None => throw new ArgumentException("Cannot convert an None to a non-None value.")
	};

	public static DBRef? RefOf(AnyOptionalSharpObject value) => value.IsNone ? null : value.Known.Object().DBRef;

	public static bool TryFromNode(AnyOptionalSharpObject node, out AnyOptionalSharpObject value)
	{
		value = node;
		return true;
	}
}
