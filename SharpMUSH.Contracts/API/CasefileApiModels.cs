namespace SharpMUSH.Library.API;

/// <summary>
/// A casefile as <c>GET /api/casefiles</c> lists it. <paramref name="Key"/> is its page's slug in the main namespace;
/// <paramref name="Level"/> is reader, contributor or lead.
/// </summary>
public sealed record CasefileSummaryDto(
	string Key,
	string Title,
	string Summary,
	string Level,
	int Members,
	DateTimeOffset UpdatedAt);

/// <summary>
/// One member of a casefile: a character (<paramref name="Kind"/> <c>character</c>, <paramref name="Principal"/> its
/// objid) or every holder of a role (<c>role</c>, <c>role:&lt;slug&gt;</c>), with how people read it.
/// </summary>
public sealed record CasefileMemberDto(string Principal, string Kind, string Name, string Level);

/// <summary>
/// One casefile. Its page is <c>/wiki/main/{Summary.Key}</c>, which the portal reads through <c>/api/wiki</c>.
/// </summary>
public sealed record CasefileDto(
	CasefileSummaryDto Summary,
	IReadOnlyList<CasefileMemberDto> Members,
	bool CanContribute,
	bool CanLead);

/// <summary><c>POST /api/casefiles</c>: open a casefile, with the briefing's Markdown.</summary>
public sealed record CreateCasefileRequest(string Title, string? Briefing);

/// <summary>
/// <c>PUT /api/casefiles/{key}/members</c>: let someone in or change their level; a null <paramref name="Level"/>
/// takes them out. <paramref name="Who"/> is a character's name, an objid, or <c>role:&lt;slug&gt;</c>.
/// </summary>
public sealed record SetCasefileMemberRequest(string Who, string? Level);

/// <summary><c>POST /api/casefiles/{key}/developments</c>: a development to add to the top of the page's Developments section.</summary>
public sealed record PostCasefileDevelopmentRequest(string Title, string Text);
