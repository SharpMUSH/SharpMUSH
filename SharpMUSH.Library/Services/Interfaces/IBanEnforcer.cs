namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>
/// The wiring seam ban-enforcement call sites (account disable, sitelock host-rule add) invoke
/// against. Lives in <c>SharpMUSH.Library</c> so those call sites (<c>AccountService</c>,
/// <c>SitelockController</c>) don't need a hard dependency on <c>SharpMUSH.Server</c>, where the
/// concrete implementation (<c>BanEnforcementService</c>) lives.
/// </summary>
public interface IBanEnforcer
{
	/// <summary>
	/// Enforces a ban for <paramref name="accountId"/>: revokes every session, invalidates cached
	/// role/permission claims, and disconnects every live game/SignalR connection bound to the
	/// account.
	/// </summary>
	ValueTask EnforceAccountBanAsync(string accountId, CancellationToken ct = default);

	/// <summary>
	/// Enforces a sitelock host rule: revokes sessions and disconnects live game/SignalR
	/// connections whose origin IP/hostname matches <paramref name="hostPattern"/>.
	/// </summary>
	ValueTask EnforceHostRuleAsync(string hostPattern, CancellationToken ct = default);
}
