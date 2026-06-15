using OneOf;
using OneOf.Types;
using SharpMUSH.Library.Models.Portal.Widgets;
using SharpMUSH.Library.Services.Interfaces;
using SurrealDb.Net.Models;

namespace SharpMUSH.Database.SurrealDB;

// IMPORTANT: SurrealDb.Net's embedded CBOR serializer ignores [JsonPropertyName].
// Property names MUST exactly match the SurrealDB field names stored in the DB —
// hence the camelCase property names on this record.
internal class SysLayoutDbRecord : Record
{
	public string scope { get; set; } = "";
	public string json { get; set; } = "";
}

public partial class SurrealDatabase : ILayoutRegistryService
{
	#region Layout Registry

	private const string SysLayoutFields = "id, scope, json";

	public async Task UpsertLayoutAsync(string scope, LayoutConfiguration layout)
	{
		var parameters = new Dictionary<string, object?>
		{
			["scope"] = scope,
			["json"] = LayoutSerialization.Serialize(layout)
		};
		await ExecuteAsync(
			"UPSERT type::thing('sys_layout', $scope) SET scope = $scope, json = $json",
			parameters);
	}

	public async Task<OneOf<LayoutConfiguration, NotFound>> GetLayoutAsync(string scope)
	{
		var response = await ExecuteAsync(
			$"SELECT {SysLayoutFields} FROM sys_layout WHERE scope = $scope",
			new Dictionary<string, object?> { ["scope"] = scope });
		var results = response.GetValue<List<SysLayoutDbRecord>>(0);

		if (results is not { Count: > 0 })
		{
			return new NotFound();
		}

		var layout = LayoutSerialization.Deserialize(results[0].json);
		return layout is null ? new NotFound() : layout;
	}

	public async Task<IReadOnlyList<string>> GetCustomizedScopesAsync()
	{
		var response = await ExecuteAsync(
			$"SELECT {SysLayoutFields} FROM sys_layout ORDER BY scope");
		var results = response.GetValue<List<SysLayoutDbRecord>>(0) ?? [];
		return results.Select(r => r.scope).ToList();
	}

	public async Task RemoveLayoutAsync(string scope)
	{
		await ExecuteAsync("DELETE type::thing('sys_layout', $scope)",
			new Dictionary<string, object?> { ["scope"] = scope });
	}

	#endregion
}
