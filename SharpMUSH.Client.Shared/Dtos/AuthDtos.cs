namespace SharpMUSH.Client.Shared.Dtos;

/// <summary>
/// Request to authenticate a user.
/// </summary>
public record AuthLoginRequest(
    string Username,
    string Password
);

/// <summary>
/// Response after successful authentication.
/// </summary>
public record AuthLoginResponse(
    string AccessToken,
    string RefreshToken,
    string Username,
    DateTime ExpiresAt
);

/// <summary>
/// Response for token refresh.
/// </summary>
public record AuthRefreshResponse(
    string AccessToken,
    DateTime ExpiresAt
);
