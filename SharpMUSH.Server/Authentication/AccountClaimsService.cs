using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Behaviors;
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
	IAccountClaimsInvalidator invalidator,
	ILogger<AccountClaimsService> logger)
{
	/// <summary>
	/// The single source of truth for the cache tag shared by every cached role/scope entry for
	/// <paramref name="accountId"/>. Read by <see cref="AccountClaimsInvalidator"/>, which is the
	/// only thing that clears it.
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
			ClaimsEntryOptions,
			tags: [AccountCacheTag(accountId)],
			token: ct);

	/// <summary>
	/// Short-lived, and explicitly the profile that is never served stale: these entries are
	/// invalidated by tag when a ban or a role change lands, and a fail-safe fallback during a slow
	/// database would hand a revoked role back. See <see cref="CacheEntryProfile"/>.
	/// </summary>
	private static readonly FusionCacheEntryOptions ClaimsEntryOptions =
		CacheEntryProfiles.Tagged.Duplicate(TimeSpan.FromSeconds(30));

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
	public async Task<IReadOnlySet<string>> ComputeGrantedScopesAsync(string accountId, PortalRole role, CancellationToken ct = default)
		// The factory's token, not the caller's: it is the one FusionCache cancels when the hard
		// timeout expires, and with background completion off that is how the role queries stop.
		=> await cache.GetOrSetAsync($"account-scopes:{accountId}:{role}",
			async token => await ComputeGrantedScopesCoreAsync(accountId, role, token),
			ClaimsEntryOptions,
			tags: [AccountCacheTag(accountId)],
			token: ct);

	private async Task<IReadOnlySet<string>> ComputeGrantedScopesCoreAsync(string accountId, PortalRole role, CancellationToken ct)
	{
		var allRoles = await roleRegistry.GetRolesAsync(ct);
		var bySlug = allRoles.ToDictionary(r => r.Slug, StringComparer.OrdinalIgnoreCase);

		var effective = new Dictionary<string, SharpRole>(StringComparer.OrdinalIgnoreCase);
		if (bySlug.TryGetValue(BuiltInRoles.SlugFor(role), out var derived))
			effective[derived.Slug] = derived;
		foreach (var assigned in await roleRegistry.GetRolesForAccountAsync(accountId, ct))
			effective[assigned.Slug] = assigned;

		// Expand umbrella scopes (e.g. wiki.admin ⇒ wiki.read/create/edit/delete) so the finer
		// gates authorize for holders of the coarser scope without per-gate "or admin" checks.
		return PortalPermission.Expand(permissionResolver.Resolve(effective.Values));
	}

	/// <summary>
	/// Clears both the cached role and granted-scope entries for <paramref name="accountId"/>
	/// (both are tagged <c>acct:{accountId}</c>), so the very next request recomputes them.
	/// </summary>
	/// <remarks>
	/// Server-layer convenience wrapper over <see cref="IAccountClaimsInvalidator"/>. The mutations
	/// that actually make these entries stale — linking and unlinking characters — invalidate through
	/// the interface from <c>AccountService</c>, because they happen in the Library layer.
	/// </remarks>
	public ValueTask InvalidateAsync(string accountId, CancellationToken ct = default)
		=> invalidator.InvalidateAsync(accountId, ct);
}
