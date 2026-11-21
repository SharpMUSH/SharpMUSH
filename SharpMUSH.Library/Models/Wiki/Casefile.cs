namespace SharpMUSH.Library.Models.Wiki;

/// <summary>
/// A casefile as a list shows it: one wiki page, filed in the Case Files category, that only its members read.
/// </summary>
/// <param name="Key">The page's slug in the main namespace.</param>
/// <param name="Title">The page's title.</param>
/// <param name="Summary">The page's first paragraph, as plain text.</param>
/// <param name="Level">The reader's level; <see cref="WikiMemberLevel.Lead"/> for a <c>wiki.admin</c> holder who is not a member.</param>
/// <param name="Members">How many entries its member list has.</param>
/// <param name="UpdatedAt">When the page or its member list last changed.</param>
public sealed record CasefileSummary(
	string Key,
	string Title,
	string Summary,
	WikiMemberLevel Level,
	int Members,
	DateTimeOffset UpdatedAt);

/// <summary>One casefile: its page and who is in it.</summary>
public sealed record Casefile(
	CasefileSummary Summary,
	WikiPage Page,
	IReadOnlyList<WikiMember> Members);
