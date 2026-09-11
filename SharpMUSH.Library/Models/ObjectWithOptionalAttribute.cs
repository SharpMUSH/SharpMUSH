namespace SharpMUSH.Library.Models;

/// <summary>
/// An <c>object[/attribute]</c> spec split into its halves, as written: <paramref name="Object"/> is
/// the unresolved object name, never empty, and <paramref name="Attribute"/> is <see langword="null"/>
/// when the spec names no attribute (never empty). Returned by
/// <see cref="HelperFunctions.SplitDbRefAndOptionalAttr"/>.
/// </summary>
public readonly record struct ObjectWithOptionalAttribute(string Object, string? Attribute);
