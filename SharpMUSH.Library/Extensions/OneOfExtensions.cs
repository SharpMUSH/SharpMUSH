﻿using OneOf;
using OneOf.Monads;
using OneOf.Types;
using SharpMUSH.Library.Models;
using None = OneOf.Types.None;

namespace SharpMUSH.Library.Extensions
{
	public static class OneOfExtensions
	{
		public static SharpObject? Object(this OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing, None> union)
			=> union.Match(
					player => player.Object,
					room => room.Object,
					exit => exit.Object,
					thing => thing.Object,
					none => (SharpObject?)null
				);

		public static SharpObject? Object(this OneOf<SharpPlayer, SharpRoom, SharpThing, None> union)
			=> union.Match(
					player => player.Object,
					room => room.Object,
					thing => thing.Object,
					none => (SharpObject?)null
				);

		public static OneOf<SharpPlayer, SharpRoom, SharpThing> MinusExit(this OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing> union)
			=> union.Match<OneOf<SharpPlayer, SharpRoom, SharpThing>>(
				player => player,
				room => room,
				exit => throw new ArgumentException("Cannot convert an exit to a non-exit."),
				thing => thing
			);

		public static OneOf<SharpPlayer, SharpExit, SharpThing> MinusRoom(this OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing> union)
			=> union.Match<OneOf<SharpPlayer, SharpExit, SharpThing>>(
				player => player,
				room => throw new ArgumentException("Cannot convert an room to a non-room."),
				exit => exit,
				thing => thing
			);

		public static OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing> WithExitOption(this OneOf<SharpPlayer, SharpRoom, SharpThing> union)
			=> union.Match<OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing>>(
				player => player,
				room => room,
				thing => thing
			);

		public static OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing> WithRoomOption(this OneOf<SharpPlayer, SharpExit, SharpThing> union)
			=> union.Match<OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing>>(
				player => player,
				exit => exit,
				thing => thing
			);

		public static OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing, None> WithRoomOption(this OneOf<SharpPlayer, SharpExit, SharpThing, None> union)
			=> union.Match<OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing, None>>(
				player => player,
				exit => exit,
				thing => thing,
				none => none
			);

		public static OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing, None> WithNoneOption(this OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing> union)
			=> union.Match<OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing, None>>(
				player => player,
				room => room,
				exit => exit,
				thing => thing
			);

		public static OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing, None, Error<string>> WithErrorOption(this OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing, None> union)
	=> union.Match<OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing, None, Error<string>>>(
		player => player,
		room => room,
		exit => exit,
		thing => thing,
		none => none
	);

		public static OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing> WithoutNone(this OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing, None> union)
			=> union.Match<OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing>>(
				player => player,
				room => room,
				exit => exit,
				thing => thing,
				none => throw new ArgumentException("Cannot convert an None to a non-None value.")
			);

		public static OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing, None> WithoutError(this OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing, None, Error<string>> union)
			=> union.Match<OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing, None>>(
				player => player,
				room => room,
				exit => exit,
				thing => thing,
				none => none,
				error => throw new ArgumentException("Cannot convert an Error to a non-Error value.")
			);

		public static OneOf<SharpPlayer, SharpRoom, SharpThing> Home(this OneOf<SharpPlayer, SharpExit, SharpThing> thing)
			=> thing.Match(
				player => player.Home(),
				exit => exit.Location(),
				thing => thing.Home());

		public static OneOf<SharpPlayer, SharpRoom, SharpThing> Location(this OneOf<SharpPlayer, SharpExit, SharpThing> thing)
			=> thing.Match(
				player => player.Location(),
				exit => exit.Home(),
				thing => thing.Location());

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
