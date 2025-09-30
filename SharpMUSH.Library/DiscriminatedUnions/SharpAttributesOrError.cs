using OneOf;
using OneOf.Types;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.DiscriminatedUnions;

[GenerateOneOf]
public class SharpAttributesOrError(OneOf<SharpAttribute[], Error<string>> input)
	: OneOfBase<SharpAttribute[], Error<string>>(input)
{
	public static implicit operator SharpAttributesOrError(SharpAttribute[] x) => new(x);
	public static implicit operator SharpAttributesOrError(Error<string> x) => new(x);

	public bool IsAttribute => IsT0;
	public bool IsError => IsT1;

	public SharpAttribute[] AsAttributes => AsT0;
	public Error<string> AsError => AsT1;
}