namespace SharpMUSH.Library.DiscriminatedUnions;

/// <summary>
/// Named replacement for the anonymous tuple (string db, string Attribute)
/// used in SplitObjectAndAttr return types.
/// </summary>
public readonly record struct ObjAttrPair(string Db, string Attribute);

/// <summary>
/// Named replacement for the anonymous tuple (string? db, string Attribute)
/// used in SplitOptionalObjectAndAttr return types.
/// </summary>
public readonly record struct OptionalObjAttr(string? Db, string Attribute);

/// <summary>
/// Named replacement for the anonymous tuple (string db, string? Attribute)
/// used in SplitDbRefAndOptionalAttr return types.
/// </summary>
public readonly record struct DbRefOptionalAttr(string Db, string? Attribute);
