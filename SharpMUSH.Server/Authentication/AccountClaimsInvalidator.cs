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
public sealed class AccountClaimsInvalidator(IFusionCache cache) : IAccountClaimsInvalidator
{
	public ValueTask InvalidateAsync(string accountId, CancellationToken ct = default)
		=> cache.RemoveByTagAsync(AccountClaimsService.AccountCacheTag(accountId), token: ct);
}
