using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Wiki;

namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>
/// Casefiles: private wiki categories built around a plot. A casefile is a category with members, characters or
/// roles, each a reader, contributor or lead (<see cref="WikiRequirementSet.Members"/>). Its own page is the
/// briefing, the pages filed in it are its files, and its developments log records what happened, newest first.
/// Only members, and <c>wiki.admin</c> holders, see any of it; the wiki itself enforces that
/// (<see cref="IWikiAccessService"/>), so every listing, search and function leaves it out for everyone else.
/// The portal, <c>@casefile</c> and the REST API all come here.
/// </summary>
public interface ICasefileService
{
	/// <summary>The casefiles <paramref name="reader"/> is a member of, or every one for a <c>wiki.admin</c> holder, most recently changed first.</summary>
	Task<IReadOnlyList<CasefileSummary>> ListAsync(WikiReader reader);

	/// <summary>The casefile <paramref name="name"/> names (a title or key); <c>NotFound</c> when there is none or the reader is not in it.</summary>
	Task<Found<Casefile>> GetAsync(WikiReader reader, string name);

	/// <summary>
	/// Opens a casefile titled <paramref name="title"/>: its briefing page, an empty developments log, and the reader
	/// as its lead. Needs <c>wiki.create</c>. Turning a category that already has pages into a casefile needs
	/// <c>wiki.admin</c>, so nobody can hide pages they did not write.
	/// </summary>
	Task<Result<Casefile>> CreateAsync(WikiReader reader, string title, string briefing);

	/// <summary>
	/// Lets <paramref name="principal"/> (an objid, or <c>role:&lt;slug&gt;</c>) into the casefile at
	/// <paramref name="level"/>, changes its level, or with null takes it out. Needs a lead or <c>wiki.admin</c>.
	/// The last lead stays: another is named first.
	/// </summary>
	Task<Result<Casefile>> SetMemberAsync(WikiReader reader, string name, string principal, WikiMemberLevel? level);

	/// <summary>
	/// Adds a development to the top of the casefile's log: a dated heading, who posted it and the files it concerns,
	/// then the text. Needs a contributor or lead. <paramref name="authorName"/> is how the post names its author.
	/// </summary>
	Task<Result<WikiPage>> PostDevelopmentAsync(WikiReader reader, string name, string authorName, string title,
		string text, IEnumerable<string> files);
}
