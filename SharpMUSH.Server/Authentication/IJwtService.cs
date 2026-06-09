using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Server.Authentication;

/// <summary>
/// Issues and refreshes JWT access/refresh token pairs.
/// </summary>
public interface IJwtService
{
	/// <summary>
	/// Issues a JWT access token and a paired refresh token for the supplied
	/// account/character combination.  <paramref name="role"/> must be pre-derived
	/// by the caller (typically via <see cref="IRoleDerivationService"/>).
	/// </summary>
	Task<JwtTokenResult> IssueTokensAsync(
		SharpAccount account,
		SharpPlayer character,
		PortalRole role,
		CancellationToken ct = default);

	/// <summary>
	/// Validates <paramref name="refreshToken"/>, revokes it (one-shot semantics),
	/// looks up the bound account and character, then issues a fresh token pair.
	/// Returns <c>null</c> when the token is invalid, expired, or the account/character
	/// is no longer accessible.
	/// </summary>
	Task<JwtTokenResult?> RefreshAsync(string refreshToken, CancellationToken ct = default);
}
