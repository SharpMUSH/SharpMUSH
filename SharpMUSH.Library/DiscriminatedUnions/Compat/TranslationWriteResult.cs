// Positional accessors (IsT0/AsT0/Match/Switch/FromT0) for call sites not yet rewritten to pattern
// matching. Generated scaffolding: it goes away once nothing calls it.

using SharpMUSH.Library.Models.Wiki;

namespace SharpMUSH.Library.DiscriminatedUnions;

partial union TranslationWriteResult
{
	public bool IsT0 => Value is WikiTranslation;
	public bool IsT1 => Value is WikiWriteConflict;
	public bool IsT2 => Value is Error<string>;

	public WikiTranslation AsT0 => Value is WikiTranslation t0
		? t0
		: throw new InvalidOperationException($"Cannot return as T0 as result is {Value?.GetType().Name ?? "null"}");

	public WikiWriteConflict AsT1 => Value is WikiWriteConflict t1
		? t1
		: throw new InvalidOperationException($"Cannot return as T1 as result is {Value?.GetType().Name ?? "null"}");

	public Error<string> AsT2 => Value is Error<string> t2
		? t2
		: throw new InvalidOperationException($"Cannot return as T2 as result is {Value?.GetType().Name ?? "null"}");

	public TResult Match<TResult>(Func<WikiTranslation, TResult> f0, Func<WikiWriteConflict, TResult> f1, Func<Error<string>, TResult> f2) => Value switch
	{
		WikiTranslation t0 => f0(t0),
		WikiWriteConflict t1 => f1(t1),
		Error<string> t2 => f2(t2),
		_ => throw new InvalidOperationException($"Unexpected case {Value?.GetType().Name ?? "null"}")
	};

	public void Switch(Action<WikiTranslation> f0, Action<WikiWriteConflict> f1, Action<Error<string>> f2)
	{
		switch (Value)
		{
			case WikiTranslation t0:
				f0(t0);
				return;
			case WikiWriteConflict t1:
				f1(t1);
				return;
			case Error<string> t2:
				f2(t2);
				return;
			default:
				throw new InvalidOperationException($"Unexpected case {Value?.GetType().Name ?? "null"}");
		}
	}

	public static TranslationWriteResult FromT0(WikiTranslation value) => new(value);
	public static TranslationWriteResult FromT1(WikiWriteConflict value) => new(value);
	public static TranslationWriteResult FromT2(Error<string> value) => new(value);
}
