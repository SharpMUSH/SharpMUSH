using OneOf;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.DiscriminatedUnions
{
	[GenerateOneOf]
	public class AnyOptionalSharpContainer : OneOfBase<SharpPlayer, SharpRoom, SharpThing, OneOf.Types.None>
	{
		protected AnyOptionalSharpContainer(OneOf<SharpPlayer, SharpRoom, SharpThing, OneOf.Types.None> input) : base(input)
		{
		}
	}
}