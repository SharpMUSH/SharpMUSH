using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Authentication;

/// <summary>A role registry held in memory, for tests of the rules above the provider.</summary>
internal sealed class InMemoryRoleRegistry : IRoleRegistryService
{
	private readonly Dictionary<string, SharpRole> _roles = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, HashSet<string>> _assignments = new();
	private readonly Dictionary<string, Dictionary<string, PermissionState>> _overrides = new();
	private readonly Dictionary<int, HashSet<string>> _objectRoles = new();
	private readonly Dictionary<int, Dictionary<string, PermissionState>> _objectOverrides = new();
	private readonly Dictionary<string, CustomPermission> _custom = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<CategoryKind, Dictionary<string, RoleCategory>> _categories = new()
	{
		[CategoryKind.Role] = new(StringComparer.OrdinalIgnoreCase),
		[CategoryKind.Permission] = new(StringComparer.OrdinalIgnoreCase)
	};

	/// <summary>A registry seeded like a new world: system and starter roles.</summary>
	public static InMemoryRoleRegistry Seeded()
	{
		var registry = new InMemoryRoleRegistry();
		foreach (var role in BuiltInRoles.All.Concat(BuiltInRoles.Starters))
			registry.Add(role);
		foreach (var kind in Enum.GetValues<CategoryKind>())
			foreach (var category in Categories.Seeds(kind))
				registry._categories[kind][category.Name] = category;
		return registry;
	}

	public InMemoryRoleRegistry Add(SharpRole role)
	{
		_roles[role.Slug] = new SharpRole
		{
			Slug = role.Slug, Name = role.Name, Category = role.Category, Color = role.Color, Priority = role.Priority, IsSystem = role.IsSystem,
			Permissions = new Dictionary<string, PermissionState>(role.Permissions)
		};
		return this;
	}

	public IReadOnlyCollection<string> AssignedTo(string accountId)
		=> _assignments.TryGetValue(accountId, out var set) ? set : [];

	public Task UpsertRoleAsync(SharpRole role)
	{
		_roles[role.Slug] = role;
		return Task.CompletedTask;
	}

	public Task<Found<SharpRole>> GetRoleAsync(string slug)
		=> Task.FromResult<Found<SharpRole>>(_roles.TryGetValue(slug, out var role) ? role : new NotFound());

	public Task<IReadOnlyList<SharpRole>> GetRolesAsync(CancellationToken cancellationToken = default)
		=> Task.FromResult<IReadOnlyList<SharpRole>>(_roles.Values.OrderByDescending(r => r.Priority).ThenBy(r => r.Slug).ToList());

	public Task RemoveRoleAsync(string slug)
	{
		_roles.Remove(slug);
		return Task.CompletedTask;
	}

	public Task AssignRoleToAccountAsync(string accountId, string roleSlug)
	{
		if (!_assignments.TryGetValue(accountId, out var set)) _assignments[accountId] = set = [];
		set.Add(roleSlug);
		return Task.CompletedTask;
	}

	public Task RemoveRoleFromAccountAsync(string accountId, string roleSlug)
	{
		if (_assignments.TryGetValue(accountId, out var set)) set.Remove(roleSlug);
		return Task.CompletedTask;
	}

	public Task<IReadOnlyList<SharpRole>> GetRolesForAccountAsync(string accountId, CancellationToken cancellationToken = default)
		=> Task.FromResult<IReadOnlyList<SharpRole>>(AssignedTo(accountId)
			.Select(slug => _roles.GetValueOrDefault(slug)).OfType<SharpRole>().ToList());

	public Task<IReadOnlyList<string>> GetAccountIdsForRoleAsync(string roleSlug)
		=> Task.FromResult<IReadOnlyList<string>>(_assignments.Where(a => a.Value.Contains(roleSlug)).Select(a => a.Key).ToList());

	public Task<IReadOnlyDictionary<string, PermissionState>> GetAccountOverridesAsync(string accountId, CancellationToken cancellationToken = default)
		=> Task.FromResult<IReadOnlyDictionary<string, PermissionState>>(
			_overrides.TryGetValue(accountId, out var overrides) ? new Dictionary<string, PermissionState>(overrides) : new Dictionary<string, PermissionState>());

	public Task SetAccountOverrideAsync(string accountId, string scope, PermissionState state)
	{
		if (!_overrides.TryGetValue(accountId, out var overrides)) _overrides[accountId] = overrides = new(StringComparer.OrdinalIgnoreCase);
		if (state == PermissionState.Inherit) overrides.Remove(scope);
		else overrides[scope] = state;
		return Task.CompletedTask;
	}

