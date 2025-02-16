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
			_ => null
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
		=> union.Match<SharpObject?>(
			player => player.Object,
			room => room.Object,
			thing => thing.Object,
			_ => null
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
			_ => throw new ArgumentException("Cannot convert an None to a non-None value.")
		);

	public static AnyOptionalSharpObject WithoutError(this AnyOptionalSharpObjectOrError union)
		=> union.Match<AnyOptionalSharpObject>(
			player => player,
			room => room,
			exit => exit,
			thing => thing,
			none => none,
			_ => throw new ArgumentException("Cannot convert an Error to a non-Error value.")
		);

	public static async ValueTask<AnySharpContainer> Home(this AnySharpContent thing)
		=> await thing.Match(
			async player => await player.Home.WithCancellation(CancellationToken.None),
			async exit => await exit.Location.WithCancellation(CancellationToken.None),
			async thing2 => await thing2.Home.WithCancellation(CancellationToken.None));

	public static async ValueTask<AnySharpContainer> Location(this AnySharpContent thing)
		=> await thing.Match(
			async player => await player.Location.WithCancellation(CancellationToken.None),
			async exit => await exit.Home.WithCancellation(CancellationToken.None),
			async thing2 => await thing2.Location.WithCancellation(CancellationToken.None));

	public static AnySharpObject Known(this AnyOptionalSharpObject union) =>
		union.Match<AnySharpObject>(
			player => player,
			room => room,
			exit => exit,
			thing => thing,
			_ => throw new ArgumentNullException(nameof(union)));

	public static OneOf<SharpObject, None> ObjectOption(this AnyOptionalSharpObject union) =>
		union.Match<OneOf<SharpObject, None>>(
			player => player.Object,
			room => room.Object,
			exit => exit.Object,
			thing => thing.Object,
			_ => new None()
		);

	public static string? Id(this AnyOptionalSharpObject union) =>
		union.Match(
			player => player.Id,
			room => room.Id,
			exit => exit.Id,
			thing => thing.Id,
			_ => null
		);

	public static string? Id(this AnyOptionalSharpContainer union) =>
		union.Match(
			player => player.Id,
			room => room.Id,
			thing => thing.Id,
			_ => null
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