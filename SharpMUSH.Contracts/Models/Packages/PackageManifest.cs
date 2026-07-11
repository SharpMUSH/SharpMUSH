namespace SharpMUSH.Library.Models.Packages;

/// <summary>
/// A parsed and validated softcode package manifest (package.yaml, format v2
/// per decisions 20.11–20.20). Declarative desired state: objects to exist,
/// attributes they carry, and symbolic <c>{{refs}}</c> in place of dbrefs.
/// </summary>
/// <param name="Format">Manifest format version (missing in YAML = 1.0).</param>
/// <param name="Name">Package id slug (lowercase, hyphens, ≤64 chars), e.g. <c>myrddins-bbs</c>.</param>
/// <param name="Version">Package semantic version.</param>
/// <param name="Authors">Author display names.</param>
/// <param name="Description">Short human-readable description.</param>
/// <param name="License">License identifier (SPDX expression recommended), or null.</param>
/// <param name="Homepage">Project homepage / documentation URL, or null.</param>
/// <param name="Keywords">Search keywords for the browse UI (≤5 advisory).</param>
/// <param name="ConventionPrefix">Advisory attribute-name prefix (e.g. <c>BBS_</c>); not enforced.</param>
/// <param name="RequiresServer">Minimum/compatible SharpMUSH server version constraint, or null.</param>
/// <param name="Replaces">Package id this package supersedes (rename continuity), or null.</param>
/// <param name="Conflicts">Packages that cannot be installed alongside this one.</param>
/// <param name="Dependencies">Packages this package requires, with version constraints.</param>
/// <param name="Configure"><c>{{?configure}}</c> parameters the installing admin supplies, keyed by name.</param>
/// <param name="Objects">Objects the package manages, in manifest order (empty for <see cref="PackageKind.Application"/> packages).</param>
/// <param name="Kind">Package kind: a softcode package (objects/attributes), an application package (a portal registration), or a managed package (a C# plugin DLL).</param>
/// <param name="Application">The dynamic-application registration this package installs, when <see cref="Kind"/> is <see cref="PackageKind.Application"/>; otherwise null.</param>
/// <param name="Binary">The compiled plugin DLL(s) this package carries, when <see cref="Kind"/> is <see cref="PackageKind.Managed"/>; otherwise null.</param>
public sealed record PackageManifest(
	PackageFormatVersion Format,
	string Name,
	PackageVersion Version,
	IReadOnlyList<string> Authors,
	string Description,
	string? License,
	string? Homepage,
	IReadOnlyList<string> Keywords,
	string? ConventionPrefix,
	VersionConstraint? RequiresServer,
	string? Replaces,
	IReadOnlyList<PackageDependencySpec> Conflicts,
	IReadOnlyList<PackageDependencySpec> Dependencies,
	IReadOnlyDictionary<string, PackageConfigureSpec> Configure,
	IReadOnlyList<PackageObjectSpec> Objects,
	PackageKind Kind = PackageKind.Softcode,
	PackageApplicationSpec? Application = null,
	PackageBinarySpec? Binary = null);

/// <summary>
/// The kinds of package the manager installs. A <see cref="Softcode"/>
/// package creates objects and manages attributes (the original Area-20 model).
/// An <see cref="Application"/> package carries no objects of its own — it
/// registers a Dynamic Application (Area 21) in the portal and <c>depends</c>
/// on the softcode package that provides its HTTP-handler routes. A
/// <see cref="Managed"/> package distributes a compiled C# plugin DLL (Phase 4
/// of the plugin system): installing it verifies the carried binaries against
/// SHA-256 hashes in the manifest and, once the operator opts in (managed code
/// runs in full server trust), deposits them into <c>plugins/&lt;id&gt;/</c> for
/// the plugin loader to pick up on the next boot.
/// </summary>
public enum PackageKind
{
	/// <summary>Objects and attributes (the default). Requires <c>objects:</c>; forbids <c>application:</c>/<c>binaries:</c>.</summary>
	Softcode,

	/// <summary>A portal application registration. Requires <c>application:</c>; forbids <c>objects:</c>/<c>binaries:</c>.</summary>
	Application,

	/// <summary>A compiled C# plugin DLL. Requires <c>binaries:</c>; forbids <c>objects:</c>/<c>application:</c>.</summary>
	Managed
}

