using SharpMUSH.Library.Authorization;

namespace SharpMUSH.Server.Authentication;

/// <summary>
/// Encapsulates the two tokens returned by a successful JWT issuance or refresh.
/// </summary>
/// <param name="AccessToken">Signed JWT access token (short-lived).</param>
/// <param name="RefreshToken">Opaque 32-hex-char refresh token (long-lived, single-use).</param>
/// <param name="ExpiresIn">Access-token lifetime in seconds.</param>
/// <param name="Role">Portal role encoded in the access token.</param>
public record JwtTokenResult(
	string AccessToken,
	string RefreshToken,
	int ExpiresIn,
	PortalRole Role);
