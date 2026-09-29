namespace SharpMUSH.Implementation.Common;

/// <summary>
/// PennMUSH's <c>list_all_flags</c> (src/flags.c) with <c>FLAG_LIST_NAMECHAR</c>, which both
/// <c>@list flags</c>/<c>@list powers</c> and <c>list(flags)</c>/<c>list(powers)</c> print.
/// </summary>
public static class FlagListHelpers
{
	/// <summary>
	/// <c>NAME (c), NAME, ...</c> in the definition's own casing, sorted as PennMUSH's <c>ALPHANUM_LIST</c>
	/// sorts them under the C locale: <c>strcoll</c> there is byte order, so <c>HOOK</c> precedes
	/// <c>Halt</c> and <c>NOSPOOF</c> precedes <c>NO_COMMAND</c>. (Under another <c>LC_COLLATE</c>
	/// PennMUSH's order follows that locale; SharpMUSH has no per-game collation, so it keeps this one.)
	/// God sees everything but internal flags; anyone else also loses the disabled ones; a player who is
	/// neither wizard nor royalty also loses dark and mdark ones.
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
			.OrderBy(f => f.Name, StringComparer.Ordinal)
			.Select(f => f.Name
				+ (f.Symbol is { Length: 1 } letter ? $" ({letter})" : string.Empty)
				+ (f.Disabled ? " (disabled)" : string.Empty));

		return string.Join(", ", visible);
	}
}
