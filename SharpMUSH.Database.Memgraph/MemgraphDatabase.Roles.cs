using Neo4j.Driver;
using OneOf;
using OneOf.Types;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;
using System.Text.Json;

namespace SharpMUSH.Database.Memgraph;

public partial class MemgraphDatabase : IRoleRegistryService
{
	#region Role Registry

	// Node label: :SysRole, keyed by slug. Permissions are persisted as a JSON string
	// (permissionsJson) because Cypher node properties cannot hold a nested map cleanly.
	// Assignment edge: (:Account)-[:ACCOUNT_HAS_ROLE]->(:SysRole).

	public async Task UpsertRoleAsync(SharpRole role)
	{
		await ExecuteWithRetryAsync("""
			MERGE (r:SysRole {slug: $slug})
			SET r.name = $name, r.color = $color, r.priority = $priority, r.isSystem = $isSystem,
			    r.permissionsJson = $permissionsJson, r.createdAt = $createdAt, r.updatedAt = $updatedAt
			""",
			new
			{
				slug = role.Slug,
				name = role.Name,
				color = (object?)role.Color ?? DBNull.Value,
				priority = role.Priority,
				isSystem = role.IsSystem,
				permissionsJson = SerializePermissions(role.Permissions),
				createdAt = role.CreatedAt,
				updatedAt = role.UpdatedAt
			});
	}

	public async Task<OneOf<SharpRole, NotFound>> GetRoleAsync(string slug)
	{
		var result = await ExecuteWithRetryAsync(
			"MATCH (r:SysRole {slug: $slug}) RETURN r", new { slug });

		return result.Result.Count == 0
			? new NotFound()
			: MapRoleNode(result.Result[0]["r"].As<INode>());
	}

	public async Task<IReadOnlyList<SharpRole>> GetRolesAsync()
	{
		var result = await ExecuteWithRetryAsync(
			"MATCH (r:SysRole) RETURN r ORDER BY r.priority DESC, r.slug");
		return result.Result.Select(r => MapRoleNode(r["r"].As<INode>())).ToList();
	}

	public async Task RemoveRoleAsync(string slug)
	{
		await ExecuteWithRetryAsync(
			"MATCH (r:SysRole {slug: $slug}) DETACH DELETE r", new { slug });
	}

	public async Task AssignRoleToAccountAsync(string accountId, string roleSlug)
	{
		var key = accountId.Contains('/') ? accountId.Split('/')[1] : accountId;
		await ExecuteWithRetryAsync("""
			MATCH (a:Account {id: $id}), (r:SysRole {slug: $slug})
			MERGE (a)-[:ACCOUNT_HAS_ROLE]->(r)
			""", new { id = key, slug = roleSlug });
	}

	public async Task RemoveRoleFromAccountAsync(string accountId, string roleSlug)
	{
		var key = accountId.Contains('/') ? accountId.Split('/')[1] : accountId;
		await ExecuteWithRetryAsync("""
			MATCH (a:Account {id: $id})-[rel:ACCOUNT_HAS_ROLE]->(r:SysRole {slug: $slug})
			DELETE rel
			""", new { id = key, slug = roleSlug });
	}

	public async Task<IReadOnlyList<SharpRole>> GetRolesForAccountAsync(string accountId)
	{
		var key = accountId.Contains('/') ? accountId.Split('/')[1] : accountId;
		var result = await ExecuteWithRetryAsync("""
			MATCH (a:Account {id: $id})-[:ACCOUNT_HAS_ROLE]->(r:SysRole)
			RETURN r ORDER BY r.priority DESC, r.slug
			""", new { id = key });
		return result.Result.Select(r => MapRoleNode(r["r"].As<INode>())).ToList();
	}

	public async Task<IReadOnlyList<string>> GetAccountIdsForRoleAsync(string roleSlug)
	{
		var result = await ExecuteWithRetryAsync("""
			MATCH (a:Account)-[:ACCOUNT_HAS_ROLE]->(r:SysRole {slug: $slug})
			RETURN a.id AS id
			""", new { slug = roleSlug });
		return result.Result.Select(r => $"node_accounts/{r["id"].As<string>()}").ToList();
	}

	private static string SerializePermissions(Dictionary<string, PermissionState> permissions) =>
		JsonSerializer.Serialize(
			permissions.ToDictionary(kvp => kvp.Key, kvp => (int)kvp.Value), JsonOptions);

	private static Dictionary<string, PermissionState> DeserializePermissions(string? json)
	{
		if (string.IsNullOrWhiteSpace(json))
			return new Dictionary<string, PermissionState>();
		var raw = JsonSerializer.Deserialize<Dictionary<string, int>>(json, JsonOptions);
		return raw is null
			? new Dictionary<string, PermissionState>()
			: raw.ToDictionary(kvp => kvp.Key, kvp => (PermissionState)kvp.Value);
	}

	private static SharpRole MapRoleNode(INode node) => new()
	{
		Id = $"node_roles/{node.Properties["slug"].As<string>()}",
		Slug = node.Properties["slug"].As<string>(),
		Name = node.Properties["name"].As<string>(),
		Color = OptionalString(node, "color"),
		Priority = node.Properties["priority"].As<int>(),
		IsSystem = node.Properties.TryGetValue("isSystem", out var isSystem) && (bool)isSystem,
		Permissions = DeserializePermissions(OptionalString(node, "permissionsJson")),
		CreatedAt = node.Properties.TryGetValue("createdAt", out var created) ? Convert.ToInt64(created) : 0,
		UpdatedAt = node.Properties.TryGetValue("updatedAt", out var updated) ? Convert.ToInt64(updated) : 0
	};

	#endregion
}
