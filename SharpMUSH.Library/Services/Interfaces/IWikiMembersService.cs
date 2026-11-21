using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Wiki;

namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>
/// Private wiki pages: a page with a member list of characters and roles, each a reader, contributor or lead
/// (<see cref="WikiRequirementSet.Members"/> on the page). Only its members, and <c>wiki.admin</c> holders, see it;
/// the wiki itself enforces that (<see cref="IWikiAccessService"/>), so every listing, search and function leaves
/// it out for everyone else. <c>@wiki/create/private</c>, <c>@wiki/member</c>, <c>wikimembers()</c> and
/// <c>wikimember()</c> come here; what a game builds on them (case files) is softcode.
/// </summary>
public interface IWikiMembersService
{
	/// <summary>
	/// The page's members and the reader's level among them (<see cref="WikiMemberLevel.Lead"/> for a <c>wiki.admin</c>
	/// holder); <c>NotFound</c> when the page has no members or the reader is not one of them.
	/// </summary>
	Task<Found<WikiPageMembers>> GetAsync(WikiReader reader, WikiPage page);

	/// <summary>
	/// Creates a page titled <paramref name="title"/> in <paramref name="ns"/> that is private from the start, with the
	/// reader as its lead. Needs what creating a page there needs, and a title no page holds.
	/// </summary>
	Task<Result<WikiPage>> CreatePrivateAsync(WikiReader reader, WikiNamespace ns, string title, string markdown);

	/// <summary>
	/// Lets <paramref name="principal"/> (an objid, or <c>role:&lt;slug&gt;</c>) into the page at <paramref name="level"/>,
	/// changes its level, or with null takes it out. Needs a lead of the page, or <c>wiki.admin</c>, which alone may
	/// make a public page private. At least one character stays a lead.
	/// </summary>
	Task<Result<WikiPageMembers>> SetMemberAsync(WikiReader reader, WikiPage page, string principal, WikiMemberLevel? level);

	/// <summary>
	/// True when <paramref name="reader"/> may not create a page titled <paramref name="title"/> because a page they
	/// cannot see holds it, such as a private page they are not in. Create paths answer it with "That title is taken",
	/// which says nothing about what holds it.
	/// </summary>
	Task<bool> TitleTakenAsync(WikiReader reader, WikiNamespace ns, string title);
}
