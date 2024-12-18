using OneOf;
using OneOf.Types;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Extensions;

public static class OneOfExtensions
{
	public static SharpObject? Object(this AnyOptionalSharpObject union)
		=> union.Match<SharpObject?>(
			player => player.Object,
			room => room.Object,
			exit => exit.Object,
			thing => thing.Object,
			none => null
		);

	public static SharpObject Object(this AnySharpContainer union) =>
		union.Match(
			player => player.Object,
			room => room.Object,
			thing => thing.Object
		);

	public static SharpObject Object(this AnySharpContent union) =>
		union.Match(
			player => player.Object,
			exit => exit.Object,
			thing => thing.Object
		);

	public static SharpObject? Object(this AnyOptionalSharpContainer union)
		=> union.Match(
			player => player.Object,
			room => room.Object,
			thing => thing.Object,
			none => (SharpObject?)null
		);

	public static SharpObject Object(this AnySharpObject union)
		=> union.Match(
			player => player.Object,
			room => room.Object,
			exit => exit.Object,
			thing => thing.Object
		);

	public static AnySharpObject WithExitOption(this AnySharpContainer union)
		=> union.Match<AnySharpObject>(
			player => player,
			room => room,
			thing => thing
		);

	public static AnySharpObject WithRoomOption(this AnySharpContainer union)
		=> union.Match<AnySharpObject>(
			player => player,
			exit => exit,
			thing => thing
		);

	public static AnyOptionalSharpObject WithRoomOption(this OneOf<SharpPlayer, SharpExit, SharpThing, None> union)
		=> union.Match<AnyOptionalSharpObject>(
			player => player,
			exit => exit,
			thing => thing,
			none => none
		);

	public static AnyOptionalSharpObject WithNoneOption(this AnySharpObject union)
		=> union.Match<AnyOptionalSharpObject>(
			player => player,
			room => room,
			exit => exit,
			thing => thing
		);

	public static AnyOptionalSharpObjectOrError WithErrorOption(this AnyOptionalSharpObject union)
		=> union.Match<AnyOptionalSharpObjectOrError>(
			player => player,
			room => room,
			exit => exit,
			thing => thing,
			none => none
		);

	public static AnySharpObject WithoutNone(this AnyOptionalSharpObject union)
		=> union.Match<AnySharpObject>(
			player => player,
			room => room,
			exit => exit,
			thing => thing,
			none => throw new ArgumentException("Cannot convert an None to a non-None value.")
		);

	public static AnyOptionalSharpObject WithoutError(this AnyOptionalSharpObjectOrError union)
		=> union.Match<AnyOptionalSharpObject>(
			player => player,
			room => room,
			exit => exit,
			thing => thing,
			none => none,
			error => throw new ArgumentException("Cannot convert an Error to a non-Error value.")
		);

	public static AnySharpContainer Home(this AnySharpContent thing)
		=> thing.Match(
			player => player.Home.Value,
			exit => exit.Location.Value,
			thing2 => thing2.Home.Value);

	public static AnySharpContainer Location(this AnySharpContent thing)
		=> thing.Match(
			player => player.Location.Value,
			exit => exit.Home.Value,
			thing2 => thing2.Location.Value);

	public static AnySharpObject Known(this AnyOptionalSharpObject union) =>
		union.Match<AnySharpObject>(
			player => player,
			room => room,
			exit => exit,
			thing => thing,
			none => throw new ArgumentNullException(nameof(union)));

	public static OneOf<SharpObject, None> ObjectOption(this AnyOptionalSharpObject union) =>
		union.Match<OneOf<SharpObject, None>>(
			player => player.Object,
			room => room.Object,
			exit => exit.Object,
			thing => thing.Object,
			none => new None()
		);

	public static string? Id(this AnyOptionalSharpObject union) =>
		union.Match(
			player => player.Id,
			room => room.Id,
			exit => exit.Id,
			thing => thing.Id,
			none => null
		);

	public static string? Id(this AnyOptionalSharpContainer union) =>
		union.Match(
			player => player.Id,
			room => room.Id,
			thing => thing.Id,
			none => null
		);

	public static string? Id(this AnySharpObject union) =>
		union.Match(
			player => player.Id,
			room => room.Id,
			exit => exit.Id,
			thing => thing.Id
		);

	public static bool IsNone(this AnyOptionalSharpObject union) => union.IsT4;

	public static bool IsNone(this AnyOptionalSharpContainer union) => union.IsT3;

	public static bool IsNone(this AnyOptionalSharpObjectOrError union) => union.IsT4;

	public static bool IsError(this AnyOptionalSharpObjectOrError union) => union.IsT5;

	public static bool IsValid(this AnyOptionalSharpObjectOrError union) => !(union.IsT4 || union.IsT5);
}