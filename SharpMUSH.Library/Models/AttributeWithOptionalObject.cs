namespace SharpMUSH.Library.Models;

/// <summary>
/// An <c>[object/]attribute</c> spec split into its halves, as written: <paramref name="Object"/> is
/// the unresolved object name, empty when the spec names none, and <paramref name="Attribute"/> is
/// never empty. Returned by <see cref="HelperFunctions.SplitOptionalObjectAndAttr"/>.
/// </summary>
public readonly record struct AttributeWithOptionalObject(string? Object, string Attribute);
