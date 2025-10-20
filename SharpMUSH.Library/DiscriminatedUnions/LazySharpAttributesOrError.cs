using OneOf;
using OneOf.Types;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.DiscriminatedUnions;

[GenerateOneOf]
public class LazySharpAttributesOrError(OneOf<IAsyncEnumerable<LazySharpAttribute>, Error<string>> input)
	: OneOfBase<IAsyncEnumerable<LazySharpAttribute>, Error<string>>(input)
{
	public static LazySharpAttributesOrError FromAsync(IAsyncEnumerable<LazySharpAttribute> x)
	{
		return new LazySharpAttributesOrError(OneOf<IAsyncEnumerable<LazySharpAttribute>, Error<string>>.FromT0(x));
	}

	public static implicit operator LazySharpAttributesOrError(Error<string> x) => new(x);

	public bool IsAttribute => IsT0;
	public bool IsError => IsT1;

	public IAsyncEnumerable<LazySharpAttribute> AsAttributes => AsT0;
	public Error<string> AsError => AsT1;
}