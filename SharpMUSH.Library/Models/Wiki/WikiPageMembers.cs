namespace SharpMUSH.Library.Models.Wiki;

/// <summary>A private page, who is in it, and the reader's level there.</summary>
/// <param name="Page">The page.</param>
/// <param name="Members">Its member list, leads first.</param>
/// <param name="Level">The reader's level; <see cref="WikiMemberLevel.Lead"/> for a <c>wiki.admin</c> holder who is not a member.</param>
public sealed record WikiPageMembers(WikiPage Page, IReadOnlyList<WikiMember> Members, WikiMemberLevel Level);
