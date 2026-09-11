using System.Runtime.CompilerServices;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.DiscriminatedUnions;

[Union]
public sealed partial class AnyOptionalSharpContent : IUnion
{
	public AnyOptionalSharpContent(SharpPlayer value) => Value = value;
	public AnyOptionalSharpContent(SharpExit value) => Value = value;
	public AnyOptionalSharpContent(SharpThing value) => Value = value;
	public AnyOptionalSharpContent(None value) => Value = value;

	public object? Value { get; }

	public override bool Equals(object? obj) => obj is AnyOptionalSharpContent other && Equals(Value, other.Value);

	public override int GetHashCode() => Value?.GetHashCode() ?? 0;

	public bool IsPlayer => Value is SharpPlayer;
	public bool IsExit => Value is SharpExit;
	public bool IsThing => Value is SharpThing;
	public bool IsNone => Value is None;

	public SharpPlayer AsPlayer => Value as SharpPlayer ?? throw UnionCase.Mismatch<SharpPlayer>(Value);
	public SharpExit AsExit => Value as SharpExit ?? throw UnionCase.Mismatch<SharpExit>(Value);
	public SharpThing AsThing => Value as SharpThing ?? throw UnionCase.Mismatch<SharpThing>(Value);

	public AnyOptionalSharpObject WithRoomOption() => this switch
	{
		SharpPlayer player => player,
		SharpExit exit => exit,
		SharpThing thing => thing,
		None none => none
	};

	public AnySharpContent WithoutNone() => this switch
	{
		SharpPlayer player => player,
		SharpExit exit => exit,
		SharpThing thing => thing,
		None => throw new Exception("Cannot convert None to a valid object.")
	};
}
