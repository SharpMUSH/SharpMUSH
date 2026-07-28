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
/// <para>To add a language: add its code and flag here, and add the matching
/// <c>SharedResource.{code}.resx</c>. Display names come from the framework.</para>
/// </remarks>
public static class PortalLocales
{
	/// <summary>Supported locale codes with their flag emoji, in display order.</summary>
	public static IReadOnlyList<(string Code, string Flag)> Supported { get; } =
	[
		("en", "\U0001F1FA\U0001F1F8"),
		("fr", "\U0001F1EB\U0001F1F7"),
	];

	/// <summary>Just the codes, for membership tests and dropdown population.</summary>
	public static IReadOnlyList<string> Codes { get; } = Supported.Select(l => l.Code).ToArray();

	private static readonly Dictionary<string, string> _flags =
		Supported.ToDictionary(l => l.Code, l => l.Flag, StringComparer.OrdinalIgnoreCase);

	/// <summary>
	/// A locale's name in its own language, first character upper-cased ("Français"). Falls back to the
	/// tag itself for a locale this runtime's ICU data does not know, which is expected: the WASM build
	/// ships a sharded ICU covering only the locales in <see cref="Supported"/>.
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

	/// <summary>
	/// The flag emoji for a supported locale, or a globe for anything else. Matched case-insensitively:
	/// a hand-typed <c>?lang=FR</c> reaches the read path uncanonicalised, and showing it a globe would
	/// read as "this language is not supported".
	/// </summary>
	public static string Flag(string code) =>
		_flags.TryGetValue(code, out var flag) ? flag : "\U0001F310";
}
