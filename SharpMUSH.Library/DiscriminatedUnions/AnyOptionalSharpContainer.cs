using OneOf;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.DiscriminatedUnions
{
	[GenerateOneOf]
	public class AnyOptionalSharpContainer : OneOfBase<SharpPlayer, SharpRoom, SharpThing, OneOf.Types.None>
	{
		public AnyOptionalSharpContainer(OneOf<SharpPlayer, SharpRoom, SharpThing, OneOf.Types.None> input) : base(input) { }
		public static implicit operator AnyOptionalSharpContainer(SharpPlayer x) => new(x);
		public static implicit operator AnyOptionalSharpContainer(SharpRoom x) => new(x);
		public static implicit operator AnyOptionalSharpContainer(SharpThing x) => new(x);
		public static implicit operator AnyOptionalSharpContainer(OneOf.Types.None x) => new(x);
		
		public AnyOptionalSharpObject WithExitOption()
			=> Match<AnyOptionalSharpObject>(
				player => player,
				room => room,
				thing => thing,
				none => none
			);
	}
}