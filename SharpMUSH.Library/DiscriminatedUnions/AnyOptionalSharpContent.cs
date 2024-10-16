using OneOf;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.DiscriminatedUnions;

[GenerateOneOf]
public class AnyOptionalSharpContent : OneOfBase<SharpPlayer, SharpExit, SharpThing, OneOf.Types.None>
{
	public AnyOptionalSharpContent(OneOf<SharpPlayer, SharpExit, SharpThing, OneOf.Types.None> input) : base(input) { }
	public static implicit operator AnyOptionalSharpContent(SharpPlayer x) => new(x);
	public static implicit operator AnyOptionalSharpContent(SharpExit x) => new(x);
	public static implicit operator AnyOptionalSharpContent(SharpThing x) => new(x);
	public static implicit operator AnyOptionalSharpContent(OneOf.Types.None x) => new(x);

	public bool IsPlayer => IsT0;
	public bool IsExit => IsT1;
	public bool IsThing => IsT2;
	public bool IsNone => IsT3;

	public SharpPlayer AsPlayer => AsT0;
	public SharpExit AsExit => AsT1;
	public SharpThing AsThing => AsT2;

	public AnyOptionalSharpObject WithRoomOption()
		=> Match<AnyOptionalSharpObject>(
			player => player,
			exit => exit,
			thing => thing,
			none => none
		);

	public AnySharpContent WithoutNone()
		=> Match<AnySharpContent>(
			player => player,
			exit => exit,
			thing => thing,
			none => throw new Exception("Cannot convert None to a valid object.")
		);
}