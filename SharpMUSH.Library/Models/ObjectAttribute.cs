namespace SharpMUSH.Library.Models;

/// <summary>
/// An <c>object/attribute</c> spec split into its halves, as written: <paramref name="Object"/> is
/// the unresolved object name and neither half is empty. Returned by
/// <see cref="HelperFunctions.SplitObjectAndAttr"/>.
/// </summary>
public readonly record struct ObjectAttribute(string Object, string Attribute);
