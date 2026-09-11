namespace SharpMUSH.Library.DiscriminatedUnions;

/// <summary>
/// A lookup that can also fail for a reason other than absence: the record, <see cref="NotFound"/>, or
/// the message of the failure.
/// </summary>
public partial union FoundResult<T>(T, NotFound, Error<string>);
