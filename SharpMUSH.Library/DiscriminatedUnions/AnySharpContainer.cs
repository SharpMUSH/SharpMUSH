using OneOf;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.DiscriminatedUnions
{
	[GenerateOneOf]
	public class AnySharpContainer : OneOfBase<SharpPlayer, SharpRoom, SharpThing>
	{
		protected AnySharpContainer(OneOf<SharpPlayer, SharpRoom, SharpThing> input) : base(input) { }
		public static implicit operator AnySharpContainer(SharpPlayer x) => new(x);
		public static implicit operator AnySharpContainer(SharpRoom x) => new(x);
		public static implicit operator AnySharpContainer(SharpThing x) => new(x);

		public AnySharpObject WithExitOption()
			=> Match<AnySharpObject>(
				player => player,
				room => room,
				thing => thing
			);
	}
}
