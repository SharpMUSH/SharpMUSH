using OneOf;
using OneOf.Types;
using SharpMUSH.Library.Services.Interfaces;
using System.Text;

namespace SharpMUSH.Implementation.Services;

/// <summary>
/// The one implementation of PennMUSH help-topic resolution. Both the in-game <c>help</c> command
/// and the portal's read-only help API go through here, so a topic can never resolve to one entry
/// on telnet and another in a browser.
/// </summary>
public sealed class HelpTopicResolver(ITextFileService textFiles) : IHelpTopicResolver
{
	/// <inheritdoc />
	public async ValueTask<OneOf<HelpEntry, HelpCandidates, None>> ResolveAsync(string corpus, string topic)
	{
		if (string.IsNullOrWhiteSpace(topic))
		{
			return new None();
		}

		// A topic the reader wildcarded themselves is matched literally — no prefix or fuzzy fallback,
		// because they already said what shape they wanted.
		if (topic.Contains('*') || topic.Contains('?'))
		{
			return await NarrowAsync(corpus, await SearchAsync(corpus, topic));
		}

		// PennMUSH tries a prefix match first (name LIKE 'topic%', first alphabetically wins) and only
		// falls back to the fuzzy pattern when that finds nothing.
		var prefixMatches = await SearchAsync(corpus, topic + "*");
		if (prefixMatches.Count > 0)
		{
			var entry = await GetExactAsync(corpus, prefixMatches[0]);
			if (entry is not null)
			{
				return entry;
			}
		}

		return await NarrowAsync(corpus, await SearchAsync(corpus, BuildFuzzyPattern(topic)));
	}

	/// <inheritdoc />
	public async ValueTask<HelpEntry?> GetExactAsync(string corpus, string topic)
	{
		var content = await textFiles.GetEntryAsync(corpus, topic);
		return content is null ? null : new HelpEntry(topic, content);
	}

	/// <inheritdoc />
	public ValueTask<IReadOnlyList<string>> ListTopicsAsync(string corpus) => SearchTopicsAsync(corpus, "*");

	/// <inheritdoc />
	public ValueTask<IReadOnlyList<string>> SearchTopicsAsync(string corpus, string pattern) =>
		SearchAsync(corpus, pattern);

	/// <inheritdoc />
	public async ValueTask<IReadOnlyList<string>> SearchContentAsync(string corpus, string searchTerm) =>
		(await textFiles.SearchContentAsync(corpus, searchTerm))
		.OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
		.ToList();

	private async ValueTask<IReadOnlyList<string>> SearchAsync(string corpus, string pattern) =>
		(await textFiles.SearchEntriesAsync(corpus, pattern))
		.OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
		.ToList();

	private async ValueTask<OneOf<HelpEntry, HelpCandidates, None>> NarrowAsync(
		string corpus,
		IReadOnlyList<string> matches)
	{
		switch (matches.Count)
		{
			case 0:
				return new None();
			case 1:
				{
					var entry = await GetExactAsync(corpus, matches[0]);
					return entry is null ? new None() : entry;
				}
			default:
				return new HelpCandidates(matches);
		}
	}

	/// <summary>
	/// Builds a fuzzy wildcard pattern from a plain topic string, matching PennMUSH's behavior:
	/// <list type="bullet">
	/// <item>Inserts <c>*</c> between words (at space boundaries)</item>
	/// <item>Inserts <c>*</c> at transitions from alphabetic to digit characters</item>
	/// </list>
	/// Digit→alpha transitions do NOT get a wildcard (matches PennMUSH source).
	/// </summary>
	public static string BuildFuzzyPattern(string topic)
	{
		if (string.IsNullOrEmpty(topic))
		{
			return topic;
		}

		var sb = new StringBuilder();
		const int StateNone = 0;
		const int StateAlpha = 1;
		const int StateDigit = 2;
		var state = StateNone;

		foreach (var c in topic)
		{
			if (char.IsWhiteSpace(c))
			{
				if (state != StateNone)
				{
					state = StateNone;
					sb.Append('*');
				}
				sb.Append(c);
			}
			else if (char.IsAsciiDigit(c))
			{
				if (state == StateAlpha)
				{
					state = StateDigit;
					sb.Append('*');
				}
				sb.Append(c);
			}
			else
			{
				if (state != StateAlpha)
				{
					state = StateAlpha;
				}
				sb.Append(c);
			}
		}

		return sb.ToString();
	}
}
