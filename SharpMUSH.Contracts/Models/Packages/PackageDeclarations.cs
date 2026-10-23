using System.Text.Json;
using System.Text.Json.Serialization;
using SharpMUSH.Library.Authorization;

namespace SharpMUSH.Library.Models.Packages;

/// <summary>
/// The roles, custom permissions and categories a package declares (manifest format 1.2): the
/// <c>categories:</c>, <c>permissions:</c> and <c>roles:</c> blocks; and the standard attribute entries of the
/// <c>attributes:</c> block (format 1.5), which are removed with the package whatever relies on them; and the
/// permissions it allows on roles it does not declare (<c>grants:</c>, format 1.15); and the properties its wiki pages'
/// facts state (<c>wiki_properties:</c>, format 1.17). An item the
/// package creates is the package's: an upgrade keeps it in step with the manifest and an uninstall
/// removes it, unless the game has come to rely on it (a role someone holds, a permission a role sets,
/// a category with something in it), which is then kept and given up. An item that already exists when
/// the package arrives is the game's and the package leaves it alone.
/// </summary>
/// <param name="RoleCategories">Role categories (<c>categories.roles</c>).</param>
/// <param name="PermissionCategories">Permission categories (<c>categories.permissions</c>).</param>
/// <param name="Permissions">Custom permissions, such as <c>scene.close</c>.</param>
/// <param name="Roles">Roles, each setting only custom permissions.</param>
/// <param name="Attributes">
/// Standard attribute entries (<c>attributes:</c>, format 1.5), as <c>@attribute</c> makes them; null for none (see
/// <see cref="AttributeEntries"/>). Null in a record stored before format 1.5.
/// </param>
/// <param name="Grants">
/// The package's own permissions it allows on roles the game already has, such as <c>wizard</c> (<c>grants:</c>,
/// format 1.15); null for none (see <see cref="RoleGrants"/>).
/// </param>
/// <param name="WikiProperties">
/// The wiki fact properties the package defines (<c>wiki_properties:</c>, format 1.17); null for none (see
/// <see cref="WikiFactProperties"/>).
/// </param>
public sealed record PackageDeclarations(
	IReadOnlyList<PackageCategorySpec> RoleCategories,
	IReadOnlyList<PackageCategorySpec> PermissionCategories,
	IReadOnlyList<PackagePermissionSpec> Permissions,
	IReadOnlyList<PackageRoleSpec> Roles,
	IReadOnlyList<PackageAttributeEntrySpec>? Attributes = null,
	IReadOnlyList<PackageGrantSpec>? Grants = null,
	IReadOnlyList<PackageWikiPropertySpec>? WikiProperties = null)
{
	/// <summary>A package that declares nothing.</summary>
	public static PackageDeclarations None { get; } = new([], [], [], []);

	private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

	/// <summary>The attribute entries the package declares; never null.</summary>
	[JsonIgnore]
	public IReadOnlyList<PackageAttributeEntrySpec> AttributeEntries => Attributes ?? [];

	/// <summary>The grants the package declares; never null.</summary>
	[JsonIgnore]
	public IReadOnlyList<PackageGrantSpec> RoleGrants => Grants ?? [];

	/// <summary>The wiki fact properties the package defines; never null.</summary>
	[JsonIgnore]
	public IReadOnlyList<PackageWikiPropertySpec> WikiFactProperties => WikiProperties ?? [];

	/// <summary>True when the package declares no category, permission, role, attribute entry, grant or wiki property.</summary>
	[JsonIgnore]
	public bool IsEmpty => RoleCategories.Count == 0 && PermissionCategories.Count == 0
		&& Permissions.Count == 0 && Roles.Count == 0 && AttributeEntries.Count == 0 && RoleGrants.Count == 0
		&& WikiFactProperties.Count == 0;

	/// <summary>The categories in the list <paramref name="kind"/>.</summary>
	public IReadOnlyList<PackageCategorySpec> Categories(CategoryKind kind)
		=> kind == CategoryKind.Role ? RoleCategories : PermissionCategories;

	/// <summary>The stored form, as a registry row keeps it.</summary>
	public string ToJson() => JsonSerializer.Serialize(this, Json);

	/// <summary>Reads what <see cref="ToJson"/> wrote.</summary>
	public static PackageDeclarations FromJson(string json) => JsonSerializer.Deserialize<PackageDeclarations>(json, Json) ?? None;
}

