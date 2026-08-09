namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>
/// The wiring seam for dropping an account's cached role/permission claims. Lives in
/// <c>SharpMUSH.Library</c> so the call sites that mutate the character set those claims are
/// derived from (<c>AccountService.LinkCharacterAsync</c> / <c>UnlinkCharacterAsync</c>) don't need
/// a dependency on <c>SharpMUSH.Server</c>, where the cache and the claim computation live.
/// </summary>
/// <remarks>
/// Deliberately narrower than <c>AccountClaimsService</c>, which computes claims from
/// <see cref="IAccountService"/>: an implementation of this interface must not depend on the
/// account service, or the object graph would be circular the moment the account service invalidates.
/// </remarks>
public interface IAccountClaimsInvalidator
{
	/// <summary>
	/// Drops every cached role/permission entry for <paramref name="accountId"/>, so the next
	/// authenticated request recomputes them from the account's current characters.
	/// </summary>
	ValueTask InvalidateAsync(string accountId, CancellationToken ct = default);
}
