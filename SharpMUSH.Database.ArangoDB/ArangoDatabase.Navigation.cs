using Core.Arango;
using Core.Arango.Migration;
using Core.Arango.Protocol;
using DotNext.Threading;
using MarkupString;
using Mediator;
using Microsoft.Extensions.Logging;
using OneOf.Types;
using SharpMUSH.Database.Models;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace SharpMUSH.Database.ArangoDB;

public partial class ArangoDatabase
{
	#region Navigation

	public IAsyncEnumerable<SharpObject> GetParentsAsync(string id, CancellationToken ct = default)
		=> arangoDb.Query.ExecuteStreamAsync<SharpObjectQueryResult>(handle,
				$"FOR v IN 1..999 OUTBOUND {id} GRAPH {DatabaseConstants.GraphParents} RETURN v", cache: true,
				cancellationToken: ct)
			.Select(SharpObjectQueryToSharpObject);
	private async ValueTask<SharpPlayer> GetObjectOwnerAsync(string id, CancellationToken ct = default)
	{
		var owner = (await arangoDb.Query.ExecuteAsync<string>(handle,
				$"FOR v IN 1..1 OUTBOUND {id} GRAPH {DatabaseConstants.GraphObjectOwners} RETURN v._id", cancellationToken: ct))
			.FirstOrDefault()
			?? throw new InvalidOperationException($"No owner found for object {id}");

		var populatedOwner = await GetObjectNodeAsync(owner, CancellationToken.None);

		return populatedOwner.AsPlayer;
	}

	private async ValueTask<SharpPlayer> GetAttributeOwnerAsync(string id, CancellationToken ct = default)
	{
		var owner = (await arangoDb.Query.ExecuteAsync<string>(handle,
			$"FOR v IN 1..1 OUTBOUND {id} GRAPH {DatabaseConstants.GraphAttributeOwners} RETURN v._id",
			cancellationToken: ct))
			.FirstOrDefault()
			?? throw new InvalidOperationException($"No owner found for attribute {id}");

		var populatedOwner = await GetObjectNodeAsync(owner, CancellationToken.None);

		return populatedOwner.AsPlayer;
	}

	public async ValueTask<AnyOptionalSharpObject> GetParentAsync(string id, CancellationToken ct = default)
	{
		// Optimized query: Get parent ID directly instead of just the key
		// cache: false to ensure fresh data after parent changes
		var parentId = (await arangoDb.Query.ExecuteAsync<string>(handle,
				$"FOR v IN 1..1 OUTBOUND {id} GRAPH {DatabaseConstants.GraphParents} RETURN v._id", cache: false,
				cancellationToken: ct))
			.FirstOrDefault();
		if (parentId is null)
		{
			return new None();
		}

		return await GetObjectNodeAsync(parentId, ct);
	}

	public async ValueTask<AnyOptionalSharpObject> GetZoneAsync(string id, CancellationToken ct = default)
	{
		// Get zone ID directly - cache: false to ensure fresh data after zone changes
		var zoneId = (await arangoDb.Query.ExecuteAsync<string>(handle,
				$"FOR v IN 1..1 OUTBOUND {id} GRAPH {DatabaseConstants.GraphZones} RETURN v._id", cache: false,
				cancellationToken: ct))
			.FirstOrDefault();
		if (zoneId is null)
		{
			return new None();
		}

		return await GetObjectNodeAsync(zoneId, ct);
	}

	private IAsyncEnumerable<SharpObject>? GetChildrenAsync(string id, CancellationToken ct = default)
		=> arangoDb.Query.ExecuteStreamAsync<SharpObjectQueryResult>(handle,
			$"FOR v IN 1..1 INBOUND {id} GRAPH {DatabaseConstants.GraphParents} RETURN v", cache: true,
			cancellationToken: ct)
		.Select(SharpObjectQueryToSharpObject);
	private async ValueTask<AnySharpContainer> GetHomeAsync(string id, CancellationToken ct = default)
	{
		var homeId = (await arangoDb.Query.ExecuteAsync<string>(handle,
			$"FOR v IN 1..1 OUTBOUND {id} GRAPH {DatabaseConstants.GraphHomes} RETURN v._id", cache: true,
			cancellationToken: ct)).First();
		var homeObject = await GetObjectNodeAsync(homeId, ct);

		return homeObject.Match<AnySharpContainer>(
			player => player,
			room => room,
			_ => throw new Exception("Invalid Location found"),
			thing => thing,
			_ => throw new Exception("Invalid Location found"));
	}

