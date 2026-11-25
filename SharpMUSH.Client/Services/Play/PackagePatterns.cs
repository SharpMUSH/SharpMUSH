using SharpMUSH.Library.Models.Portal.Play;

namespace SharpMUSH.Client.Services.Play;

/// <summary>
/// A list of package names, each exact (<c>myapp.private</c>) or a prefix ending in <c>.*</c> (<c>myapp.*</c>).
/// Names compare without regard to case, as the engine's do.
/// </summary>
public sealed class PackagePatterns
{
	private readonly string[] _exact;
	private readonly string[] _prefixes;

	private PackagePatterns(string[] exact, string[] prefixes)
	{
		_exact = exact;
		_prefixes = prefixes;
	}

	/// <summary>The patterns in <paramref name="patterns"/>; blank entries and a bare <c>*</c> are left out.</summary>
	public static PackagePatterns Of(IEnumerable<string> patterns)
	{
		List<string> exact = [];
		List<string> prefixes = [];
		foreach (var pattern in patterns.Select(raw => raw.Trim()))
		{
			if (pattern.EndsWith(".*", StringComparison.Ordinal))
			{
				if (pattern.Length > 2) prefixes.Add(pattern[..^1]);
			}
			else if (pattern.Length > 0 && !pattern.Contains('*'))
			{
				exact.Add(pattern);
			}
		}

		return new PackagePatterns([.. exact], [.. prefixes]);
	}

	/// <summary>Whether a package of this name is never delivered to an application.</summary>
	public static bool IsReserved(string package) => PlayPackageNames.IsReserved(package);

	/// <summary>Whether <paramref name="package"/> matches one of the patterns.</summary>
	public bool Matches(string package) =>
		_exact.Any(name => string.Equals(name, package, StringComparison.OrdinalIgnoreCase))
		|| _prefixes.Any(prefix => package.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
}
