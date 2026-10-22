using System.Collections.Concurrent;
using System.Net;
using System.Text.RegularExpressions;
using SharpMUSH.Library.Markup;
using SharpMUSH.Library.Utilities;

namespace SharpMUSH.Library.Common;

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

	/// <summary>God can't connect from the site (PennMUSH <c>ACS_GOD</c>).</summary>
	public const string NoGodFlag = "!god";

	/// <summary>Wizards can't connect from the site (PennMUSH <c>ACS_WIZARD</c>).</summary>
	public const string NoWizardFlag = "!wizard";

	/// <summary>Wizards and Royalty can't connect from the site (PennMUSH <c>ACS_ADMIN</c>).</summary>
	public const string NoAdminFlag = "!admin";

	/// <summary>Players who connect from the site are set SUSPECT (PennMUSH <c>ACS_SUSPECT</c>).</summary>
	public const string SuspectOption = "suspect";

	/// <summary>Refused attempts from the site are not logged (PennMUSH <c>ACS_DENY_SILENT</c>).</summary>
	public const string DenySilentOption = "deny_silent";

	/// <summary>The rule's pattern is a regular expression, not a wildcard (PennMUSH <c>ACS_REGEXP</c>).</summary>
	public const string RegexpOption = "regexp";

	/// <summary>
	/// True if any rule in <paramref name="rules"/> both matches <paramref name="ip"/>/<paramref name="host"/>
	/// (via <see cref="Matches"/>) and carries <paramref name="surfaceFlag"/> among its access flags.
	/// Used to gate the auth surfaces (Task 15) on <c>!connect</c>/<c>!create</c>/<c>!guest</c> rules —
	/// anonymous browsing never calls this, so it never gates plain page views.
	/// </summary>
	public static bool IsBlocked(IReadOnlyDictionary<string, string[]> rules, string ip, string host, string surfaceFlag)
		=> Applies(rules, ip, host, surfaceFlag);

	/// <summary>
	/// True if any rule in <paramref name="rules"/> both matches <paramref name="ip"/>/<paramref name="host"/>
	/// and carries <paramref name="option"/> (compared without case, as PennMUSH's <c>parse_access_options</c>
	/// reads them).
	/// </summary>
	public static bool Applies(IReadOnlyDictionary<string, string[]> rules, string ip, string host, string option)
	{
		// Parsed once per call rather than once per rule.
		var ipAddress = ParseAddress(ip);

		return rules
			.Where(rule => HasOption(rule.Value, option))
			.Any(rule => Matches(rule.Key, rule.Value, ip, ipAddress, host));
	}

	/// <summary>
	/// PennMUSH <c>site_can_access</c>'s class checks (<c>src/access.c:409-417</c>): a matching rule with
	/// <see cref="NoGodFlag"/> refuses God, <see cref="NoWizardFlag"/> a Wizard, and <see cref="NoAdminFlag"/>
	/// a Wizard or Royalty.
	/// </summary>
	public static bool RefusesPrivileged(IReadOnlyDictionary<string, string[]> rules, string ip, string host,
		bool isGod, bool isWizard, bool isAdmin)
		=> isGod && Applies(rules, ip, host, NoGodFlag)
			|| isWizard && Applies(rules, ip, host, NoWizardFlag)
			|| isAdmin && Applies(rules, ip, host, NoAdminFlag);

	private static bool HasOption(IEnumerable<string> options, string option)
		=> options.Any(candidate => string.Equals(candidate, option, StringComparison.OrdinalIgnoreCase));

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
		=> Matches(rulePattern, [], ip, ParseAddress(ip), host);

	/// <summary>
	/// <see cref="Matches(string, string, string)"/> for a rule with <paramref name="options"/>: with
	/// <see cref="RegexpOption"/> the pattern is a case-sensitive regular expression matched against the
	/// host name and the address, as PennMUSH compiles it (<c>src/access.c:161-164</c>).
	/// </summary>
	public static bool Matches(string rulePattern, IReadOnlyCollection<string> options, string ip, string host)
		=> Matches(rulePattern, options, ip, ParseAddress(ip), host);

	private static bool Matches(string rulePattern, IReadOnlyCollection<string> options, string ip, IPAddress? ipAddress, string host)
	{
		if (string.IsNullOrEmpty(rulePattern))
		{
			return false;
		}

		if (HasOption(options, RegexpOption))
		{
			return RegexCache.GetOrAdd(rulePattern, static p => CompileRegexp(p)) is { } regex
				&& (!string.IsNullOrEmpty(host) && SoftcodeRegex.IsMatch(regex, host)
					|| !string.IsNullOrEmpty(ip) && SoftcodeRegex.IsMatch(regex, ip));
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
		var shape = RuleCache.GetOrAdd(rulePattern, static p => RuleShape.Parse(p));
		if (shape.Network is { } network)
		{
			return ipAddress is not null && network.Contains(ipAddress);
		}

		if (shape.Address is { } ruleAddress)
		{
			return ipAddress is not null && ruleAddress.Equals(ipAddress);
		}

		return WildcardMatch(ip, rulePattern);
	}

	private static IPAddress? ParseAddress(string ip)
		=> !string.IsNullOrEmpty(ip) && IPAddress.TryParse(ip, out var address) ? address : null;

	/// <summary>
	/// What a rule's text parses as: a CIDR block, a bare address, or (both null) a glob. Cached per rule
	/// for the same reason as <see cref="GlobCache"/> — the rule set is admin-authored and small, and the
	/// parse would otherwise repeat for every rule on every authenticated request.
	/// </summary>
	private sealed record RuleShape(IPNetwork? Network, IPAddress? Address)
	{
		public static RuleShape Parse(string rulePattern)
		{
			if (IPNetwork.TryParse(rulePattern, out var network))
			{
				return new RuleShape(network, null);
			}

			return new RuleShape(null, IPAddress.TryParse(rulePattern, out var address) ? address : null);
		}
	}

	private static readonly ConcurrentDictionary<string, RuleShape> RuleCache = new();

	/// <summary>
	/// Compiled glob patterns, keyed by the rule text they came from. Matching runs on the
	/// authentication path of every authenticated request, not only at login, so rebuilding the pattern
	/// string and re-parsing it once per rule per call is worth avoiding.
	/// </summary>
	/// <remarks>
	/// A cache of its own rather than relying on the bounded one inside <see cref="SoftcodeRegex"/>:
	/// that one is least-recently-used and shared with player-authored patterns, so a sitelock rule could
	/// be evicted and recompiled on the path that decides whether a connection is allowed. Its keys are
	/// admin-authored rules, so this is bounded by the rule set. The regex is built here rather than by
	/// <see cref="SoftcodeRegex.Create"/>, which shortens the timeout to whatever is left of a running
	/// command's execution budget — a value that must not be cached for every later connection.
	/// </remarks>
	private static readonly ConcurrentDictionary<string, Regex> GlobCache = new();

	/// <summary>Compiled <see cref="RegexpOption"/> patterns, cached for the same reason as <see cref="GlobCache"/>.</summary>
	private static readonly ConcurrentDictionary<string, Regex?> RegexCache = new();

	/// <summary>The rule's regular expression, or null when it does not compile (such a rule matches nothing).</summary>
	private static Regex? CompileRegexp(string pattern)
	{
		try
		{
			return new Regex(pattern, RegexOptions.CultureInvariant, SoftcodeRegex.MatchTimeout);
		}
		catch (ArgumentException)
		{
			return null;
		}
	}

	/// <summary>Whether <paramref name="pattern"/> compiles as a <see cref="RegexpOption"/> rule.</summary>
	public static bool IsValidRegexp(string pattern) => CompileRegexp(pattern) is not null;

	/// <summary>
	/// Whether <paramref name="text"/> matches the sitelock rule <paramref name="pattern"/>, read as a
	/// general MUSH wildcard (<see cref="MushText.Glob"/>) — PennMUSH's <c>site_check_access</c> uses
	/// <c>quick_wild</c> (<c>src/access.c</c>). Case-insensitive; <c>\</c> makes the next character
	/// literal. The non-backtracking engine keeps a many-star rule linear on every connection.
	/// </summary>
	private static bool WildcardMatch(string text, string pattern)
		=> SoftcodeRegex.IsMatch(
			GlobCache.GetOrAdd(pattern, static p => new Regex(MushText.Glob.ToRegex(p),
				RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking,
				SoftcodeRegex.MatchTimeout)),
			text);
}
