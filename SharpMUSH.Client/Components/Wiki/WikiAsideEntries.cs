namespace SharpMUSH.Client.Components.Wiki;

/// <summary>A table-of-contents entry: the heading's text and the anchor its id gives.</summary>
public sealed record WikiTocEntry(string Text, string Anchor);

/// <summary>A character the body links to: the link text and its /character/ href.</summary>
public sealed record WikiMention(string Name, string Href);
