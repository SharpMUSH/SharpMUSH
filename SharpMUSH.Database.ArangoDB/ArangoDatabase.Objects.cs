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
		var transaction = await arangoDb.Transaction.BeginAsync(handle,
			new ArangoTransaction()
			{
				Collections = new ArangoTransactionScope
				{
					Exclusive =
					[
						DatabaseConstants.Objects, DatabaseConstants.Rooms, DatabaseConstants.IsObject,
						DatabaseConstants.HasObjectOwner
					]
				}
			}, ct);

		try
		{
			var time = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

			var obj = await arangoDb.Document.CreateAsync(transaction, DatabaseConstants.Objects,
				new SharpObjectCreateRequest(name, DatabaseConstants.TypeRoom, [], time, time), cancellationToken: ct);
			var room = await arangoDb.Document.CreateAsync(transaction, DatabaseConstants.Rooms, new SharpRoomCreateRequest(),
				cancellationToken: ct);

			await arangoDb.Graph.Edge.CreateAsync(transaction, DatabaseConstants.GraphObjects, DatabaseConstants.IsObject,
				new SharpEdgeCreateRequest(room.Id, obj.Id), cancellationToken: ct);
			await arangoDb.Graph.Edge.CreateAsync(transaction, DatabaseConstants.GraphObjectOwners, DatabaseConstants.HasObjectOwner,
				new SharpEdgeCreateRequest(obj.Id, creator.Id!), cancellationToken: ct);

			await arangoDb.Transaction.CommitAsync(transaction, ct);
			return new DBRef(int.Parse(obj.Key), time);
		}
		catch
		{
			await arangoDb.Transaction.AbortAsync(transaction, ct);
			throw;
		}
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
		// Relinking must replace the destination, not accumulate a second HasHome edge.
		await UnlinkExitAsync(exit, ct);
		await arangoDb.Graph.Edge.CreateAsync(handle, DatabaseConstants.GraphHomes, DatabaseConstants.HasHome,
			new SharpEdgeCreateRequest(exit.Id!, location.Id), cancellationToken: ct);
		return true;
	}

	public async ValueTask<bool> UnlinkExitAsync(SharpExit exit, CancellationToken ct = default)
	{
		var result = await arangoDb.Query.ExecuteAsync<SharpEdgeQueryResult>(handle,
			// HasHome edges go FROM the exit TO its destination, so traverse OUTBOUND from the exit.
			$"FOR v, e IN 1..1 OUTBOUND {exit.Id} GRAPH {DatabaseConstants.GraphHomes} RETURN e", cancellationToken: ct);

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
		if (location.IsT3) // None
		{
			return await UnlinkRoomAsync(room, ct);
		}

		await UnlinkRoomAsync(room, ct);

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
		var transaction = await arangoDb.Transaction.BeginAsync(handle,
			new ArangoTransaction()
			{
				Collections = new ArangoTransactionScope
				{
					Exclusive =
					[
						DatabaseConstants.Objects, DatabaseConstants.Exits, DatabaseConstants.IsObject,
						DatabaseConstants.AtLocation, DatabaseConstants.HasHome, DatabaseConstants.HasObjectOwner
					]
				}
			}, ct);

		try
		{
			var time = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

			var obj = await arangoDb.Document.CreateAsync<SharpObjectCreateRequest, SharpObjectQueryResult>(transaction,
				DatabaseConstants.Objects,
				new SharpObjectCreateRequest(name, DatabaseConstants.TypeExit, [], time, time), cancellationToken: ct);
			var exit = await arangoDb.Document.CreateAsync(transaction, DatabaseConstants.Exits,
				new SharpExitCreateRequest(aliases), cancellationToken: ct);

			await arangoDb.Graph.Edge.CreateAsync(transaction, DatabaseConstants.GraphObjects, DatabaseConstants.IsObject,
				new SharpEdgeCreateRequest(exit.Id, obj.Id), cancellationToken: ct);
			await arangoDb.Graph.Edge.CreateAsync(transaction, DatabaseConstants.GraphLocations, DatabaseConstants.AtLocation,
				new SharpEdgeCreateRequest(exit.Id, location.Id), cancellationToken: ct);
			// No HasHome edge: a new exit is unlinked until @link points it somewhere, matching PennMUSH's
			// Destination() == NOTHING. Seeding it to the source room made every exit look self-linked.
			await arangoDb.Graph.Edge.CreateAsync(transaction, DatabaseConstants.GraphObjectOwners, DatabaseConstants.HasObjectOwner,
				new SharpEdgeCreateRequest(obj.Id, creator.Id!), cancellationToken: ct);

			await arangoDb.Transaction.CommitAsync(transaction, ct);
			return new DBRef(int.Parse(obj.Key), time);
		}
		catch
		{
			await arangoDb.Transaction.AbortAsync(transaction, ct);
			throw;
		}
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
			return;
		}

		if (zoneEdge is null)
		{
			await arangoDb.Graph.Edge.CreateAsync(handle, DatabaseConstants.GraphZones, DatabaseConstants.HasZone,
				new { _from = obj.Object().Id, _to = zone!.Object().Id }, cancellationToken: ct);
		}
		else if (zone is null)
		{
			await arangoDb.Graph.Edge.RemoveAsync<object>(handle, DatabaseConstants.GraphZones, DatabaseConstants.HasZone,
				zoneEdge!.Key, cancellationToken: ct);
		}
		else
		{
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
		return result.FirstOrDefault();
	}

	public async ValueTask SetObjectOwner(AnySharpObject obj, SharpPlayer owner, CancellationToken ct = default)
	{
		var transaction = await arangoDb.Transaction.BeginAsync(handle,
			new ArangoTransaction()
			{
				Collections = new ArangoTransactionScope
				{
					Exclusive = [DatabaseConstants.HasObjectOwner]
				}
			}, ct);

		try
		{
			var response = await arangoDb.Query.ExecuteAsync<string>(transaction,
				$"FOR v,e IN 1..1 OUTBOUND @startVertex GRAPH {DatabaseConstants.GraphObjectOwners} RETURN e._key",
				new Dictionary<string, object> { { StartVertex, obj.Object().Id! } }, cancellationToken: ct);

			var contentEdgeKey = response.FirstOrDefault()
				?? throw new InvalidOperationException($"No owner edge found for object {obj.Object().Id}");

			await arangoDb.Graph.Edge.UpdateAsync(transaction, DatabaseConstants.GraphObjectOwners, DatabaseConstants.HasObjectOwner,
				contentEdgeKey, new { To = owner.Id }, cancellationToken: ct);

			await arangoDb.Transaction.CommitAsync(transaction, ct);
		}
		catch
		{
			await arangoDb.Transaction.AbortAsync(transaction, ct);
			throw;
		}
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
		// Single AQL query that fetches both the Objects document and its typed vertex in one round-trip.
		// Uses FOR...IN to emit two rows: first the Objects doc, then the typed vertex.
		var query = await arangoDb.Query.ExecuteAsync<SharpObjectQueryResult>(handle,
			$"LET obj = DOCUMENT('{DatabaseConstants.Objects}', @key) " +
			$"FILTER obj != null " +
			$"LET typed = FIRST(FOR v IN 1..1 INBOUND obj GRAPH {DatabaseConstants.GraphObjects} RETURN v) " +
			$"FILTER typed != null " +
			$"FOR item IN [{ObjectWithRelations("obj")}, typed] RETURN item",
			new Dictionary<string, object> { { "key", dbref.Number.ToString() } },
			cache: true, cancellationToken: cancellationToken);

		if (query.Count < 2) return new None();

		var obj = query[0];
		var res = query[1];

		if (dbref.CreationMilliseconds is not null
				&& obj.CreationTime != dbref.CreationMilliseconds)
			return new None();

		var id = res.Id;

		var convertObject = SharpObjectQueryToSharpObject(obj);

		return obj.Type switch
		{
			DatabaseConstants.TypeThing => new SharpThing
			{
				Id = id,
				Object = convertObject,
				Location = new(ct => relations.LocationOf(id, convertObject.Id!, ct)),
				Home = new(ct => relations.HomeOf(id, convertObject.Id!, convertObject.Key, ct))
			},
			DatabaseConstants.TypePlayer => new SharpPlayer
			{
				Id = id,
				Object = convertObject,
				Aliases = res.Aliases,
				Location = new(ct => relations.LocationOf(id, convertObject.Id!, ct)),
				Home = new(ct => relations.HomeOf(id, convertObject.Id!, convertObject.Key, ct)),
				PasswordHash = res.PasswordHash,
				PasswordSalt = res.PasswordSalt,
				Quota = res.Quota
			},
			DatabaseConstants.TypeRoom => new SharpRoom
			{
				Id = id,
				Object = convertObject,
				Location = new(ct => relations.DropToOf(id, convertObject.Id!, convertObject.Key, ct))
			},
			DatabaseConstants.TypeExit => new SharpExit
			{
				Id = id,
				Object = convertObject,
				Aliases = res.Aliases,
				Location = new(ct => relations.LocationOf(id, convertObject.Id!, ct)),
				Home = new(ct => relations.ExitDestinationOf(id, convertObject.Id!, convertObject.Key, ct))
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
				$"FOR v IN 0..1 INBOUND @start GRAPH {DatabaseConstants.GraphObjects} RETURN {ObjectWithRelations("v")}",
				new Dictionary<string, object> { { "start", dbId } },
				cache: true, cancellationToken: cancellationToken);
			query.Reverse();
		}
		else
		{
			query = await arangoDb.Query.ExecuteAsync<System.Text.Json.JsonElement>(handle,
				$"FOR v IN 0..1 OUTBOUND @start GRAPH {DatabaseConstants.GraphObjects} RETURN {ObjectWithRelations("v")}",
				new Dictionary<string, object> { { "start", dbId } },
				cache: true, cancellationToken: cancellationToken);
		}

		if (query.Count < 2) return new None();

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
				Location = new(ct => relations.LocationOf(id, convertObject.Id!, ct)),
				Home = new(ct => relations.HomeOf(id, convertObject.Id!, convertObject.Key, ct))
			},
			DatabaseConstants.Players => new SharpPlayer
			{
				Id = id, Object = convertObject, Aliases = res.GetProperty("Aliases").EnumerateArray().Select(x => x.GetString()!).ToArray(),
				Location = new(ct => relations.LocationOf(id, convertObject.Id!, ct)),
				Home = new(ct => relations.HomeOf(id, convertObject.Id!, convertObject.Key, ct)),
				PasswordHash = res.GetProperty("PasswordHash").GetString()!,
				PasswordSalt = res.TryGetProperty("PasswordSalt", out var saltProp) ? saltProp.GetString() : null,
				Quota = res.GetProperty("Quota").GetInt32()
			},
			DatabaseConstants.Rooms => new SharpRoom
			{
				Id = id,
				Object = convertObject,
				Location = new(ct => relations.DropToOf(id, convertObject.Id!, convertObject.Key, ct))
			},
			DatabaseConstants.Exits => new SharpExit
			{
				Id = id, Object = convertObject, Aliases = res.GetProperty("Aliases").EnumerateArray().Select(x => x.GetString()!).ToArray(),
				Location = new(ct => relations.LocationOf(id, convertObject.Id!, ct)),
				Home = new(ct => relations.ExitDestinationOf(id, convertObject.Id!, convertObject.Key, ct))
			},
			_ => new None(),
		};
	}

	/// <summary>
	/// AQL: the Objects document <paramref name="variable"/> with its flag and power documents
	/// attached, so an object arrives with its relations in the round trip that loads it. Every
	/// <c>HasFlag</c> / <c>HasPower</c> then answers from the object; nothing re-reads storage
	/// through a loaded instance.
	/// </summary>
	private static string ObjectWithRelations(string variable) =>
		$"MERGE({variable}, {{ FlagDocs: (FOR f IN 1..1 OUTBOUND {variable} GRAPH {DatabaseConstants.GraphFlags} RETURN f), " +
		$"PowerDocs: (FOR p IN 1..1 OUTBOUND {variable} GRAPH {DatabaseConstants.GraphPowers} RETURN p) }})";

	private static readonly System.Text.Json.JsonSerializerOptions ArangoJson = new()
	{
		PropertyNamingPolicy = new Core.Arango.Serialization.Json.ArangoJsonDefaultPolicy()
	};

	/// <summary>
	/// The object's flags, materialised from the documents that rode along with the object. Every
	/// query that builds an object projects them (<see cref="ObjectWithRelations"/>); one that does
	/// not is a bug, not a slow path.
	/// </summary>
	private Lazy<IAsyncEnumerable<SharpObjectFlag>> FlagsOf(string id, string type, SharpObjectFlagQueryResult[]? docs)
	{
		var upperType = type.ToUpper();
		if (docs is null)
		{
			throw new InvalidOperationException("Object loaded without its flags: every query that builds an object must project its relations.");
		}

		var flags = docs.Select(SharpObjectFlagQueryToSharpFlag).Append(ObjectTypeFlag.For(upperType)).ToArray();
		return new(() => flags.ToAsyncEnumerable());
	}

	private Lazy<IAsyncEnumerable<SharpPower>> PowersOf(string id, SharpPowerQueryResult[]? docs)
	{
		if (docs is null)
		{
			throw new InvalidOperationException("Object loaded without its powers: every query that builds an object must project its relations.");
		}

		var powers = docs.Select(SharpPowerQueryToSharpPower).ToArray();
		return new(() => powers.ToAsyncEnumerable());
	}

	private static T[]? RelationDocs<T>(System.Text.Json.JsonElement obj, string property)
		=> obj.TryGetProperty(property, out var el) && el.ValueKind == System.Text.Json.JsonValueKind.Array
			? System.Text.Json.JsonSerializer.Deserialize<T[]>(el.GetRawText(), ArangoJson)
			: null;

	private SharpObject SharpObjectQueryToSharpObject(System.Text.Json.JsonElement obj)
	{
		var id = obj.GetProperty("_id").GetString()!;
		var key = int.Parse(obj.GetProperty("_key").GetString()!);
		var type = obj.GetProperty("Type").GetString()!;
		WarningType warnings = WarningType.None;
		if (obj.TryGetProperty("Warnings", out var warningsProp))
		{
			warnings = (WarningType)warningsProp.GetUInt32();
		}
		return new SharpObject
		{
			Id = id,
			Key = key,
			Name = obj.GetProperty("Name").GetString()!,
			Type = type,
			CreationTime = obj.GetProperty("CreationTime").GetInt64(),
			ModifiedTime = obj.GetProperty("ModifiedTime").GetInt64(),
			Warnings = warnings,
			Locks = ImmutableDictionary<string, Library.Models.SharpLockData>.Empty,
			// FreshAsyncEnumerable, not the iterator directly: the Lazy caches one instance that every
			// call site enumerates, and an async iterator's state machine is not safe to share. See #798.
			Flags = FlagsOf(id, type, RelationDocs<SharpObjectFlagQueryResult>(obj, "FlagDocs")),
			Powers = PowersOf(id, RelationDocs<SharpPowerQueryResult>(obj, "PowerDocs")),
			Attributes = new(() => new FreshAsyncEnumerable<SharpAttribute>(enumCt => GetTopLevelAttributesAsync(id, enumCt))),
			LazyAttributes = new(() => new FreshAsyncEnumerable<LazySharpAttribute>(enumCt => GetTopLevelLazyAttributesAsync(id, enumCt))),
			AllAttributes = new(() => new FreshAsyncEnumerable<SharpAttribute>(enumCt => GetAllAttributesAsync(id, enumCt))),
			LazyAllAttributes = new(() => new FreshAsyncEnumerable<LazySharpAttribute>(enumCt => GetAllLazyAttributesAsync(id, enumCt))),
			Owner = new(ct => relations.OwnerOf(id, key, ct)),
			Parent = new(ct => relations.ParentOf(id, key, ct)),
			Zone = new(ct => relations.ZoneOf(id, key, ct)),
			Children = new(() => new FreshAsyncEnumerable<SharpObject>(enumCt => GetChildrenAsync(id, enumCt)!))
		};
	}

	public async ValueTask<SharpObject?> GetBaseObjectNodeAsync(DBRef dbref,
		CancellationToken cancellationToken = default)
	{
		// DOCUMENT() + FILTER rather than Document.GetAsync: the latter raises ArangoException on a
		// missing document, so the "not found" contract this method advertises — and that namelist()
		// and GetObjectsByZoneAsync both branch on — was unreachable, and asking about a dbref that
		// does not exist threw instead of answering. The other two providers already return null.
		var result = await arangoDb.Query.ExecuteAsync<SharpObjectQueryResult>(handle,
			$"LET obj = DOCUMENT('{DatabaseConstants.Objects}', @key) FILTER obj != null RETURN {ObjectWithRelations("obj")}",
			bindVars: new Dictionary<string, object> { { "key", dbref.Number.ToString() } },
			cache: true, cancellationToken: cancellationToken);

		var obj = result.FirstOrDefault();

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
			Warnings = obj.Warnings,
			// FreshAsyncEnumerable, not the iterator directly: the Lazy caches one instance that every
			// call site enumerates, and an async iterator's state machine is not safe to share. See #798.
			Flags = FlagsOf(obj.Id, obj.Type, obj.FlagDocs),
			Powers = PowersOf(obj.Id, obj.PowerDocs),
			Attributes = new Lazy<IAsyncEnumerable<SharpAttribute>>(() => new FreshAsyncEnumerable<SharpAttribute>(enumCt => GetTopLevelAttributesAsync(obj.Id, enumCt))),
			LazyAttributes = new Lazy<IAsyncEnumerable<LazySharpAttribute>>(() => new FreshAsyncEnumerable<LazySharpAttribute>(enumCt => GetTopLevelLazyAttributesAsync(obj.Id, enumCt))),
			AllAttributes = new Lazy<IAsyncEnumerable<SharpAttribute>>(() => new FreshAsyncEnumerable<SharpAttribute>(enumCt => GetAllAttributesAsync(obj.Id, enumCt))),
			LazyAllAttributes = new Lazy<IAsyncEnumerable<LazySharpAttribute>>(() => new FreshAsyncEnumerable<LazySharpAttribute>(enumCt => GetAllLazyAttributesAsync(obj.Id, enumCt))),
			Owner = new(ct => relations.OwnerOf(obj.Id, int.Parse(obj.Key), ct)),
			Parent = new(ct => relations.ParentOf(obj.Id, int.Parse(obj.Key), ct)),
			Zone = new(ct => relations.ZoneOf(obj.Id, int.Parse(obj.Key), ct)),
			Children = new Lazy<IAsyncEnumerable<SharpObject>?>(() => new FreshAsyncEnumerable<SharpObject>(enumCt => GetChildrenAsync(obj.Id, enumCt)!))
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
				.Where(kvp => !string.Equals(kvp.Key, lockName, StringComparison.OrdinalIgnoreCase))
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
		// The Locks object must be REPLACED, not merged: a PATCH with mergeObjects
		// would merge the filtered dictionary into the stored one and re-add the
		// very key we are removing. mergeObjects:false replaces the Locks attribute
		// wholesale (other top-level attributes, absent from this patch, are untouched).
		await arangoDb.Document.UpdateAsync(handle, DatabaseConstants.Objects, new
		{
			_key = target.Key.ToString(),
			Locks = target.Locks
				.Where(kvp => !string.Equals(kvp.Key, lockName, StringComparison.OrdinalIgnoreCase))
				.Select(kvp => new KeyValuePair<string, SharpLockDataQueryResult>(
					kvp.Key,
					new SharpLockDataQueryResult
					{
						LockString = kvp.Value.LockString,
						Flags = kvp.Value.Flags.ToString()
					}))
				.ToDictionary(kvp => kvp.Key, kvp => kvp.Value)
		}, mergeObjects: false, cancellationToken: ct);
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

	public async IAsyncEnumerable<AnySharpObject> GetAllTypedObjectsAsync([EnumeratorCancellation] CancellationToken ct = default)
	{
		// Each call to GetObjectNodeAsync here is the *direct* database method, not the
		// mediator-routed GetObjectNodeQuery that passes through QueryCachingBehavior →
		// FusionCache per-key SemaphoreSlim.  This stream therefore does NOT contend with
		// concurrent player commands on the FusionCache lock.
		var objectIds = arangoDb.Query.ExecuteStreamAsync<string>(handle,
			$"FOR v IN {DatabaseConstants.Objects:@} RETURN v._id",
			cancellationToken: ct) ?? AsyncEnumerable.Empty<string>();

		await foreach (var id in objectIds.WithCancellation(ct))
		{
			var optionalObj = await GetObjectNodeAsync(id, ct);
			if (!optionalObj.IsNone)
			{
				yield return optionalObj.WithoutNone();
			}
		}
	}

	public async IAsyncEnumerable<SharpObject> GetFilteredObjectsAsync(ObjectSearchFilter filter, [EnumeratorCancellation] CancellationToken ct = default)
	{
		var filters = new List<string>();
		var bindVars = new Dictionary<string, object>();

		if (filter.Types != null && filter.Types.Length > 0)
		{
			filters.Add("v.Type IN @types");
			bindVars["types"] = filter.Types;
		}

		if (!string.IsNullOrEmpty(filter.NamePattern))
		{
			if (filter.UseRegex)
			{
				filters.Add("REGEX_TEST(v.Name, @namePattern, true)");
			}
			else
			{
				filters.Add("CONTAINS(LOWER(v.Name), LOWER(@namePattern))");
			}
			bindVars["namePattern"] = filter.NamePattern;
		}

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

		if (filter.Owner.HasValue)
		{
			// Two hops, not one. The ownership edge lands on the typed node_players vertex, whose _key is
			// an Arango-generated id — only the node_objects document is keyed by dbref. Comparing the
			// dbref to the player vertex's key matched nothing, ever; hop on to the owner's own object.
			filters.Add($@"LENGTH(FOR owner IN 1..1 OUTBOUND v._id GRAPH '{DatabaseConstants.GraphObjectOwners}'
				FOR ownerObject IN 1..1 OUTBOUND owner GRAPH '{DatabaseConstants.GraphObjects}'
				FILTER ownerObject._key == @ownerKey
				LIMIT 1
				RETURN 1) > 0");
			bindVars["ownerKey"] = filter.Owner.Value.Number.ToString();
		}

		if (filter.Zone.HasValue)
		{
			filters.Add($@"LENGTH(FOR zone IN 1..1 OUTBOUND v._id GRAPH '{DatabaseConstants.GraphZones}' 
				FILTER zone._key == @zoneKey 
				LIMIT 1
				RETURN 1) > 0");
			bindVars["zoneKey"] = filter.Zone.Value.Number.ToString();
		}

		if (filter.Parent.HasValue)
		{
			filters.Add($@"LENGTH(FOR parent IN 1..1 OUTBOUND v._id GRAPH '{DatabaseConstants.GraphParents}' 
				FILTER parent._key == @parentKey 
				LIMIT 1
				RETURN 1) > 0");
			bindVars["parentKey"] = filter.Parent.Value.Number.ToString();
		}

		if (!string.IsNullOrEmpty(filter.HasFlag))
		{
			// Flags are edges to node_object_flags, not an array on the object — `v.Flags[*].Name` read a
			// field that node_objects documents do not have, so the predicate was false for every row.
			// The Type disjunct reproduces the type-named flag GetObjectFlagsAsync synthesises, which
			// HelperFunctions.HasFlag sees and which no edge backs. Aliases count for the same reason
			// they count in that helper: Penn's ptab_flag holds them alongside the names.
			filters.Add($@"(LOWER(v.Type) == LOWER(@flagName) OR LENGTH(
				FOR flag IN 1..1 OUTBOUND v._id GRAPH '{DatabaseConstants.GraphFlags}'
				FILTER LOWER(flag.Name) == LOWER(@flagName)
					OR LOWER(@flagName) IN (FOR a IN (flag.Aliases OR []) RETURN LOWER(a))
				LIMIT 1
				RETURN 1) > 0)");
			bindVars["flagName"] = filter.HasFlag;
		}

		if (!string.IsNullOrEmpty(filter.HasPower))
		{
			// Same defect as HasFlag above, and matching on Alias too because HelperFunctions.HasPower
			// does. There is no synthesised type-power, so no Type disjunct here.
			filters.Add($@"LENGTH(
				FOR power IN 1..1 OUTBOUND v._id GRAPH '{DatabaseConstants.GraphPowers}'
				FILTER LOWER(power.Name) == LOWER(@powerName) OR LOWER(power.Alias) == LOWER(@powerName)
				LIMIT 1
				RETURN 1) > 0");
			bindVars["powerName"] = filter.HasPower;
		}

		var filterClause = filters.Count > 0 ? $"FILTER {string.Join(" AND ", filters)}" : "";

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

	public async IAsyncEnumerable<AnySharpContent> GetHomedAtAsync(DBRef home,
		[EnumeratorCancellation] CancellationToken ct = default)
	{
		var homeNode = await GetObjectNodeAsync(home, ct);
		if (homeNode.IsNone) yield break;

		// Home edges run FROM the content's typed vertex TO its home's typed vertex, so traverse
		// INBOUND from the home. Rooms come back too — a room's drop-to reuses this edge — and are
		// dropped, because a drop-to is not a home.
		var ids = arangoDb.Query.ExecuteStreamAsync<string>(handle,
			$"FOR v IN 1..1 INBOUND @{StartVertex} GRAPH {DatabaseConstants.GraphHomes} RETURN v._id",
			bindVars: new Dictionary<string, object> { { StartVertex, homeNode.Id()! } },
			cancellationToken: ct);

		await foreach (var id in ids.WithCancellation(ct))
		{
			var candidate = await GetObjectNodeAsync(id, ct);
			if (candidate.IsNone || candidate.IsRoom) continue;

			yield return candidate.Known.AsContent;
		}
	}

	public async IAsyncEnumerable<SharpExit> GetEntrancesAsync(DBRef destination,
		[EnumeratorCancellation] CancellationToken ct = default)
	{
		// An exit's destination is its home edge (at_location is its *source* room), so entrances are
		// the exit-shaped subset of what is homed here. Traversing at_location instead returned exits
		// leading OUT of the room, and doing it from the Objects document — which at_location never
		// touches — returned nothing at all.
		await foreach (var content in GetHomedAtAsync(destination, ct))
		{
			if (content.IsExit)
			{
				yield return content.AsExit;
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
		// Every caller passes an ALREADY-HASHED password (via PasswordService.HashPassword /
		// @password / @newpassword, or an imported PennMUSH hash when salt != null). Store it
		// verbatim; hashing here would double-hash and the character could never connect.
		await arangoDb.Document.UpdateAsync(handle, DatabaseConstants.Players, new
		{
			_key = ExtractKey(player.Id!),
			PasswordHash = password,
			PasswordSalt = salt
		}, mergeObjects: true, cancellationToken: ct);
	}

	public async ValueTask SetPlayerQuotaAsync(SharpPlayer player, int quota, CancellationToken ct = default)
	{
		await arangoDb.Document.UpdateAsync(handle, DatabaseConstants.Players, new
		{
			_key = ExtractKey(player.Id!),
			Quota = quota
		}, mergeObjects: true, cancellationToken: ct);
	}

	public async ValueTask<int> GetOwnedObjectCountAsync(SharpPlayer player, CancellationToken ct = default)
	{
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

	public async ValueTask<int> GetObjectCountAsync(CancellationToken ct = default)
	{
		var result = await arangoDb.Query.ExecuteAsync<int>(
			handle,
			$"FOR v IN {DatabaseConstants.Objects:@} COLLECT WITH COUNT INTO length RETURN length",
			cache: false,
			cancellationToken: ct);

		return result.FirstOrDefault();
	}

	public async ValueTask<bool> DeleteObjectAsync(DBRef dbref, CancellationToken ct = default)
	{
		var node = await GetObjectNodeAsync(dbref, ct);
		if (node.IsNone)
		{
			return false;
		}

		var known = node.Known;
		var objectId = known.Object().Id!;
		var typedId = known.Id()!;
		var name = known.Object().Name;

		// The attribute subtree hangs off the typed vertex (see GraphAttributes' TECH DEBT note), and
		// each attribute carries its own flag/entry/owner edges, so the whole tree has to come along.
		var attributeIds = await arangoDb.Query.ExecuteAsync<string>(handle,
			$"FOR v IN 1..999 OUTBOUND @{StartVertex} GRAPH {DatabaseConstants.GraphAttributes} RETURN v._id",
			bindVars: new Dictionary<string, object> { { StartVertex, typedId } }, cancellationToken: ct);

		// Expanded object data (node_object_data) hangs off the Objects document.
		var objectDataIds = await arangoDb.Query.ExecuteAsync<string>(handle,
			$"FOR v IN 1..1 OUTBOUND @{StartVertex} GRAPH {DatabaseConstants.GraphObjectData} RETURN v._id",
			bindVars: new Dictionary<string, object> { { StartVertex, objectId } }, cancellationToken: ct);

		// Mail *received* dies with the object (PennMUSH clear_player -> do_mail_clear + do_mail_purge).
		// Mail it sent to others survives with a dangling sender; MailFromAsync already yields None there.
		var receivedMailIds = await arangoDb.Query.ExecuteAsync<string>(handle,
			$"FOR v IN 1..1 OUTBOUND @{StartVertex} GRAPH {DatabaseConstants.GraphMail} " +
			"FILTER IS_SAME_COLLECTION(@mailCollection, v) RETURN v._id",
			bindVars: new Dictionary<string, object>
			{
				{ StartVertex, typedId },
				{ "mailCollection", DatabaseConstants.Mails }
			}, cancellationToken: ct);

		string[] doomedVertices = new[] { objectId, typedId }
			.Concat(attributeIds)
			.Concat(objectDataIds)
			.Concat(receivedMailIds)
			.ToArray();

		var transaction = await arangoDb.Transaction.BeginAsync(handle,
			new ArangoTransaction
			{
				LockTimeout = DatabaseBehaviorConstants.TransactionTimeout,
				WaitForSync = true,
				Collections = new ArangoTransactionScope
				{
					Exclusive =
					[
						DatabaseConstants.Objects, DatabaseConstants.Players, DatabaseConstants.Rooms,
						DatabaseConstants.Things, DatabaseConstants.Exits, DatabaseConstants.Attributes,
						DatabaseConstants.ObjectData, DatabaseConstants.Mails,
						.. DatabaseConstants.edgeCollections
					]
				}
			}, ct);

		try
		{
			// Sweep every edge incident to a doomed vertex in either direction. The inbound half is what
			// unsets other objects' parent/zone/home/location references to us, standing in for the
			// db_top scan in PennMUSH free_object() (src/destroy.c) that a graph store cannot do.
			foreach (var edgeCollection in DatabaseConstants.edgeCollections)
			{
				await arangoDb.Query.ExecuteAsync<ArangoVoid>(transaction,
					"FOR e IN @@edge FILTER e._from IN @ids OR e._to IN @ids REMOVE e IN @@edge",
					bindVars: new Dictionary<string, object>
					{
						{ "@edge", edgeCollection },
						{ "ids", doomedVertices }
					}, cancellationToken: ct);
			}

			// Attribute leaves before their branches, so a mid-flight failure cannot orphan a subtree.
			IEnumerable<string> documentOrder = attributeIds.Reverse<string>()
				.Concat(objectDataIds)
				.Concat(receivedMailIds)
				.Concat(new[] { typedId, objectId });

			foreach (var vertexId in documentOrder)
			{
				var parts = vertexId.Split('/', 2);
				await arangoDb.Query.ExecuteAsync<ArangoVoid>(transaction,
					"FOR d IN @@c FILTER d._key == @key REMOVE d IN @@c",
					bindVars: new Dictionary<string, object> { { "@c", parts[0] }, { "key", parts[1] } },
					cancellationToken: ct);
			}

			await arangoDb.Transaction.CommitAsync(transaction, ct);
		}
		catch
		{
			await arangoDb.Transaction.AbortAsync(transaction, ct);
			throw;
		}

		logger.LogInformation("Deleted object #{DbRef} ({Name}) from the database", dbref.Number, name);

		return true;
	}

	#endregion
}
