using System.Globalization;

namespace SharpMUSH.Library.Models.Wiki;

/// <summary>
/// The read model a localized wiki request resolves to. Never stored.
/// </summary>
/// <remarks>
/// Resolved content sits on this wrapper and never on <see cref="Page"/>. If <c>Page.Title</c> stayed
/// authoritative-looking, a caller would eventually render the English title beside French body text
/// and nobody would notice for months.
/// <para>
/// <see cref="Locale"/> and <see cref="RequestedLocale"/> are guaranteed to be already-normalised,
/// parseable tags, so the <see cref="CultureInfo.GetCultureInfo(string)"/> calls in
/// <see cref="IsFallback"/> cannot throw. That rests on two things, not one:
/// <c>IWikiLocalizationService</c> is the only thing that constructs this record and it normalises the
/// <em>requested</em> tag first, <b>and</b> no unparseable locale can be in the store to begin with
/// because every write boundary goes through <c>WikiHelpers.NormalizeLocale</c>. The second half is what
/// makes this an invariant rather than a convention a future caller can break.
/// </para>
/// </remarks>
/// <param name="Page">Identity and inherited metadata ONLY — never a content source.</param>
/// <param name="Locale">The locale actually served.</param>
/// <param name="RequestedLocale">The locale the reader asked for, after normalisation.</param>
/// <param name="Title">Resolved title.</param>
/// <param name="MarkdownSource">Resolved Markdown body.</param>
/// <param name="RenderedHtml">Resolved HTML.</param>
/// <param name="PlainText">Resolved plain text.</param>
/// <param name="Published">The <em>served</em> row's flag — the translation's when a translation is
/// served, the page's when the source is served.</param>
/// <param name="RevisionNumber">The served row's revision counter.</param>
public sealed record LocalizedWikiPage(
	WikiPage Page,
	string Locale,
	string RequestedLocale,
	string Title,
	string MarkdownSource,
	string RenderedHtml,
	string PlainText,
	bool Published,
	int RevisionNumber)
{
	/// <summary>
	/// True when the served locale is a different <em>language</em> from the requested one, which is
	/// what the reader-facing notice keys off. Compares languages rather than tags so that serving
	/// <c>fr</c> to an <c>fr-CA</c> reader does not banner every Canadian visit.
	/// </summary>
	public bool IsFallback =>
		!string.Equals(
			CultureInfo.GetCultureInfo(Locale).TwoLetterISOLanguageName,
			CultureInfo.GetCultureInfo(RequestedLocale).TwoLetterISOLanguageName,
			StringComparison.OrdinalIgnoreCase);
}
