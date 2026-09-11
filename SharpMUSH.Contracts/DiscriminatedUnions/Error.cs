namespace SharpMUSH.Library.DiscriminatedUnions;

/// <summary>
/// The union case for a failure that carries no detail.
/// </summary>
public readonly record struct Error;

/// <summary>
/// The union case for a failure described by <paramref name="Value"/> — nearly always the message to
/// report.
/// </summary>
public readonly record struct Error<T>(T Value);
