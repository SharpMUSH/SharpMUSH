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
	#region Parent/Zone Navigation

	public async ValueTask<AnyOptionalSharpObject> GetParentAsync(string id, CancellationToken cancellationToken = default)
	{
		var key = ExtractKey(id);
		var parameters = new Dictionary<string, object?> { ["key"] = key };
		var response = await ExecuteAsync(
			"SELECT * FROM object WHERE key IN (SELECT VALUE out.key FROM has_parent WHERE in = type::thing('object', $key))",
			parameters, cancellationToken);

		var records = response.GetValue<List<ObjectRecord>>(0)!;
		if (records.Count == 0) return new None();

		return await BuildTypedObjectFromObjectRecord(records[0], cancellationToken);
	}

	public async IAsyncEnumerable<SharpObject> GetParentsAsync(string id, [EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		var key = ExtractKey(id);
		var currentKey = key;

		// Walk the parent chain iteratively since SurrealDB lacks variable-depth traversal
		var visited = new HashSet<int>();
		while (true)
		{
			if (!visited.Add(currentKey)) yield break;

			var parameters = new Dictionary<string, object?> { ["key"] = currentKey };
			var response = await ExecuteAsync(
				"SELECT * FROM object WHERE key IN (SELECT VALUE out.key FROM has_parent WHERE in = type::thing('object', $key))",
				parameters, cancellationToken);

			var records = response.GetValue<List<ObjectRecord>>(0)!;
			if (records.Count == 0) yield break;

			var parentObj = MapRecordToSharpObject(records[0]);
			yield return parentObj;

			currentKey = parentObj.Key;
		}
	}

	public async ValueTask<bool> IsReachableViaParentOrZoneAsync(AnySharpObject startObject, AnySharpObject targetObject,
		int maxDepth = 100, CancellationToken cancellationToken = default)
	{
		var currentKey = startObject.Object().Key;
		var targetKey = targetObject.Object().Key;
		var visited = new HashSet<int>();
		var depth = 0;

		while (depth < maxDepth)
		{
			if (currentKey == targetKey) return true;
			if (!visited.Add(currentKey)) return false;

			var parameters = new Dictionary<string, object?> { ["key"] = currentKey };

			// Get parent keys
			var parentResponse = await ExecuteAsync(
				"SELECT VALUE out.key FROM has_parent WHERE in = type::thing('object', $key)",
				parameters, cancellationToken);
			var parentKeys = parentResponse.GetValue<List<int>>(0)!;

			// Get zone keys
			var zoneResponse = await ExecuteAsync(
				"SELECT VALUE out.key FROM has_zone WHERE in = type::thing('object', $key)",
				parameters, cancellationToken);
			var zoneKeys = zoneResponse.GetValue<List<int>>(0)!;

			var nextKeys = new List<int>();
			nextKeys.AddRange(parentKeys);
			nextKeys.AddRange(zoneKeys);

			if (nextKeys.Count == 0) return false;

			// BFS-style: check all next keys
			foreach (var nk in nextKeys)
			{
				if (nk == targetKey) return true;
			}

			// Follow the first path (parent takes precedence)
			currentKey = nextKeys[0];
			depth++;
		}

		return false;
	}

	public async IAsyncEnumerable<SharpObject> GetObjectsByZoneAsync(AnySharpObject zone, [EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		var zoneKey = zone.Object().Key;
		var parameters = new Dictionary<string, object?> { ["key"] = zoneKey };
		var response = await ExecuteAsync(
			"SELECT * FROM object WHERE key IN (SELECT VALUE in.key FROM has_zone WHERE out = type::thing('object', $key))",
			parameters, cancellationToken);

		var records = response.GetValue<List<ObjectRecord>>(0)!;
		foreach (var record in records)
			yield return MapRecordToSharpObject(record);
	}

	#endregion

	#region Location/Contents/Exits

	public async ValueTask<AnyOptionalSharpContainer> GetLocationAsync(DBRef obj, int depth = 1, CancellationToken cancellationToken = default)
	{
		var baseObject = await GetObjectNodeAsync(obj, cancellationToken);
		if (baseObject.IsNone) return new None();

		var typedId = baseObject.Id()!;
		return await GetLocationFromTypedIdAsync(typedId, depth, cancellationToken);
	}

	public async ValueTask<AnySharpContainer> GetLocationAsync(AnySharpObject obj, int depth = 1, CancellationToken cancellationToken = default)
		=> (await GetLocationAsync(obj.Object().DBRef, depth, cancellationToken)).WithoutNone();

	public async ValueTask<AnySharpContainer> GetLocationAsync(string id, int depth = 1, CancellationToken cancellationToken = default)
	{
		var result = await GetLocationFromTypedIdAsync(id, depth, cancellationToken);
		return result.WithoutNone();
	}

	private async ValueTask<AnyOptionalSharpContainer> GetLocationFromTypedIdAsync(string typedId, int depth, CancellationToken ct)
	{
		var key = ExtractKey(typedId);
		var currentKey = key;
		var maxHops = depth == -1 ? 999 : depth;
		var hops = 0;

		// Walk at_location edges up to maxHops
		int? lastValidContainerKey = null;

		while (hops < maxHops)
		{
			var parameters = new Dictionary<string, object?> { ["key"] = currentKey };
			var response = await ExecuteAsync(
				"SELECT VALUE out.key FROM at_location WHERE in.key = $key",
				parameters, ct);

			var records = response.GetValue<List<int>>(0)!;
			if (records.Count == 0) break;

			var destKey = records[0];
			lastValidContainerKey = destKey;
			currentKey = destKey;
			hops++;
		}

		if (lastValidContainerKey == null) return new None();

		var typed = await BuildTypedObjectFromKey(lastValidContainerKey.Value, ct);
		if (typed.IsNone) return new None();

		return typed.Match<AnyOptionalSharpContainer>(
			player => player,
			room => room,
			_ => throw new Exception("Invalid Location: Exit"),
			thing => thing,
			_ => new None());
	}

	public async IAsyncEnumerable<AnySharpContent> GetContentsAsync(DBRef obj, [EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		var baseObject = await GetObjectNodeAsync(obj, cancellationToken);
		if (baseObject.IsNone) yield break;

		var containerKey = ExtractKey(baseObject.Id()!);
		await foreach (var item in GetContentsForKeyAsync(containerKey, cancellationToken))
			yield return item;
	}

	public async IAsyncEnumerable<AnySharpContent> GetContentsAsync(AnySharpContainer node, [EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		var containerKey = ExtractKey(node.Id);
		await foreach (var item in GetContentsForKeyAsync(containerKey, cancellationToken))
			yield return item;
	}

	private async IAsyncEnumerable<AnySharpContent> GetContentsForKeyAsync(int containerKey, [EnumeratorCancellation] CancellationToken ct = default)
	{
		var parameters = new Dictionary<string, object?> { ["key"] = containerKey };
		var response = await ExecuteAsync(
			"SELECT VALUE in.key FROM at_location WHERE out.key = $key",
			parameters, ct);

		var records = response.GetValue<List<int>>(0)!;
		foreach (var contentKey in records)
		{
			var typed = await BuildTypedObjectFromKey(contentKey, ct);
			if (typed.IsNone) continue;

			var content = typed.Match<AnySharpContent?>(
				player => player,
				_ => null, // Room cannot be content
				exit => exit,
				thing => thing,
				_ => null);

			if (content != null)
				yield return content;
		}
	}

	public async IAsyncEnumerable<SharpExit> GetExitsAsync(DBRef obj, [EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		var baseObject = await GetObjectNodeAsync(obj, cancellationToken);
		if (baseObject.IsNone) yield break;

		var containerKey = ExtractKey(baseObject.Known.Id()!);
		await foreach (var exit in GetExitsForKeyAsync(containerKey, cancellationToken))
			yield return exit;
	}

	public async IAsyncEnumerable<SharpExit> GetExitsAsync(AnySharpContainer node, [EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		var containerKey = ExtractKey(node.Id);
		await foreach (var exit in GetExitsForKeyAsync(containerKey, cancellationToken))
			yield return exit;
	}

	private async IAsyncEnumerable<SharpExit> GetExitsForKeyAsync(int containerKey, [EnumeratorCancellation] CancellationToken ct = default)
	{
		var parameters = new Dictionary<string, object?> { ["key"] = containerKey };
		var response = await ExecuteAsync(
			"SELECT VALUE in FROM at_location WHERE out.key = $key AND in.id LIKE 'exit:%'",
			parameters, ct);

		var records = response.GetValue<List<ExitRecord>>(0)!;
		foreach (var exitRecord in records)
		{
			var key = exitRecord.key;
			var objParams = new Dictionary<string, object?> { ["key"] = key };
			var objResponse = await ExecuteAsync("SELECT * FROM object WHERE key = $key", objParams, ct);
			var objResults = objResponse.GetValue<List<ObjectRecord>>(0)!;
			if (objResults.Count > 0)
			{
				var sharpObj = MapRecordToSharpObject(objResults[0]);
				yield return BuildExit(ExitId(key), exitRecord, sharpObj);
			}
		}
	}

	public async ValueTask MoveObjectAsync(AnySharpContent enactorObj, AnySharpContainer destination, CancellationToken cancellationToken = default)
	{
		var srcKey = ExtractKey(enactorObj.Id);
		var destKey = ExtractKey(destination.Id);
		var srcTable = GetContentTable(enactorObj);
		var destTable = GetContainerTable(destination);

		var parameters = new Dictionary<string, object?>
		{
			["srcKey"] = srcKey,
			["destKey"] = destKey
		};

		await ExecuteAsync(
			$"DELETE at_location WHERE in = {srcTable}:$srcKey;" +
			$"RELATE {srcTable}:$srcKey->at_location->{destTable}:$destKey",
			parameters, cancellationToken);
	}

	public async IAsyncEnumerable<AnySharpObject> GetNearbyObjectsAsync(DBRef obj, [EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		var self = (await GetObjectNodeAsync(obj, cancellationToken)).WithoutNone();
		var location = await self.Where();

		yield return self;

		await foreach (var item in GetContentsAsync(self.Object().DBRef, cancellationToken))
			yield return item.WithRoomOption();

		await foreach (var item in GetContentsAsync(location.Object().DBRef, cancellationToken))
			yield return item.WithRoomOption();
	}

	public async IAsyncEnumerable<AnySharpObject> GetNearbyObjectsAsync(AnySharpObject obj, [EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		var location = await obj.Where();

		yield return obj;

		await foreach (var item in GetContentsAsync(obj.Object().DBRef, cancellationToken))
			yield return item.WithRoomOption();

		await foreach (var item in GetContentsAsync(location.Object().DBRef, cancellationToken))
			yield return item.WithRoomOption();
	}

	public IAsyncEnumerable<SharpObjectFlag> GetObjectFlagsAsync(string id, string type, CancellationToken cancellationToken = default)
		=> GetObjectFlagsForIdAsync(id, type, cancellationToken);

	#endregion
}