/// <summary>A role or permission category a package declares.</summary>
/// <param name="Name">The name, as <see cref="Authorization.Categories.IsValidName"/> allows; matched without case.</param>
/// <param name="Description">What belongs in it.</param>
public sealed record PackageCategorySpec(string Name, string Description);

/// <summary>A custom permission a package declares.</summary>
/// <param name="Name">The scope, lowercase (<see cref="CustomPermissions.IsValidName"/>).</param>
/// <param name="Category">The permission category it goes in: declared by the package, or already in the game.</param>
/// <param name="Description">What holding it lets someone do.</param>
public sealed record PackagePermissionSpec(string Name, string Category, string Description);

/// <summary>
/// A role a package declares. It sets only custom permissions: a built-in permission is the game's to
/// hand out, with <c>@role</c>, so installing a package never grants staff powers.
/// </summary>
/// <param name="Slug">The short name <c>roles()</c> lists and <c>hasrole()</c> takes.</param>
/// <param name="Name">The display name.</param>
/// <param name="Category">The role category it goes in: declared by the package, or already in the game.</param>
/// <param name="Color">A hex colour such as <c>#5aa9ff</c>, or null.</param>
/// <param name="Priority">Its place in the role order, from 1 to one below the wizard role's.</param>
/// <param name="Permissions">The custom permissions it allows or denies.</param>
public sealed record PackageRoleSpec(
	string Slug,
	string Name,
	string Category,
	string? Color,
	int Priority,
	IReadOnlyDictionary<string, PermissionState> Permissions);

/// <summary>
/// A permission a package allows on a role it does not declare (the <c>grants:</c> block, format 1.15), such as
/// <c>softcode.jobs.admin</c> on <c>wizard</c>. The permission is one the package declares. The apply allows it only
/// where the role leaves it unset, and the package's uninstall clears only what its apply set.
/// </summary>
/// <param name="Role">The role's slug.</param>
/// <param name="Permission">The custom permission, lowercase.</param>
public sealed record PackageGrantSpec(string Role, string Permission);

/// <summary>
/// A property a package's wiki facts state (the <c>wiki_properties:</c> block, format 1.17), as <c>@wiki/property</c>
/// defines it. Removing it leaves the facts pages state with it in place.
/// </summary>
/// <param name="Id">The property key, such as <c>pf2e:level</c>.</param>
/// <param name="Type">number, boolean, page or keyword.</param>
/// <param name="Multiple">Whether a page may state more than one value.</param>
/// <param name="Values">For a keyword property, the values it allows, lowercase; empty for any.</param>
/// <param name="Label">What the portal calls it, or empty.</param>
/// <param name="Description">What it records, or empty.</param>
public sealed record PackageWikiPropertySpec(
	string Id,
	string Type,
	bool Multiple,
	IReadOnlyList<string> Values,
	string Label = "",
	string Description = "")
{
	/// <summary>Whether <paramref name="other"/> holds the same values, the lists compared by content.</summary>
	public bool SameAs(PackageWikiPropertySpec other)
		=> Id == other.Id && Type == other.Type && Multiple == other.Multiple && Values.SequenceEqual(other.Values)
			&& Label == other.Label && Description == other.Description;
}

