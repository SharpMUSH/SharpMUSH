namespace SharpMUSH.Library.Models.Wiki;

/// <summary>
/// A casefile as a list shows it: a private wiki category (one with members) and how far the reader may go in it.
/// </summary>
/// <param name="Key">The category's key (<c>WikiHelpers.CategoryKey</c>).</param>
/// <param name="Title">The title of the category's own page, else its label.</param>
/// <param name="Summary">The first paragraph of the briefing, as plain text; empty when there is no briefing.</param>
/// <param name="Level">The reader's level; <see cref="WikiMemberLevel.Lead"/> for a <c>wiki.admin</c> holder who is not a member.</param>
/// <param name="Files">How many pages are filed in it, the developments log left out.</param>
/// <param name="Members">How many entries its member list has.</param>
/// <param name="UpdatedAt">When its briefing, a file or its developments last changed.</param>
public sealed record CasefileSummary(
	string Key,
	string Title,
	string Summary,
	WikiMemberLevel Level,
	int Files,
	int Members,
	DateTimeOffset UpdatedAt);

/// <summary>One casefile with everything the reader may see of it.</summary>
/// <param name="Briefing">The category's own page, which holds the briefing a newcomer reads first; null when it has none.</param>
/// <param name="Developments">The developments log, newest first; null until the first is posted on a casefile made from an older category.</param>
/// <param name="Files">The pages filed in it that the reader may see, by title, the developments log left out.</param>
public sealed record Casefile(
	CasefileSummary Summary,
	WikiPage? Briefing,
	WikiPage? Developments,
	IReadOnlyList<WikiPage> Files,
	IReadOnlyList<WikiMember> Members);
