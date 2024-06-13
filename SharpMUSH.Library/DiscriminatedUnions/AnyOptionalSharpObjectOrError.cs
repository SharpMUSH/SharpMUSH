using OneOf;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.DiscriminatedUnions
{
	[GenerateOneOf]
	public class AnyOptionalSharpObjectOrError : OneOfBase<SharpPlayer, SharpRoom, SharpExit, SharpThing, OneOf.Types.None, OneOf.Types.Error<string>>
	{
		public AnyOptionalSharpObjectOrError(OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing, OneOf.Types.None, OneOf.Types.Error<string>> input) : base(input) { }
		public static implicit operator AnyOptionalSharpObjectOrError(SharpPlayer x) => new(x);
		public static implicit operator AnyOptionalSharpObjectOrError(SharpRoom x) => new(x);
		public static implicit operator AnyOptionalSharpObjectOrError(SharpExit x) => new(x);
		public static implicit operator AnyOptionalSharpObjectOrError(SharpThing x) => new(x);
		public static implicit operator AnyOptionalSharpObjectOrError(OneOf.Types.None x) => new(x);
		public static implicit operator AnyOptionalSharpObjectOrError(OneOf.Types.Error<string> x) => new(x);
	}
}