	public Task<IReadOnlyList<string>> GetObjectRolesAsync(int number, CancellationToken cancellationToken = default)
		=> Task.FromResult<IReadOnlyList<string>>(_objectRoles.TryGetValue(number, out var set) ? set.Order().ToList() : []);

	public Task AssignRoleToObjectAsync(int number, string roleSlug)
	{
		if (!_objectRoles.TryGetValue(number, out var set)) _objectRoles[number] = set = new(StringComparer.OrdinalIgnoreCase);
		set.Add(roleSlug);
		return Task.CompletedTask;
	}

	public Task RemoveRoleFromObjectAsync(int number, string roleSlug)
	{
		if (_objectRoles.TryGetValue(number, out var set)) set.Remove(roleSlug);
		return Task.CompletedTask;
	}

	public Task<IReadOnlyList<int>> GetObjectsForRoleAsync(string roleSlug, CancellationToken cancellationToken = default)
		=> Task.FromResult<IReadOnlyList<int>>(_objectRoles.Where(o => o.Value.Contains(roleSlug)).Select(o => o.Key).Order().ToList());

	public Task<IReadOnlyDictionary<string, PermissionState>> GetObjectOverridesAsync(int number, CancellationToken cancellationToken = default)
		=> Task.FromResult<IReadOnlyDictionary<string, PermissionState>>(
			_objectOverrides.TryGetValue(number, out var overrides) ? new Dictionary<string, PermissionState>(overrides) : new Dictionary<string, PermissionState>());

	public Task SetObjectOverrideAsync(int number, string scope, PermissionState state)
	{
		if (!_objectOverrides.TryGetValue(number, out var overrides)) _objectOverrides[number] = overrides = new(StringComparer.OrdinalIgnoreCase);
		if (state == PermissionState.Inherit) overrides.Remove(scope);
		else overrides[scope] = state;
		return Task.CompletedTask;
	}

	public Task<IReadOnlyList<CustomPermission>> GetCustomPermissionsAsync(CancellationToken cancellationToken = default)
		=> Task.FromResult<IReadOnlyList<CustomPermission>>(_custom.Values.OrderBy(p => p.Scope, StringComparer.Ordinal).ToList());

	public Task UpsertCustomPermissionAsync(CustomPermission permission)
	{
		_custom[permission.Scope] = permission;
		return Task.CompletedTask;
	}

	public Task<IReadOnlyList<RoleCategory>> GetCategoriesAsync(CategoryKind kind, CancellationToken cancellationToken = default)
		=> Task.FromResult<IReadOnlyList<RoleCategory>>(_categories[kind].Values.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToList());

	public Task UpsertCategoryAsync(CategoryKind kind, RoleCategory category)
	{
		_categories[kind].Remove(category.Name);
		_categories[kind][category.Name] = category;
		return Task.CompletedTask;
	}

	public Task RenameCategoryAsync(CategoryKind kind, string name, RoleCategory renamed)
	{
		_categories[kind].Remove(name);
		_categories[kind][renamed.Name] = renamed;
		if (kind == CategoryKind.Role)
			foreach (var role in _roles.Values.Where(r => string.Equals(r.Category, name, StringComparison.OrdinalIgnoreCase)))
				role.Category = renamed.Name;
		else
			foreach (var permission in _custom.Values.Where(p => string.Equals(p.Category, name, StringComparison.OrdinalIgnoreCase)).ToList())
				_custom[permission.Scope] = permission with { Category = renamed.Name };
		return Task.CompletedTask;
	}

	public Task RemoveCategoryAsync(CategoryKind kind, string name)
	{
		_categories[kind].Remove(name);
		return Task.CompletedTask;
	}

	public Task<PermissionOverrideHolders> GetOverridesOfAsync(string scope, CancellationToken cancellationToken = default)
		=> Task.FromResult(new PermissionOverrideHolders(
			_overrides.Where(o => o.Value.ContainsKey(scope)).ToDictionary(o => o.Key, o => o.Value[scope]),
			_objectOverrides.Where(o => o.Value.ContainsKey(scope)).ToDictionary(o => o.Key, o => o.Value[scope])));

	public Task RemoveCustomPermissionAsync(string scope)
	{
		_custom.Remove(scope);
		foreach (var role in _roles.Values)
			role.Permissions = role.Permissions.Where(p => !string.Equals(p.Key, scope, StringComparison.OrdinalIgnoreCase)).ToDictionary();
		foreach (var overrides in _overrides.Values.Concat(_objectOverrides.Values))
			foreach (var key in overrides.Keys.Where(k => string.Equals(k, scope, StringComparison.OrdinalIgnoreCase)).ToList())
				overrides.Remove(key);
		return Task.CompletedTask;
	}
}
