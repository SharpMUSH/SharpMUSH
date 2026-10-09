namespace SharpMUSH.Configuration.Options;

/// <summary>
/// The stand-ins a client without Unicode is sent: one entry per character, before the built-in ones. A
/// character is in the table once; setting it again replaces its text.
/// </summary>
public record AsciiTranslationsOptions(
	[property: SharpConfig(
		Name = "ascii_translations",
		Category = "AsciiTranslations",
		Description = "What a client without Unicode is sent for a character it cannot show, by character",
		Group = "ASCII Clients",
		Order = 1,
		Tooltip = "Format: character → the ASCII text sent for it; empty text leaves the character out")]
	Dictionary<string, string> Translations
);
