using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>
/// Storage for roles, their assignments to accounts and to game objects, and permission overrides on
/// both. Implemented by every database provider; system data that travels with backups. Rules about
/// who may change what live in <c>IRoleManagementService</c>, not here.
/// Roles are keyed by <see cref="SharpRole.Slug"/>; assignments link an account id or an object's
/// dbref number to a role slug. An object's assignments and overrides are removed with the object.
/// Single-fetch returns <see cref="Found{T}"/>, matching the other registries.
/// </summary>
public interface IRoleRegistryService
{
	/// <summary>Creates or replaces a role (keyed by <see cref="SharpRole.Slug"/>).</summary>
	Task UpsertRoleAsync(SharpRole role);

	/// <summary>Fetches one role by slug.</summary>
	Task<Found<SharpRole>> GetRoleAsync(string slug);

	/// <summary>Token-aware read. Existing plugin providers retain their original method slot;
	/// built-in providers override this overload to cancel their underlying database operation.</summary>
	async Task<Found<SharpRole>> GetRoleAsync(string slug, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		return await GetRoleAsync(slug).WaitAsync(cancellationToken);
	}

	/// <summary>Lists all roles, ordered by <see cref="SharpRole.Priority"/> descending then slug.</summary>
	Task<IReadOnlyList<SharpRole>> GetRolesAsync(CancellationToken cancellationToken = default);

	/// <summary>Removes a role by slug. Does not error if absent. (Callers must guard system roles.)</summary>
	Task RemoveRoleAsync(string slug);

	/// <summary>Assigns a role (by slug) to an account (by id). Idempotent.</summary>
	Task AssignRoleToAccountAsync(string accountId, string roleSlug);

	/// <summary>Removes a role assignment from an account. Does not error if absent.</summary>
	Task RemoveRoleFromAccountAsync(string accountId, string roleSlug);

	/// <summary>The roles explicitly assigned to an account (excludes the implicit ones).</summary>
	Task<IReadOnlyList<SharpRole>> GetRolesForAccountAsync(string accountId, CancellationToken cancellationToken = default);

	/// <summary>The account ids a role is assigned to.</summary>
	Task<IReadOnlyList<string>> GetAccountIdsForRoleAsync(string roleSlug);

	/// <summary>
	/// An account's overrides (Discord's member overwrite): scope → Allow or Deny. Scopes left on
	/// Inherit are absent.
	/// </summary>
	Task<IReadOnlyDictionary<string, PermissionState>> GetAccountOverridesAsync(string accountId, CancellationToken cancellationToken = default);

	/// <summary>Sets one per-account override; <see cref="PermissionState.Inherit"/> removes it.</summary>
	Task SetAccountOverrideAsync(string accountId, string scope, PermissionState state);

	/// <summary>The role slugs assigned to the object with dbref number <paramref name="number"/>.</summary>
	Task<IReadOnlyList<string>> GetObjectRolesAsync(int number, CancellationToken cancellationToken = default);

	/// <summary>Assigns a role (by slug) to an object. Idempotent.</summary>
	Task AssignRoleToObjectAsync(int number, string roleSlug);

	/// <summary>Removes a role from an object. Does not error if absent.</summary>
	Task RemoveRoleFromObjectAsync(int number, string roleSlug);

	/// <summary>The dbref numbers of the objects a role is assigned to.</summary>
	Task<IReadOnlyList<int>> GetObjectsForRoleAsync(string roleSlug, CancellationToken cancellationToken = default);

	/// <summary>
	/// The overrides set on an object (PennMUSH's powers are these): scope → Allow or Deny. Scopes left
	/// on Inherit are absent.
	/// </summary>
	Task<IReadOnlyDictionary<string, PermissionState>> GetObjectOverridesAsync(int number, CancellationToken cancellationToken = default);

	/// <summary>Sets one override on an object; <see cref="PermissionState.Inherit"/> removes it.</summary>
	Task SetObjectOverrideAsync(int number, string scope, PermissionState state);

	/// <summary>The custom permissions the world defines, ordered by scope.</summary>
	Task<IReadOnlyList<CustomPermission>> GetCustomPermissionsAsync(CancellationToken cancellationToken = default);

	/// <summary>Defines a custom permission, or replaces its description (keyed by <see cref="CustomPermission.Scope"/>).</summary>
	Task UpsertCustomPermissionAsync(CustomPermission permission);

	/// <summary>Every account and object override of <paramref name="scope"/>, as <see cref="RemoveCustomPermissionAsync"/> would clear them.</summary>
	Task<PermissionOverrideHolders> GetOverridesOfAsync(string scope, CancellationToken cancellationToken = default);

	/// <summary>
	/// Removes a custom permission and every setting of it: on roles, on accounts and on objects, in one
	/// write. Does not error if absent.
	/// </summary>
	Task RemoveCustomPermissionAsync(string scope);

	/// <summary>The categories in the list <paramref name="kind"/>, ordered by name.</summary>
	Task<IReadOnlyList<RoleCategory>> GetCategoriesAsync(CategoryKind kind, CancellationToken cancellationToken = default);

	/// <summary>Creates a category in the list <paramref name="kind"/>, or replaces its description (keyed by <see cref="RoleCategory.Name"/> without case).</summary>
	Task UpsertCategoryAsync(CategoryKind kind, RoleCategory category);

	/// <summary>
	/// Renames the category <paramref name="name"/> in the list <paramref name="kind"/> to <paramref name="renamed"/>
	/// and moves every role (or, for <see cref="CategoryKind.Permission"/>, every custom permission) in it, in one write.
	/// </summary>
	Task RenameCategoryAsync(CategoryKind kind, string name, RoleCategory renamed);

	/// <summary>Removes a category from the list <paramref name="kind"/>. Does not error if absent. (Callers must check nothing is in it.)</summary>
	Task RemoveCategoryAsync(CategoryKind kind, string name);
}
