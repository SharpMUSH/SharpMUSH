using System.Reflection;

namespace SharpMUSH.Server.Services;

/// <summary>
/// The default packages the server ships and installs at first boot (via
/// <see cref="DefaultPackagesBootstrapService"/>), plus a loader for their embedded
/// manifests. The package manager is the delivery mechanism for all default softcode —
/// each entry here is a bundled <c>examples/packages/&lt;id&gt;/package.yaml</c> embedded as a
/// resource. Adding a default package is a one-line addition to <see cref="All"/>.
/// </summary>
public static class BundledPackages
{
	/// <summary>
	/// A bundled package and whether it attaches to the configured <c>http_handler</c> object
	/// (attach-mode packages are skipped when no handler is configured; create-mode packages
	/// always install).
	/// </summary>
	public readonly record struct Descriptor(string PackageId, bool RequiresHttpHandler);

	/// <summary>Bundled packages in dependency order (a dependency precedes its dependents).</summary>
	public static readonly IReadOnlyList<Descriptor> All =
	[
		new("http-handler", RequiresHttpHandler: true),
		new("profile-handler", RequiresHttpHandler: true),
		new("common-functions", RequiresHttpHandler: false),
	];

	/// <summary>The raw YAML of one bundled package manifest (embedded resource).</summary>
	public static string ManifestYaml(string packageId)
	{
		var resource = $"SharpMUSH.Server.BundledPackages.{packageId}.package.yaml";
		using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource)
			?? throw new InvalidOperationException($"Bundled resource '{resource}' not found.");
		using var reader = new StreamReader(stream);
		return reader.ReadToEnd();
	}
}
