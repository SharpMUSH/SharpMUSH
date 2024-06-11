using OneOf;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.DiscriminatedUnions
{
	[GenerateOneOf]
	public class AnyOptionalSharpObject : OneOfBase<SharpPlayer, SharpRoom, SharpExit, SharpThing, OneOf.Types.None>
	{
		protected AnyOptionalSharpObject(OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing, OneOf.Types.None> input) : base(input) { }

		public static implicit operator AnyOptionalSharpObject(SharpPlayer x) => new(x);
		public static implicit operator AnyOptionalSharpObject(SharpRoom x) => new(x);
		public static implicit operator AnyOptionalSharpObject(SharpExit x) => new(x);
		public static implicit operator AnyOptionalSharpObject(SharpThing x) => new(x);
		public static implicit operator AnyOptionalSharpObject(OneOf.Types.None x) => new(x);
	}
}