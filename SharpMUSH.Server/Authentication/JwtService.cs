using System.Diagnostics.CodeAnalysis;
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
	IRoleRegistryService roleRegistry,
	IPermissionResolver permissionResolver,
	IMediator mediator,
	ILogger<JwtService> logger) : IJwtService
{
	private readonly JwtOptions _opts = options.Value;

	/// <inheritdoc />
	// account.Id is a non-secret GUID identifier placed in log messages for service observability, not a password or secret.
	[SuppressMessage("Security", "cs/cleartext-storage-of-sensitive-information",
		Justification = "account.Id is a non-secret GUID identifier used for observability, not a password or secret value.")]
	public async Task<JwtTokenResult> IssueTokensAsync(
		SharpAccount account,
		SharpPlayer character,
		PortalRole role,
		CancellationToken ct = default)
	{
		// The portal role is ACCOUNT-level: the highest flag-derived role across every character
		// the account owns (one Wizard character makes the whole account Wizard-privileged), not
		// just the active one. Characters are resolved by stable dbref/key — never by (renamable)
		// name. The character_* claims below are display context only and refresh with the token.
		var effectiveRole = await ComputeAccountRoleAsync(account, role, ct);

		var scopes = await ComputeGrantedScopesAsync(account, effectiveRole);
		var accessToken = BuildAccessToken(account, character, effectiveRole, scopes, out var expiresIn);

		var charRef = new DBRef(character.Object.Key, character.Object.CreationTime);
		var refreshTtl = TimeSpan.FromDays(_opts.RefreshTokenLifetimeDays);
		var refreshToken = await refreshTokenStore.CreateTokenAsync(account.Id!, charRef, refreshTtl, ct);

		logger.LogInformation(
			"JWT issued for account {AccountId}, character #{Key}, role {Role}",
			account.Id, character.Object.Key, effectiveRole);

		return new JwtTokenResult(accessToken, refreshToken, expiresIn, effectiveRole);
	}

	/// <inheritdoc />
	// accountId is a non-secret GUID identifier derived from the refresh token payload for service lookups, not a password or secret.
	[SuppressMessage("Security", "cs/cleartext-storage-of-sensitive-information",
		Justification = "accountId is a non-secret GUID identifier derived from the refresh token payload for service lookups, not a password or secret value.")]
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

	// account.Id is a non-secret GUID identifier placed in the standard JWT 'sub' claim
	// per RFC 7519 §4.1.2. Username in 'unique_name' is a display name, not a password or
	// secret. The token is signed (HMAC-SHA256) and transmitted only over TLS.
	[SuppressMessage("Security", "cs/cleartext-storage-of-sensitive-information",
		Justification = "JWT sub/unique_name claims are standard bearer-token identifiers, not secret data.")]
	/// <summary>
	/// The account-level flag-derived role: the highest <see cref="PortalRole"/> across every
	/// character the account owns (so a Wizard on any character lifts the whole account). Falls
	/// back to the active <paramref name="activeRole"/> if the character list can't be loaded.
	/// Characters are resolved by stable key/dbref, so character renames never affect the result.
	/// </summary>
	private async Task<PortalRole> ComputeAccountRoleAsync(SharpAccount account, PortalRole activeRole, CancellationToken ct)
	{
		try
		{
			var characters = await accountService.GetCharactersAsync(account.Id!, ct);
			if (characters.Count == 0)
				return activeRole;

			var perCharacter = new List<(int, IEnumerable<SharpObjectFlag>)>(characters.Count);
			foreach (var c in characters)
				perCharacter.Add((c.Object.Key, await c.Object.Flags.Value.ToListAsync(ct)));

			var accountRole = roleDerivation.DeriveAccountRole(perCharacter);
			return accountRole > activeRole ? accountRole : activeRole;
		}
		catch (Exception ex)
		{
			logger.LogWarning(ex,
				"Could not derive account-level role for account {AccountId}; using the active character's role.",
				account.Id);
			return activeRole;
		}
	}

	/// <summary>
	/// Computes the granted permission scopes for an account: the account's effective roles are
	/// the (current, possibly admin-edited) built-in role for its flag-derived <paramref name="role"/>
	/// unioned with its explicitly-assigned roles, resolved by priority/three-state.
	/// </summary>
	private async Task<IReadOnlySet<string>> ComputeGrantedScopesAsync(SharpAccount account, PortalRole role)
	{
		var allRoles = await roleRegistry.GetRolesAsync();
		var bySlug = allRoles.ToDictionary(r => r.Slug, StringComparer.OrdinalIgnoreCase);

		var effective = new Dictionary<string, SharpRole>(StringComparer.OrdinalIgnoreCase);
		if (bySlug.TryGetValue(BuiltInRoles.SlugFor(role), out var derived))
			effective[derived.Slug] = derived;
		foreach (var assigned in await roleRegistry.GetRolesForAccountAsync(account.Id!))
			effective[assigned.Slug] = assigned;

		// Expand umbrella scopes (e.g. wiki.admin ⇒ wiki.read/create/edit/delete) so the finer
		// gates authorize for holders of the coarser scope without per-gate "or admin" checks.
		return PortalPermission.Expand(permissionResolver.Resolve(effective.Values));
	}

	private string BuildAccessToken(SharpAccount account, SharpPlayer character, PortalRole role,
		IReadOnlySet<string> scopes, out int expiresIn)
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

		// Discord-style permission scopes — the gates authorize on these (the Role claim above
		// is kept for back-compat and per-app MinimumRole thresholds).
		claims.AddRange(scopes.Select(s => new Claim(PortalPermission.ClaimType, s)));

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