/// <summary>
/// The binary payload a <see cref="PackageKind.Managed"/> package carries: the
/// hashed plugin DLL(s) (and optional dependency assemblies / <c>plugin.json</c>)
/// shipped alongside <c>package.yaml</c> in the package source, plus the minimum
/// server/plugin-contract version they were built against. Each file is verified
/// against its declared SHA-256 at install time; any mismatch rejects the apply.
/// </summary>
/// <param name="MinServerVersion">Minimum SharpMUSH server / plugin-contract version the binaries require; the install refuses an older server.</param>
/// <param name="Files">The carried files, in manifest order. At least one must be present.</param>
public sealed record PackageBinarySpec(
	VersionConstraint MinServerVersion,
	IReadOnlyList<PackageBinaryFile> Files);

/// <summary>One carried binary file and the SHA-256 the installer must verify it against.</summary>
/// <param name="FileName">File name relative to the package source directory (no path separators — a flat manifest of files to deposit into <c>plugins/&lt;id&gt;/</c>).</param>
/// <param name="Sha256">Lowercase hex SHA-256 of the file's bytes; the install rejects a mismatch.</param>
public sealed record PackageBinaryFile(string FileName, string Sha256);

/// <summary>
/// The portal registration an application package installs: it maps one-to-one
/// onto a <c>RegisteredApplication</c> in the Area-21 application registry, but
/// its string fields may carry <c>{{?configure}}</c>/<c>{{$well_known}}</c>/
/// <c>{{dependency/ref}}</c> refs that are resolved at apply time, so the
/// installing admin can tune role, placement, and endpoints per game.
/// </summary>
/// <param name="Slug">URL-safe unique key; the application renders at <c>/apps/{slug}</c>. Defaults to the package id.</param>
/// <param name="DisplayName">Human-readable label shown in nav and the widget palette.</param>
/// <param name="Icon">Material icon name, or null for a default.</param>
/// <param name="Kind">Full-page (<c>page</c>) or placeable widget (<c>widget</c>).</param>
/// <param name="SchemaUrl">Relative HTTP-handler route returning the Portal Schema Document (e.g. <c>http/chargen/schema</c>).</param>
/// <param name="DataUrl">Optional relative route returning data values (view display / form prefill).</param>
/// <param name="SubmitRoute">Optional base relative route for the schema's POST actions.</param>
/// <param name="MinimumRole">Lowest portal role that may see the nav entry / open the route (may be a configure ref, resolved at apply).</param>
/// <param name="NavPlacement">Nav section hint for page apps, or null to hide from nav.</param>
/// <param name="Zones">Allowed layout zones for widget apps, or null/empty for none.</param>
/// <param name="Order">Sort order within nav / listings.</param>
public sealed record PackageApplicationSpec(
	string Slug,
	string DisplayName,
	string? Icon,
	PackageApplicationDisplay Kind,
	string SchemaUrl,
	string? DataUrl,
	string? SubmitRoute,
	string MinimumRole,
	string? NavPlacement,
	IReadOnlyList<string> Zones,
	int Order);

/// <summary>How an application surfaces in the portal (mirrors <c>ApplicationKind</c>).</summary>
public enum PackageApplicationDisplay
{
	/// <summary>A nav-linked full-page application at <c>/apps/{slug}</c>.</summary>
	Page,

	/// <summary>A schema-driven widget that can be placed into a layout zone.</summary>
	Widget
}

/// <summary>
/// The manifest format version (decision 20.17). Consumers warn on a newer
/// minor and reject a newer major (the Python Metadata-Version rule).
/// </summary>
/// <param name="Major">Major format version; unknown majors are rejected.</param>
/// <param name="Minor">Minor format version; unknown minors warn and proceed.</param>
public sealed record PackageFormatVersion(int Major, int Minor)
{
	/// <summary>The format version this parser implements.</summary>
	public static PackageFormatVersion Supported { get; } = new(1, 1);

	public override string ToString() => Minor == 0 ? $"{Major}" : $"{Major}.{Minor}";
}

