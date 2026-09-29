namespace SharpMUSH.Library.DiscriminatedUnions;

/// <summary>
/// A name fit more than one candidate. PennMUSH's <c>AMBIGUOUS</c> sentinel dbref, which
/// <c>short_page</c> (<c>src/bsd.c:6376</c>) answers with when a prefix fits two connected players.
/// </summary>
public record struct AmbiguousName;

/// <summary>
/// The player a page names, or why no player was named: PennMUSH's <c>do_page</c>
/// (<c>src/speech.c:908-921</c>) reports a miss and an ambiguity differently, and neither is an error
/// that ends the command — the remaining names in the list are still resolved.
/// </summary>
public union PageRecipient(AnySharpObject, NotFound, AmbiguousName);
