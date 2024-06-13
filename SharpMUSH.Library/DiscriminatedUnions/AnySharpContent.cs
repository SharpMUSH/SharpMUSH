using OneOf;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.DiscriminatedUnions
{
	[GenerateOneOf]
	public class AnySharpContent : OneOfBase<SharpPlayer, SharpExit, SharpThing>
	{
		public AnySharpContent(OneOf<SharpPlayer, SharpExit, SharpThing> input) : base(input) { }
		public static implicit operator AnySharpContent(SharpPlayer x) => new(x);
		public static implicit operator AnySharpContent(SharpExit x) => new(x);
		public static implicit operator AnySharpContent(SharpThing x) => new(x);

		public AnySharpObject WithRoomOption()
			=> Match<AnySharpObject>(
				player => player,
				exit => exit,
				thing => thing
			);
	}
}
