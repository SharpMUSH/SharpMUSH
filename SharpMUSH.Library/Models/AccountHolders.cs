namespace SharpMUSH.Library.Models;

/// <summary>
/// An account's attributes are kept on a holder that is not an object in the database: it has no dbref of its own,
/// is written <c>#acct:&lt;key&gt;</c>, and is found only by the commands and functions that read and write attributes.
/// Its number lies in a reserved negative range that no object can take, so a holder's attribute rows never meet an
/// object's, and every lookup of an object by number answers that there is none.
/// </summary>
public static class AccountHolders
{
	/// <summary>How a holder is written: <c>#acct:</c> and the account's key.</summary>
	public const string Prefix = "#acct:";

	/// <summary>Every number below this is a holder's.</summary>
	public const int Ceiling = -(1 << 30);

	/// <summary>The largest account key a holder can name.</summary>
	public const int MaxKey = Ceiling - int.MinValue - 1;

	/// <summary>Whether <paramref name="dbref"/> names an account's holder rather than an object.</summary>
	public static bool IsAccountHolder(this DBRef dbref) => dbref.Number < Ceiling;

	/// <summary>The holder of the account with key <paramref name="accountKey"/>.</summary>
	public static DBRef For(int accountKey) => new(int.MinValue + accountKey);

	/// <summary>The holder of the account <paramref name="accountId"/> (<c>node_accounts/5</c> or <c>5</c>), when it has one.</summary>
	public static DBRef? ForAccount(string? accountId)
	{
		var key = accountId?[(accountId.IndexOf('/') + 1)..];
		return int.TryParse(key, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var number)
			&& number <= MaxKey
				? For(number)
				: null;
	}

	/// <summary>How softcode names the account <paramref name="accountId"/>: its holder, <c>#acct:&lt;key&gt;</c>; empty when it has none.</summary>
	public static string RefOf(string? accountId) => ForAccount(accountId)?.ToString() ?? string.Empty;

	/// <summary>The number (key) of the account whose holder <paramref name="holder"/> is.</summary>
	public static int AccountNumber(this DBRef holder) => holder.Number - int.MinValue;

	/// <summary>The id of the account whose holder <paramref name="holder"/> is.</summary>
	public static string AccountId(this DBRef holder) => $"node_accounts/{holder.AccountNumber()}";

	/// <summary><c>#acct:&lt;key&gt;</c>, read case-insensitively; nothing when <paramref name="text"/> is not one.</summary>
	public static DBRef? Parse(ReadOnlySpan<char> text)
		=> text.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)
			&& text[Prefix.Length..] is { Length: > 0 } digits && digits[0] is >= '0' and <= '9'
			&& int.TryParse(digits, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var key)
			&& key <= MaxKey
				? For(key)
				: null;
}