	private async ValueTask<AnyOptionalSharpContainer> GetDropToAsync(string id, CancellationToken ct = default)
	{
		var dropToResult = await arangoDb.Query.ExecuteAsync<string>(handle,
			$"FOR v IN 1..1 OUTBOUND {id} GRAPH {DatabaseConstants.GraphHomes} RETURN v._id", cache: true,
			cancellationToken: ct);

		if (!dropToResult.Any())
		{
			return new None();
		}

		var dropToId = dropToResult.First();
		var dropToObject = await GetObjectNodeAsync(dropToId, ct);

		return dropToObject.Match<AnyOptionalSharpContainer>(
			player => player,
			room => room,
			_ => new None(),
			thing => thing,
			_ => new None());
	}
	public async IAsyncEnumerable<AnySharpObject> GetNearbyObjectsAsync(DBRef obj,
		[EnumeratorCancellation] CancellationToken ct = default)
	{
		var self = (await GetObjectNodeAsync(obj, ct)).WithoutNone();
		var location = await self.Where();

		yield return self;

		await foreach (var item in GetContentsAsync(self.Object().DBRef, ct))
		{
			yield return item.WithRoomOption();
		}

		await foreach (var item in GetContentsAsync(location.Object().DBRef, ct))
		{
			yield return item.WithRoomOption();
		}
	}

	public async IAsyncEnumerable<AnySharpObject> GetNearbyObjectsAsync(AnySharpObject obj,
		[EnumeratorCancellation] CancellationToken ct = default)
	{
		var location = await obj.Where();

		yield return obj;

		await foreach (var item in GetContentsAsync(obj.Object().DBRef, ct))
		{
			yield return item.WithRoomOption();
		}

		await foreach (var item in GetContentsAsync(location.Object().DBRef, ct))
		{
			yield return item.WithRoomOption();
		}
	}

	/// <summary>
	/// Gets the location of an object, at X depth, with 0 returning the same object, and -1 going until it can't go deeper.
	/// </summary>
	/// <param name="obj">Location</param>
	/// <param name="depth">Depth</param></param>
	/// <param name="ct">Cancellation Token</param>
	/// <returns>The deepest findable object based on depth</returns>
	public async ValueTask<AnyOptionalSharpContainer> GetLocationAsync(DBRef obj, int depth = 1,
		CancellationToken ct = default)
	{
		var baseObject = await GetObjectNodeAsync(obj, ct);
		if (baseObject.IsNone) return new None();

		var variableDepth = depth == -1 ? "0" : $"0..{depth}";
		var locationQuery =
			$"FOR v IN {variableDepth} OUTBOUND @startVertex GRAPH {DatabaseConstants.GraphLocations} RETURN v._id";
		var query = await arangoDb.Query.ExecuteAsync<string>(handle, locationQuery, new Dictionary<string, object>()
		{
			{ StartVertex, baseObject.Id()! }
		}, cancellationToken: ct);
		var locationBaseObj = await GetObjectNodeAsync(query.Last(), CancellationToken.None);
		var trueLocation = locationBaseObj.Match<AnyOptionalSharpContainer>(
			player => player,
			room => room,
			_ => throw new Exception("Invalid Location found"),
			thing => thing,
			_ => throw new Exception("Invalid Location found"));

		return trueLocation;
	}

	/// <summary>
	/// Gets the location of an object, at X depth, with 0 returning the same object, and -1 going until it can't go deeper.
	/// </summary>
	/// <param name="id">Location ID</param>
	/// <param name="depth">Depth</param>
	/// <param name="ct">Cancellation Token</param>
	/// <returns>The deepest findable object based on depth</returns>
	public async ValueTask<AnySharpContainer> GetLocationAsync(string id, int depth = 1, CancellationToken ct = default)
	{
		var variableDepth = depth == -1 ? "0" : $"0..{depth}";
		var locationQuery =
			$"FOR v IN {variableDepth} OUTBOUND @startVertex GRAPH {DatabaseConstants.GraphLocations} RETURN v._id";
		var query = await arangoDb.Query.ExecuteAsync<string>(handle, locationQuery, new Dictionary<string, object>()
		{
			{ StartVertex, id }
		}, cancellationToken: ct);
		var locationBaseObj = await GetObjectNodeAsync(query.Last(), CancellationToken.None);
		var trueLocation = locationBaseObj.Match<AnySharpContainer>(
			player => player,
			room => room,
			_ => throw new Exception("Invalid Location found"),
			thing => thing,
			_ => throw new Exception("Invalid Location found"));

		return trueLocation;
	}

