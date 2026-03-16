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
	#region Objects

	public async ValueTask<DBRef> CreatePlayerAsync(string name, string password, DBRef location, DBRef home, int quota,
		string? salt = null, CancellationToken ct = default)
	{
		var time = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
		var objectLocation = await GetObjectNodeAsync(location, ct);
		var objectHome = await GetObjectNodeAsync(home, ct);

		var transaction = new ArangoTransaction
		{
			LockTimeout = DatabaseBehaviorConstants.TransactionTimeout,
			WaitForSync = true,
			Collections = new ArangoTransactionScope
			{
				Exclusive =
				[
					DatabaseConstants.Objects,
					DatabaseConstants.Players,
					DatabaseConstants.IsObject,
					DatabaseConstants.HasObjectOwner,
					DatabaseConstants.AtLocation,
					DatabaseConstants.HasHome
				]
			}
		};

		var transactionHandle = await arangoDb.Transaction.BeginAsync(handle, transaction, ct);

		var obj = await arangoDb.Graph.Vertex.CreateAsync<SharpObjectCreateRequest, SharpObjectQueryResult>(
			transactionHandle, DatabaseConstants.GraphObjects,
			DatabaseConstants.Objects, new SharpObjectCreateRequest(
				name,
				DatabaseConstants.TypePlayer,
				[],
				time,
				time
			), returnNew: true, cancellationToken: ct);

		// If salt is provided (imported password), use the password as-is (it's already hashed)
		// Otherwise, hash the password for new players
		var hashedPassword = salt != null
			? password
			: passwordService.HashPassword($"#{obj.New.Key}:{obj.New.CreationTime}", password);

		var playerResult = await arangoDb.Document.CreateAsync<SharpPlayerCreateRequest, SharpPlayerQueryResult>(
			transactionHandle,
			DatabaseConstants.Players,
			new SharpPlayerCreateRequest([], hashedPassword, salt, quota), cancellationToken: ct);

		await arangoDb.Graph.Edge.CreateAsync(transactionHandle, DatabaseConstants.GraphObjects, DatabaseConstants.IsObject,
			new SharpEdgeCreateRequest(playerResult.Id, obj.New.Id), cancellationToken: ct);

		await arangoDb.Graph.Edge.CreateAsync(transactionHandle, DatabaseConstants.GraphObjectOwners,
			DatabaseConstants.HasObjectOwner, new SharpEdgeCreateRequest(obj.New.Id, playerResult.Id),
			cancellationToken: ct);

		var idx = objectLocation.Match(
			player => player.Id,
			room => room.Id,
			_ => throw new ArgumentException("An Exit is not a valid location to create a player!"),
			thing => thing.Id,
			_ => throw new ArgumentException("A player must have a valid creation location!"));

		var homeIdx = objectHome.Match(
			player => player.Id,
			room => room.Id,
			_ => throw new ArgumentException("An Exit is not a valid location to create a player!"),
			thing => thing.Id,
			_ => throw new ArgumentException("A player must have a valid creation location!"));

		await arangoDb.Graph.Edge.CreateAsync(transactionHandle, DatabaseConstants.GraphLocations,
			DatabaseConstants.AtLocation, new SharpEdgeCreateRequest(playerResult.Id, idx!), cancellationToken: ct);

		await arangoDb.Graph.Edge.CreateAsync(transactionHandle, DatabaseConstants.GraphHomes, DatabaseConstants.HasHome,
			new SharpEdgeCreateRequest(playerResult.Id, homeIdx!), cancellationToken: ct);

		await arangoDb.Transaction.CommitAsync(transactionHandle, ct);

		return new DBRef(int.Parse(obj.New.Key), time);
	}

	public async ValueTask<DBRef> CreateRoomAsync(string name, SharpPlayer creator, CancellationToken ct = default)
	{
		var time = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

		var obj = await arangoDb.Document.CreateAsync(handle, DatabaseConstants.Objects,
			new SharpObjectCreateRequest(name, DatabaseConstants.TypeRoom, [], time, time), cancellationToken: ct);
		var room = await arangoDb.Document.CreateAsync(handle, DatabaseConstants.Rooms, new SharpRoomCreateRequest(),
			cancellationToken: ct);

		await arangoDb.Graph.Edge.CreateAsync(handle, DatabaseConstants.GraphObjects, DatabaseConstants.IsObject,
			new SharpEdgeCreateRequest(room.Id, obj.Id), cancellationToken: ct);
		await arangoDb.Graph.Edge.CreateAsync(handle, DatabaseConstants.GraphObjectOwners, DatabaseConstants.HasObjectOwner,
			new SharpEdgeCreateRequest(obj.Id, creator.Id!), cancellationToken: ct);

		return new DBRef(int.Parse(obj.Key), time);
	}

	public async ValueTask<DBRef> CreateThingAsync(string name, AnySharpContainer location, SharpPlayer creator,
		AnySharpContainer home, CancellationToken ct = default)
	{
		var transaction = await arangoDb.Transaction.BeginAsync(handle,
			new ArangoTransaction()
			{
				Collections = new ArangoTransactionScope
				{
					Exclusive =
					[
						DatabaseConstants.Objects, DatabaseConstants.Things, DatabaseConstants.IsObject,
						DatabaseConstants.AtLocation, DatabaseConstants.HasHome, DatabaseConstants.HasObjectOwner
					]
				}
			}, ct);
		var time = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

		var obj = await arangoDb.Document.CreateAsync<SharpObjectCreateRequest, SharpObjectQueryResult>(transaction,
			DatabaseConstants.Objects,
			new SharpObjectCreateRequest(name, DatabaseConstants.TypeThing, [], time, time), cancellationToken: ct);
		var thing = await arangoDb.Document.CreateAsync(transaction, DatabaseConstants.Things,
			new SharpThingCreateRequest([]), cancellationToken: ct);

		await arangoDb.Graph.Edge.CreateAsync(transaction, DatabaseConstants.GraphObjects, DatabaseConstants.IsObject,
			new SharpEdgeCreateRequest(thing.Id, obj.Id), cancellationToken: ct);
		await arangoDb.Graph.Edge.CreateAsync(transaction, DatabaseConstants.GraphLocations, DatabaseConstants.AtLocation,
			new SharpEdgeCreateRequest(thing.Id, location.Id), cancellationToken: ct);
		await arangoDb.Graph.Edge.CreateAsync(transaction, DatabaseConstants.GraphHomes, DatabaseConstants.HasHome,
			new SharpEdgeCreateRequest(thing.Id, home.Id), cancellationToken: ct);
		await arangoDb.Graph.Edge.CreateAsync(transaction, DatabaseConstants.GraphObjectOwners,
			DatabaseConstants.HasObjectOwner,
			new SharpEdgeCreateRequest(obj.Id, creator.Id!), cancellationToken: ct);

		await arangoDb.Transaction.CommitAsync(transaction, ct);
		return new DBRef(int.Parse(obj.Key), time);
	}

	public async ValueTask<bool> LinkExitAsync(SharpExit exit, AnySharpContainer location, CancellationToken ct = default)
	{
		await arangoDb.Graph.Edge.CreateAsync(handle, DatabaseConstants.GraphHomes, DatabaseConstants.HasHome,
			new SharpEdgeCreateRequest(exit.Id!, location.Id), cancellationToken: ct);
		return true;
	}

	public async ValueTask<bool> UnlinkExitAsync(SharpExit exit, CancellationToken ct = default)
	{
		var result = await arangoDb.Query.ExecuteAsync<SharpEdgeQueryResult>(handle,
			$"FOR v, e IN 1..1 INBOUND {exit.Id} GRAPH {DatabaseConstants.GraphHomes} RETURN e", cancellationToken: ct);

		if (!result.Any())
		{
			return false;
		}

		await arangoDb.Graph.Edge.RemoveAsync<object>(handle,
			DatabaseConstants.GraphHomes, DatabaseConstants.HasHome, result.First().Key, cancellationToken: ct);

		return true;
	}

	public async ValueTask<bool> LinkRoomAsync(SharpRoom room, AnyOptionalSharpContainer location, CancellationToken ct = default)
	{
		// If location is None, just unlink any existing location
		if (location.IsT3) // None
		{
			return await UnlinkRoomAsync(room, ct);
		}

		// First, unlink any existing location
		await UnlinkRoomAsync(room, ct);

		// Create edge for location (drop-to)
		var locationId = location.Match(
			player => player.Id!,
			room => room.Id!,
			thing => thing.Id!,
			_ => throw new InvalidOperationException("Invalid location type"));

		await arangoDb.Graph.Edge.CreateAsync(handle, DatabaseConstants.GraphHomes, DatabaseConstants.HasHome,
			new SharpEdgeCreateRequest(room.Id!, locationId), cancellationToken: ct);
		return true;
	}

	public async ValueTask<bool> UnlinkRoomAsync(SharpRoom room, CancellationToken ct = default)
	{
		var result = await arangoDb.Query.ExecuteAsync<SharpEdgeQueryResult>(handle,
			$"FOR v, e IN 1..1 OUTBOUND {room.Id} GRAPH {DatabaseConstants.GraphHomes} RETURN e", cancellationToken: ct);

		if (!result.Any())
		{
			return false;
		}

		await arangoDb.Graph.Edge.RemoveAsync<object>(handle,
			DatabaseConstants.GraphHomes, DatabaseConstants.HasHome, result.First().Key, cancellationToken: ct);

		return true;
	}

	public async ValueTask<DBRef> CreateExitAsync(string name, string[] aliases, AnySharpContainer location,
		SharpPlayer creator, CancellationToken ct = default)
	{
		var time = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

		var obj = await arangoDb.Document.CreateAsync<SharpObjectCreateRequest, SharpObjectQueryResult>(handle,
			DatabaseConstants.Objects,
			new SharpObjectCreateRequest(name, DatabaseConstants.TypeExit, [], time, time), cancellationToken: ct);
		var exit = await arangoDb.Document.CreateAsync(handle, DatabaseConstants.Exits,
			new SharpExitCreateRequest(aliases), cancellationToken: ct);

		await arangoDb.Graph.Edge.CreateAsync(handle, DatabaseConstants.GraphObjects, DatabaseConstants.IsObject,
			new SharpEdgeCreateRequest(exit.Id, obj.Id), cancellationToken: ct);
		await arangoDb.Graph.Edge.CreateAsync(handle, DatabaseConstants.GraphLocations, DatabaseConstants.AtLocation,
			new SharpEdgeCreateRequest(exit.Id, location.Id), cancellationToken: ct);
		/* await arangoDB.Graph.Edge.CreateAsync(handle, DatabaseConstants.graphHomes, DatabaseConstants.hasHome,
			new SharpEdgeCreateRequest(exit.Id, location.Id)); */
		await arangoDb.Graph.Edge.CreateAsync(handle, DatabaseConstants.GraphObjectOwners, DatabaseConstants.HasObjectOwner,
			new SharpEdgeCreateRequest(obj.Id, creator.Id!), cancellationToken: ct);

		return new DBRef(int.Parse(obj.Key), time);
	}
	public async ValueTask SetObjectName(AnySharpObject obj, MString value,
		CancellationToken ct = default)
		=> await arangoDb.Document.UpdateAsync(handle, DatabaseConstants.Objects,
			new
			{
				_key = obj.Object().Key.ToString(),
				Name = MModule.plainText(value)
			}, cancellationToken: ct);

	public async ValueTask SetContentHome(AnySharpContent obj, AnySharpContainer home, CancellationToken ct = default)
	{
		var response = await arangoDb.Query.ExecuteAsync<string>(handle,
			$"FOR v,e IN 1..1 OUTBOUND @startVertex GRAPH {DatabaseConstants.GraphHomes} RETURN e._key",
			new Dictionary<string, object> { { StartVertex, obj.Id } }, cancellationToken: ct);

		var contentEdgeKey = response.First();

		await arangoDb.Graph.Edge.UpdateAsync(handle, DatabaseConstants.GraphHomes, DatabaseConstants.HasHome,
			contentEdgeKey, new { To = home.Id }, cancellationToken: ct);
	}

	public async ValueTask SetContentLocation(AnySharpContent obj, AnySharpContainer location,
		CancellationToken ct = default)
	{
		var response = await arangoDb.Query.ExecuteAsync<string>(handle,
			$"FOR v,e IN 1..1 OUTBOUND @startVertex GRAPH {DatabaseConstants.GraphLocations} RETURN e._key",
			new Dictionary<string, object> { { StartVertex, obj.Id } }, cancellationToken: ct);

		var contentEdgeKey = response.First();

		await arangoDb.Graph.Edge.UpdateAsync(handle, DatabaseConstants.GraphLocations, DatabaseConstants.AtLocation,
			contentEdgeKey, new { To = location.Id }, cancellationToken: ct);
	}

	public async ValueTask SetObjectParent(AnySharpObject obj, AnySharpObject? parent, CancellationToken ct = default)
	{
		var fromId = obj.Object().Id!;
		var toId = parent?.Object().Id;

		var response = await arangoDb.Query.ExecuteAsync<SharpEdgeQueryResult>(handle,
			$"FOR v,e IN 1..1 OUTBOUND @startVertex GRAPH {DatabaseConstants.GraphParents} RETURN e",
			new Dictionary<string, object> { { StartVertex, fromId } }, cancellationToken: ct);

		var parentEdge = response.FirstOrDefault();

		if (parentEdge is null && parent is null)
		{
			return;
		}

		if (parentEdge is null)
		{
			await arangoDb.Graph.Edge.CreateAsync(handle, DatabaseConstants.GraphParents, DatabaseConstants.HasParent,
				new { _from = fromId, _to = toId }, cancellationToken: ct);
		}
		else if (parent is null)
		{
			await arangoDb.Graph.Edge.RemoveAsync<object>(handle, DatabaseConstants.GraphParents, DatabaseConstants.HasParent,
				parentEdge!.Key, cancellationToken: ct);
		}
		else
		{
			await arangoDb.Graph.Edge.UpdateAsync(handle, DatabaseConstants.GraphParents, DatabaseConstants.HasParent,
				parentEdge!.Key, new { _to = toId }, cancellationToken: ct);
		}
	}

	public async ValueTask UnsetObjectParent(AnySharpObject obj, CancellationToken ct = default)
		=> await SetObjectParent(obj, null, ct);

	public async ValueTask SetObjectZone(AnySharpObject obj, AnySharpObject? zone, CancellationToken ct = default)
	{
		var response = await arangoDb.Query.ExecuteAsync<SharpEdgeQueryResult>(handle,
			$"FOR v,e IN 1..1 OUTBOUND @startVertex GRAPH {DatabaseConstants.GraphZones} RETURN e",
			new Dictionary<string, object> { { StartVertex, obj.Object().Id! } }, cancellationToken: ct);

		var zoneEdge = response.FirstOrDefault();

		if (zoneEdge is null && zone is null)
		{
			// No existing zone and we're not setting one - nothing to do
			return;
		}

		if (zoneEdge is null)
		{
			// No existing zone, create new edge
			await arangoDb.Graph.Edge.CreateAsync(handle, DatabaseConstants.GraphZones, DatabaseConstants.HasZone,
				new { _from = obj.Object().Id, _to = zone!.Object().Id }, cancellationToken: ct);
		}
		else if (zone is null)
		{
			// Removing zone - edge exists (zoneEdge is not null at this point)
			await arangoDb.Graph.Edge.RemoveAsync<object>(handle, DatabaseConstants.GraphZones, DatabaseConstants.HasZone,
				zoneEdge!.Key, cancellationToken: ct);
		}
		else
		{
			// Updating zone - edge exists (zoneEdge is not null at this point)
			await arangoDb.Graph.Edge.UpdateAsync(handle, DatabaseConstants.GraphZones, DatabaseConstants.HasZone,
				zoneEdge!.Key, new { _to = zone.Object().Id }, cancellationToken: ct);
		}
	}

	public async ValueTask UnsetObjectZone(AnySharpObject obj, CancellationToken ct = default)
		=> await SetObjectZone(obj, null, ct);

	public async ValueTask<bool> IsReachableViaParentOrZoneAsync(AnySharpObject startObject, AnySharpObject targetObject, int maxDepth = 100, CancellationToken ct = default)
	{
		// Use ArangoDB graph traversal to check if targetObject is reachable from startObject
		// following both parent and zone edges in a combined traversal
		// We traverse using the edge collections directly instead of named graphs
		var query = $@"
			FOR v IN 1..@maxDepth OUTBOUND @startVertex {DatabaseConstants.HasParent}, {DatabaseConstants.HasZone}
				OPTIONS {{uniqueVertices: 'global', order: 'bfs'}}
				FILTER v._id == @targetVertex
				LIMIT 1
				RETURN true
		";

		var bindVars = new Dictionary<string, object>
		{
			{ "startVertex", startObject.Object().Id! },
			{ "targetVertex", targetObject.Object().Id! },
			{ "maxDepth", maxDepth }
		};

		var result = await arangoDb.Query.ExecuteAsync<bool>(handle, query, bindVars, cancellationToken: ct);
		return result.FirstOrDefault(); // Returns true if found, false if not
	}

	public async ValueTask SetObjectOwner(AnySharpObject obj, SharpPlayer owner, CancellationToken ct = default)
	{
		var response = await arangoDb.Query.ExecuteAsync<string>(handle,
			$"FOR v,e IN 1..1 OUTBOUND @startVertex GRAPH {DatabaseConstants.GraphObjectOwners} RETURN e._key",
			new Dictionary<string, object> { { StartVertex, obj.Object().Id! } }, cancellationToken: ct);

		var contentEdgeKey = response.First();

		await arangoDb.Graph.Edge.UpdateAsync(handle, DatabaseConstants.GraphObjectOwners, DatabaseConstants.HasObjectOwner,
			contentEdgeKey, new { To = owner.Id }, cancellationToken: ct);
	}

	public async ValueTask SetObjectWarnings(AnySharpObject obj, WarningType warnings, CancellationToken ct = default)
		=> await arangoDb.Document.UpdateAsync(handle, DatabaseConstants.Objects,
			new
			{
				_key = obj.Object().Key.ToString(),
				Warnings = warnings
			}, cancellationToken: ct);
	public async ValueTask<AnyOptionalSharpObject> GetObjectNodeAsync(DBRef dbref,
		CancellationToken cancellationToken = default)
	{
		SharpObjectQueryResult? obj;
		try
		{
			obj = await arangoDb.Document.GetAsync<SharpObjectQueryResult>(handle, DatabaseConstants.Objects,
				dbref.Number.ToString(), cancellationToken: cancellationToken);
		}
		catch
		{
			obj = null;
		}

		if (obj is null
				|| dbref.CreationMilliseconds is not null
				&& obj.CreationTime != dbref.CreationMilliseconds)
			return new None();

		var startVertex = obj.Id;
		var res = (await arangoDb.Query.ExecuteAsync<SharpObjectQueryResult>(handle,
				$"FOR v IN 1..1 INBOUND {startVertex} GRAPH {DatabaseConstants.GraphObjects} RETURN v", cache: true,
				cancellationToken: cancellationToken))
			.FirstOrDefault();

		if (res is null) return new None();

		var id = res.Id;

		var convertObject = SharpObjectQueryToSharpObject(obj);

		return obj.Type switch
		{
			DatabaseConstants.TypeThing => new SharpThing
			{
				Id = id,
				Object = convertObject,
				Location = new(async ct => await mediator.Send(new GetCertainLocationQuery(id), ct)),
				Home = new(async ct => await GetHomeAsync(id, ct))
			},
			DatabaseConstants.TypePlayer => new SharpPlayer
			{
				Id = id,
				Object = convertObject,
				Aliases = res.Aliases,
				Location = new(async ct => await mediator.Send(new GetCertainLocationQuery(id), ct)),
				Home = new(async ct => await GetHomeAsync(id, ct)),
				PasswordHash = res.PasswordHash,
				PasswordSalt = res.PasswordSalt,
				Quota = res.Quota
			},
			DatabaseConstants.TypeRoom => new SharpRoom
			{
				Id = id,
				Object = convertObject,
				Location = new(async ct => await GetDropToAsync(id, ct))
			},
			DatabaseConstants.TypeExit => new SharpExit
			{
				Id = id,
				Object = convertObject,
				Aliases = res.Aliases,
				Location = new(async ct => await mediator.Send(new GetCertainLocationQuery(id), ct)),
				Home = new(async ct => await GetHomeAsync(id, ct))
			},
			_ => throw new ArgumentException($"Invalid Object Type found: '{obj.Type}'")
		};
	}

	private async ValueTask<AnyOptionalSharpObject> GetObjectNodeAsync(string dbId,
		CancellationToken cancellationToken = default)
	{
		ArangoList<System.Text.Json.JsonElement>? query;
		if (dbId.StartsWith(DatabaseConstants.Objects))
		{
			query = await arangoDb.Query.ExecuteAsync<System.Text.Json.JsonElement>(handle,
				$"FOR v IN 0..1 INBOUND {dbId} GRAPH {DatabaseConstants.GraphObjects} RETURN v",
				cache: true, cancellationToken: cancellationToken);
			query.Reverse();
		}
		else
		{
			query = await arangoDb.Query.ExecuteAsync<System.Text.Json.JsonElement>(handle,
				$"FOR v IN 0..1 OUTBOUND {dbId} GRAPH {DatabaseConstants.GraphObjects} RETURN v", cache: true,
				cancellationToken: cancellationToken);
		}

		var res = query.First();
		var obj = query.Last();

		var id = res.GetProperty("_id").GetString()!;
		var collection = id.Split("/")[0];

		var convertObject = SharpObjectQueryToSharpObject(obj);

		return collection switch
		{
			DatabaseConstants.Things => new SharpThing
			{
				Id = id, Object = convertObject,
				Location = new(async ct => await mediator.Send(new GetCertainLocationQuery(id), ct)),
				Home = new(async ct => await GetHomeAsync(id, ct))
			},
			DatabaseConstants.Players => new SharpPlayer
			{
				Id = id, Object = convertObject, Aliases = res.GetProperty("Aliases").EnumerateArray().Select(x => x.GetString()!).ToArray(),
				Location = new(async ct => await mediator.Send(new GetCertainLocationQuery(id), ct)),
				Home = new(async ct => await GetHomeAsync(id, ct)),
				PasswordHash = res.GetProperty("PasswordHash").GetString()!,
				PasswordSalt = res.TryGetProperty("PasswordSalt", out var saltProp) ? saltProp.GetString() : null,
				Quota = res.GetProperty("Quota").GetInt32()
			},
			DatabaseConstants.Rooms => new SharpRoom
			{
				Id = id,
				Object = convertObject,
				Location = new(async ct => await GetDropToAsync(id, ct))
			},
			DatabaseConstants.Exits => new SharpExit
			{
				Id = id, Object = convertObject, Aliases = res.GetProperty("Aliases").EnumerateArray().Select(x => x.GetString()!).ToArray(),
				Location = new(async ct => await mediator.Send(new GetCertainLocationQuery(id), ct)),
				Home = new(async ct => await GetHomeAsync(id, ct))
			},
			_ => throw new ArgumentException($"Invalid Object Type found: '{obj.GetProperty("Type").GetString()}'"),
		};
	}

	private SharpObject SharpObjectQueryToSharpObject(System.Text.Json.JsonElement obj)
	{
		var id = obj.GetProperty("_id").GetString()!;
		var type = obj.GetProperty("Type").GetString()!;
		return new SharpObject
		{
			Id = id,
			Key = int.Parse(obj.GetProperty("_key").GetString()!),
			Name = obj.GetProperty("Name").GetString()!,
			Type = type,
			CreationTime = obj.GetProperty("CreationTime").GetInt64(),
			ModifiedTime = obj.GetProperty("ModifiedTime").GetInt64(),
			Locks = ImmutableDictionary<string, Library.Models.SharpLockData>.Empty, // Empty locks for JSON element conversion
			Flags = new(() => GetObjectFlagsAsync(id, type.ToUpper(), CancellationToken.None)),
			Powers = new(() => GetPowersAsync(id, CancellationToken.None)),
			Attributes = new(() => GetTopLevelAttributesAsync(id, CancellationToken.None)),
			LazyAttributes = new(() => GetTopLevelLazyAttributesAsync(id, CancellationToken.None)),
			AllAttributes = new(() => GetAllAttributesAsync(id, CancellationToken.None)),
			LazyAllAttributes = new(() => GetAllLazyAttributesAsync(id, CancellationToken.None)),
			Owner = new(async ct => await GetObjectOwnerAsync(id, ct)),
			Parent = new(async ct => await GetParentAsync(id, ct)),
			Zone = new(async ct => await GetZoneAsync(id, ct)),
			Children = new(() => GetChildrenAsync(id, CancellationToken.None))
		};
	}

	public async ValueTask<SharpObject?> GetBaseObjectNodeAsync(DBRef dbref,
		CancellationToken cancellationToken = default)
	{
		var obj = await arangoDb.Document.GetAsync<SharpObjectQueryResult>(handle, DatabaseConstants.Objects,
			dbref.Number.ToString(), cancellationToken: cancellationToken);

		if (obj is null)
		{
			return null;
		}

		if (dbref.CreationMilliseconds.HasValue && obj.CreationTime != dbref.CreationMilliseconds)
		{
			return null;
		}

		return SharpObjectQueryToSharpObject(obj);
	}

	private SharpObject SharpObjectQueryToSharpObject(SharpObjectQueryResult obj) =>
		new()
		{
			Name = obj.Name,
			Type = obj.Type,
			Id = obj.Id,
			Key = int.Parse(obj.Key),
			Locks = (obj.Locks ?? [])
				.ToImmutableDictionary(
					kvp => kvp.Key,
					kvp =>
					{
						var flags = Library.Services.LockService.LockFlags.Default;
						if (!string.IsNullOrEmpty(kvp.Value.Flags))
						{
							if (!Enum.TryParse<Library.Services.LockService.LockFlags>(kvp.Value.Flags, out flags))
							{
								// If parsing fails (corrupted data), use Default flags
								flags = Library.Services.LockService.LockFlags.Default;
							}
						}
						return new Library.Models.SharpLockData(kvp.Value.LockString, flags);
					}),
			CreationTime = obj.CreationTime,
			ModifiedTime = obj.ModifiedTime,
			Flags =
				new Lazy<IAsyncEnumerable<SharpObjectFlag>>(() => GetObjectFlagsAsync(obj.Id, obj.Type.ToUpper(), CancellationToken.None)),
			Powers = new Lazy<IAsyncEnumerable<SharpPower>>(() => GetPowersAsync(obj.Id, CancellationToken.None)),
			Attributes =
				new Lazy<IAsyncEnumerable<SharpAttribute>>(() =>
					GetTopLevelAttributesAsync(obj.Id, CancellationToken.None)),
			LazyAttributes =
				new Lazy<IAsyncEnumerable<LazySharpAttribute>>(() =>
					GetTopLevelLazyAttributesAsync(obj.Id, CancellationToken.None)),
			AllAttributes =
				new Lazy<IAsyncEnumerable<SharpAttribute>>(() => GetAllAttributesAsync(obj.Id, CancellationToken.None)),
			LazyAllAttributes =
				new Lazy<IAsyncEnumerable<LazySharpAttribute>>(() =>
					GetAllLazyAttributesAsync(obj.Id, CancellationToken.None)),
			Owner = new AsyncLazy<SharpPlayer>(async ct => await GetObjectOwnerAsync(obj.Id, ct)),
			Parent = new AsyncLazy<AnyOptionalSharpObject>(async ct => await GetParentAsync(obj.Id, ct)),
			Zone = new AsyncLazy<AnyOptionalSharpObject>(async ct => await GetZoneAsync(obj.Id, ct)),
			Children = new Lazy<IAsyncEnumerable<SharpObject>?>(() => GetChildrenAsync(obj.Id, CancellationToken.None))
		};
	public async ValueTask SetLockAsync(SharpObject target, string lockName, Library.Models.SharpLockData lockData,
		CancellationToken ct = default)
	{
		var dbLockData = new SharpLockDataQueryResult
		{
			LockString = lockData.LockString,
			Flags = lockData.Flags.ToString()
		};

		await arangoDb.Document.UpdateAsync(handle, DatabaseConstants.Objects, new
		{
			_key = target.Key.ToString(),
			Locks = target.Locks
				.Select(kvp => new KeyValuePair<string, SharpLockDataQueryResult>(
					kvp.Key,
					new SharpLockDataQueryResult
					{
						LockString = kvp.Value.LockString,
						Flags = kvp.Value.Flags.ToString()
					}))
				.Append(new KeyValuePair<string, SharpLockDataQueryResult>(lockName, dbLockData))
				.ToDictionary(kvp => kvp.Key, kvp => kvp.Value)
		}, mergeObjects: true, cancellationToken: ct);
	}

	public async ValueTask UnsetLockAsync(SharpObject target, string lockName, CancellationToken ct = default)
	{
		await arangoDb.Document.UpdateAsync(handle, DatabaseConstants.Objects, new
		{
			_key = target.Key.ToString(),
			Locks = target.Locks
				.Where(kvp => kvp.Key != lockName)
				.Select(kvp => new KeyValuePair<string, SharpLockDataQueryResult>(
					kvp.Key,
					new SharpLockDataQueryResult
					{
						LockString = kvp.Value.LockString,
						Flags = kvp.Value.Flags.ToString()
					}))
				.ToDictionary(kvp => kvp.Key, kvp => kvp.Value)
		}, mergeObjects: true, cancellationToken: ct);
	}
	public IAsyncEnumerable<SharpPlayer> GetPlayerByNameOrAliasAsync(string name,
		CancellationToken ct = default)
		=> (arangoDb.Query.ExecuteStreamAsync<string>(handle,
				$"FOR v IN {DatabaseConstants.Objects} FILTER v.Type == @type && (v.Name == @name || @name IN v.Aliases) RETURN v._id",
				bindVars: new Dictionary<string, object>
				{
					{ "name", name },
					{ "type", DatabaseConstants.TypePlayer }
				}, cancellationToken: ct) ?? AsyncEnumerable.Empty<string>())
			.Select(GetObjectNodeAsync)
			.Select(x => x.AsPlayer);
	public async IAsyncEnumerable<SharpObject> GetAllObjectsAsync([EnumeratorCancellation] CancellationToken ct = default)
	{
		var objectIds = arangoDb.Query.ExecuteStreamAsync<string>(handle,
			$"FOR v IN {DatabaseConstants.Objects:@} RETURN v._id",
			cancellationToken: ct) ?? AsyncEnumerable.Empty<string>();

		await foreach (var id in objectIds.WithCancellation(ct))
		{
			var optionalObj = await GetObjectNodeAsync(id, ct);
			if (!optionalObj.IsNone)
			{
				yield return optionalObj.Known.Object();
			}
		}
	}

	public async IAsyncEnumerable<SharpObject> GetFilteredObjectsAsync(ObjectSearchFilter filter, [EnumeratorCancellation] CancellationToken ct = default)
	{
		// Build AQL query with filters applied at database level
		var filters = new List<string>();
		var bindVars = new Dictionary<string, object>();

		// Type filter
		if (filter.Types != null && filter.Types.Length > 0)
		{
			filters.Add("v.Type IN @types");
			bindVars["types"] = filter.Types;
		}

		// Name pattern filter (case-insensitive substring match or regex)
		if (!string.IsNullOrEmpty(filter.NamePattern))
		{
			if (filter.UseRegex)
			{
				// Use REGEX_TEST for regex matching (case-insensitive)
				filters.Add("REGEX_TEST(v.Name, @namePattern, true)");
			}
			else
			{
				// Use CONTAINS for substring matching (case-insensitive)
				filters.Add("CONTAINS(LOWER(v.Name), LOWER(@namePattern))");
			}
			bindVars["namePattern"] = filter.NamePattern;
		}

		// DBRef range filters
		if (filter.MinDbRef.HasValue)
		{
			filters.Add("TO_NUMBER(v._key) >= @minDbRef");
			bindVars["minDbRef"] = filter.MinDbRef.Value;
		}
		if (filter.MaxDbRef.HasValue)
		{
			filters.Add("TO_NUMBER(v._key) <= @maxDbRef");
			bindVars["maxDbRef"] = filter.MaxDbRef.Value;
		}

		// Owner filter - requires traversing the HasObjectOwner edge
		if (filter.Owner.HasValue)
		{
			filters.Add($@"LENGTH(FOR owner IN 1..1 OUTBOUND v._id GRAPH '{DatabaseConstants.GraphObjectOwners}' 
				FILTER owner._key == @ownerKey 
				LIMIT 1
				RETURN 1) > 0");
			bindVars["ownerKey"] = filter.Owner.Value.Number.ToString();
		}

		// Zone filter - requires checking zone relationship
		if (filter.Zone.HasValue)
		{
			filters.Add($@"LENGTH(FOR zone IN 1..1 OUTBOUND v._id GRAPH '{DatabaseConstants.GraphZones}' 
				FILTER zone._key == @zoneKey 
				LIMIT 1
				RETURN 1) > 0");
			bindVars["zoneKey"] = filter.Zone.Value.Number.ToString();
		}

		// Parent filter - requires checking parent relationship
		if (filter.Parent.HasValue)
		{
			filters.Add($@"LENGTH(FOR parent IN 1..1 OUTBOUND v._id GRAPH '{DatabaseConstants.GraphParents}' 
				FILTER parent._key == @parentKey 
				LIMIT 1
				RETURN 1) > 0");
			bindVars["parentKey"] = filter.Parent.Value.Number.ToString();
		}

		// Flag filter - requires checking flags array
		if (!string.IsNullOrEmpty(filter.HasFlag))
		{
			filters.Add("@flagName IN v.Flags[*].Name");
			bindVars["flagName"] = filter.HasFlag;
		}

		// Power filter - requires checking powers array
		if (!string.IsNullOrEmpty(filter.HasPower))
		{
			filters.Add("@powerName IN v.Powers[*].Name");
			bindVars["powerName"] = filter.HasPower;
		}

		// Build the complete query
		var filterClause = filters.Count > 0 ? $"FILTER {string.Join(" AND ", filters)}" : "";

		// Add LIMIT clause for pagination (START/COUNT)
		var limitClause = "";
		if (filter.Skip.HasValue || filter.Limit.HasValue)
		{
			var skip = filter.Skip ?? 0;
			// ArangoDB syntax: LIMIT offset, count or LIMIT count (when offset is 0)
			// When only skip is provided without limit, we skip but don't limit the count
			if (filter.Limit.HasValue)
			{
				limitClause = skip > 0 ? $"LIMIT {skip}, {filter.Limit.Value}" : $"LIMIT {filter.Limit.Value}";
			}
			else if (skip > 0)
			{
				// Skip without limit - use a very large number for count
				limitClause = $"LIMIT {skip}, 999999999";
			}
		}

		var query = $"FOR v IN {DatabaseConstants.Objects:@} {filterClause} {limitClause} RETURN v._id".Trim();

		var objectIds = arangoDb.Query.ExecuteStreamAsync<string>(handle, query, bindVars, cancellationToken: ct)
			?? AsyncEnumerable.Empty<string>();

		await foreach (var id in objectIds.WithCancellation(ct))
		{
			var optionalObj = await GetObjectNodeAsync(id, ct);
			if (!optionalObj.IsNone)
			{
				yield return optionalObj.Known.Object();
			}
		}
	}

	public async IAsyncEnumerable<SharpPlayer> GetAllPlayersAsync([EnumeratorCancellation] CancellationToken ct = default)
	{
		var playerIds = arangoDb.Query.ExecuteStreamAsync<string>(handle,
			$"FOR v IN {DatabaseConstants.Objects:@} FILTER v.Type == @playerType RETURN v._id",
			bindVars: new Dictionary<string, object> { { "playerType", DatabaseConstants.TypePlayer } },
			cancellationToken: ct) ?? AsyncEnumerable.Empty<string>();

		await foreach (var id in playerIds.WithCancellation(ct))
		{
			var optionalObj = await GetObjectNodeAsync(id, ct);
			if (!optionalObj.IsNone && optionalObj.IsPlayer)
			{
				yield return optionalObj.AsPlayer;
			}
		}
	}

	public async IAsyncEnumerable<SharpExit> GetEntrancesAsync(DBRef destination,
		[EnumeratorCancellation] CancellationToken ct = default)
	{
		// Query to find all exits that lead to the destination
		// Exits are connected to their destination via the AtLocation edge in GraphLocations
		var exitIds = arangoDb.Query.ExecuteStreamAsync<string>(handle,
			$@"FOR v, e IN 1..1 INBOUND @destination GRAPH @graph
			   FILTER v.Type == @exitType
			   RETURN v._id",
			bindVars: new Dictionary<string, object>
			{
				{ "destination", $"{DatabaseConstants.Objects}/{destination.Number}" },
				{ "graph", DatabaseConstants.GraphLocations },
				{ "exitType", DatabaseConstants.TypeExit }
			}, cancellationToken: ct) ?? AsyncEnumerable.Empty<string>();

		await foreach (var id in exitIds.WithCancellation(ct))
		{
			var optionalObj = await GetObjectNodeAsync(id, ct);
			if (!optionalObj.IsNone)
			{
				yield return optionalObj.AsExit;
			}
		}
	}

	public async ValueTask MoveObjectAsync(AnySharpContent enactorObj, AnySharpContainer destination,
		CancellationToken ct = default)
	{
		var edge = (await arangoDb.Query.ExecuteAsync<SharpEdgeQueryResult>(handle,
				$"FOR v,e IN 1..1 OUTBOUND {enactorObj.Id} GRAPH {DatabaseConstants.GraphLocations} RETURN e",
				cancellationToken: ct))
			.Single();

		await arangoDb.Graph.Edge.UpdateAsync(handle,
			DatabaseConstants.GraphLocations,
			DatabaseConstants.AtLocation,
			edge.Key,
			new
			{
				From = enactorObj.Id,
				To = destination.Id
			},
			waitForSync: true, cancellationToken: ct);
	}
	public async ValueTask SetPlayerPasswordAsync(SharpPlayer player, string password, string? salt = null, CancellationToken ct = default)
	{
		// If salt is provided (imported password), use the password as-is (it's already hashed)
		// Otherwise, hash the password for new passwords
		var hashed = salt != null
			? password
			: passwordService.HashPassword(player.Object.DBRef.ToString(), password);

		await arangoDb.Document.UpdateAsync(handle, DatabaseConstants.Players, new
		{
			player.Id,
			PasswordHash = hashed,
			PasswordSalt = salt
		}, mergeObjects: true, cancellationToken: ct);
	}

	public async ValueTask SetPlayerQuotaAsync(SharpPlayer player, int quota, CancellationToken ct = default)
	{
		await arangoDb.Document.UpdateAsync(handle, DatabaseConstants.Players, new
		{
			player.Id,
			Quota = quota
		}, mergeObjects: true, cancellationToken: ct);
	}

	public async ValueTask<int> GetOwnedObjectCountAsync(SharpPlayer player, CancellationToken ct = default)
	{
		// Query to count all objects owned by the player
		// Uses the HasObjectOwner edge in the GraphObjectOwners graph
		// HasObjectOwner edges go FROM Object TO Player, so we traverse INBOUND from the Player
		var query = $@"
			FOR v, e IN 1..1 INBOUND @playerId GRAPH {DatabaseConstants.GraphObjectOwners}
			COLLECT WITH COUNT INTO length
			RETURN length
		";

		var bindVars = new Dictionary<string, object>
		{
			{ "playerId", player.Id! }
		};

		var result = await arangoDb.Query.ExecuteAsync<int>(
			handle,
			query,
			bindVars: bindVars,
			cache: false,
			cancellationToken: ct);

		return result.FirstOrDefault();
	}

	#endregion
}
