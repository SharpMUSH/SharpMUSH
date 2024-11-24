using OneOf;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.DiscriminatedUnions;

[GenerateOneOf]
public class OptionalSharpAttributeOrError(OneOf<SharpAttribute, OneOf.Types.None, OneOf.Types.Error<string>> input)
	: OneOfBase<SharpAttribute, OneOf.Types.None, OneOf.Types.Error<string>>(input)
{
		public static implicit operator OptionalSharpAttributeOrError(SharpAttribute x) => new(x);
		public static implicit operator OptionalSharpAttributeOrError(OneOf.Types.None x) => new(x);
		public static implicit operator OptionalSharpAttributeOrError(OneOf.Types.Error<string> x) => new(x);

		public bool IsAttribute => IsT0;
		public bool IsNone => IsT1;
		public bool IsError => IsT2;

		public SharpAttribute AsAttribute => AsT0;
		public OneOf.Types.Error<string> AsError => AsT2;
}