	public async ValueTask<AnySharpContainer> GetLocationAsync(AnySharpObject obj, int depth = 1,
		CancellationToken ct = default) =>
		(await GetLocationAsync(obj.Object().DBRef, depth, ct)).WithoutNone();

	public async IAsyncEnumerable<AnySharpContent> GetContentsAsync(DBRef obj, [EnumeratorCancellation] CancellationToken ct = default)
	{
		var baseObject = await GetObjectNodeAsync(obj, ct);
		if (baseObject.IsNone) yield break;

		await foreach (var content in GetContentsBatchAsync(baseObject.Id()!, ct))
		{
			yield return content;
		}
	}

	public async IAsyncEnumerable<AnySharpContent> GetContentsAsync(AnySharpContainer node,
		[EnumeratorCancellation] CancellationToken ct = default)
	{
		await foreach (var content in GetContentsBatchAsync(node.Id, ct))
		{
			yield return content;
		}
	}

	/// <summary>
	/// Batch-fetches all contents of a container in a single AQL query, avoiding N+1 round-trips.
	/// Returns both the typed vertex and its Objects document for each content item.
	/// </summary>
	private async IAsyncEnumerable<AnySharpContent> GetContentsBatchAsync(string startVertex,
		[EnumeratorCancellation] CancellationToken ct = default)
	{
		var results = arangoDb.Query.ExecuteStreamAsync<System.Text.Json.JsonElement>(handle,
			$"FOR typed IN 1..1 INBOUND @startVertex GRAPH {DatabaseConstants.GraphLocations} " +
			$"LET obj = FIRST(FOR o IN 1..1 OUTBOUND typed GRAPH {DatabaseConstants.GraphObjects} RETURN o) " +
			$"FILTER obj != null " +
			$"RETURN {{typed: typed, obj: obj}}",
			new Dictionary<string, object> { { StartVertex, startVertex } },
			cancellationToken: ct);

		await foreach (var result in results.WithCancellation(ct))
		{
			var typedEl = result.GetProperty("typed");
			var objEl = result.GetProperty("obj");
			var node = HydrateObjectFromElements(typedEl, objEl);
			yield return node.Match<AnySharpContent>(
				player => player,
				_ => throw new Exception("Invalid Contents found"),
				exit => exit,
				thing => thing,
				_ => throw new Exception("Invalid Contents found")
			);
		}
	}

	/// <summary>
	/// Hydrates a fully-typed object from raw JSON elements of the typed vertex and its Objects document.
	/// Used by batch methods (GetContentsBatchAsync, GetExitsBatchAsync) to construct objects from
	/// already-fetched JSON data without additional DB round-trips, unlike GetObjectNodeAsync which
	/// fetches from the database.
	/// </summary>
	private AnyOptionalSharpObject HydrateObjectFromElements(
		System.Text.Json.JsonElement typedVertex,
		System.Text.Json.JsonElement objectVertex)
	{
		var id = typedVertex.GetProperty("_id").GetString()!;
		var collection = id.Split("/")[0];
		var sharpObject = SharpObjectQueryToSharpObject(objectVertex);

		return collection switch
		{
			DatabaseConstants.Things => new SharpThing
			{
				Id = id, Object = sharpObject,
				Location = new(async ct => await mediator.Send(new GetCertainLocationQuery(id), ct)),
				Home = new(async ct => await GetHomeAsync(id, ct))
			},
			DatabaseConstants.Players => new SharpPlayer
			{
				Id = id, Object = sharpObject,
				Aliases = typedVertex.GetProperty("Aliases").EnumerateArray().Select(x => x.GetString()!).ToArray(),
				Location = new(async ct => await mediator.Send(new GetCertainLocationQuery(id), ct)),
				Home = new(async ct => await GetHomeAsync(id, ct)),
				PasswordHash = typedVertex.GetProperty("PasswordHash").GetString()!,
				PasswordSalt = typedVertex.TryGetProperty("PasswordSalt", out var saltProp) ? saltProp.GetString() : null,
				Quota = typedVertex.GetProperty("Quota").GetInt32()
			},
			DatabaseConstants.Rooms => new SharpRoom
			{
				Id = id,
				Object = sharpObject,
				Location = new(async ct => await GetDropToAsync(id, ct))
			},
			DatabaseConstants.Exits => new SharpExit
			{
				Id = id, Object = sharpObject,
				Aliases = typedVertex.GetProperty("Aliases").EnumerateArray().Select(x => x.GetString()!).ToArray(),
				Location = new(async ct => await mediator.Send(new GetCertainLocationQuery(id), ct)),
				Home = new(async ct => await GetHomeAsync(id, ct))
			},
			_ => throw new ArgumentException($"Invalid Object Type found: '{objectVertex.GetProperty("Type").GetString()}'"),
		};
	}
	public record SharpExitQuery(
		SharpExitQueryResult Exit,
		SharpObjectQueryResult Obj
	);

