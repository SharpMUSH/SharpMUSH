namespace SharpMUSH.Library.DiscriminatedUnions;

/// <summary>
/// An <c>object/attribute</c> pair split into its halves, or <see cref="None"/> when the text is not one.
/// </summary>
public partial union ObjectAttributeSplit((string db, string Attribute), None);
