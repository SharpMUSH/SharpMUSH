using System.Text.RegularExpressions;

namespace SharpMUSH.Library.Secrets;

/// <summary>
/// The hosts a secret may be sent to by <c>@http</c>: a host name such as <c>api.example.com</c>, which matches
/// only itself, or <c>*.example.com</c>, which matches every host under <c>example.com</c> but not
/// <c>example.com</c> itself. Names are compared in lower case.
/// </summary>
public static partial class SecretHosts
{
	/// <summary>Whether <paramref name="pattern"/> (already in lower case) is a host name, or one after <c>*.</c>.</summary>
	public static bool IsValidPattern(string pattern) => pattern.Length <= 253 && PatternRegex().IsMatch(pattern);

	/// <summary>Whether <paramref name="host"/> matches one of <paramref name="patterns"/>.</summary>
	public static bool Allows(IEnumerable<string> patterns, string host)
	{
		var lower = host.ToLowerInvariant();
		return patterns.Any(pattern => pattern.StartsWith("*.", StringComparison.Ordinal)
			? lower.EndsWith(pattern[1..], StringComparison.Ordinal)
			: lower == pattern);
	}

	[GeneratedRegex(@"^(?:\*\.)?[a-z0-9](?:[a-z0-9-]*[a-z0-9])?(?:\.[a-z0-9](?:[a-z0-9-]*[a-z0-9])?)*$", RegexOptions.CultureInvariant)]
	private static partial Regex PatternRegex();
}
