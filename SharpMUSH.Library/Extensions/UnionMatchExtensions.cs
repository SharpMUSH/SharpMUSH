using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Extensions;

/// <summary>
/// Match() extension methods for all union types.
/// These provide the same API as OneOf's .Match() to preserve call-site compatibility
/// while using native C# union types under the hood.
/// </summary>
public static class UnionMatchExtensions
{
	// ── AnySharpObject ──────────────────────────────────────────────────────

	public static T Match<T>(this AnySharpObject u,
		Func<SharpPlayer, T> player,
		Func<SharpRoom,   T> room,
		Func<SharpExit,   T> exit,
		Func<SharpThing,  T> thing) => u.Value switch
	{
		SharpPlayer p => player(p),
		SharpRoom   r => room(r),
		SharpExit   e => exit(e),
		SharpThing  t => thing(t),
		_ => throw new InvalidOperationException("Unexpected AnySharpObject case")
	};

	public static async ValueTask<T> Match<T>(this AnySharpObject u,
		Func<SharpPlayer, ValueTask<T>> player,
		Func<SharpRoom,   ValueTask<T>> room,
		Func<SharpExit,   ValueTask<T>> exit,
		Func<SharpThing,  ValueTask<T>> thing) => u.Value switch
	{
		SharpPlayer p => await player(p),
		SharpRoom   r => await room(r),
		SharpExit   e => await exit(e),
		SharpThing  t => await thing(t),
		_ => throw new InvalidOperationException("Unexpected AnySharpObject case")
	};

	// ── AnySharpContainer ───────────────────────────────────────────────────

	public static T Match<T>(this AnySharpContainer u,
		Func<SharpPlayer, T> player,
		Func<SharpRoom,   T> room,
		Func<SharpThing,  T> thing) => u.Value switch
	{
		SharpPlayer p => player(p),
		SharpRoom   r => room(r),
		SharpThing  t => thing(t),
		_ => throw new InvalidOperationException("Unexpected AnySharpContainer case")
	};

	public static async ValueTask<T> Match<T>(this AnySharpContainer u,
		Func<SharpPlayer, ValueTask<T>> player,
		Func<SharpRoom,   ValueTask<T>> room,
		Func<SharpThing,  ValueTask<T>> thing) => u.Value switch
	{
		SharpPlayer p => await player(p),
		SharpRoom   r => await room(r),
		SharpThing  t => await thing(t),
		_ => throw new InvalidOperationException("Unexpected AnySharpContainer case")
	};

	// ── AnySharpContent ─────────────────────────────────────────────────────

	public static T Match<T>(this AnySharpContent u,
		Func<SharpPlayer, T> player,
		Func<SharpExit,   T> exit,
		Func<SharpThing,  T> thing) => u.Value switch
	{
		SharpPlayer p => player(p),
		SharpExit   e => exit(e),
		SharpThing  t => thing(t),
		_ => throw new InvalidOperationException("Unexpected AnySharpContent case")
	};

	public static async ValueTask<T> Match<T>(this AnySharpContent u,
		Func<SharpPlayer, ValueTask<T>> player,
		Func<SharpExit,   ValueTask<T>> exit,
		Func<SharpThing,  ValueTask<T>> thing) => u.Value switch
	{
		SharpPlayer p => await player(p),
		SharpExit   e => await exit(e),
		SharpThing  t => await thing(t),
		_ => throw new InvalidOperationException("Unexpected AnySharpContent case")
	};

	// ── AnyOptionalSharpObject ──────────────────────────────────────────────

	public static T Match<T>(this AnyOptionalSharpObject u,
		Func<SharpPlayer, T> player,
		Func<SharpRoom,   T> room,
		Func<SharpExit,   T> exit,
		Func<SharpThing,  T> thing,
		Func<None, T> none) => u.Value switch
	{
		SharpPlayer p => player(p),
		SharpRoom   r => room(r),
		SharpExit   e => exit(e),
		SharpThing  t => thing(t),
		None n        => none(n),
		_ => none(default)
	};

	// ── AnyOptionalSharpObjectOrError ───────────────────────────────────────

	public static T Match<T>(this AnyOptionalSharpObjectOrError u,
		Func<SharpPlayer, T> player,
		Func<SharpRoom,   T> room,
		Func<SharpExit,   T> exit,
		Func<SharpThing,  T> thing,
		Func<None,       T> none,
		Func<SharpError, T> error) => u.Value switch
	{
		SharpPlayer p => player(p),
		SharpRoom   r => room(r),
		SharpExit   e => exit(e),
		SharpThing  t => thing(t),
		SharpError  err => error(err),
		None n          => none(n),
		_ => none(default)
	};

	public static async ValueTask<T> Match<T>(this AnyOptionalSharpObjectOrError u,
		Func<SharpPlayer, ValueTask<T>> player,
		Func<SharpRoom,   ValueTask<T>> room,
		Func<SharpExit,   ValueTask<T>> exit,
		Func<SharpThing,  ValueTask<T>> thing,
		Func<None,       ValueTask<T>> none,
		Func<SharpError, ValueTask<T>> error) => u.Value switch
	{
		SharpPlayer p => await player(p),
		SharpRoom   r => await room(r),
		SharpExit   e => await exit(e),
		SharpThing  t => await thing(t),
		SharpError  err => await error(err),
		None n          => await none(n),
		_ => await none(default)
	};

