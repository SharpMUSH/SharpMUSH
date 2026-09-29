namespace SharpMUSH.Implementation.Common;

/// <summary>
/// PennMUSH's <c>list_all_flags</c> (src/flags.c) with <c>FLAG_LIST_NAMECHAR</c>, which both
/// <c>@list flags</c>/<c>@list powers</c> and <c>list(flags)</c>/<c>list(powers)</c> print.
/// </summary>
public static class FlagListHelpers
{
	/// <summary>
	/// <c>NAME (c), NAME, ...</c> in the definition's own casing. God sees everything but internal
	/// flags; anyone else also loses the disabled ones; a player who is neither wizard nor royalty also
	/// loses dark and mdark ones.
	/// </summary>
	public static string Format(
		IEnumerable<(string Name, string Symbol, string[] SetPermissions, bool Disabled)> flags,
		bool god, bool privileged)
	{
		bool Has(string[] permissions, string permission)
			=> permissions.Contains(permission, StringComparer.OrdinalIgnoreCase);

		var visible = flags
			.Where(f => !Has(f.SetPermissions, "internal"))
			.Where(f => god || !(f.Disabled || Has(f.SetPermissions, "disabled")))
			.Where(f => privileged || !(Has(f.SetPermissions, "dark") || Has(f.SetPermissions, "mdark")))
			.OrderBy(f => SortKey(f.Name), StringComparer.Ordinal)
			.ThenBy(f => f.Name, StringComparer.Ordinal)
			.Select(f => f.Name
				+ (f.Symbol is { Length: 1 } letter ? $" ({letter})" : string.Empty)
				+ (f.Disabled ? " (disabled)" : string.Empty));

		return string.Join(", ", visible);
	}

	/// <summary>
	/// PennMUSH sorts the names as <c>ALPHANUM_LIST</c>, which is <c>strcoll</c>: in the locale a game
	/// runs under, case and punctuation only break ties, so <c>NOSPOOF</c> falls between <c>NO_LOG</c>
	/// and <c>NO_TEL</c>, and <c>Can_spoof</c> between <c>CAN_HTTP</c> and <c>Chat_Privs</c>.
	/// </summary>
	private static string SortKey(string name)
		=> string.Concat(name.Where(char.IsLetterOrDigit)).ToUpperInvariant();
}
