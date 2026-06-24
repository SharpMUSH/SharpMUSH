using Neo4j.Driver;
using OneOf;
using OneOf.Types;
using SharpMUSH.Library.Models.Portal.Widgets;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Database.Memgraph;

public partial class MemgraphDatabase : ILayoutRegistryService
{
	#region Layout Registry

	public async Task UpsertLayoutAsync(string scope, LayoutConfiguration layout)
	{
		await ExecuteWithRetryAsync("""
			MERGE (l:SysLayout {scope: $scope})
			SET l.json = $json
			""",
			new
			{
				scope,
				json = LayoutSerialization.Serialize(layout)
			});
	}

	public async Task<OneOf<LayoutConfiguration, NotFound>> GetLayoutAsync(string scope)
	{
		var result = await ExecuteWithRetryAsync(
			"MATCH (l:SysLayout {scope: $scope}) RETURN l", new { scope });

		if (result.Result.Count == 0)
		{
			return new NotFound();
		}

		var json = result.Result[0]["l"].As<INode>().Properties["json"].As<string>();
		var layout = LayoutSerialization.Deserialize(json);
		return layout is null ? new NotFound() : layout;
	}

	public async Task<IReadOnlyList<string>> GetCustomizedScopesAsync()
	{
		var result = await ExecuteWithRetryAsync(
			"MATCH (l:SysLayout) RETURN l.scope AS scope ORDER BY l.scope");
		return result.Result.Select(r => r["scope"].As<string>()).ToList();
	}

	public async Task RemoveLayoutAsync(string scope)
	{
		await ExecuteWithRetryAsync(
			"MATCH (l:SysLayout {scope: $scope}) DETACH DELETE l", new { scope });
	}

	#endregion
}
