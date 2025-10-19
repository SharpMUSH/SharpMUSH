using OneOf;
using OneOf.Types;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.DiscriminatedUnions;

[GenerateOneOf]
public class LazySharpAttributesOrError(OneOf<LazySharpAttribute[], Error<string>> input)
	: OneOfBase<LazySharpAttribute[], Error<string>>(input)
{
	public static implicit operator LazySharpAttributesOrError(LazySharpAttribute[] x) => new(x);
	public static implicit operator LazySharpAttributesOrError(Error<string> x) => new(x);

	public bool IsAttribute => IsT0;
	public bool IsError => IsT1;

	public LazySharpAttribute[] AsAttributes => AsT0;
	public Error<string> AsError => AsT1;
}