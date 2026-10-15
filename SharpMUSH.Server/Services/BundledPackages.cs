using System.Reflection;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Services;

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
		// Available, not installed: administrators opt into the global @editor command.
		new("softcode-editor", BundledPackageHandler.None, InstallAtFirstBoot: false),
		new("http-handler", BundledPackageHandler.Http, InstallAtFirstBoot: true),
		new("profile-handler", BundledPackageHandler.Http, InstallAtFirstBoot: true),
		new("room-contents", BundledPackageHandler.Event, InstallAtFirstBoot: true),
		// The Play sidebar's Channels and Pages, as room-contents is its Here and Exits.
		new("comm-feed", BundledPackageHandler.Event, InstallAtFirstBoot: true),
		// +help before its contributors: scene and wiki-reader attach their SRC registration to the
		// librarian, so it has to exist first. The stock helpfiles already promise players a +help,
		// which is why this installs at first boot rather than shipping available-and-unenabled.
		new("plus-help", BundledPackageHandler.None, InstallAtFirstBoot: true),
		new("scene", BundledPackageHandler.None, InstallAtFirstBoot: true),
		// Available, not installed: +wiki lands an object in the master room, and a game that
		// never asked for a wiki front end should not find one there after an upgrade.
		new("wiki-reader", BundledPackageHandler.None, InstallAtFirstBoot: false),
		// Available, not installed: the job queue adds +job, +request and their kin, and its portal
		// page is a separate application package so a game can run the queue in-game only.
		new("jobs", BundledPackageHandler.Http, InstallAtFirstBoot: false),
		new("jobs-app", BundledPackageHandler.None, InstallAtFirstBoot: false),
		// Offered, not installed: a new game reads its messages from the stored text until the administrator
		// chooses the Messages object (the setup wizard or the package page).
		new("messages", BundledPackageHandler.None, InstallAtFirstBoot: false),
		// Available, not installed: +radio lands an object in the master room, and is the worked example of
		// a system built on @feed.
		new("radio", BundledPackageHandler.None, InstallAtFirstBoot: false),
		// Available, not installed: joins radio and scene, so +radio/log puts a frequency into a scene.
		new("radio-scene", BundledPackageHandler.None, InstallAtFirstBoot: false),
		// Available, not installed: the scene package keeps any detail an owner sets, and a game chooses whether
		// it wants a verse, a canon and content warnings named for it.
		new("scene-verse", BundledPackageHandler.None, InstallAtFirstBoot: false),
		// Available, not installed: the bulletin boards add +bbread and its kin as game commands, and a
		// game that already runs its own board code should not find them taken after an upgrade. Its
		// portal page is a separate application package, as jobs-app is for jobs.
		new("bboards", BundledPackageHandler.Http, InstallAtFirstBoot: false),
		new("bboards-app", BundledPackageHandler.None, InstallAtFirstBoot: false),
		// Available, not installed: +who and +where are names a game may already use for its own code.
		new("who-where", BundledPackageHandler.None, InstallAtFirstBoot: false),
		// Available, not installed: +status is a name a game may already use for its own code. A status shows in
		// who-where's +who.
		new("status", BundledPackageHandler.None, InstallAtFirstBoot: false),
		// Available, not installed: +watch and +friend are names a game may already use for its own code.
		new("watch", BundledPackageHandler.None, InstallAtFirstBoot: false),
		// Available, not installed: +finger is a name a game may already use for its own code.
		new("finger", BundledPackageHandler.None, InstallAtFirstBoot: false),
		// Available, not installed: a game links to Discord only when its administrator sets up a bot.
		new("discord", BundledPackageHandler.None, InstallAtFirstBoot: false),
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

	/// <summary>
	/// The README a bundled package ships, in Markdown: its <c>examples/packages/&lt;id&gt;/README.md</c> when this
	/// build embeds one, else one written from <paramref name="manifest"/> — what it is, whether a new game gets it,
	/// what it needs and what it creates.
	/// </summary>
	public static string Readme(Descriptor descriptor, PackageManifest manifest) =>
		EmbeddedReadme(descriptor.PackageId) switch
		{
			string readme => readme,
			NotFound => DescribedReadme(descriptor, manifest)
		};

	/// <summary>The <c>README.md</c> embedded beside a bundled package's manifest, or <see cref="NotFound"/>.</summary>
	public static Found<string> EmbeddedReadme(string packageId)
	{
		using var stream = Assembly.GetExecutingAssembly()
			.GetManifestResourceStream($"SharpMUSH.Server.BundledPackages.{packageId}.README.md");
		if (stream is null)
		{
			return new NotFound();
		}

		using var buffer = new MemoryStream();
		stream.CopyTo(buffer);
		return PackageReadme.Decode(buffer.ToArray());
	}

	private static string DescribedReadme(Descriptor descriptor, PackageManifest manifest)
	{
		var lines = new List<string>
		{
			$"# {manifest.Name} {manifest.Version}",
			"",
			manifest.Description,
			"",
			descriptor.InstallAtFirstBoot
				? "Installed at a new game's first boot unless declined."
				: "Shipped with this server and not installed automatically."
		};

		if (manifest.Dependencies.Count > 0)
		{
			lines.Add("");
			lines.Add("## Requires");
			lines.AddRange(manifest.Dependencies.Select(d => $"- `{d.PackageId}` {d.Constraint}"));
		}

		// An attach entry adds attributes to an object the package does not create.
		var created = manifest.Objects.Where(o => !o.IsAttach).ToList();
		if (created.Count > 0)
		{
			lines.Add("");
			lines.Add("## Creates");
			lines.AddRange(created.Select(o => $"- {(string.IsNullOrEmpty(o.Name) ? o.Ref : o.Name)} (`{o.Type}`)"));
		}

		return string.Join('\n', lines);
	}

	/// <summary>The descriptor of the bundled package <paramref name="packageId"/>, or <see cref="NotFound"/>.</summary>
	public static Found<Descriptor> Find(string packageId) =>
		All.FirstOrDefault(d => string.Equals(d.PackageId, packageId, StringComparison.OrdinalIgnoreCase)) switch
		{
			{ PackageId: not null } descriptor => descriptor,
			_ => new NotFound()
		};

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
