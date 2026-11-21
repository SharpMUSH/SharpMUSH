using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Wiki;

namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>
/// Casefiles: private wiki pages built around a plot. A casefile is one page in the main namespace, filed in the
/// Case Files category, with a member list of characters and roles, each a reader, contributor or lead
/// (<see cref="WikiRequirementSet.Members"/> on the page). The page holds the briefing a newcomer reads first and,
/// under its Developments heading, what has happened, newest first. Only members, and <c>wiki.admin</c> holders,
/// see it; the wiki itself enforces that (<see cref="IWikiAccessService"/>), so every listing, search and function
/// leaves it out for everyone else. The portal, <c>@casefile</c> and the REST API all come here.
/// </summary>
public interface ICasefileService
{
	/// <summary>The casefiles <paramref name="reader"/> is a member of, or every one for a <c>wiki.admin</c> holder, most recently changed first.</summary>
	Task<IReadOnlyList<CasefileSummary>> ListAsync(WikiReader reader);

	/// <summary>The casefile <paramref name="name"/> names (its title or slug); <c>NotFound</c> when there is none or the reader is not in it.</summary>
	Task<Found<Casefile>> GetAsync(WikiReader reader, string name);

	/// <summary>
	/// Opens a casefile titled <paramref name="title"/>: a page in the Case Files category holding
	/// <paramref name="briefing"/>, with the reader as its lead. Needs <c>wiki.create</c>, and a title no page holds.
	/// </summary>
	Task<Result<Casefile>> CreateAsync(WikiReader reader, string title, string briefing);

	/// <summary>
	/// Lets <paramref name="principal"/> (an objid, or <c>role:&lt;slug&gt;</c>) into the casefile at
	/// <paramref name="level"/>, changes its level, or with null takes it out. Needs a lead or <c>wiki.admin</c>.
	/// At least one character stays a lead.
	/// </summary>
	Task<Result<Casefile>> SetMemberAsync(WikiReader reader, string name, string principal, WikiMemberLevel? level);

	/// <summary>
	/// Adds a development to the top of the page's Developments section: a dated heading, who posted it, then the
	/// text. Needs what editing the page needs: a contributor or lead, and <c>wiki.edit</c>. <paramref name="authorName"/>
	/// is how the post names its author.
	/// </summary>
	Task<Result<WikiPage>> PostDevelopmentAsync(WikiReader reader, string name, string authorName, string title, string text);

	/// <summary>
	/// True when <paramref name="reader"/> may not create a page titled <paramref name="title"/> because a page they
	/// cannot see holds it, such as a casefile they are not in. Create paths answer it with "That title is taken",
	/// which says nothing about what holds it.
	/// </summary>
	Task<bool> TitleTakenAsync(WikiReader reader, WikiNamespace ns, string title);
}
