using OneOf;
using OneOf.Types;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;
using SurrealDb.Net.Models;
using System.Text.Json;

namespace SharpMUSH.Database.SurrealDB;

// IMPORTANT: SurrealDb.Net's embedded CBOR serializer ignores [JsonPropertyName].
// Property names MUST exactly match the SurrealDB field names stored in the DB —
// hence the camelCase property names on this record.
internal class RoleDbRecord : Record
{
	public string slug { get; set; } = "";
	public string name { get; set; } = "";
	public string? color { get; set; }
	public int priority { get; set; }
	public bool isSystem { get; set; }
	public string permissionsJson { get; set; } = "{}";
	public long createdAt { get; set; }
	public long updatedAt { get; set; }
}

internal class RoleAccountRefRecord : Record;

public partial class SurrealDatabase : IRoleRegistryService
{
	#region Role Registry

	private const string RoleFields =
		"id, slug, name, color, priority, isSystem, permissionsJson, createdAt, updatedAt";

	// Same fields projected through the account_has_role edge's `out` (the role).
	private const string RoleFieldsViaEdge =
		"out.id AS id, out.slug AS slug, out.name AS name, out.color AS color, out.priority AS priority, " +
		"out.isSystem AS isSystem, out.permissionsJson AS permissionsJson, out.createdAt AS createdAt, out.updatedAt AS updatedAt";

	public async Task UpsertRoleAsync(SharpRole role)
	{
		var permissions = role.Permissions.ToDictionary(kvp => kvp.Key, kvp => (int)kvp.Value);
		var parameters = new Dictionary<string, object?>
		{
			["slug"] = role.Slug,
			["name"] = role.Name,
			["color"] = role.Color,
			["priority"] = role.Priority,
			["isSystem"] = role.IsSystem,
			["permissionsJson"] = JsonSerializer.Serialize(permissions, JsonOptions),
			["createdAt"] = role.CreatedAt,
			["updatedAt"] = role.UpdatedAt
		};
		await ExecuteAsync("""
			UPSERT type::thing('role', $slug) SET slug = $slug, name = $name, color = $color,
				priority = $priority, isSystem = $isSystem, permissionsJson = $permissionsJson,
				createdAt = $createdAt, updatedAt = $updatedAt
			""", parameters);
	}

	public async Task<OneOf<SharpRole, NotFound>> GetRoleAsync(string slug)
	{
		var response = await ExecuteAsync(
			$"SELECT {RoleFields} FROM role WHERE slug = $slug",
			new Dictionary<string, object?> { ["slug"] = slug });
		var results = response.GetValue<List<RoleDbRecord>>(0);

		return results?.Count > 0 ? MapRole(results[0]) : new NotFound();
	}

	public async Task<IReadOnlyList<SharpRole>> GetRolesAsync()
	{
		var response = await ExecuteAsync(
			$"SELECT {RoleFields} FROM role ORDER BY priority DESC, slug");
		var results = response.GetValue<List<RoleDbRecord>>(0) ?? [];
		return results.Select(MapRole).ToList();
	}

	public async Task RemoveRoleAsync(string slug)
	{
		await ExecuteAsync("DELETE type::thing('role', $slug)",
			new Dictionary<string, object?> { ["slug"] = slug });
	}

	public async Task AssignRoleToAccountAsync(string accountId, string roleSlug)
	{
		var key = NormalizeSurrealId(accountId, "account");
		var parameters = new Dictionary<string, object?>
		{
			["accountId"] = new StringRecordId(key),
			["slug"] = roleSlug
		};
		// Filter by out.slug (a field compare — avoids escaping the role record id), and resolve
		// the RELATE target via a subquery so it's a real (escaped) record id, mirroring how
		// account_owns_character relates to the player id from a subquery. Idempotent.
		await ExecuteAsync("""
			LET $r = (SELECT id FROM role WHERE slug = $slug)[0].id;
			DELETE account_has_role WHERE in = $accountId AND out.slug = $slug;
			RELATE $accountId->account_has_role->$r
			""", parameters);
	}

	public async Task RemoveRoleFromAccountAsync(string accountId, string roleSlug)
	{
		var key = NormalizeSurrealId(accountId, "account");
		var parameters = new Dictionary<string, object?>
		{
			["accountId"] = new StringRecordId(key),
			["slug"] = roleSlug
		};
		await ExecuteAsync("""
			DELETE account_has_role WHERE in = $accountId AND out.slug = $slug
			""", parameters);
	}

	public async Task<IReadOnlyList<SharpRole>> GetRolesForAccountAsync(string accountId)
	{
		var key = NormalizeSurrealId(accountId, "account");
		var parameters = new Dictionary<string, object?> { ["accountId"] = new StringRecordId(key) };
		var response = await ExecuteAsync($"""
			SELECT {RoleFieldsViaEdge} FROM account_has_role WHERE in = $accountId
			""", parameters);
		var results = response.GetValue<List<RoleDbRecord>>(0) ?? [];
		return results.Select(MapRole).OrderByDescending(r => r.Priority).ThenBy(r => r.Slug).ToList();
	}

	public async Task<IReadOnlyList<string>> GetAccountIdsForRoleAsync(string roleSlug)
	{
		var parameters = new Dictionary<string, object?> { ["slug"] = roleSlug };
		var response = await ExecuteAsync("""
			SELECT in.id AS id FROM account_has_role WHERE out.slug = $slug
			""", parameters);
		var results = response.GetValue<List<RoleAccountRefRecord>>(0) ?? [];
		return results.Select(r => NormalizeAccountId(r.Id)).ToList();
	}

	private static SharpRole MapRole(RoleDbRecord r)
	{
		var permissions = string.IsNullOrWhiteSpace(r.permissionsJson)
			? new Dictionary<string, int>()
			: JsonSerializer.Deserialize<Dictionary<string, int>>(r.permissionsJson, JsonOptions)
			  ?? new Dictionary<string, int>();

		return new SharpRole
		{
			Id = $"node_roles/{r.slug}",
			Slug = r.slug,
			Name = r.name,
			Color = r.color,
			Priority = r.priority,
			IsSystem = r.isSystem,
			Permissions = permissions.ToDictionary(kvp => kvp.Key, kvp => (PermissionState)kvp.Value),
			CreatedAt = r.createdAt,
			UpdatedAt = r.updatedAt
		};
	}

	#endregion
}
