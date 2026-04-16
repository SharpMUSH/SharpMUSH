namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>
/// Provides locale-aware string lookup backed by embedded .resx resource files.
/// Keys correspond to constant names in <see cref="SharpMUSH.Library.Definitions.ErrorMessages.Notifications"/>.
/// The neutral (English) baseline lives in Resources/Notifications.resx.
/// Culture-specific overrides live in Resources/Notifications.{locale}.resx (future).
/// </summary>
public interface ILocalizationService
{
	/// <summary>Gets the translated string for <paramref name="key"/> in the given locale.
	/// Falls back to English if the locale has no translation.</summary>
	string Get(string key, string? locale = null);

	/// <summary>Gets and formats the translated string for <paramref name="key"/> with the supplied arguments.
	/// If <paramref name="args"/> is empty the raw template is returned unchanged.</summary>
	string Format(string key, string? locale, params object[] args);

	/// <summary>Returns true when a non-fallback translation exists for the given key and locale.</summary>
	bool HasTranslation(string key, string locale);

	/// <summary>BCP-47 locale tags for which at least one translation file exists (e.g. "en").</summary>
	IReadOnlyList<string> AvailableLocales { get; }
}
