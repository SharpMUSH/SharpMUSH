using OneOf;
using OneOf.Types;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.DiscriminatedUnions;

[GenerateOneOf]
public class AnyOptionalSharpContainer : OneOfBase<SharpPlayer, SharpRoom, SharpThing, None>
{
	public AnyOptionalSharpContainer(OneOf<SharpPlayer, SharpRoom, SharpThing, None> input) : base(input) { }
	public static implicit operator AnyOptionalSharpContainer(SharpPlayer x) => new(x);
	public static implicit operator AnyOptionalSharpContainer(SharpRoom x) => new(x);
	public static implicit operator AnyOptionalSharpContainer(SharpThing x) => new(x);
	public static implicit operator AnyOptionalSharpContainer(None x) => new(x);

	public bool IsPlayer => IsT0;
	public bool IsRoom => IsT1;
	public bool IsThing => IsT2;
	public bool IsNone => IsT3;

	public SharpPlayer AsPlayer => AsT0;
	public SharpRoom AsRoom => AsT1;
	public SharpThing AsThing => AsT2;

	public AnyOptionalSharpObject WithExitOption()
		=> Match<AnyOptionalSharpObject>(
			player => player,
			room => room,
			thing => thing,
			none => none
		);

	public AnySharpContainer WithoutNone()
		=> Match<AnySharpContainer>(
			player => player,
			room => room,
			thing => thing,
			none => throw new Exception("Cannot convert None to a valid object.")
		);
}