using OneOf;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.DiscriminatedUnions;

[GenerateOneOf]
public class AnyOptionalSharpObject : OneOfBase<SharpPlayer, SharpRoom, SharpExit, SharpThing, OneOf.Types.None>
{
	public AnyOptionalSharpObject(OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing, OneOf.Types.None> input) : base(input) { }
	public static implicit operator AnyOptionalSharpObject(SharpPlayer x) => new(x);
	public static implicit operator AnyOptionalSharpObject(SharpRoom x) => new(x);
	public static implicit operator AnyOptionalSharpObject(SharpExit x) => new(x);
	public static implicit operator AnyOptionalSharpObject(SharpThing x) => new(x);
	public static implicit operator AnyOptionalSharpObject(OneOf.Types.None x) => new(x);

	public bool IsPlayer => IsT0;
	public bool IsRoom => IsT1;
	public bool IsExit => IsT2;
	public bool IsThing => IsT3;
	public bool IsNone => IsT4;

	public SharpPlayer AsPlayer => AsT0;
	public SharpRoom AsRoom => AsT1;
	public SharpExit AsExit => AsT2;
	public SharpThing AsThing => AsT3;
}