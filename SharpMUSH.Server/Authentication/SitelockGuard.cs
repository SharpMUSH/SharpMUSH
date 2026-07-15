using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Server.Authentication;

/// <summary>
/// Gates the web/HTTP auth surfaces (game login, account registration, first-run setup claim,
/// SignalR connect) on sitelock rules (Task 15). Reads the live
/// <see cref="IOptionsWrapper{SharpMUSHOptions}"/>.<c>CurrentValue.SitelockRules</c> on every call
/// (never cached), so an admin adding a rule via <c>SitelockController</c> takes effect on the very
/// next request — the same reload path <c>BanEnforcementService.EnforceHostRuleAsync</c> relies on.
///
/// Anonymous page browsing is never routed through this guard — it exists only at the login/create/
/// guest entry points gated by <see cref="Connect"/>/<see cref="Create"/>/<see cref="Guest"/>.
///
/// The telnet surface (<c>SocketCommands.Connect</c>/<c>HandleGuestLogin</c>, in
/// SharpMUSH.Implementation) cannot depend on this Server-layer type — it calls the same underlying
/// <see cref="SitelockMatcher.IsBlocked"/> directly instead, so both surfaces share one matching
/// implementation without a circular project reference.
/// </summary>
public class SitelockGuard(IOptionsWrapper<SharpMUSHOptions> options)
{
	/// <summary>Surface flag for game connections, login, and OTT issuance.</summary>
	public const string Connect = SitelockMatcher.ConnectFlag;

	/// <summary>Surface flag for account/player creation (registration, first-run setup claim).</summary>
	public const string Create = SitelockMatcher.CreateFlag;

	/// <summary>Surface flag for guest logins.</summary>
	public const string Guest = SitelockMatcher.GuestFlag;

	/// <summary>
	/// True if any configured sitelock rule matches <paramref name="ip"/>/<paramref name="host"/> and
	/// carries <paramref name="surfaceFlag"/> (one of <see cref="Connect"/>, <see cref="Create"/>,
	/// <see cref="Guest"/>) among its access flags.
	/// </summary>
	/// <remarks>Virtual solely so unit tests (e.g. <c>GameHubTests</c>) can NSubstitute-partial-mock
	/// this single decision point without constructing a full <see cref="SharpMUSHOptions"/>.</remarks>
	public virtual bool IsBlocked(string ip, string host, string surfaceFlag) =>
		SitelockMatcher.IsBlocked(options.CurrentValue.SitelockRules.Rules, ip, host, surfaceFlag);
}
