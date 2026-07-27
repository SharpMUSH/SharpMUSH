namespace SharpMUSH.Configuration.Options;

public record WikiOptions(
	[property: SharpConfig(
		Name = "wiki_default_locale",
		Category = "Wiki",
		Description = "Locale wiki pages fall back to when a reader's locale has no translation",
		Group = "Wiki",
		Order = 1,
		Tooltip = "A BCP-47 language tag, e.g. 'en', 'fr' or 'pt-BR'",
		ValidationPattern = @"^[A-Za-z]{2,3}(-[A-Za-z0-9]{2,8})*$")]
	string DefaultLocale = WikiOptions.DefaultLocaleFallback
)
{
	/// <summary>
	/// The locale used when nothing else supplies one: the parameter default above, the resolver's
	/// last resort when a configured value is unusable, and what the wiki-translation migration stamps
	/// on rows that predate <c>WikiPage.SourceLocale</c>.
	/// </summary>
	/// <remarks>
	/// One constant so those three cannot drift. A migration in particular <em>cannot</em> read
	/// <c>Wiki.DefaultLocale</c> at runtime — the configured value lives in the very database the
	/// migration is preparing — so it needs a compile-time value, and this is it.
	/// </remarks>
	public const string DefaultLocaleFallback = "en";
}
