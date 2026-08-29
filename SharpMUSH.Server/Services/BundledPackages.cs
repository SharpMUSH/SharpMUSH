using System.Reflection;
using SharpMUSH.Library.Models.Packages;

namespace SharpMUSH.Server.Services;

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

/// <summary>
/// The packages the server ships inside its own assembly, and the loader for their embedded
/// manifests. The package manager is the delivery mechanism for all default softcode — each
/// entry here is a bundled <c>examples/packages/&lt;id&gt;/package.yaml</c> embedded as a
/// resource.
///
/// Shipping a package is not the same decision as installing it. Every entry in
/// <see cref="All"/> is installable offline through the reserved <c>bundled</c> package source
/// (see <c>PackagesController</c>); only the entries flagged
/// <see cref="Descriptor.InstallAtFirstBoot"/> are installed into a new game by
/// <see cref="DefaultPackagesBootstrapService"/>. An application that puts objects in front of
/// players — wiki-reader's <c>+wiki</c> object in the master room — ships available and unenabled
/// so the admin chooses it; the handlers and function libraries the portal itself depends on
/// install at boot.
/// </summary>
public static class BundledPackages
{
	/// <summary>
	/// A bundled package, the configured handler object it attaches to (if any), and whether a
	/// new game gets it. An attach-mode package is skipped when its handler is not configured
	/// (there would be no target to resolve <c>{{$http_handler}}</c> / <c>{{$event_handler}}</c>
	/// against); create-mode packages need no such target.
	/// </summary>
	/// <param name="PackageId">Package id, matching the embedded manifest's <c>package:</c>.</param>
	/// <param name="Requires">The configured handler object the package attaches to, if any.</param>
	/// <param name="InstallAtFirstBoot">
	/// Whether bootstrap installs this into a game that does not have it. False ships the package
	/// in the image without installing it — the admin installs it from the bundled source when they
	/// want it. Once installed, bootstrap maintains it either way.
	/// </param>
	public readonly record struct Descriptor(
		string PackageId,
		BundledPackageHandler Requires,
		bool InstallAtFirstBoot);

	/// <summary>
	/// Bundled packages in dependency order (a dependency precedes its dependents), which is also
	/// the order the offline catalogue and first-boot install walk them in.
	/// </summary>
	public static readonly IReadOnlyList<Descriptor> All =
	[
		new("http-handler", BundledPackageHandler.Http, InstallAtFirstBoot: true),
		new("profile-handler", BundledPackageHandler.Http, InstallAtFirstBoot: true),
		new("room-contents", BundledPackageHandler.Event, InstallAtFirstBoot: true),
		new("common-functions", BundledPackageHandler.None, InstallAtFirstBoot: true),
		new("scene", BundledPackageHandler.None, InstallAtFirstBoot: true),
		// Available, not installed: +wiki lands an object in the master room, and a game that
		// never asked for a wiki front end should not find one there after an upgrade.
		new("wiki-reader", BundledPackageHandler.None, InstallAtFirstBoot: false),
	];

	/// <summary>
	/// Reserved remote name for the catalogue. Never stored in <c>sys_remotes</c> — the packages
	/// API synthesizes it into the remote list and resolves it from embedded resources. Defined in
	/// <see cref="BundledPackageSource"/> because the portal has to recognise it too.
	/// </summary>
	public const string RemoteName = BundledPackageSource.RemoteName;

	/// <inheritdoc cref="BundledPackageSource.SourceRepo"/>
	public const string SourceRepo = BundledPackageSource.SourceRepo;

	/// <inheritdoc cref="BundledPackageSource.SourceCommit"/>
	public const string SourceCommit = BundledPackageSource.SourceCommit;

	/// <summary>True when <paramref name="packageId"/> ships in this build's catalogue.</summary>
	public static bool Contains(string packageId) =>
		All.Any(d => string.Equals(d.PackageId, packageId, StringComparison.OrdinalIgnoreCase));

	/// <inheritdoc cref="BundledPackageSource.IsCatalogueRemote"/>
	public static bool IsCatalogueRemote(string? name) => BundledPackageSource.IsCatalogueRemote(name);

	/// <inheritdoc cref="BundledPackageSource.IsCatalogueSource"/>
	public static bool IsCatalogueSource(string? sourceRepo) => BundledPackageSource.IsCatalogueSource(sourceRepo);

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
