// Positional accessors (IsT0/AsT0/Match/Switch/FromT0) for call sites not yet rewritten to pattern
// matching. Generated scaffolding: it goes away once nothing calls it.

using SharpMUSH.Client.Models;

namespace SharpMUSH.Client.Services;

partial union TranslationSaveResult
{
	public bool IsT0 => Value is WikiTranslationInfo;
	public bool IsT1 => Value is WikiTranslationSaveError;

	public WikiTranslationInfo AsT0 => Value is WikiTranslationInfo t0
		? t0
		: throw new InvalidOperationException($"Cannot return as T0 as result is {Value?.GetType().Name ?? "null"}");

	public WikiTranslationSaveError AsT1 => Value is WikiTranslationSaveError t1
		? t1
		: throw new InvalidOperationException($"Cannot return as T1 as result is {Value?.GetType().Name ?? "null"}");

	public TResult Match<TResult>(Func<WikiTranslationInfo, TResult> f0, Func<WikiTranslationSaveError, TResult> f1) => Value switch
	{
		WikiTranslationInfo t0 => f0(t0),
		WikiTranslationSaveError t1 => f1(t1),
		_ => throw new InvalidOperationException($"Unexpected case {Value?.GetType().Name ?? "null"}")
	};

	public void Switch(Action<WikiTranslationInfo> f0, Action<WikiTranslationSaveError> f1)
	{
		switch (Value)
		{
			case WikiTranslationInfo t0:
				f0(t0);
				return;
			case WikiTranslationSaveError t1:
				f1(t1);
				return;
			default:
				throw new InvalidOperationException($"Unexpected case {Value?.GetType().Name ?? "null"}");
		}
	}

	public static TranslationSaveResult FromT0(WikiTranslationInfo value) => new(value);
	public static TranslationSaveResult FromT1(WikiTranslationSaveError value) => new(value);
}
