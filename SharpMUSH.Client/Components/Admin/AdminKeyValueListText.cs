using SharpMUSH.Client.Services;

namespace SharpMUSH.Client.Components.Admin;

/// <summary>
/// Every string an <c>AdminKeyValueList</c> renders. The page supplies them, already localized, so
/// each config section keeps its own <c>SharedResource</c> keys while sharing the behaviour.
/// </summary>
/// <param name="ValuesLabel">
/// The label over the comma-separated value input, or <see langword="null"/> for a key-only list
/// such as banned names. Null is what tells the component there is no second field.
/// </param>
/// <param name="ValuesRequired">
/// Shown when the operator typed a value list that parsed to nothing. Only meaningful when
/// <paramref name="ValuesLabel"/> is set.
/// </param>
/// <param name="Added">Success line for an added entry, given its key.</param>
/// <param name="Removed">Success line for a removed entry, given its key.</param>
/// <param name="LoadFailed">
/// Failure line for the initial read. Takes the <see cref="ApiFailure"/> rather than a string so a
/// page can distinguish "your session expired" from "the server is down" — the distinction the old
/// bool-returning services threw away.
/// </param>
public sealed record AdminKeyValueListText(
	string AddTitle,
	string KeyLabel,
	string KeyPlaceholder,
	string AddButton,
	string ListTitle,
	string Empty,
	Func<string, string> Added,
	Func<string, string> Removed,
	Func<ApiFailure, string> LoadFailed,
	Func<string, ApiFailure, string> AddFailed,
	Func<string, ApiFailure, string> RemoveFailed)
{
	public string? ValuesLabel { get; init; }

	public string? ValuesPlaceholder { get; init; }

	public string? ValuesRequired { get; init; }
}