/// <summary>A dependency (or conflict) declaration on another package.</summary>
/// <param name="PackageId">The other package's id slug.</param>
/// <param name="Constraint">Version constraint the other package must satisfy.</param>
/// <param name="Source">Optional hint for where to obtain a dependency when it is not installed (unused for conflicts).</param>
public sealed record PackageDependencySpec(
	string PackageId,
	VersionConstraint Constraint,
	PackageSourceHint? Source = null);

/// <summary>
/// Tells an installer where a package can be obtained: a git repo, optionally
/// narrowed to a directory within it and a branch. Used by dependency
/// resolution to offer "requires who-where — fetch it from here?" instead of
/// a dead-end error when a dependency is not installed.
/// </summary>
/// <param name="Repo">Git repo URL (https or ssh).</param>
/// <param name="Path">Directory of the package within the repo (for monorepos), or null for the repo root.</param>
/// <param name="Branch">Branch to install from, or null for the remote's default branch.</param>
public sealed record PackageSourceHint(string Repo, string? Path, string? Branch);

/// <summary>
/// Declares a <c>{{?configure}}</c> parameter: a value the installing admin
/// is prompted for during review (decision 20.19).
/// </summary>
/// <param name="Key">The ref name used in the manifest (lowercase).</param>
/// <param name="Label">Human-readable prompt shown to the installing admin.</param>
/// <param name="Type">Value type; only dbref-typed refs may appear in parent/location/destination.</param>
/// <param name="Default">Optional default value (forbidden for dbref type).</param>
public sealed record PackageConfigureSpec(
	string Key,
	string Label,
	PackageConfigureType Type = PackageConfigureType.Dbref,
	string? Default = null);

/// <summary>Value types a configure parameter may declare.</summary>
public enum PackageConfigureType
{
	Dbref,
	String,
	Number,
	Boolean
}

/// <summary>
/// An object the package manages. Two modes: a <em>created</em> object (the
/// package creates and owns it, with <see cref="Type"/>/<see cref="Name"/>/
/// structure), or an <em>attach</em> object (decision 20.3 cross-object
/// attribute ownership — when <see cref="Target"/> is set the object already
/// exists, e.g. <c>{{$http_handler}}</c>, and the package only manages the
/// declared attributes on it: it is never created, restructured, or
/// destroyed).
/// </summary>
/// <param name="Ref">Abstract intra-package name (lowercase); other objects reference it as <c>{{ref}}</c>.</param>
/// <param name="Type">The game object type to create (placeholder for attach objects).</param>
/// <param name="Name">In-game object name (empty for attach objects).</param>
/// <param name="Target">Attach mode: an existing object (<c>{{$well_known}}</c> or <c>{{?configure}}</c>) to manage attributes on, or null for a created object.</param>
/// <param name="Parent">Optional @parent, as a symbolic ref (created objects only).</param>
/// <param name="Location">Where the object lives: required for exits (source room), optional for things/players, forbidden for rooms.</param>
/// <param name="Destination">Exit destination; exits only.</param>
/// <param name="PreviousRefs">Former ref names of this object (rename continuity across versions, decision 20.15).</param>
/// <param name="Flags">Flag names to set on the object.</param>
/// <param name="Powers">Power names to set on the object.</param>
/// <param name="Locks">Lock values keyed by lock type name.</param>
/// <param name="Attributes">Attributes to set, keyed by attribute name.</param>
public sealed record PackageObjectSpec(
	string Ref,
	PackageObjectType Type,
	string Name,
	PackageRef? Target,
	PackageRef? Parent,
	PackageRef? Location,
	PackageRef? Destination,
	IReadOnlyList<string> PreviousRefs,
	IReadOnlyList<string> Flags,
	IReadOnlyList<string> Powers,
	IReadOnlyDictionary<string, string> Locks,
	IReadOnlyDictionary<string, PackageAttributeSpec> Attributes)
{
	/// <summary>True when this entry manages attributes on an existing object rather than creating one.</summary>
	public bool IsAttach => Target is not null;
}

/// <summary>Game object types a package may create.</summary>
public enum PackageObjectType
{
	Thing,
	Room,
	Exit,
	Player
}

/// <summary>An attribute a package sets on an object.</summary>
/// <param name="Value">Attribute value; may contain symbolic refs resolved at apply time.</param>
/// <param name="Flags">Attribute flag names to set.</param>
public sealed record PackageAttributeSpec(string Value, IReadOnlyList<string> Flags);
