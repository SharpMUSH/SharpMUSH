namespace SharpMUSH.Library.DiscriminatedUnions;

/// <summary>
/// The record a lookup asked for, or <see cref="NotFound"/>. A lookup-then-act operation with nothing
/// to return is a <c>Found&lt;None&gt;</c>.
/// </summary>
public union Found<T>(T, NotFound);
