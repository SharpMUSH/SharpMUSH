using OneOf;
using OneOf.Monads;
using SharpMUSH.Library.Models;
using None = OneOf.Types.None;

namespace SharpMUSH.Library.Extensions
{
	public static class OneOfExtensions
	{
		public static SharpObject? Object(this OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing, None> union) =>
			union.Match(
					player => player.Object,
					room => room.Object,
					exit => exit.Object,
					thing => thing.Object,
					none => (SharpObject?)null
				);

		public static OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing> Known(this OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing, None> union) =>
			union.Match<OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing>>(
				player => player,
				room => room,
				exit => exit,
				thing => thing,
				none => throw new ArgumentNullException(nameof(union)));

		public static Option<SharpObject> ObjectOption(this OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing, None> union) =>
			union.Match<Option<SharpObject>>(
					player => player.Object,
					room => room.Object,
					exit => exit.Object,
					thing => thing.Object,
					none => new OneOf.Monads.None()
				);

		public static string? Id(this OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing, None> union) =>
			union.Match(
					player => player.Id,
					room => room.Id,
					exit => exit.Id,
					thing => thing.Id,
					none => null
				);

		public static SharpObject Object(this OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing> union) =>
			union.Match(
					player => player.Object,
					room => room.Object,
					exit => exit.Object,
					thing => thing.Object
				);

		public static string? Id(this OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing> union) =>
			union.Match(
					player => player.Id,
					room => room.Id,
					exit => exit.Id,
					thing => thing.Id
				);

		public static SharpObject Object(this OneOf<SharpPlayer, SharpRoom, SharpThing> union) =>
			union.Match(
					player => player.Object,
					room => room.Object,
					thing => thing.Object
				);

		public static SharpObject? Object(this OneOf<SharpPlayer, SharpExit, SharpThing, None> union) =>
			union.Match(
					player => player.Object,
					exit => exit.Object,
					thing => thing.Object,
					none => (SharpObject?)null
				);
	}
}
