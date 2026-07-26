using System.Reflection;

namespace SharpMUSH.Server.Services;

/// <summary>
/// The default packages the server ships and installs at first boot (via
/// <see cref="DefaultPackagesBootstrapService"/>), plus a loader for their embedded
/// manifests. The package manager is the delivery mechanism for all default softcode —
/// each entry here is a bundled <c>examples/packages/&lt;id&gt;/package.yaml</c> embedded as a
/// resource. Adding a default package is a one-line addition to <see cref="All"/>.
/// </summary>
/// <summary>The configured handler object a bundled attach-mode package needs.</summary>
public enum BundledPackageHandler
{
	/// <summary>Create mode — installs regardless of handler configuration.</summary>
	None,

	/// <summary>Attaches to the configured <c>http_handler</c> object.</summary>
	Http,

	/// <summary>Attaches to the configured <c>event_handler</c> object.</summary>
	Event
}

public static class BundledPackages
{
	/// <summary>
	/// A bundled package and the configured handler object it attaches to, if any. An
	/// attach-mode package is skipped when its handler is not configured (there would be no
	/// target to resolve <c>{{$http_handler}}</c> / <c>{{$event_handler}}</c> against);
	/// create-mode packages always install.
	/// </summary>
	public readonly record struct Descriptor(string PackageId, BundledPackageHandler Requires);

	/// <summary>Bundled packages in dependency order (a dependency precedes its dependents).</summary>
	public static readonly IReadOnlyList<Descriptor> All =
	[
		new("http-handler", BundledPackageHandler.Http),
		new("profile-handler", BundledPackageHandler.Http),
		new("room-contents", BundledPackageHandler.Event),
		new("common-functions", BundledPackageHandler.None),
		new("scene", BundledPackageHandler.None),
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
