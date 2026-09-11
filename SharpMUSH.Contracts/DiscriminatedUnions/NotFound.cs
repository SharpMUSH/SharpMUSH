namespace SharpMUSH.Library.DiscriminatedUnions;

/// <summary>
/// The union case for a lookup that matched nothing. Kept apart from <see cref="None"/> so a caller can
/// tell "no such record" from "the record exists and is empty".
/// </summary>
public readonly record struct NotFound;
