using OneOf;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.DiscriminatedUnions;

[GenerateOneOf]
public class SharpAttributesOrError(OneOf<SharpAttribute[], OneOf.Types.Error<string>> input) 
	: OneOfBase<SharpAttribute[], OneOf.Types.Error<string>>(input)
{
	public static implicit operator SharpAttributesOrError(SharpAttribute[] x) => new(x);
	public static implicit operator SharpAttributesOrError(OneOf.Types.Error<string> x) => new(x);

	public bool IsAttribute => IsT0;
	public bool IsError => IsT1;

	public SharpAttribute[] AsAttributes => AsT0;
	public OneOf.Types.Error<string> AsError => AsT1;
}