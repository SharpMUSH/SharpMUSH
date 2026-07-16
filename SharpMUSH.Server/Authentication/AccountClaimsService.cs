using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;
using ZiggyCreatures.Caching.Fusion;

namespace SharpMUSH.Server.Authentication;

/// <summary>
/// Derives the account-level <see cref="PortalRole"/> and granted permission scopes for an
/// account. Shared by every claims-issuing caller (<c>AuthController</c>'s account-login/register
/// endpoints, <see cref="AccountSessionAuthenticationHandler"/>, <c>AdminAccountsController</c>'s
/// Wizard gate) so they all compute the same claims from a single source.
/// </summary>
/// <remarks>
/// Role/scope resolution is wrapped in FusionCache (30s TTL) so per-request server-side
/// resolution (see <see cref="AccountSessionAuthenticationHandler"/>) is near-free. Both the
/// role and scope cache entries for an account are tagged <c>acct:{accountId}</c>, so a single
/// <see cref="InvalidateAsync"/> call clears both.
/// </remarks>
public class AccountClaimsService(
	IAccountService accountService,
	IRoleDerivationService roleDerivation,
	IRoleRegistryService roleRegistry,
	IPermissionResolver permissionResolver,
	IFusionCache cache,
	ILogger<AccountClaimsService> logger)
{
	/// <summary>
	/// The single source of truth for the cache tag shared by every cached role/scope entry for
	/// <paramref name="accountId"/>. Also used directly by <see cref="SharpMUSH.Server.Services.BanEnforcementService"/>
	/// (via <see cref="ZiggyCreatures.Caching.Fusion.IFusionCache.RemoveByTagAsync"/>) so cache
	/// invalidation doesn't require depending on this whole service.
	/// </summary>
	public static string AccountCacheTag(string accountId) => $"acct:{accountId}";

	/// <summary>
	/// The account-level flag-derived role: the highest <see cref="PortalRole"/> across every
	/// character the account owns (so a Wizard on any character lifts the whole account). Falls
	/// back to the active <paramref name="activeRole"/> if the character list can't be loaded, or
	/// if the account has no characters at all.
	/// Characters are resolved by stable key/dbref, so character renames never affect the result.
	/// </summary>
	// account.Id is a non-secret GUID identifier placed in the standard JWT 'sub' claim
	// per RFC 7519 §4.1.2. Username in 'unique_name' is a display name, not a password or
	// secret. The token is signed (HMAC-SHA256) and transmitted only over TLS.
	[SuppressMessage("Security", "cs/cleartext-storage-of-sensitive-information",
		Justification = "JWT sub/unique_name claims are standard bearer-token identifiers, not secret data.")]
	public async Task<PortalRole> ComputeAccountRoleAsync(string accountId, PortalRole activeRole, CancellationToken ct = default)
		=> await cache.GetOrSetAsync($"account-role:{accountId}:{activeRole}",
			async token => await ComputeAccountRoleCoreAsync(accountId, activeRole, token),
			options => options.Duration = TimeSpan.FromSeconds(30),
			tags: [AccountCacheTag(accountId)],
			token: ct);

	private async Task<PortalRole> ComputeAccountRoleCoreAsync(string accountId, PortalRole activeRole, CancellationToken ct)
	{
		try
		{
			var characters = await accountService.GetCharactersAsync(accountId, ct);
			if (characters.Count == 0)
				return activeRole;

			var perCharacter = await characters.ToAsyncEnumerable()
				.Select(async (c, innerCt) => (c.Object.Key, (IEnumerable<SharpObjectFlag>)await c.Object.Flags.Value.ToListAsync(innerCt)))
				.ToListAsync(ct);

			var accountRole = roleDerivation.DeriveAccountRole(perCharacter);
			return accountRole > activeRole ? accountRole : activeRole;
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			logger.LogWarning(ex,
				"Could not derive account-level role for account {AccountId}; using the active character's role.",
				Helpers.LogSanitizer.Sanitize(accountId));
			return activeRole;
		}
	}

	/// <summary>
	/// Convenience overload for callers with no specific "active character" context (account
	/// login/register, admin gating): floors at <see cref="PortalRole.Guest"/>, the lowest
	/// <see cref="PortalRole"/>, so the result is simply the account's highest character-derived
	/// role, or Guest if it has no characters.
	/// </summary>
	public Task<PortalRole> ComputeAccountRoleAsync(string accountId, CancellationToken ct = default) =>
		ComputeAccountRoleAsync(accountId, PortalRole.Guest, ct);

	/// <summary>
	/// Computes the granted permission scopes for an account: the account's effective roles are
	/// the (current, possibly admin-edited) built-in role for its flag-derived <paramref name="role"/>
	/// unioned with its explicitly-assigned roles, resolved by priority/three-state.
	/// </summary>
	public async Task<IReadOnlySet<string>> ComputeGrantedScopesAsync(string accountId, PortalRole role)
		=> await cache.GetOrSetAsync($"account-scopes:{accountId}:{role}",
			async _ => await ComputeGrantedScopesCoreAsync(accountId, role),
			options => options.Duration = TimeSpan.FromSeconds(30),
			tags: [AccountCacheTag(accountId)],
			token: default);

	private async Task<IReadOnlySet<string>> ComputeGrantedScopesCoreAsync(string accountId, PortalRole role)
	{
		var allRoles = await roleRegistry.GetRolesAsync();
		var bySlug = allRoles.ToDictionary(r => r.Slug, StringComparer.OrdinalIgnoreCase);

		var effective = new Dictionary<string, SharpRole>(StringComparer.OrdinalIgnoreCase);
		if (bySlug.TryGetValue(BuiltInRoles.SlugFor(role), out var derived))
			effective[derived.Slug] = derived;
		foreach (var assigned in await roleRegistry.GetRolesForAccountAsync(accountId))
			effective[assigned.Slug] = assigned;

		// Expand umbrella scopes (e.g. wiki.admin ⇒ wiki.read/create/edit/delete) so the finer
		// gates authorize for holders of the coarser scope without per-gate "or admin" checks.
		return PortalPermission.Expand(permissionResolver.Resolve(effective.Values));
	}

	/// <summary>
	/// Clears both the cached role and granted-scope entries for <paramref name="accountId"/>
	/// (both are tagged <c>acct:{accountId}</c>). Called by ban/disable enforcement paths so a
	/// freshly-revoked account doesn't keep its stale claims for the remainder of the 30s TTL.
	/// </summary>
	public async ValueTask InvalidateAsync(string accountId)
	{
		await cache.RemoveByTagAsync(AccountCacheTag(accountId));
	}
}
