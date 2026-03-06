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

namespace SharpMUSH.Database.Memgraph;

public partial class MemgraphDatabase
{
	#region Parent/Zone Navigation

	public async ValueTask<AnyOptionalSharpObject> GetParentAsync(string id, CancellationToken cancellationToken = default)
	{
		var key = ExtractKey(id);
		var result = await ExecuteWithRetryAsync("MATCH (o:Object {key: $key})-[:HAS_PARENT]->(parent:Object) RETURN parent", new { key }, cancellationToken);
		if (result.Result.Count == 0) return new None();
		return await BuildTypedObjectFromObjectNode(result.Result[0]["parent"].As<INode>(), cancellationToken);
	}

	public async IAsyncEnumerable<SharpObject> GetParentsAsync(string id, [EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		var key = ExtractKey(id);
		var result = await ExecuteWithRetryAsync("MATCH (o:Object {key: $key})-[:HAS_PARENT*1..999]->(parent:Object) RETURN parent", new { key }, cancellationToken);
		foreach (var record in result.Result)
			yield return MapNodeToSharpObject(record["parent"].As<INode>());
	}

	public async ValueTask<bool> IsReachableViaParentOrZoneAsync(AnySharpObject startObject, AnySharpObject targetObject,
	int maxDepth = 100, CancellationToken cancellationToken = default)
	{
		var startKey = startObject.Object().Key;
		var targetKey = targetObject.Object().Key;
		var result = await ExecuteWithRetryAsync("""
MATCH path = (start:Object {key: $startKey})-[:HAS_PARENT|HAS_ZONE*1..100]->(target:Object {key: $targetKey})
RETURN count(path) AS cnt
LIMIT 1
""", new { startKey, targetKey }, cancellationToken);
		return result.Result.Count > 0 && result.Result[0]["cnt"].As<long>() > 0;
	}

	public async IAsyncEnumerable<SharpObject> GetObjectsByZoneAsync(AnySharpObject zone, [EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		var zoneKey = zone.Object().Key;
		var result = await ExecuteWithRetryAsync("MATCH (o:Object)-[:HAS_ZONE]->(z:Object {key: $key}) RETURN o", new { key = zoneKey }, cancellationToken);
		foreach (var record in result.Result)
			yield return MapNodeToSharpObject(record["o"].As<INode>());
	}

	#endregion

	#region Location/Contents/Exits

	public async ValueTask<AnyOptionalSharpContainer> GetLocationAsync(DBRef obj, int depth = 1, CancellationToken cancellationToken = default)
	{
		var baseObject = await GetObjectNodeAsync(obj, cancellationToken);
		if (baseObject.IsNone) return new None();

		var typedId = baseObject.Id()!;
		var key = ExtractKey(typedId);

		var maxHops = depth == -1 ? 999 : depth;
		var cypher = "MATCH path = (start {key: $key})-[:AT_LOCATION*0.." + maxHops + "]->(dest) " +
		"WITH dest, size(path) AS pathLen ORDER BY pathLen DESC LIMIT 1 " +
		"MATCH (dest)-[:IS_OBJECT]->(destObj:Object) RETURN destObj";
		var result = await ExecuteWithRetryAsync(cypher, new { key }, cancellationToken);

		if (result.Result.Count == 0) return new None();

		var destObjNode = result.Result[0]["destObj"].As<INode>();
		var located = await BuildTypedObjectFromObjectNode(destObjNode, cancellationToken);
		return located.Match<AnyOptionalSharpContainer>(
		player => player,
		room => room,
		_ => throw new Exception("Invalid Location: Exit"),
		thing => thing,
		_ => throw new Exception("Invalid Location: None"));
	}

	public async ValueTask<AnySharpContainer> GetLocationAsync(AnySharpObject obj, int depth = 1, CancellationToken cancellationToken = default)
	=> (await GetLocationAsync(obj.Object().DBRef, depth, cancellationToken)).WithoutNone();

	public async ValueTask<AnySharpContainer> GetLocationAsync(string id, int depth = 1, CancellationToken cancellationToken = default)
	{
		var key = ExtractKey(id);
		var maxHops = depth == -1 ? 999 : depth;
		var cypher2 = "MATCH path = (start {key: $key})-[:AT_LOCATION*0.." + maxHops + "]->(dest) " +
		"WITH dest, size(path) AS pathLen ORDER BY pathLen DESC LIMIT 1 " +
		"MATCH (dest)-[:IS_OBJECT]->(destObj:Object) RETURN destObj";
		var result = await ExecuteWithRetryAsync(cypher2, new { key }, cancellationToken);

		var destObjNode = result.Result[0]["destObj"].As<INode>();
		var located = await BuildTypedObjectFromObjectNode(destObjNode, cancellationToken);
		return located.Match<AnySharpContainer>(
		player => player,
		room => room,
		_ => throw new Exception("Invalid Location: Exit"),
		thing => thing,
		_ => throw new Exception("Invalid Location: None"));
	}

	public async IAsyncEnumerable<AnySharpContent> GetContentsAsync(DBRef obj, [EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		var baseObject = await GetObjectNodeAsync(obj, cancellationToken);
		if (baseObject.IsNone) yield break;

		var typedKey = ExtractKey(baseObject.Id()!);
		var result = await ExecuteWithRetryAsync("""
MATCH (content)-[:AT_LOCATION]->(container {key: $key})
MATCH (content)-[:IS_OBJECT]->(contentObj:Object)
RETURN contentObj
""", new { key = typedKey }, cancellationToken);

		foreach (var contentObjNode in result.Result.Select(r => r["contentObj"].As<INode>()))
		{
			var contentObj = await BuildTypedObjectFromObjectNode(contentObjNode, cancellationToken);
			yield return contentObj.Match<AnySharpContent>(
			player => player,
			_ => throw new Exception("Room cannot be content"),
			exit => exit,
			thing => thing,
			_ => throw new Exception("None cannot be content"));
		}
	}

	public async IAsyncEnumerable<AnySharpContent> GetContentsAsync(AnySharpContainer node, [EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		var containerKey = ExtractKey(node.Id);
		var result = await ExecuteWithRetryAsync("""
MATCH (content)-[:AT_LOCATION]->(container {key: $key})
MATCH (content)-[:IS_OBJECT]->(contentObj:Object)
RETURN contentObj
""", new { key = containerKey }, cancellationToken);

		foreach (var contentObjNode in result.Result.Select(r => r["contentObj"].As<INode>()))
		{
			var contentObj = await BuildTypedObjectFromObjectNode(contentObjNode, cancellationToken);
			yield return contentObj.Match<AnySharpContent>(
			player => player,
			_ => throw new Exception("Room cannot be content"),
			exit => exit,
			thing => thing,
			_ => throw new Exception("None cannot be content"));
		}
	}

	public async IAsyncEnumerable<SharpExit> GetExitsAsync(DBRef obj, [EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		var baseObject = await GetObjectNodeAsync(obj, cancellationToken);
		if (baseObject.IsNone) yield break;

		var containerKey = ExtractKey(baseObject.Known.Id()!);
		var result = await ExecuteWithRetryAsync("""
MATCH (e:Exit)-[:AT_LOCATION]->(container {key: $key})
MATCH (e)-[:IS_OBJECT]->(o:Object {type: 'EXIT'})
RETURN o, e
""", new { key = containerKey }, cancellationToken);

		foreach (var record in result.Result)
		{
			var objNode = record["o"].As<INode>();
			var exitNode = record["e"].As<INode>();
			var sharpObj = MapNodeToSharpObject(objNode);
			var key = objNode["key"].As<int>();
			yield return BuildExit(ExitId(key), exitNode, sharpObj);
		}
	}

	public async IAsyncEnumerable<SharpExit> GetExitsAsync(AnySharpContainer node, [EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		var containerKey = ExtractKey(node.Id);
		var result = await ExecuteWithRetryAsync("""
MATCH (e:Exit)-[:AT_LOCATION]->(container {key: $key})
MATCH (e)-[:IS_OBJECT]->(o:Object {type: 'EXIT'})
RETURN o, e
""", new { key = containerKey }, cancellationToken);

		foreach (var record in result.Result)
		{
			var objNode = record["o"].As<INode>();
			var exitNode = record["e"].As<INode>();
			var sharpObj = MapNodeToSharpObject(objNode);
			var key = objNode["key"].As<int>();
			yield return BuildExit(ExitId(key), exitNode, sharpObj);
		}
	}

	public async ValueTask MoveObjectAsync(AnySharpContent enactorObj, AnySharpContainer destination, CancellationToken cancellationToken = default)
	{
		var srcKey = ExtractKey(enactorObj.Id);
		var destKey = ExtractKey(destination.Id);
		await ExecuteWithRetryAsync("""
MATCH (src {key: $srcKey})-[r:AT_LOCATION]->()
DELETE r
WITH src
MATCH (dest {key: $destKey})
WHERE dest:Room OR dest:Player OR dest:Thing
CREATE (src)-[:AT_LOCATION]->(dest)
""", new { srcKey, destKey }, cancellationToken);
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

	#endregion
}
