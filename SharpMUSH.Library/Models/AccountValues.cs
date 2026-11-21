using System.Text.Json;
using SharpMUSH.Library.DiscriminatedUnions;

namespace SharpMUSH.Library.Models;

/// <summary>
/// One account value: JSON kept on a web account under <paramref name="Name"/>. <paramref name="Owner"/> is the objid
/// of the player who created it, or who last locked it. A <paramref name="Locked"/> value changes only for its owner
/// and for those who may override (<c>game.override</c>), as a locked attribute does.
/// </summary>
public sealed record AccountValue(string Name, string Json, string Owner, bool Locked);

/// <summary>A write refused because the account already holds <see cref="AccountValues.MaxPerAccount"/> values.</summary>
public readonly record struct AccountValuesFull;

/// <summary>A change refused because the value is locked and the writer is not its owner.</summary>
public readonly record struct AccountValueLocked;

/// <summary>
/// What a value write did, decided inside the write: <see cref="Success"/>, <see cref="NotFound"/> when there is no
/// such account, <see cref="AccountValuesFull"/> or <see cref="AccountValueLocked"/>; nothing is written for the last three.
/// </summary>
public union AccountValueWrite(Success, NotFound, AccountValuesFull, AccountValueLocked);

/// <summary>
/// What removing or (un)locking a value did: <see cref="Success"/>, <see cref="NotFound"/> when the account has no such
/// value, or <see cref="AccountValueLocked"/>, in which case nothing changed.
/// </summary>
public union AccountValueChange(Success, NotFound, AccountValueLocked);

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
