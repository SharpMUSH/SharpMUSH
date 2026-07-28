using System.Globalization;

namespace SharpMUSH.Client.Resources;

/// <summary>
/// The locales the portal chrome ships translations for. One list, read by the nav language picker and by
/// the wiki editor's locale dropdown, so a language can never appear in one and not the other.
/// </summary>
/// <remarks>
/// This is deliberately <em>not</em> the set of locales wiki content may be translated into. A game may
/// translate its wiki into any locale <see cref="CultureInfo.GetCultureInfo(string, bool)"/> accepts — the
/// chrome falls back to English, the content does not — so the editor offers this list plus whatever
/// translations already exist plus a free-text field.
/// <para>To add a language: add its code here, add the matching <c>SharedResource.{code}.resx</c>, and add
/// the code to <c>SatelliteResourceLanguages</c> in <c>SharpMUSH.Client.csproj</c>. Display names come from
/// the framework. <c>DeclaredLocaleCoverageTests</c> fails a code declared without a translation, and
/// <c>PortalSurfacesTests</c> fails one left out of the csproj.</para>
/// <para>Languages are named, not flagged. A flag is a country and a country is not a language: 🇪🇸 for
/// <c>es</c> privileges Spain over Latin America, <c>pt-BR</c> and <c>pt-PT</c> would need two flags for
/// one language, and <c>zh-Hans</c> is a script with no flag at all. The native name is unambiguous in
/// every one of those cases and is the thing a speaker actually scans for.</para>
/// </remarks>
public static class PortalLocales
{
	/// <summary>Supported locale codes, in display order: English first, then alphabetically.</summary>
	public static IReadOnlyList<string> Codes { get; } = ["en", "de", "fr"];

	/// <summary>
	/// A locale's name in its own language, first character upper-cased ("Français"). Falls back to the
	/// tag itself for a locale this runtime does not know — not expected for anything in
	/// <see cref="Codes"/> now that the client loads full ICU data, but the wiki editor accepts free-text
	/// tags and a typo must render as itself rather than throw.
	/// </summary>
	public static string DisplayName(string code)
	{
		try
		{
			var culture = CultureInfo.GetCultureInfo(code, predefinedOnly: true);
			var name = culture.NativeName;
			if (name.Length > 0 && char.IsLower(name[0]))
				name = char.ToUpper(name[0], culture) + name[1..];
			return name;
		}
		catch (CultureNotFoundException)
		{
			return code;
		}
	}
}
