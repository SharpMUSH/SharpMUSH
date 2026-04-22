using DotNext.Threading;
using MarkupString;
using Microsoft.Extensions.Logging;
using OneOf.Types;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;
using SurrealDb.Net;
using SurrealDb.Net.Models.Response;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SharpMUSH.Database.SurrealDB;

public partial class SurrealDatabase
{
	#region Expanded Data

	public async ValueTask SetExpandedObjectData(string sharpObjectId, string dataType, dynamic data, CancellationToken cancellationToken = default)
	{
		var objKey = ExtractKey(sharpObjectId);

		// Check if existing data exists
		var parameters = new Dictionary<string, object?>
		{
			["key"] = objKey,
			["objId"] = sharpObjectId,
			["dataType"] = dataType
		};

		var existing = await ExecuteAsync(
			"SELECT data FROM object_data WHERE objectKey = $key AND dataType = $dataType",
			parameters, cancellationToken);

		var existingResults = existing.GetValue<List<ExpandedDataDbRecord>>(0)!;

		string jsonData;
		if (existingResults.Count > 0)
		{
			// Merge with existing data: non-null values from new data override existing
			var existingJson = existingResults[0].data;
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

			var updateParams = new Dictionary<string, object?>
			{
				["key"] = objKey,
				["dataType"] = dataType,
				["data"] = jsonData
			};
			await ExecuteAsync(
				"UPDATE object_data SET data = $data WHERE objectKey = $key AND dataType = $dataType",
				updateParams, cancellationToken);
		}
		else
		{
			jsonData = JsonSerializer.Serialize((object)data, JsonOptions);
			var createParams = new Dictionary<string, object?>
			{
				["key"] = objKey,
				["objId"] = sharpObjectId,
				["dataType"] = dataType,
				["data"] = jsonData
			};
			await ExecuteAsync(
				"CREATE object_data SET objectKey = $key, sharpObjectId = $objId, dataType = $dataType, data = $data",
				createParams, cancellationToken);
		}
	}

	public async ValueTask<T?> GetExpandedObjectData<T>(string sharpObjectId, string dataType, CancellationToken cancellationToken = default)
	{
		var objKey = ExtractKey(sharpObjectId);
		var parameters = new Dictionary<string, object?>
		{
			["key"] = objKey,
			["dataType"] = dataType
		};

		var response = await ExecuteAsync(
			"SELECT data FROM object_data WHERE objectKey = $key AND dataType = $dataType",
			parameters, cancellationToken);

		var results = response.GetValue<List<ExpandedDataDbRecord>>(0)!;
		if (results.Count == 0) return default;

		var jsonData = results[0].data;
		if (string.IsNullOrEmpty(jsonData)) return default;
		return JsonSerializer.Deserialize<T>(jsonData, JsonOptions);
	}

	public async ValueTask SetExpandedServerData(string dataType, dynamic data, CancellationToken cancellationToken = default)
	{
		var jsonData = JsonSerializer.Serialize((object)data, JsonOptions);
		var parameters = new Dictionary<string, object?>
		{
			["dataType"] = dataType,
			["data"] = jsonData
		};

		await ExecuteAsync(
			"UPSERT server_data:⟨$dataType⟩ SET dataType = $dataType, data = $data",
			parameters, cancellationToken);
	}

	public async ValueTask<T?> GetExpandedServerData<T>(string dataType, CancellationToken cancellationToken = default)
	{
		try
		{
			var parameters = new Dictionary<string, object?> { ["dataType"] = dataType };
			var response = await ExecuteAsync(
				"SELECT data FROM server_data:⟨$dataType⟩",
				parameters, cancellationToken);

			var results = response.GetValue<List<ExpandedDataDbRecord>>(0)!;
			if (results.Count == 0) return default;

			var jsonData = results[0].data;
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
