using SharpMUSH.Library.DiscriminatedUnions;

namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>A single resolved help entry: the canonical topic name and its markdown body.</summary>
/// <param name="Topic">The topic name as indexed, not as the caller spelled it.</param>
/// <param name="Markdown">The raw markdown body, exactly as the engine hands it to the renderer.</param>
public sealed record HelpEntry(string Topic, string Markdown)
{
	public HelpArticle? Article { get; init; }
	public string? SectionId { get; init; }
}

/// <summary>Several topics matched and none of them is the answer on its own.</summary>
/// <param name="Topics">Candidate topic names, ordered case-insensitively.</param>
public sealed record HelpCandidates(IReadOnlyList<string> Topics);

/// <summary>The topics around one help topic, by the help's own links.</summary>
/// <param name="Topic">The topic as the index names it (<c>Mail</c> for <c>@mail</c>), or null when nothing answered.</param>
/// <param name="Linked">What the topic's page links to, in reading order.</param>
/// <param name="Nearby">What those link to, breadth-first outward, each topic once.</param>
public sealed record HelpRelatedTopics(string? Topic, IReadOnlyList<string> Linked, IReadOnlyList<string> Nearby);

/// <summary>
/// Resolves a help topic the way the game's <c>help</c> command does: literal wildcards first,
/// then a prefix match, then PennMUSH's fuzzy pattern.
/// </summary>
/// <remarks>
/// This exists so the portal and the telnet game cannot drift. <c>help @mail</c> typed at a telnet
/// prompt and <c>/help/@mail</c> opened in the portal run the same resolution over the same index —
/// there is deliberately no second implementation to keep in step.
/// <para>
/// A "corpus" is a text-file category directory (<c>help</c>, <c>ahelp</c>, <c>news</c>). Resolution
/// never crosses corpora: <c>ahelp</c> is administrator-only in game, so a <c>help</c> lookup that
/// could fall through to it would be a disclosure bug, not a convenience.
/// </para>
/// </remarks>
public interface IHelpTopicResolver
{
	/// <summary>
	/// Resolves <paramref name="topic"/> within <paramref name="corpus"/>.
	/// </summary>
	/// <param name="corpus">Category directory to search — <c>help</c>, <c>ahelp</c>, <c>news</c>.</param>
	/// <param name="topic">What the reader asked for; may contain <c>*</c> and <c>?</c> wildcards.</param>
	/// <returns>
	/// The entry when exactly one thing matches, the candidate list when several do, or
	/// <see cref="None"/> when nothing does.
	/// </returns>
	ValueTask<HelpResolution> ResolveAsync(string corpus, string topic);

	/// <summary>Fetches one entry by its exact indexed name, without any fuzzy fallback.</summary>
	ValueTask<HelpEntry?> GetExactAsync(string corpus, string topic);

	/// <summary>Every topic name in the corpus, ordered case-insensitively.</summary>
	ValueTask<IReadOnlyList<string>> ListTopicsAsync(string corpus);

	/// <summary>
	/// Topic <em>names</em> matching a glob pattern (<c>*</c>, <c>?</c>), ordered case-insensitively.
	/// This is a name match, not a body search — see <see cref="SearchContentAsync"/> for that.
	/// </summary>
	ValueTask<IReadOnlyList<string>> SearchTopicsAsync(string corpus, string pattern);

	/// <summary>Topic names whose bodies contain <paramref name="searchTerm"/>, ordered case-insensitively.</summary>
	ValueTask<IReadOnlyList<string>> SearchContentAsync(string corpus, string searchTerm);

	/// <summary>
	/// The topics around <paramref name="topic"/>: at most <paramref name="linkedLimit"/> its page links to, then at
	/// most <paramref name="nearbyLimit"/> more found by following links breadth-first from those. Topics on the same
	/// page are left out, and so is the corpus's index topic (the one named as the corpus is), which is never
	/// followed either: it links everything.
	/// </summary>
	ValueTask<HelpRelatedTopics> RelatedAsync(string corpus, string topic, int linkedLimit, int nearbyLimit);
}
