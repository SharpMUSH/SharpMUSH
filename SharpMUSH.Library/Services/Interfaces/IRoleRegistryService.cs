using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>
/// Storage for portal roles (Discord-style RBAC) and account↔role assignments. Implemented by
/// every database provider; system data, never visible to softcode, travels with backups.
/// Roles are keyed by <see cref="SharpRole.Slug"/>; assignments link an account id to a role slug.
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

	/// <summary>The roles explicitly assigned to an account (excludes flag-derived built-ins).</summary>
	Task<IReadOnlyList<SharpRole>> GetRolesForAccountAsync(string accountId, CancellationToken cancellationToken = default);

	/// <summary>The account ids a role is assigned to.</summary>
	Task<IReadOnlyList<string>> GetAccountIdsForRoleAsync(string roleSlug);
}