	// ── AnySharpObjectOrErrorCallState ──────────────────────────────────────

	public static T Match<T>(this AnySharpObjectOrErrorCallState u,
		Func<AnySharpObject,     T> obj,
		Func<SharpErrorCallState, T> error) => u.Value switch
	{
		AnySharpObject   o => obj(o),
		SharpErrorCallState e => error(e),
		_ => throw new InvalidOperationException("Unexpected AnySharpObjectOrErrorCallState case")
	};

	// ── OptionalSharpAttributeOrError ───────────────────────────────────────

	public static T Match<T>(this OptionalSharpAttributeOrError u,
		Func<SharpAttribute[], T> attr,
		Func<None,            T> none,
		Func<SharpError,      T> error) => u.Value switch
	{
		SharpAttribute[] a => attr(a),
		SharpError       e => error(e),
		None n             => none(n),
		_ => none(default)
	};

	// ── OptionalLazySharpAttributeOrError ───────────────────────────────────

	public static T Match<T>(this OptionalLazySharpAttributeOrError u,
		Func<LazySharpAttribute[], T> attr,
		Func<None,                 T> none,
		Func<SharpError,           T> error) => u.Value switch
	{
		LazySharpAttribute[] a => attr(a),
		SharpError           e => error(e),
		None n                 => none(n),
		_ => none(default)
	};

	// ── SharpAttributesOrError ───────────────────────────────────────────────

	public static T Match<T>(this SharpAttributesOrError u,
		Func<SharpAttribute[], T> attr,
		Func<SharpError,       T> error) => u.Value switch
	{
		SharpAttribute[] a => attr(a),
		SharpError       e => error(e),
		_ => throw new InvalidOperationException("Unexpected SharpAttributesOrError case")
	};

	// ── LazySharpAttributesOrError ───────────────────────────────────────────

	public static T Match<T>(this LazySharpAttributesOrError u,
		Func<IAsyncEnumerable<LazySharpAttribute>, T> attr,
		Func<SharpError,                           T> error) => u.Value switch
	{
		IAsyncEnumerable<LazySharpAttribute> a => attr(a),
		SharpError                           e => error(e),
		_ => throw new InvalidOperationException("Unexpected LazySharpAttributesOrError case")
	};

	// ── AnyOptionalSharpContainer ────────────────────────────────────────────

	public static T Match<T>(this AnyOptionalSharpContainer u,
		Func<SharpPlayer, T> player,
		Func<SharpRoom,   T> room,
		Func<SharpThing,  T> thing,
		Func<None,        T> none) => u.Value switch
	{
		SharpPlayer p => player(p),
		SharpRoom   r => room(r),
		SharpThing  t => thing(t),
		None n        => none(n),
		_ => none(default)
	};

	// ── Option<T> ────────────────────────────────────────────────────────────

	public static TResult Match<T, TResult>(this Option<T> u,
		Func<T, TResult>    some,
		Func<None, TResult> none) =>
		u.IsSome() ? some(u.AsValue()) : none(default);

	// ── DbRefOrName ──────────────────────────────────────────────────────────

	public static T Match<T>(this DbRefOrName u,
		Func<DBRef,   T> dbref,
		Func<string,  T> name) => u.Value switch
	{
		DBRef   d => dbref(d),
		string  s => name(s),
		_ => throw new InvalidOperationException("Unexpected DbRefOrName case")
	};

	// ── SharpResult ──────────────────────────────────────────────────────────

	public static T Match<T>(this SharpResult u,
		Func<SharpSuccess, T> success,
		Func<SharpError,   T> error) => u.Value switch
	{
		SharpSuccess s => success(s),
		SharpError   e => error(e),
		_ => throw new InvalidOperationException("Unexpected SharpResult case")
	};

	// ── SemaphoreTarget ──────────────────────────────────────────────────────

	public static T Match<T>(this SemaphoreTarget u,
		Func<long,          T> handle,
		Func<DBRef,         T> dbref,
		Func<DbRefAttribute, T> attr) => u.Value switch
	{
		long            l => handle(l),
		DBRef           d => dbref(d),
		DbRefAttribute  a => attr(a),
		_ => throw new InvalidOperationException("Unexpected SemaphoreTarget case")
	};

	// ── AttributeTarget ──────────────────────────────────────────────────────

	public static T Match<T>(this AttributeTarget u,
		Func<AnySharpObject,    T> obj,
		Func<SharpAttributeEntry, T> attr,
		Func<SharpChannel,      T> channel,
		Func<None,              T> none) => u.Value switch
	{
		AnySharpObject     o => obj(o),
		SharpAttributeEntry a => attr(a),
		SharpChannel       c => channel(c),
		None n               => none(n),
		_ => none(default)
	};
}
