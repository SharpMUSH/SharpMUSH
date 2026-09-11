namespace SharpMUSH.Library.DiscriminatedUnions;

/// <summary>
/// A <c>dbref[/attribute]</c> pair split into its halves, or <c>false</c> when the text is not one.
/// </summary>
public partial union DbRefOptionalAttributeSplit((string db, string? Attribute), bool);
