namespace SharpMUSH.Tests.BUnit.Resources;

/// <summary>
/// Which portal surface a resource key belongs to, keyed by its name prefix.
/// <para>
/// A C# mirror of the <c>SURFACES</c> map in <c>tools/i18n/extract_untranslated.py</c>, which is the
/// source of truth — the translation tooling batches by it and the runbook
/// (<c>docs/localization/adding-languages.md</c>) defines the player-facing/staff split in terms of it.
/// <see cref="PortalSurfacesTests"/> parses the Python file at test time and fails if the two drift.
/// </para>
/// <para>
/// The split exists because staff surfaces are allowed to lag: they are roughly two thirds of the
/// strings and the least urgent, so only the player-facing keys are gated per locale.
/// </para>
/// </summary>
internal static class PortalSurfaces
{
	/// <summary>The fallback for a key matching no prefix, matching the Python <c>surface_of</c>.</summary>
	public const string Default = "staff: MUSH server configuration";

	public static readonly IReadOnlyDictionary<string, string> ByPrefix = new Dictionary<string, string>(StringComparer.Ordinal)
	{
		["Auth"] = "player-facing: sign-in, registration, account",
		["Nav"] = "player-facing: navigation, settings, mail, play",
		["Rol"] = "player-facing: scenes and character profiles / staff: roles",
		["Wk"] = "mixed: wiki authoring and media library",
		["Wid"] = "player-facing: dashboard widgets",
		["Term"] = "player-facing: in-browser telnet terminal and softcode editor",
		["Res"] = "mixed: wiki, scenes, config leftovers",
		["WidgetZone"] = "staff: layout editor zone names",
		["Adm"] = "staff: admin pages and dashboard",
		["Lay"] = "staff: layout and application administration",
		["Pkg"] = "staff: softcode package manager",
		["Enum"] = "mixed: permission labels and enum display names",
	};

	/// <summary>
	/// Longest prefix wins, as in the Python tool. Not an incidental detail: <c>WidgetZone</c> keys are
	/// staff strings that also start with the player-facing <c>Wid</c> prefix, so shortest-first
	/// matching would pull the layout editor's zone names into the gated set.
	/// </summary>
	public static string SurfaceOf(string key)
	{
		foreach (var prefix in ByPrefix.Keys.OrderByDescending(p => p.Length).ThenBy(p => p, StringComparer.Ordinal))
		{
			if (key.StartsWith(prefix, StringComparison.Ordinal))
			{
				return ByPrefix[prefix];
			}
		}

		return Default;
	}

	public static bool IsPlayerFacing(string key) =>
		SurfaceOf(key).StartsWith("player-facing", StringComparison.Ordinal);
}
