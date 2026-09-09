using OneOf;
using OneOf.Types;
using SharpMUSH.Database.Lightning.Records;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Plugins.Storage.Lightning;

namespace SharpMUSH.Database.Lightning;

/// <summary>
/// <see cref="Library.Services.Interfaces.IRoleRegistryService"/>: portal roles (Discord-style RBAC)
/// and account-role assignments. Ported from <c>SurrealDatabase.Roles.cs</c>. Roles are keyed by
/// <see cref="SharpRole.Slug"/> in <see cref="Tables.Role"/>; an assignment is a duplicate entry in
/// <see cref="Tables.AccountRole"/> keyed by account id with the role slug as the duplicate value —
/// idempotent for free, since LMDB's dupsort tables collapse an exact (key, value) pair written twice.
/// </summary>
public partial class LightningDatabase
{
	private static RoleRecord ToRoleRecord(SharpRole role) => new()
	{
		Slug = role.Slug,
		Name = role.Name,
		Color = role.Color,
		Priority = role.Priority,
		IsSystem = role.IsSystem,
		Permissions = role.Permissions.ToDictionary(kvp => kvp.Key, kvp => (int)kvp.Value),
		CreatedAt = role.CreatedAt,
		UpdatedAt = role.UpdatedAt
	};

	private static SharpRole MapRole(RoleRecord r) => new()
	{
		Id = $"node_roles/{r.Slug}",
		Slug = r.Slug,
		Name = r.Name,
		Color = r.Color,
		Priority = r.Priority,
		IsSystem = r.IsSystem,
		Permissions = r.Permissions.ToDictionary(kvp => kvp.Key, kvp => (PermissionState)kvp.Value),
		CreatedAt = r.CreatedAt,
		UpdatedAt = r.UpdatedAt
	};

	private static SharpRole? ReadRoleBySlug(ITx tx, string slug)
		=> tx.TryGet(Tables.Role, Keys.Str(slug), out var bytes) ? MapRole(Codec.Deserialize<RoleRecord>(bytes)) : null;

	public async Task UpsertRoleAsync(SharpRole role)
		=> await Store.WriteAsync(tx => tx.Put(Tables.Role, Keys.Str(role.Slug), Codec.Serialize(ToRoleRecord(role))));

	public Task<OneOf<SharpRole, NotFound>> GetRoleAsync(string slug)
	{
		var role = Store.Read(tx => ReadRoleBySlug(tx, slug));
		return Task.FromResult<OneOf<SharpRole, NotFound>>(role is null ? new NotFound() : role);
	}

	public Task<IReadOnlyList<SharpRole>> GetRolesAsync(CancellationToken cancellationToken = default)
	{
		var roles = Store.Read(tx => tx.Range(Tables.Role, [])
			.Select(e => MapRole(Codec.Deserialize<RoleRecord>(e.Value)))
			.OrderByDescending(r => r.Priority)
			.ThenBy(r => r.Slug, StringComparer.Ordinal)
			.ToList());
		return Task.FromResult<IReadOnlyList<SharpRole>>(roles);
	}

	public async Task RemoveRoleAsync(string slug)
		=> await Store.WriteAsync(tx => tx.Delete(Tables.Role, Keys.Str(slug)));

	public async Task AssignRoleToAccountAsync(string accountId, string roleSlug)
	{
		var key = ParseAccountId(accountId);
		await Store.WriteAsync(tx => tx.Put(Tables.AccountRole, Keys.Str(key), Keys.Str(roleSlug)));
	}

	public async Task RemoveRoleFromAccountAsync(string accountId, string roleSlug)
	{
		var key = ParseAccountId(accountId);
		await Store.WriteAsync(tx => tx.Delete(Tables.AccountRole, Keys.Str(key), Keys.Str(roleSlug)));
	}

	public Task<IReadOnlyList<SharpRole>> GetRolesForAccountAsync(string accountId, CancellationToken cancellationToken = default)
	{
		var key = ParseAccountId(accountId);
		var roles = Store.Read(tx => tx.Dups(Tables.AccountRole, Keys.Str(key))
			.Select(v => Keys.ReadStr(v))
			.Select(slug => ReadRoleBySlug(tx, slug))
			.Where(role => role is not null)
			.Select(role => role!)
			.OrderByDescending(r => r.Priority)
			.ThenBy(r => r.Slug, StringComparer.Ordinal)
			.ToList());
		return Task.FromResult<IReadOnlyList<SharpRole>>(roles);
	}

	public Task<IReadOnlyList<string>> GetAccountIdsForRoleAsync(string roleSlug)
	{
		var slug = Keys.Str(roleSlug);
		var accountIds = Store.Read(tx => tx.Range(Tables.AccountRole, [])
			.Where(e => e.Value.AsSpan().SequenceEqual(slug))
			.Select(e => $"node_accounts/{Keys.ReadStr(e.Key)}")
			.ToList());
		return Task.FromResult<IReadOnlyList<string>>(accountIds);
	}
}
