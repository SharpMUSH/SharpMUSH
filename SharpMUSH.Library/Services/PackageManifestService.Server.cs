using SharpMUSH.Library.Models.Packages;

namespace SharpMUSH.Library.Services;

/// <summary>Reads the engine-version range: <c>requires_server</c>, and <c>verified_server</c> and <c>maximum_server</c> (format 1.6).</summary>
public partial class PackageManifestService
{
	/// <summary>
	/// Reads <c>verified_server</c> (the newest SharpMUSH version the author tested on) and <c>maximum_server</c> (the
	/// newest it runs on, one to three components), and checks the three bounds agree. <c>requires_server</c> is the
	/// lowest version only: an upper bound in it would refuse a newer server outright, which is what
	/// <c>maximum_server</c> asks the administrator about instead.
	/// </summary>
	private static (PackageVersion? Verified, PackageVersionCeiling? Maximum) ReadServerRange(
		Dictionary<string, object?> doc, VersionConstraint? minimum, List<PackageManifestIssue> issues)
	{
		if (minimum?.Clauses.Any(c => c.Comparison is not (VersionComparison.GreaterOrEqual or VersionComparison.Greater)) == true)
		{
			issues.Add(PackageManifestIssue.Error("requires_server",
				$"'{minimum}' sets an upper bound or an exact version; requires_server is the lowest version only, such as \">=2.0\". "
				+ "State the newest version the package runs on in maximum_server."));
		}

		PackageVersion? verified = null;
		if (OptionalString(doc, "verified_server", issues) is { } verifiedText)
		{
			if (PackageVersion.TryParse(verifiedText, out var parsed))
			{
				verified = parsed;
			}
			else
			{
				issues.Add(PackageManifestIssue.Error("verified_server", $"'{verifiedText}' is not a version, such as 2.0."));
			}
		}

		PackageVersionCeiling? maximum = null;
		if (OptionalString(doc, "maximum_server", issues) is { } maximumText)
		{
			if (PackageVersionCeiling.TryParse(maximumText, out var parsed))
			{
				maximum = parsed;
			}
			else
			{
				issues.Add(PackageManifestIssue.Error("maximum_server",
					$"'{maximumText}' is not a version of one to three numbers, such as 2 (every 2.x) or 2.4."));
			}
		}

		var lowest = minimum?.Clauses.Select(c => c.Version).Max();
		if (maximum is not null && lowest is not null && maximum.IsExceededBy(lowest))
		{
			issues.Add(PackageManifestIssue.Error("maximum_server",
				$"maximum_server {maximum} is below requires_server {minimum}; no SharpMUSH version satisfies both."));
		}

		if (verified is not null && lowest is not null && verified.CompareTo(lowest) < 0)
		{
			issues.Add(PackageManifestIssue.Warning("verified_server",
				$"verified_server {verified} is below requires_server {minimum}."));
		}

		if (verified is not null && maximum is not null && maximum.IsExceededBy(verified))
		{
			issues.Add(PackageManifestIssue.Warning("verified_server",
				$"verified_server {verified} is above maximum_server {maximum}."));
		}

		return (verified, maximum);
	}
}
