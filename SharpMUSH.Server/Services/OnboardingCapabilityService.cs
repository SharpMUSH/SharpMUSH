using SharpMUSH.Library;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Portal.Onboarding;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Server.Services;

public sealed record OnboardingCommandMatch(string Objid, string Attribute, string Value);

public interface IOnboardingCommandProbe
{
	Task<IReadOnlyList<OnboardingCommandMatch>> MatchAsync(DBRef character, string command, CancellationToken cancellationToken);
}

/// <summary>Only reviewed package versions with reachable, unchanged command handlers are advertised.</summary>
public sealed class OnboardingCapabilityService(IPackageRegistryService registry, IOnboardingCommandProbe probe)
{
	private sealed record Adapter(string Package, string Version, string Command);
	private static readonly Adapter[] Reviewed =
	[
		new("plus-help", "1.8.0", "+help"),
		new("plus-help", "1.8.1", "+help"),
		new("scene", "1.15.0", "+scene/help"),
		new("onboarding", "1.0.1", "+welcome")
	];

	public async Task<IReadOnlyList<GuideCapability>> ResolveAsync(DBRef character, CancellationToken cancellationToken)
	{
		var installed = await registry.GetInstalledPackagesAsync();
		var result = new List<GuideCapability>();
		foreach (var adapter in Reviewed)
		{
			cancellationToken.ThrowIfCancellationRequested();
			var package = installed.FirstOrDefault(item => item.Id == adapter.Package && item.Version == adapter.Version);
			if (package is null) continue;
			var reference = ReferenceUrl(package);
			// Bundled packages have no Git commit URL. Their reserved source identity is
			// valid provenance; the version and live handler baseline checks still apply.
			var bundled = package.SourceRepo == SharpMUSH.Library.Models.Packages.BundledPackageSource.SourceRepo
				&& package.InstalledCommit == SharpMUSH.Library.Models.Packages.BundledPackageSource.SourceCommit
				&& package.SourcePath == package.Id;
			if (reference is null && !bundled) continue;
			var baseline = await registry.GetManagedAttributesAsync(package.Id);
			var matches = await probe.MatchAsync(character, adapter.Command, cancellationToken);
			// Dispatch runs all matches in its first matching scope. Even one foreign/edited match
			// makes the reviewed package description unsafe to present as this command's behavior.
			if (matches.Count == 0 || matches.Any(match => !baseline.Any(attribute =>
				attribute.Objid == match.Objid && attribute.Attribute == match.Attribute
				&& attribute.BaselineVersion == package.Version && attribute.BaselineValue == match.Value))) continue;
			result.Add(new GuideCapability(package.Id, adapter.Command, "Game-defined; read its help first",
				reference, package.Id, package.Version));
		}
		return result;
	}

	private static string? ReferenceUrl(SharpMUSH.Library.Models.Packages.InstalledPackageRecord package)
	{
		var repo = package.SourceRepo.TrimEnd('/');
		if (repo.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) repo = repo[..^4];
		if (!repo.Equals("https://github.com/SharpMUSH/SharpMUSH-Packages", StringComparison.OrdinalIgnoreCase)
			&& !repo.Equals("https://github.com/SharpMUSH/SharpMUSH", StringComparison.OrdinalIgnoreCase)) return null;
		if (package.InstalledCommit.Length != 40 || !package.InstalledCommit.All(Uri.IsHexDigit)) return null;
		var path = package.SourcePath;
		if (string.IsNullOrEmpty(path) || path.Split('/').Any(segment => segment.Length == 0
			|| segment is "." or ".." || segment.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_')))) return null;
		return $"{repo}/blob/{package.InstalledCommit}/{path}/package.yaml";
	}
}
