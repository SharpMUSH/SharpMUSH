using SharpMUSH.Library.Behaviors;
using SharpMUSH.Library.Services.Interfaces;
using ZiggyCreatures.Caching.Fusion;

namespace SharpMUSH.Server.Authentication;

/// <summary>
/// The one place the <c>acct:{accountId}</c> cache tag is cleared. Everything that invalidates an
/// account's claims — character link/unlink in <c>AccountService</c>, ban enforcement, and
/// <see cref="AccountClaimsService.InvalidateAsync"/> itself — goes through here.
/// </summary>
/// <remarks>
/// Split out of <see cref="AccountClaimsService"/> rather than implemented on it: that service
/// computes claims from <see cref="IAccountService"/>, and <c>AccountService</c> is now a caller,
/// so making it the invalidator would close a DI cycle. Clearing the tag needs nothing but the
/// cache, so the seam costs nothing to keep separate.
/// </remarks>
public sealed class AccountClaimsInvalidator(IFusionCache cache, ObjectVersions versions) : IAccountClaimsInvalidator
{
	/// <remarks>
	/// Also expires every object's cached grants: the account's roles and overrides reach each character
	/// linked to it, and which characters those are is what a link or unlink changes. The tag's version moves
	/// first, as <c>CacheInvalidationBehavior</c> moves it, for the grants kept outside the cache.
	/// </remarks>
	public async ValueTask InvalidateAsync(string accountId, CancellationToken ct = default)
	{
		await cache.RemoveByTagAsync(AccountClaimsService.AccountCacheTag(accountId), token: ct);
		versions.BumpTag(Library.Definitions.CacheTags.Grants);
		await cache.RemoveByTagAsync(Library.Definitions.CacheTags.Grants, token: ct);
	}
}
