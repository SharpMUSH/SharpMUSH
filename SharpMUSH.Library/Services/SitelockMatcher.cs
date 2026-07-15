using System.Net;
using System.Text.RegularExpressions;

namespace SharpMUSH.Library.Services;

/// <summary>
/// Shared sitelock host-rule matcher used by both the <c>@SITELOCK/CHECK</c> command and
/// <c>BanEnforcementService</c>'s host-rule enforcement, so the two never drift apart.
/// </summary>
public static class SitelockMatcher
{
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
	/// Simple wildcard matching for sitelock patterns (<c>*</c> and <c>?</c> wildcards), lifted
	/// from the former private <c>WizardCommands.WildcardMatch</c> so it is shared across the
	/// connect-time check and ban-enforcement matchers.
	/// </summary>
	private static bool WildcardMatch(string text, string pattern)
	{
		var regexPattern = "^" + Regex.Escape(pattern)
			.Replace("\\*", ".*")
			.Replace("\\?", ".") + "$";

		return Regex.IsMatch(text, regexPattern, RegexOptions.IgnoreCase);
	}
}
