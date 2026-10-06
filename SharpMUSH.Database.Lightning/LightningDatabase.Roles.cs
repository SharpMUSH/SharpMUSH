using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Database.Lightning.Records;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Plugins.Storage.Lightning;

namespace SharpMUSH.Database.Lightning;

/// <summary>
/// <see cref="Library.Services.Interfaces.IRoleRegistryService"/>: roles (Discord-style RBAC), their
/// assignments and overrides. Roles are keyed by <see cref="SharpRole.Slug"/> in <see cref="Tables.Role"/>;
/// an account assignment is a duplicate entry in <see cref="Tables.AccountRole"/> keyed by account id
/// with the role slug as the duplicate value — idempotent for free, since LMDB's dupsort tables collapse
/// an exact (key, value) pair written twice. An object assignment is the <see cref="Tables.ObjRole"/>
/// edge pair keyed by dbref, so the object-delete cascade removes it; an object's overrides are one
/// <see cref="Tables.ObjPermission"/> record, deleted with the object.
/// </summary>
public partial class LightningDatabase
{
	private static RoleRecord ToRoleRecord(SharpRole role) => new()
	{
		Slug = role.Slug,
		Name = role.Name,
		Category = role.Category,
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
		Category = r.Category,
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

	public Task<Found<SharpRole>> GetRoleAsync(string slug)
		=> GetRoleAsync(slug, CancellationToken.None);

	public Task<Found<SharpRole>> GetRoleAsync(string slug, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		var role = Store.Read(tx => ReadRoleBySlug(tx, slug));
		cancellationToken.ThrowIfCancellationRequested();
		return Task.FromResult<Found<SharpRole>>(role is null ? new NotFound() : role);
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

	public Task<IReadOnlyDictionary<string, PermissionState>> GetAccountOverridesAsync(string accountId, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		var key = ParseAccountId(accountId);
		var overrides = Store.Read(tx => ReadOverrides(tx, key));
		return Task.FromResult<IReadOnlyDictionary<string, PermissionState>>(
			overrides.ToDictionary(kvp => kvp.Key, kvp => (PermissionState)kvp.Value, StringComparer.OrdinalIgnoreCase));
	}

	public async Task SetAccountOverrideAsync(string accountId, string scope, PermissionState state)
	{
		var key = ParseAccountId(accountId);
		await Store.WriteAsync(tx =>
		{
			var overrides = ReadOverrides(tx, key);
			if (state == PermissionState.Inherit) overrides.Remove(scope);
			else overrides[scope] = (int)state;
			if (overrides.Count == 0) tx.Delete(Tables.AccountPermission, Keys.Str(key));
			else tx.Put(Tables.AccountPermission, Keys.Str(key), Codec.Serialize(overrides));
		});
	}

	private static Dictionary<string, int> ReadOverrides(ITx tx, string accountKey)
		=> tx.TryGet(Tables.AccountPermission, Keys.Str(accountKey), out var bytes)
			? new Dictionary<string, int>(Codec.Deserialize<Dictionary<string, int>>(bytes), StringComparer.OrdinalIgnoreCase)
			: new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

	public Task<IReadOnlyList<string>> GetObjectRolesAsync(int number, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		return Task.FromResult<IReadOnlyList<string>>(Store.Read(tx => ReadObjectRoles(tx, number)));
	}

	internal static List<string> ReadObjectRoles(ITx tx, long number)
		=> tx.Dups(Tables.ObjRole.Forward, Keys.Dbref(number)).Select(v => Keys.ReadStr(v)).ToList();

	public async Task AssignRoleToObjectAsync(int number, string roleSlug)
		=> await Store.WriteAsync(tx => PutObjectRole(tx, number, roleSlug));

	internal static void PutObjectRole(ITx tx, long number, string roleSlug)
	{
		var slug = Keys.Str(roleSlug.ToLowerInvariant());
		tx.Put(Tables.ObjRole.Forward, Keys.Dbref(number), slug);
		tx.Put(Tables.ObjRole.Reverse, slug, Keys.Dbref(number));
	}

	public async Task RemoveRoleFromObjectAsync(int number, string roleSlug)
		=> await Store.WriteAsync(tx => DeleteObjectRole(tx, number, roleSlug));

	internal static void DeleteObjectRole(ITx tx, long number, string roleSlug)
	{
		var slug = Keys.Str(roleSlug.ToLowerInvariant());
		tx.Delete(Tables.ObjRole.Forward, Keys.Dbref(number), slug);
		tx.Delete(Tables.ObjRole.Reverse, slug, Keys.Dbref(number));
	}

	public Task<IReadOnlyList<int>> GetObjectsForRoleAsync(string roleSlug, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		var slug = Keys.Str(roleSlug.ToLowerInvariant());
		var numbers = Store.Read(tx => tx.Dups(Tables.ObjRole.Reverse, slug).Select(v => (int)Keys.ReadDbref(v)).ToList());
		return Task.FromResult<IReadOnlyList<int>>(numbers);
	}

	public Task<IReadOnlyDictionary<string, PermissionState>> GetObjectOverridesAsync(int number, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		var overrides = Store.Read(tx => ReadObjectOverrides(tx, number));
		return Task.FromResult<IReadOnlyDictionary<string, PermissionState>>(
			overrides.ToDictionary(kvp => kvp.Key, kvp => (PermissionState)kvp.Value, StringComparer.OrdinalIgnoreCase));
	}

	public async Task SetObjectOverrideAsync(int number, string scope, PermissionState state)
		=> await Store.WriteAsync(tx => WriteObjectOverride(tx, number, scope, state));

	internal static void WriteObjectOverride(ITx tx, long number, string scope, PermissionState state)
	{
		var overrides = ReadObjectOverrides(tx, number);
		if (state == PermissionState.Inherit) overrides.Remove(scope);
		else overrides[scope] = (int)state;
		if (overrides.Count == 0) tx.Delete(Tables.ObjPermission, Keys.Dbref(number));
		else tx.Put(Tables.ObjPermission, Keys.Dbref(number), Codec.Serialize(overrides));
	}

	internal static Dictionary<string, int> ReadObjectOverrides(ITx tx, long number)
		=> tx.TryGet(Tables.ObjPermission, Keys.Dbref(number), out var bytes)
			? new Dictionary<string, int>(Codec.Deserialize<Dictionary<string, int>>(bytes), StringComparer.OrdinalIgnoreCase)
			: new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

	public Task<IReadOnlyList<CustomPermission>> GetCustomPermissionsAsync(CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		var permissions = Store.Read(tx => tx.Range(Tables.CustomPermission, [])
			.Select(e => Codec.Deserialize<CustomPermissionRecord>(e.Value))
			.Select(r => new CustomPermission(r.Scope, r.Category, r.Description, r.CreatedAt))
			.OrderBy(p => p.Scope, StringComparer.Ordinal)
			.ToList());
		return Task.FromResult<IReadOnlyList<CustomPermission>>(permissions);
	}

	public async Task UpsertCustomPermissionAsync(CustomPermission permission)
		=> await Store.WriteAsync(tx => tx.Put(Tables.CustomPermission, Keys.Str(permission.Scope), Codec.Serialize(new CustomPermissionRecord
		{
			Scope = permission.Scope,
			Category = permission.Category,
			Description = permission.Description,
			CreatedAt = permission.CreatedAt
		})));

	public Task<IReadOnlyList<RoleCategory>> GetCategoriesAsync(CategoryKind kind, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		var categories = Store.Read(tx => tx.Range(CategoryTable(kind), [])
			.Select(e => MapCategory(Codec.Deserialize<RoleCategoryRecord>(e.Value)))
			.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
			.ToList());
		return Task.FromResult<IReadOnlyList<RoleCategory>>(categories);
	}

	public async Task UpsertCategoryAsync(CategoryKind kind, RoleCategory category)
		=> await Store.WriteAsync(tx => PutCategory(tx, kind, category));

	public async Task RenameCategoryAsync(CategoryKind kind, string name, RoleCategory renamed)
		=> await Store.WriteAsync(tx =>
		{
			tx.Delete(CategoryTable(kind), CategoryKey(name));
			PutCategory(tx, kind, renamed);

			if (kind == CategoryKind.Role)
				foreach (var (key, value) in tx.Range(Tables.Role, []).ToList())
				{
					var role = Codec.Deserialize<RoleRecord>(value);
					if (string.Equals(role.Category, name, StringComparison.OrdinalIgnoreCase))
						tx.Put(Tables.Role, key, Codec.Serialize(role with { Category = renamed.Name }));
				}
			else
				foreach (var (key, value) in tx.Range(Tables.CustomPermission, []).ToList())
				{
					var permission = Codec.Deserialize<CustomPermissionRecord>(value);
					if (string.Equals(permission.Category, name, StringComparison.OrdinalIgnoreCase))
						tx.Put(Tables.CustomPermission, key, Codec.Serialize(permission with { Category = renamed.Name }));
				}
		});

	public async Task RemoveCategoryAsync(CategoryKind kind, string name)
		=> await Store.WriteAsync(tx => tx.Delete(CategoryTable(kind), CategoryKey(name)));

	internal static TableDef CategoryTable(CategoryKind kind)
		=> kind == CategoryKind.Role ? Tables.RoleCategory : Tables.PermissionCategory;

	internal static byte[] CategoryKey(string name) => Keys.Str(name.ToLowerInvariant());

	internal static void PutCategory(ITx tx, CategoryKind kind, RoleCategory category)
		=> tx.Put(CategoryTable(kind), CategoryKey(category.Name), Codec.Serialize(new RoleCategoryRecord
		{
			Name = category.Name,
			Description = category.Description,
			CreatedAt = category.CreatedAt
		}));

	private static RoleCategory MapCategory(RoleCategoryRecord r) => new(r.Name, r.Description, r.CreatedAt);

	public Task<PermissionOverrideHolders> GetOverridesOfAsync(string scope, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		return Task.FromResult(Store.Read(tx =>
		{
			var accounts = new Dictionary<string, PermissionState>(StringComparer.Ordinal);
			foreach (var (key, value) in tx.Range(Tables.AccountPermission, []))
			{
				if (StateOf(value, scope) is { } state) accounts[$"node_accounts/{Keys.ReadStr(key)}"] = state;
			}

			var objects = new Dictionary<int, PermissionState>();
			foreach (var (key, value) in tx.Range(Tables.ObjPermission, []))
			{
				if (StateOf(value, scope) is { } state) objects[(int)Keys.ReadDbref(key)] = state;
			}

			return new PermissionOverrideHolders(accounts, objects);
		}));
	}

	private static PermissionState? StateOf(byte[] overrides, string scope)
		=> new Dictionary<string, int>(Codec.Deserialize<Dictionary<string, int>>(overrides), StringComparer.OrdinalIgnoreCase)
			.TryGetValue(scope, out var state) ? (PermissionState)state : null;

	public async Task RemoveCustomPermissionAsync(string scope)
		=> await Store.WriteAsync(tx =>
		{
			tx.Delete(Tables.CustomPermission, Keys.Str(scope));

			foreach (var (key, value) in tx.Range(Tables.Role, []).ToList())
			{
				var role = Codec.Deserialize<RoleRecord>(value);
				var kept = role.Permissions.Where(p => !string.Equals(p.Key, scope, StringComparison.OrdinalIgnoreCase))
					.ToDictionary(p => p.Key, p => p.Value);
				if (kept.Count != role.Permissions.Count) tx.Put(Tables.Role, key, Codec.Serialize(role with { Permissions = kept }));
			}

			foreach (var table in new[] { Tables.AccountPermission, Tables.ObjPermission })
				foreach (var (key, value) in tx.Range(table, []).ToList())
				{
					var overrides = new Dictionary<string, int>(Codec.Deserialize<Dictionary<string, int>>(value), StringComparer.OrdinalIgnoreCase);
					if (!overrides.Remove(scope)) continue;
					if (overrides.Count == 0) tx.Delete(table, key);
					else tx.Put(table, key, Codec.Serialize(overrides));
				}
		});
}