	public async IAsyncEnumerable<SharpExit> GetExitsAsync(DBRef obj, [EnumeratorCancellation] CancellationToken ct = default)
	{
		var baseObject = await GetObjectNodeAsync(obj, ct);
		if (baseObject.IsNone) yield break;

		await foreach (var exit in GetExitsBatchAsync(baseObject.Known().Id()!, ct))
		{
			yield return exit;
		}
	}

	public async IAsyncEnumerable<SharpExit> GetExitsAsync(AnySharpContainer node,
		[EnumeratorCancellation] CancellationToken ct = default)
	{
		await foreach (var exit in GetExitsBatchAsync(node.Id, ct))
		{
			yield return exit;
		}
	}

	/// <summary>
	/// Batch-fetches all exits of a container in a single AQL query, avoiding N+1 round-trips.
	/// Filters to the exits collection and returns fully hydrated SharpExit objects.
	/// </summary>
	private async IAsyncEnumerable<SharpExit> GetExitsBatchAsync(string startVertex,
		[EnumeratorCancellation] CancellationToken ct = default)
	{
		var results = arangoDb.Query.ExecuteStreamAsync<System.Text.Json.JsonElement>(handle,
			$"FOR typed IN 1..1 INBOUND @startVertex GRAPH {DatabaseConstants.GraphLocations} " +
			$"FILTER IS_SAME_COLLECTION('{DatabaseConstants.Exits}', typed) " +
			$"LET obj = FIRST(FOR o IN 1..1 OUTBOUND typed GRAPH {DatabaseConstants.GraphObjects} RETURN o) " +
			$"FILTER obj != null " +
			$"RETURN {{typed: typed, obj: obj}}",
			new Dictionary<string, object> { { StartVertex, startVertex } },
			cancellationToken: ct);

		await foreach (var result in results.WithCancellation(ct))
		{
			var typedEl = result.GetProperty("typed");
			var objEl = result.GetProperty("obj");
			var node = HydrateObjectFromElements(typedEl, objEl);
			if (node.IsNone) continue;
			yield return node.Known().AsExit;
		}
	}
	public async IAsyncEnumerable<SharpObject> GetObjectsByZoneAsync(AnySharpObject zone,
		[EnumeratorCancellation] CancellationToken ct = default)
	{
		var zoneId = zone.Object().Id!;

		// Query to find all objects that have this zone set
		const string zoneQuery =
			$"FOR v IN 1..1 INBOUND @startVertex GRAPH {DatabaseConstants.GraphZones} RETURN v._id";

		var queryIds = await arangoDb.Query.ExecuteAsync<string>(handle, zoneQuery,
			new Dictionary<string, object>
			{
				{ StartVertex, zoneId }
			}, cancellationToken: ct);

		await foreach (var id in queryIds.ToAsyncEnumerable().WithCancellation(ct))
		{
			// Parse the id safely - format should be "collection/key"
			var parts = id.Split('/');
			if (parts.Length == 2 && int.TryParse(parts[1], out var key))
			{
				var obj = await GetBaseObjectNodeAsync(new DBRef(key), ct);
				if (obj != null)
				{
					yield return obj;
				}
			}
		}
	}

	#endregion
}
