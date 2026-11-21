using System.Text.Json;
using SharpMUSH.Library.DiscriminatedUnions;

namespace SharpMUSH.Library.Models;

/// <summary>The three things an account value's lock can gate.</summary>
public enum AccountValueLockKind
{
	/// <summary>Reading the value, and seeing its name in a listing.</summary>
	View,

	/// <summary>Creating the value or changing its JSON.</summary>
	Write,

	/// <summary>Removing the value.</summary>
	Delete
}

/// <summary>
/// A view, write and delete lock, each a bound lock expression evaluated with the player named as the locked object and
/// the caller as the unlocker. An empty lock sets nothing, so the next layer decides: a value's own lock, then the rules
/// (<see cref="AccountValueRule"/>), then the default permission.
/// </summary>
public sealed record AccountValueLocks(string View, string Write, string Delete)
{
	public static AccountValueLocks None { get; } = new(string.Empty, string.Empty, string.Empty);

	public bool IsEmpty => View.Length == 0 && Write.Length == 0 && Delete.Length == 0;

	public string this[AccountValueLockKind kind] => kind switch
	{
		AccountValueLockKind.View => View,
		AccountValueLockKind.Write => Write,
		_ => Delete
	};

	public AccountValueLocks With(AccountValueLockKind kind, string lockString) => kind switch
	{
		AccountValueLockKind.View => this with { View = lockString },
		AccountValueLockKind.Write => this with { Write = lockString },
		_ => this with { Delete = lockString }
	};
}

/// <summary>One account value: JSON kept on a web account under <paramref name="Name"/>, and the locks set on it alone.</summary>
public sealed record AccountValue(string Name, string Json, AccountValueLocks Locks);

/// <summary>
/// A game-wide rule: the locks every account's values whose names match the wildcard <paramref name="Pattern"/> take,
/// unless a value sets its own.
/// </summary>
public sealed record AccountValueRule(string Pattern, AccountValueLocks Locks);

/// <summary>A write refused because the account already holds <see cref="AccountValues.MaxPerAccount"/> values.</summary>
public readonly record struct AccountValuesFull;

/// <summary>
/// A write refused because the value's locks (or whether it exists) changed after the caller checked them. Nothing was
/// written; the caller checks again.
/// </summary>
public readonly record struct AccountValueChanged;

/// <summary>
/// What a value write did, decided inside the write: <see cref="Success"/>, <see cref="NotFound"/> when there is no
/// such account, <see cref="AccountValuesFull"/> or <see cref="AccountValueChanged"/>; nothing is written for the last three.
/// </summary>
public union AccountValueWrite(Success, NotFound, AccountValuesFull, AccountValueChanged);

/// <summary>
/// What removing a value or setting its locks did: <see cref="Success"/>, <see cref="NotFound"/> when the account has no
/// such value, or <see cref="AccountValueChanged"/>, in which case nothing changed.
/// </summary>
public union AccountValueChange(Success, NotFound, AccountValueChanged);

/// <summary>The rules an account value's name and JSON follow, checked before anything reaches the store.</summary>
public static class AccountValues
{
	/// <summary>The most values one account holds.</summary>
	public const int MaxPerAccount = 1000;

	/// <summary>The longest value name, in characters.</summary>
	public const int MaxNameLength = 64;

	/// <summary>
	/// The value name <paramref name="given"/> stands for: 1 to <see cref="MaxNameLength"/> printable ASCII characters
	/// other than space and <c>/</c>, folded to lower case so <c>XP</c> and <c>xp</c> are one value.
	/// </summary>
	public static Result<string> Name(string given)
		=> given.Length is > 0 and <= MaxNameLength && given.All(c => c is > ' ' and <= '~' and not '/')
			? given.ToLowerInvariant()
			: new Error<string>($"#-1 ACCOUNT VALUE NAME MUST BE 1 TO {MaxNameLength} PRINTABLE ASCII CHARACTERS WITHOUT SPACES OR /");

	/// <summary>The lock kind <paramref name="given"/> names: <c>view</c>, <c>write</c> or <c>delete</c>, in any case.</summary>
	public static Result<AccountValueLockKind> LockKind(string given) => given.Trim().ToLowerInvariant() switch
	{
		"view" => AccountValueLockKind.View,
		"write" => AccountValueLockKind.Write,
		"delete" => AccountValueLockKind.Delete,
		_ => new Error<string>("#-1 LOCK MUST BE VIEW, WRITE OR DELETE")
	};

	/// <summary>
	/// <paramref name="given"/> as a value: one JSON value (an object, array, string, number, true, false or null) of at
	/// most <paramref name="maxBytes"/> bytes of UTF-8, with the whitespace around it trimmed.
	/// </summary>
	public static Result<string> Json(string given, int maxBytes)
	{
		var json = given.Trim();
		if (System.Text.Encoding.UTF8.GetByteCount(json) > maxBytes)
			return new Error<string>("#-1 ACCOUNT VALUE TOO LONG");

		try
		{
			using var _ = JsonDocument.Parse(json);
			return json;
		}
		catch (JsonException)
		{
			return new Error<string>("#-1 INVALID JSON");
		}
	}
}

/// <summary>How the game-wide rules apply to one value name.</summary>
public static class AccountValueRules
{
	/// <summary>
	/// The lock of <paramref name="kind"/> the rules give <paramref name="name"/>: that of the most specific rule whose
	/// pattern matches it and that sets one, the longest pattern first and then ordinal order; empty when none does.
	/// </summary>
	public static string Lock(IEnumerable<AccountValueRule> rules, string name, AccountValueLockKind kind)
		=> rules
			.Where(rule => rule.Locks[kind].Length > 0 && Utilities.SoftcodeRegex.Wildcard(rule.Pattern).IsMatch(name))
			.OrderByDescending(rule => rule.Pattern.Length)
			.ThenBy(rule => rule.Pattern, StringComparer.Ordinal)
			.Select(rule => rule.Locks[kind])
			.FirstOrDefault() ?? string.Empty;

	/// <summary>The lock of <paramref name="kind"/> that decides for the value <paramref name="name"/>: its own, else the rules'.</summary>
	public static string Effective(IEnumerable<AccountValueRule> rules, string name, AccountValueLocks own, AccountValueLockKind kind)
		=> own[kind] is { Length: > 0 } set ? set : Lock(rules, name, kind);
}

