using OneOf;
using OneOf.Types;
using SharpMUSH.Library.Models.Portal.Widgets;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Database.ArangoDB;

public partial class ArangoDatabase : ILayoutRegistryService
{
	#region Layout Registry

	private class LayoutDbDoc
	{
		public string Scope { get; set; } = "";
		public string Json { get; set; } = "";
	}

	public async Task UpsertLayoutAsync(string scope, LayoutConfiguration layout)
	{
		await arangoDb.Query.ExecuteAsync<object>(handle,
			"UPSERT { _key: @key } INSERT @doc REPLACE @doc IN @@c",
			bindVars: new Dictionary<string, object>
			{
				{ "@c", DatabaseConstants.Layouts },
				{ "key", scope },
				{ "doc", new Dictionary<string, object?>
					{
						["_key"] = scope,
						["Scope"] = scope,
						["Json"] = LayoutSerialization.Serialize(layout)
					}
				}
			});
	}

	public async Task<OneOf<LayoutConfiguration, NotFound>> GetLayoutAsync(string scope)
	{
		var result = await arangoDb.Query.ExecuteAsync<LayoutDbDoc>(handle,
			"FOR d IN @@c FILTER d._key == @key RETURN d",
			bindVars: new Dictionary<string, object>
			{
				{ "@c", DatabaseConstants.Layouts },
				{ "key", scope }
			});

		if (result.Count == 0)
		{
			return new NotFound();
		}

		var layout = LayoutSerialization.Deserialize(result[0].Json);
		return layout is null ? new NotFound() : layout;
	}

	public async Task<IReadOnlyList<string>> GetCustomizedScopesAsync()
	{
		var result = await arangoDb.Query.ExecuteAsync<string>(handle,
			"FOR d IN @@c SORT d.Scope RETURN d.Scope",
			bindVars: new Dictionary<string, object> { { "@c", DatabaseConstants.Layouts } });

		return result.ToList();
	}

	public async Task RemoveLayoutAsync(string scope)
	{
		await arangoDb.Query.ExecuteAsync<object>(handle,
			"FOR d IN @@c FILTER d._key == @key REMOVE d IN @@c",
			bindVars: new Dictionary<string, object>
			{
				{ "@c", DatabaseConstants.Layouts },
				{ "key", scope }
			});
	}

	#endregion
}
