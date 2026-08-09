using System.Collections.Concurrent;
using System.Net;
using System.Text.RegularExpressions;

namespace SharpMUSH.Library.Services;

/// <summary>
/// Shared sitelock host-rule matcher used by both the <c>@SITELOCK/CHECK</c> command and
/// <c>BanEnforcementService</c>'s host-rule enforcement, so the two never drift apart.
/// </summary>
public static class SitelockMatcher
{
	/// <summary>Surface flag gating game connections, telnet/web login, and OTT issuance (Task 15).</summary>
	public const string ConnectFlag = "!connect";

	/// <summary>Surface flag gating account/player creation (web registration, first-run setup claim).</summary>
	public const string CreateFlag = "!create";

	/// <summary>Surface flag gating guest logins specifically (on top of, not instead of, <see cref="ConnectFlag"/>).</summary>
	public const string GuestFlag = "!guest";

	/// <summary>
	/// True if any rule in <paramref name="rules"/> both matches <paramref name="ip"/>/<paramref name="host"/>
	/// (via <see cref="Matches"/>) and carries <paramref name="surfaceFlag"/> among its access flags.
	/// Used to gate the auth surfaces (Task 15) on <c>!connect</c>/<c>!create</c>/<c>!guest</c> rules —
	/// anonymous browsing never calls this, so it never gates plain page views.
	/// </summary>
	public static bool IsBlocked(IReadOnlyDictionary<string, string[]> rules, string ip, string host, string surfaceFlag)
	{
		foreach (var (pattern, flags) in rules)
		{
			if (Array.IndexOf(flags, surfaceFlag) < 0)
			{
				continue;
			}

			if (Matches(pattern, ip, host))
			{
				return true;
			}
		}

		return false;
	}

	/// <summary>
	/// True if <paramref name="rulePattern"/> matches this connection. A rule matches when any of
	/// the following holds: it is a <c>*</c>/<c>?</c> glob that matches <paramref name="host"/>;
	/// it is a CIDR block (IPv4 or IPv6) that contains <paramref name="ip"/>; it is a bare IP
	/// address equal to <paramref name="ip"/>; or it is a glob that matches the
	/// <paramref name="ip"/> string itself. CIDR/bare-IP parsing is tried before falling back to
	/// glob, so a pattern like <c>"10.0.0.0/8"</c> is never misread as a literal glob. Null or
	/// empty arguments never match rather than throwing.
	/// </summary>
	public static bool Matches(string rulePattern, string ip, string host)
	{
		if (string.IsNullOrEmpty(rulePattern))
		{
			return false;
		}

		if (!string.IsNullOrEmpty(host) && WildcardMatch(host, rulePattern))
		{
			return true;
		}

		if (string.IsNullOrEmpty(ip))
		{
			return false;
		}

		// Try CIDR/bare-IP first — a pattern like "10.0.0.0/8" must never fall through to the glob
		// branch below (its "." and "/" would be escaped literally and never match anything).
		if (IPNetwork.TryParse(rulePattern, out var network))
		{
			return IPAddress.TryParse(ip, out var ipAddress) && network.Contains(ipAddress);
		}

		if (IPAddress.TryParse(rulePattern, out var ruleAddress))
		{
			return IPAddress.TryParse(ip, out var ipAddress) && ruleAddress.Equals(ipAddress);
		}

		return WildcardMatch(ip, rulePattern);
	}

	/// <summary>
	/// Compiled glob patterns, keyed by the rule text they came from. Matching now runs on the
	/// authentication path of every authenticated request, not only at login, so rebuilding the pattern
	/// string and re-parsing it once per rule per call is worth avoiding. The rule set is admin-authored
	/// and small, and every caller passes a rule as the pattern, so its keys are bounded by the rule set
	/// and this never grows unbounded.
	/// </summary>
	private static readonly ConcurrentDictionary<string, Regex> GlobCache = new();

	/// <summary>
	/// Simple wildcard matching for sitelock patterns (<c>*</c> and <c>?</c> wildcards), lifted
	/// from the former private <c>WizardCommands.WildcardMatch</c> so it is shared across the
	/// connect-time check and ban-enforcement matchers.
	/// </summary>
	private static bool WildcardMatch(string text, string pattern)
		=> GlobCache.GetOrAdd(pattern, static p => new Regex(
				"^" + Regex.Escape(p).Replace("\\*", ".*").Replace("\\?", ".") + "$",
				RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
			.IsMatch(text);
}
