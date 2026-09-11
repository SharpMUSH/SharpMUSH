namespace SharpMUSH.Library.DiscriminatedUnions;

/// <summary>
/// A value, or the message explaining why there is none. An operation with nothing to return on
/// success is a <c>Result&lt;Success&gt;</c>.
/// </summary>
public partial union Result<T>(T, Error<string>);
