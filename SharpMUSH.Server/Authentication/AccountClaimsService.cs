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
	IAdministrativeCapabilityService capabilities,
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
	/// The account's coarse portal tier (<see cref="BuiltInRoles.TierOf"/>), from the same context every
	/// policy gate resolves: the account's roles, every role assigned to one of its characters, and
	/// <c>player</c> once it has a character. God for the account linked to player #1.
	/// </summary>
	// account.Id is a non-secret GUID identifier placed in the standard JWT 'sub' claim
	// per RFC 7519 §4.1.2. Username in 'unique_name' is a display name, not a password or
	// secret. The token is signed (HMAC-SHA256) and transmitted only over TLS.
	[SuppressMessage("Security", "cs/cleartext-storage-of-sensitive-information",
		Justification = "JWT sub/unique_name claims are standard bearer-token identifiers, not secret data.")]
	public async Task<PortalRole> ComputeAccountRoleAsync(string accountId, CancellationToken ct = default)
		=> await cache.GetOrSetAsync($"account-role:{accountId}",
			async token => await ComputeAccountRoleCoreAsync(accountId, token),
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

	private async Task<PortalRole> ComputeAccountRoleCoreAsync(string accountId, CancellationToken ct)
	{
		try
		{
			return BuiltInRoles.TierOf(await capabilities.GetContextAsync(new CapabilityActor(accountId), ct));
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			logger.LogWarning(ex, "Could not derive the portal tier for account {AccountId}; using Guest.",
				Library.Logging.LogSanitizer.Sanitize(accountId));
			return PortalRole.Guest;
		}
	}

	/// <summary>
	/// The account's granted permission scopes, account-wide (no active character), resolved by
	/// <see cref="IAdministrativeCapabilityService"/> exactly as every policy gate resolves them.
	/// </summary>
	public async Task<IReadOnlySet<string>> ComputeGrantedScopesAsync(string accountId, CancellationToken ct = default)
		// The factory's token, not the caller's: it is the one FusionCache cancels when the hard
		// timeout expires, and with background completion off that is how the role queries stop.
		=> await cache.GetOrSetAsync($"account-scopes:{accountId}",
			async token => await capabilities.GetGrantedScopesAsync(new CapabilityActor(accountId), token),
			ClaimsEntryOptions,
			tags: [AccountCacheTag(accountId)],
			token: ct);

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
