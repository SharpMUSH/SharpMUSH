using Core.Arango;
using Core.Arango.Protocol;
using OneOf;
using OneOf.Types;
using SharpMUSH.Database.Models;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;
using System.Text.Json;

namespace SharpMUSH.Database.ArangoDB;

public partial class ArangoDatabase : IRoleRegistryService
{
	#region Role Registry

	private class RoleDbDoc
	{
		public string Slug { get; set; } = "";
		public string Name { get; set; } = "";
		public string? Color { get; set; }
		public int Priority { get; set; }
		public bool IsSystem { get; set; }
		public string PermissionsJson { get; set; } = "";
		public long CreatedAt { get; set; }
		public long UpdatedAt { get; set; }
	}

	public async Task UpsertRoleAsync(SharpRole role)
	{
		await arangoDb.Query.ExecuteAsync<object>(handle,
			"UPSERT { _key: @key } INSERT @doc REPLACE @doc IN @@c",
			bindVars: new Dictionary<string, object>
			{
				{ "@c", DatabaseConstants.Roles },
				{ "key", role.Slug },
				{ "doc", ToDoc(role) }
			});

		static Dictionary<string, object?> ToDoc(SharpRole r) => new()
		{
			["_key"] = r.Slug,
			["Slug"] = r.Slug,
			["Name"] = r.Name,
			["Color"] = r.Color,
			["Priority"] = r.Priority,
			["IsSystem"] = r.IsSystem,
			["PermissionsJson"] = PermissionsToJson(r.Permissions),
			["CreatedAt"] = r.CreatedAt,
			["UpdatedAt"] = r.UpdatedAt
		};
	}

	public async Task<OneOf<SharpRole, NotFound>> GetRoleAsync(string slug)
	{
		var result = await arangoDb.Query.ExecuteAsync<RoleDbDoc>(handle,
			"FOR d IN @@c FILTER d._key == @key RETURN d",
			bindVars: new Dictionary<string, object>
			{
				{ "@c", DatabaseConstants.Roles },
				{ "key", slug }
			});

		return result.Count == 0 ? new NotFound() : Map(result[0]);
	}

	public async Task<IReadOnlyList<SharpRole>> GetRolesAsync()
	{
		var result = await arangoDb.Query.ExecuteAsync<RoleDbDoc>(handle,
			"FOR d IN @@c SORT d.Priority DESC, d.Slug RETURN d",
			bindVars: new Dictionary<string, object> { { "@c", DatabaseConstants.Roles } });

		return result.Select(Map).ToList();
	}

	public async Task RemoveRoleAsync(string slug)
	{
		await arangoDb.Query.ExecuteAsync<object>(handle,
			"FOR d IN @@c FILTER d._key == @key REMOVE d IN @@c",
			bindVars: new Dictionary<string, object>
			{
				{ "@c", DatabaseConstants.Roles },
				{ "key", slug }
			});
	}

	public async Task AssignRoleToAccountAsync(string accountId, string roleSlug)
	{
		var roleId = $"{DatabaseConstants.Roles}/{roleSlug}";
		var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

		await arangoDb.Query.ExecuteAsync<object>(handle,
			"UPSERT { _from: @from, _to: @to } INSERT @doc UPDATE {} IN @@edge",
			bindVars: new Dictionary<string, object>
			{
				{ "@edge", DatabaseConstants.AccountHasRole },
				{ "from", accountId },
				{ "to", roleId },
				{ "doc", new Dictionary<string, object?> { ["_from"] = accountId, ["_to"] = roleId, ["CreatedAt"] = now } }
			});
	}

	public async Task RemoveRoleFromAccountAsync(string accountId, string roleSlug)
	{
		var roleId = $"{DatabaseConstants.Roles}/{roleSlug}";
		await arangoDb.Query.ExecuteAsync<ArangoVoid>(handle,
			"FOR e IN @@edge FILTER e._from == @from AND e._to == @to REMOVE e IN @@edge",
			bindVars: new Dictionary<string, object>
			{
				{ "@edge", DatabaseConstants.AccountHasRole },
				{ "from", accountId },
				{ "to", roleId }
			});
	}

	public async Task<IReadOnlyList<SharpRole>> GetRolesForAccountAsync(string accountId)
	{
		var result = await arangoDb.Query.ExecuteAsync<RoleDbDoc>(handle,
			"FOR e IN @@edge FILTER e._from == @from FOR r IN @@roles FILTER r._id == e._to RETURN r",
			bindVars: new Dictionary<string, object>
			{
				{ "@edge", DatabaseConstants.AccountHasRole },
				{ "@roles", DatabaseConstants.Roles },
				{ "from", accountId }
			});

		return result.Select(Map).OrderByDescending(r => r.Priority).ThenBy(r => r.Slug).ToList();
	}

	public async Task<IReadOnlyList<string>> GetAccountIdsForRoleAsync(string roleSlug)
	{
		var roleId = $"{DatabaseConstants.Roles}/{roleSlug}";
		var result = await arangoDb.Query.ExecuteAsync<string>(handle,
			"FOR e IN @@edge FILTER e._to == @to RETURN e._from",
			bindVars: new Dictionary<string, object>
			{
				{ "@edge", DatabaseConstants.AccountHasRole },
				{ "to", roleId }
			});

		return result.ToList();
	}

	private static SharpRole Map(RoleDbDoc d) => new()
	{
		Id = $"{DatabaseConstants.Roles}/{d.Slug}",
		Slug = d.Slug,
		Name = d.Name,
		Color = d.Color,
		Priority = d.Priority,
		IsSystem = d.IsSystem,
		Permissions = PermissionsFromJson(d.PermissionsJson),
		CreatedAt = d.CreatedAt,
		UpdatedAt = d.UpdatedAt
	};

	private static string PermissionsToJson(Dictionary<string, PermissionState> permissions) =>
		JsonSerializer.Serialize(permissions.ToDictionary(kv => kv.Key, kv => (int)kv.Value));

	private static Dictionary<string, PermissionState> PermissionsFromJson(string? json)
	{
		if (string.IsNullOrEmpty(json))
		{
			return new Dictionary<string, PermissionState>();
		}

		var raw = JsonSerializer.Deserialize<Dictionary<string, int>>(json);
		return raw is null
			? new Dictionary<string, PermissionState>()
			: raw.ToDictionary(kv => kv.Key, kv => (PermissionState)kv.Value);
	}

	#endregion
}
