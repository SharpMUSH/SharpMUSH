using SharpMUSH.Library.DiscriminatedUnions;

namespace SharpMUSH.Client.Services;

/// <summary>
/// What the server answered, or <see cref="Error"/> when it could not be asked. Callers that can say
/// why a call failed use <see cref="ApiResult{T}"/> instead.
/// </summary>
public partial union ServerResult<T>(T, Error);
