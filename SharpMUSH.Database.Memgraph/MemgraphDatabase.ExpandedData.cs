using DotNext.Threading;
using MarkupString;
using Microsoft.Extensions.Logging;
using Neo4j.Driver;
using OneOf.Types;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using MString = MarkupString.MarkupStringModule.MarkupString;

namespace SharpMUSH.Database.Memgraph;

public partial class MemgraphDatabase
{
	#region Expanded Data

	public async ValueTask SetExpandedObjectData(string sharpObjectId, string dataType, dynamic data, CancellationToken cancellationToken = default)
	{
		var objKey = ExtractKey(sharpObjectId);

		// Check if node exists
		var existing = await ExecuteWithRetryAsync("""
MATCH (o:Object {key: $key})-[:HAS_EXPANDED_DATA]->(d:ExpandedObjectData {sharpObjectId: $objId, dataType: $dataType})
RETURN d.data AS data
""", new { key = objKey, objId = sharpObjectId, dataType }, cancellationToken);

		string jsonData;
		if (existing.Result.Count > 0)
		{
			// Merge with existing data: non-null values from new data override existing
			var existingJson = existing.Result[0]["data"].As<string>();
			var existingDoc = JsonSerializer.Deserialize<JsonElement>(existingJson, JsonOptions);
			var newDoc = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize((object)data, JsonOptions), JsonOptions);

			var merged = new Dictionary<string, JsonElement>();
			foreach (var prop in existingDoc.EnumerateObject())
				merged[prop.Name] = prop.Value;
			foreach (var prop in newDoc.EnumerateObject())
			{
				if (prop.Value.ValueKind != JsonValueKind.Null)
					merged[prop.Name] = prop.Value;
			}
			jsonData = JsonSerializer.Serialize(merged, JsonOptions);

			await ExecuteWithRetryAsync("""
MATCH (o:Object {key: $key})-[:HAS_EXPANDED_DATA]->(d:ExpandedObjectData {sharpObjectId: $objId, dataType: $dataType})
SET d.data = $data
""", new { key = objKey, objId = sharpObjectId, dataType, data = jsonData }, cancellationToken);
		}
		else
		{
			jsonData = JsonSerializer.Serialize((object)data, JsonOptions);
			await ExecuteWithRetryAsync("""
MATCH (o:Object {key: $key})
CREATE (d:ExpandedObjectData {sharpObjectId: $objId, dataType: $dataType, data: $data})
CREATE (o)-[:HAS_EXPANDED_DATA]->(d)
""", new { key = objKey, objId = sharpObjectId, dataType, data = jsonData }, cancellationToken);
		}
	}

	public async ValueTask<T?> GetExpandedObjectData<T>(string sharpObjectId, string dataType, CancellationToken cancellationToken = default)
	{
		var objKey = ExtractKey(sharpObjectId);
		var result = await ExecuteWithRetryAsync("""
MATCH (o:Object {key: $key})-[:HAS_EXPANDED_DATA]->(d:ExpandedObjectData {sharpObjectId: $objId, dataType: $dataType})
RETURN d.data AS data
""", new { key = objKey, objId = sharpObjectId, dataType }, cancellationToken);

		if (result.Result.Count == 0) return default;
		var jsonData = result.Result[0]["data"].As<string>();
		if (string.IsNullOrEmpty(jsonData)) return default;
		return JsonSerializer.Deserialize<T>(jsonData, JsonOptions);
	}

	public async ValueTask SetExpandedServerData(string dataType, dynamic data, CancellationToken cancellationToken = default)
	{
		var jsonData = JsonSerializer.Serialize((object)data, JsonOptions);
		await ExecuteWithRetryAsync("""
MERGE (d:ExpandedServerData {dataType: $dataType})
SET d.data = $data
""", new { dataType, data = jsonData }, cancellationToken);
	}

	public async ValueTask<T?> GetExpandedServerData<T>(string dataType, CancellationToken cancellationToken = default)
	{
		try
		{
			var result = await ExecuteWithRetryAsync("MATCH (d:ExpandedServerData {dataType: $dataType}) RETURN d.data AS data", new { dataType }, cancellationToken);

			if (result.Result.Count == 0) return default;
			var jsonData = result.Result[0]["data"].As<string>();
			if (string.IsNullOrEmpty(jsonData)) return default;
			return JsonSerializer.Deserialize<T>(jsonData, JsonOptions);
		}
		catch (Exception ex)
		{
			logger.LogWarning(ex, "Failed to retrieve expanded server data for type '{DataType}'", dataType);
			return default;
		}
	}

	#endregion
}
