using OneOf;
using OneOf.Types;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.DiscriminatedUnions;

[GenerateOneOf]
public class AnyOptionalSharpObjectOrError : OneOfBase<SharpPlayer, SharpRoom, SharpExit, SharpThing, None,
	Error<string>>
{
	public AnyOptionalSharpObjectOrError(
		OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing, None, Error<string>> input) :
		base(input)
	{
	}

	public static implicit operator AnyOptionalSharpObjectOrError(SharpPlayer x) => new(x);
	public static implicit operator AnyOptionalSharpObjectOrError(SharpRoom x) => new(x);
	public static implicit operator AnyOptionalSharpObjectOrError(SharpExit x) => new(x);
	public static implicit operator AnyOptionalSharpObjectOrError(SharpThing x) => new(x);
	public static implicit operator AnyOptionalSharpObjectOrError(None x) => new(x);
	public static implicit operator AnyOptionalSharpObjectOrError(Error<string> x) => new(x);

	public bool IsPlayer => IsT0;
	public bool IsRoom => IsT1;
	public bool IsExit => IsT2;
	public bool IsThing => IsT3;
	public bool IsNone => IsT4;
	public bool IsError => IsT5;

	public SharpPlayer AsPlayer => AsT0;
	public SharpRoom AsRoom => AsT1;
	public SharpExit AsExit => AsT2;
	public SharpThing AsThing => AsT3;

	public AnySharpObject AsAnyObject => Match(
		player => new AnySharpObject(player), 
		room => new AnySharpObject(room),
		exit => new AnySharpObject(exit), 
		thing => new AnySharpObject(thing), 
		_ => throw new ArgumentOutOfRangeException(),
		_ => throw new ArgumentOutOfRangeException());

	public None AsNone => AsT4;
	public Error<string> AsError => AsT5;
}