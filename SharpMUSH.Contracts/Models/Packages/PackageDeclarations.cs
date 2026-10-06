using System.Text.Json;
using System.Text.Json.Serialization;
using SharpMUSH.Library.Authorization;

namespace SharpMUSH.Library.Models.Packages;

/// <summary>
/// The roles, custom permissions and categories a package declares (manifest format 1.2): the
/// <c>categories:</c>, <c>permissions:</c> and <c>roles:</c> blocks. An item the
/// package creates is the package's: an upgrade keeps it in step with the manifest and an uninstall
/// removes it, unless the game has come to rely on it (a role someone holds, a permission a role sets,
/// a category with something in it), which is then kept and given up. An item that already exists when
/// the package arrives is the game's and the package leaves it alone.
/// </summary>
/// <param name="RoleCategories">Role categories (<c>categories.roles</c>).</param>
/// <param name="PermissionCategories">Permission categories (<c>categories.permissions</c>).</param>
/// <param name="Permissions">Custom permissions, such as <c>scene.close</c>.</param>
/// <param name="Roles">Roles, each setting only custom permissions.</param>
public sealed record PackageDeclarations(
	IReadOnlyList<PackageCategorySpec> RoleCategories,
	IReadOnlyList<PackageCategorySpec> PermissionCategories,
	IReadOnlyList<PackagePermissionSpec> Permissions,
	IReadOnlyList<PackageRoleSpec> Roles)
{
	/// <summary>A package that declares nothing.</summary>
	public static PackageDeclarations None { get; } = new([], [], [], []);

	private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

	/// <summary>True when the package declares no category, permission or role.</summary>
	[JsonIgnore]
	public bool IsEmpty => RoleCategories.Count == 0 && PermissionCategories.Count == 0
		&& Permissions.Count == 0 && Roles.Count == 0;

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
	Role
}

/// <summary>How messages name a <see cref="PackageDeclarationKind"/>.</summary>
public static class PackageDeclarationKindNames
{
	/// <summary>"role category", "permission category", "permission" or "role".</summary>
	public static string Noun(this PackageDeclarationKind kind) => kind switch
	{
		PackageDeclarationKind.RoleCategory => "role category",
		PackageDeclarationKind.PermissionCategory => "permission category",
		PackageDeclarationKind.Permission => "permission",
		_ => "role"
	};
}

/// <summary>One declared item's action in a plan, shown on the review screen and by <c>@package</c>.</summary>
/// <param name="Kind">What sort of item it is.</param>
/// <param name="Name">The category name, permission scope or role slug.</param>
/// <param name="Action">What the apply does with it.</param>
/// <param name="Detail">Why, or what changes, in a sentence; null when the action says it all.</param>
public sealed record PackageDeclarationChange(
	PackageDeclarationKind Kind,
	string Name,
	PackageDeclarationAction Action,
	string? Detail = null);
