namespace SharpMUSH.Library.DiscriminatedUnions;

/// <summary>
/// An <c>[object/]attribute</c> pair split into its halves, or <c>false</c> when the text is not one.
/// </summary>
public partial union OptionalObjectAttributeSplit((string? db, string Attribute), bool);
