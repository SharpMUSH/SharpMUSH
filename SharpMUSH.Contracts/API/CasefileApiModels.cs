namespace SharpMUSH.Library.API;

/// <summary>A casefile as <c>GET /api/casefiles</c> lists it. <paramref name="Level"/> is reader, contributor or lead.</summary>
public sealed record CasefileSummaryDto(
	string Key,
	string Title,
	string Summary,
	string Level,
	int Files,
	int Members,
	DateTimeOffset UpdatedAt);

/// <summary>
/// One member of a casefile: a character (<paramref name="Kind"/> <c>character</c>, <paramref name="Principal"/> its
/// objid) or every holder of a role (<c>role</c>, <c>role:&lt;slug&gt;</c>), with how people read it.
/// </summary>
public sealed record CasefileMemberDto(string Principal, string Kind, string Name, string Level);

/// <summary>A page filed in a casefile, linked as <c>/wiki/{Namespace}/{Slug}</c>.</summary>
public sealed record CasefileFileDto(string Namespace, string Slug, string Title, DateTimeOffset UpdatedAt, string? LastEditor);

/// <summary>
/// One casefile. The briefing is the page <c>/wiki/category/{Summary.Key}</c> and the developments log the page
/// <paramref name="DevelopmentsSlug"/> in the main namespace; the portal reads both through <c>/api/wiki</c>.
/// </summary>
public sealed record CasefileDto(
	CasefileSummaryDto Summary,
	bool HasBriefing,
	string? DevelopmentsSlug,
	IReadOnlyList<CasefileFileDto> Files,
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

/// <summary><c>POST /api/casefiles/{key}/developments</c>: a development and the titles of the files it concerns.</summary>
public sealed record PostCasefileDevelopmentRequest(string Title, string Text, IReadOnlyList<string>? Files);
