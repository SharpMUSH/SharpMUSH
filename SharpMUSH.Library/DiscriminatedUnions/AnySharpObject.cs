using OneOf;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.DiscriminatedUnions
{
	[GenerateOneOf]
	public class AnySharpObject : OneOfBase<SharpPlayer, SharpRoom, SharpExit, SharpThing>
	{
		protected AnySharpObject(OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing> input) : base(input) { }

		public static implicit operator AnySharpObject(SharpPlayer x) => new(x);
		public static implicit operator AnySharpObject(SharpRoom x) => new(x);
		public static implicit operator AnySharpObject(SharpExit x) => new(x);
		public static implicit operator AnySharpObject(SharpThing x) => new(x);

		public AnySharpContainer MinusExit()
			=> Match<AnySharpContainer>(
				player => player,
				room => room,
				exit => throw new ArgumentException("Cannot convert an exit to a non-exit."),
				thing => thing
				);

		public AnySharpContent MinusRoom() 
			=> Match<AnySharpContent>(
				player => player,
				room => throw new ArgumentException("Cannot convert an room to a non-room."),
				exit => exit,
				thing => thing
			);

	}
}
