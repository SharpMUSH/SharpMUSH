namespace SharpMUSH.Library.Models.Wiki;

/// <summary>
/// One fact a page states about itself, kept beside it like its categories: <paramref name="Property"/> is a
/// qualified property key (<c>pf2e:level</c>), <paramref name="Value"/> its value as written. An integer is
/// written in invariant decimal, a Boolean as <c>true</c> or <c>false</c>, a keyword as its text, and a page
/// reference as the target page's id.
/// </summary>
public sealed record WikiPageFact(string Property, string Value);