/// <summary>
/// A standard attribute entry a package declares (the <c>attributes:</c> block, format 1.5): what
/// <c>@attribute/access</c>, <c>@attribute/limit</c> and <c>@attribute/enum</c> would make. An entry only sets the
/// default flags a new copy of the attribute gets and checks the values written to it.
/// </summary>
/// <param name="Name">The attribute name, upper case; branches joined by <c>`</c>.</param>
/// <param name="Flags">The default attribute flags, lower case.</param>
/// <param name="Limit">The caseless regular expression every new value must match, or null.</param>
/// <param name="Enum">The choices a new value must name, or null. An entry has a limit or an enum, not both.</param>
/// <param name="EnumDelimiter">The character the choices are joined with, as <c>@attribute/enum &lt;delim&gt;</c> takes it.</param>
/// <param name="Retroactive">
/// Whether writing the default flags also gives them to every copy of the attribute already on an object, as
/// <c>@attribute/access/retroactive</c> does; otherwise they reach new copies only.
/// </param>
/// <param name="Index">
/// The index on the attribute's values, as <c>@attribute/index</c> puts it (<c>index:</c>), or null for none. Applying
/// it builds the index over the copies already on objects, in the background.
/// </param>
/// <param name="Watch">
/// The pattern naming the event handler's attributes that run when a copy of the attribute changes, as
/// <c>@attribute/watch</c> sets it (<c>watch:</c>, format 1.9), upper case, or null when the attribute is not watched.
/// </param>
public sealed record PackageAttributeEntrySpec(
	string Name,
	IReadOnlyList<string> Flags,
	string? Limit = null,
	IReadOnlyList<string>? Enum = null,
	char EnumDelimiter = ' ',
	bool Retroactive = false,
	AttributeIndexOptions? Index = null,
	string? Watch = null)
{
	/// <summary>Whether <paramref name="other"/> holds the same values, the lists compared by content.</summary>
	public bool SameAs(PackageAttributeEntrySpec other)
		=> Name == other.Name && Flags.SequenceEqual(other.Flags) && Limit == other.Limit
			&& (Enum ?? []).SequenceEqual(other.Enum ?? []) && (Enum is null) == (other.Enum is null)
			&& EnumDelimiter == other.EnumDelimiter && Retroactive == other.Retroactive && Index == other.Index
			&& Watch == other.Watch;
}

/// <summary>What happens to one declared item in a plan.</summary>
public enum PackageDeclarationAction
{
	/// <summary>It does not exist; the package creates it and it is the package's.</summary>
	Create,

	/// <summary>It is the package's and the new version changes it.</summary>
	Update,

	/// <summary>It is the package's and stays as it is.</summary>
	NoChange,

	/// <summary>The game already has it; the package uses it as it is and never changes or removes it.</summary>
	Existing,

	/// <summary>It is the package's and the new version drops it; it is removed.</summary>
	Remove,

	/// <summary>The new version drops it but the game relies on it; it is kept and is no longer the package's.</summary>
	Release,

	/// <summary>It cannot be applied (it belongs to another package, or names a category or permission there is none of).</summary>
	Blocked
}

/// <summary>The kind of item a <see cref="PackageDeclarationChange"/> describes.</summary>
public enum PackageDeclarationKind
{
	RoleCategory,
	PermissionCategory,
	Permission,
	Role,

	/// <summary>A standard attribute entry (format 1.5).</summary>
	Attribute,

	/// <summary>A permission allowed on a role the package does not declare (format 1.15).</summary>
	Grant,

	/// <summary>A wiki fact property (format 1.17).</summary>
	WikiProperty
}

/// <summary>How messages name a <see cref="PackageDeclarationKind"/>.</summary>
public static class PackageDeclarationKindNames
{
	/// <summary>"role category", "permission category", "permission", "attribute entry", "grant", "wiki property" or "role".</summary>
	public static string Noun(this PackageDeclarationKind kind) => kind switch
	{
		PackageDeclarationKind.RoleCategory => "role category",
		PackageDeclarationKind.PermissionCategory => "permission category",
		PackageDeclarationKind.Permission => "permission",
		PackageDeclarationKind.Attribute => "attribute entry",
		PackageDeclarationKind.Grant => "grant",
		PackageDeclarationKind.WikiProperty => "wiki property",
		_ => "role"
	};
}

/// <summary>One declared item's action in a plan, shown on the review screen and by <c>@package</c>.</summary>
/// <param name="Kind">What sort of item it is.</param>
/// <param name="Name">
/// The category name, permission scope, role slug, attribute name or wiki property key; for a grant,
/// <c>&lt;role&gt;: &lt;permission&gt;</c>.
/// </param>
/// <param name="Action">What the apply does with it.</param>
/// <param name="Detail">Why, or what changes, in a sentence; null when the action says it all.</param>
/// <param name="Retroactive">
/// For an attribute entry declared <c>retroactive</c> whose default flags the apply writes: how many copies of
/// the attribute already on objects get them. Null otherwise. The installer confirms before such an apply.
/// </param>
public sealed record PackageDeclarationChange(
	PackageDeclarationKind Kind,
	string Name,
	PackageDeclarationAction Action,
	string? Detail = null,
	int? Retroactive = null);
