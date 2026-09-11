namespace SharpMUSH.Client.Services;

/// <summary>
/// The body an API call returned, or the <see cref="ApiFailure"/> that stopped it.
/// </summary>
public union ApiResult<T>(T, ApiFailure);
