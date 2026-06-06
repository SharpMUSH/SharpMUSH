using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Mediator;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Server.Authentication;

/// <summary>
/// Issues and refreshes JWT access/refresh token pairs backed by
/// <see cref="IRefreshTokenStore"/> and <see cref="IRoleDerivationService"/>.
/// </summary>
public class JwtService(
	IOptions<JwtOptions> options,
	IRefreshTokenStore refreshTokenStore,
	IRoleDerivationService roleDerivation,
	IAccountService accountService,
	IMediator mediator,
	ILogger<JwtService> logger) : IJwtService
{
	private readonly JwtOptions _opts = options.Value;

	/// <inheritdoc />
	public async Task<JwtTokenResult> IssueTokensAsync(
		SharpAccount account,
		SharpPlayer character,
		PortalRole role,
		CancellationToken ct = default)
	{
		var accessToken = BuildAccessToken(account, character, role, out var expiresIn);

		var charRef = new DBRef(character.Object.Key, character.Object.CreationTime);
		var refreshTtl = TimeSpan.FromDays(_opts.RefreshTokenLifetimeDays);
		var refreshToken = await refreshTokenStore.CreateTokenAsync(account.Id!, charRef, refreshTtl, ct);

		logger.LogInformation(
			"JWT issued for account {AccountId}, character #{Key}, role {Role}",
			account.Id, character.Object.Key, role);

		return new JwtTokenResult(accessToken, refreshToken, expiresIn, role);
	}

	/// <inheritdoc />
	public async Task<JwtTokenResult?> RefreshAsync(string refreshToken, CancellationToken ct = default)
	{
		var payload = await refreshTokenStore.ValidateAsync(refreshToken, ct);
		if (payload is null)
		{
			logger.LogInformation("JWT refresh rejected: unknown or expired token");
			return null;
		}

		// Revoke immediately — single-use semantics.
		await refreshTokenStore.RevokeAsync(refreshToken, ct);

		var (accountId, charRef) = payload.Value;

		var account = await accountService.GetByIdAsync(accountId, ct);
		if (account is null || account.IsDisabled)
		{
			logger.LogInformation("JWT refresh rejected: account {AccountId} not found or disabled", accountId);
			return null;
		}

		// Re-fetch the character so we have up-to-date flags.
		var objNode = await mediator.Send(new GetObjectNodeQuery(charRef), ct);
		if (!objNode.IsPlayer)
		{
			logger.LogInformation("JWT refresh rejected: character {CharRef} not found or not a player", charRef);
			return null;
		}

		var character = objNode.AsPlayer;
		var flags = await character.Object.Flags.Value.ToListAsync(ct);
		var role = roleDerivation.DeriveRole(character.Object.Key, flags);

		return await IssueTokensAsync(account, character, role, ct);
	}

	// -----------------------------------------------------------------------
	// Helpers
	// -----------------------------------------------------------------------

	private string BuildAccessToken(SharpAccount account, SharpPlayer character, PortalRole role,
		out int expiresIn)
	{
		var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_opts.SigningKey));
		var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

		expiresIn = _opts.AccessTokenLifetimeMinutes * 60;
		var notBefore = DateTime.UtcNow;
		var expires = notBefore.AddSeconds(expiresIn);

		var claims = new List<Claim>
		{
			new(JwtRegisteredClaimNames.Sub, account.Id!),
			new(JwtRegisteredClaimNames.UniqueName, account.Username),
			new(ClaimTypes.Role, role.ToString()),
			new("character_key", character.Object.Key.ToString()),
			new("character_creation_time", character.Object.CreationTime.ToString()),
			new("character_name", character.Object.Name),
		};

		var token = new JwtSecurityToken(
			issuer: _opts.Issuer,
			audience: _opts.Audience,
			claims: claims,
			notBefore: notBefore,
			expires: expires,
			signingCredentials: creds);

		return new JwtSecurityTokenHandler().WriteToken(token);
	}
}
