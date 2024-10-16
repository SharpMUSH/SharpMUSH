﻿using Core.Arango;
using Core.Arango.Migration;
using Core.Arango.Protocol;
using Microsoft.Extensions.Logging;
using OneOf.Types;
using SharpMUSH.Database.Models;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services;
using System.Collections.Immutable;

namespace SharpMUSH.Database.ArangoDB;

// TODO: Unit of Work / Transaction around all of this! Otherwise it risks the stability of the Database.
// TODO: Critical!
public class ArangoDatabase(
	ILogger<ArangoDatabase> logger,
	IArangoContext arangoDB,
	ArangoHandle handle,
	IPasswordService passwordService
) : ISharpDatabase
{
	public async Task Migrate()
	{
		logger.LogInformation("Migrating Database");

		var migrator = new ArangoMigrator(arangoDB)
		{
			HistoryCollection = "MigrationHistory"
		};

		if (!await migrator.Context.Database.ExistAsync(handle))
		{
			await migrator.Context.Database.CreateAsync(handle);
		}

		migrator.AddMigrations(typeof(ArangoDatabase).Assembly);
		await migrator.UpgradeAsync(handle);

		logger.LogInformation("Migration Completed.");
	}

	public async Task<DBRef> CreatePlayerAsync(string name, string password, DBRef location)
	{
		var time = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
		var objectLocation = await GetObjectNodeAsync(location);

		var transaction = new ArangoTransaction()
		{
			LockTimeout = DatabaseBehaviorConstants.TransactionTimeout,
			WaitForSync = true,
			Collections = new ArangoTransactionScope
			{
				Write =
				[
					DatabaseConstants.objects,
					DatabaseConstants.players,
					DatabaseConstants.isObject,
					DatabaseConstants.hasObjectOwner,
					DatabaseConstants.atLocation,
					DatabaseConstants.hasHome
				]
			}
		};

		var transactionHandle = await arangoDB.Transaction.BeginAsync(handle, transaction);

		var obj = await arangoDB.Document.CreateAsync<SharpObjectCreateRequest, SharpObjectQueryResult>(
			transactionHandle,
			DatabaseConstants.objects,
			new SharpObjectCreateRequest(
				name,
				DatabaseConstants.typePlayer,
				[],
				time,
				time
			),
			returnNew: true);

		var hashedPassword = passwordService.HashPassword($"#{obj.New.Key}:{obj.New.CreationTime}", password);

		var playerResult = await arangoDB.Document.CreateAsync<SharpPlayerCreateRequest, SharpPlayerQueryResult>(
			transactionHandle,
			DatabaseConstants.players,
			new SharpPlayerCreateRequest([], hashedPassword));

		await arangoDB.Document.CreateAsync(
			transactionHandle,
			DatabaseConstants.isObject,
			new SharpEdgeCreateRequest(playerResult.Id, obj.Id));

		await arangoDB.Document.CreateAsync(
			transactionHandle,
			DatabaseConstants.hasObjectOwner,
			new SharpEdgeCreateRequest(playerResult.Id, playerResult.Id));

		var idx = objectLocation.Match(
			player => player.Id,
			room => room.Id,
			exit => throw new ArgumentException("An Exit is not a valid location to create a player!"),
			thing => thing.Id,
			none => throw new ArgumentException("A player must have a valid creation location!"));

		await arangoDB.Document.CreateAsync(
			transactionHandle,
			DatabaseConstants.atLocation,
			new SharpEdgeCreateRequest(playerResult.Id, idx!));

		await arangoDB.Document.CreateAsync(
			transactionHandle,
			DatabaseConstants.hasHome,
			new SharpEdgeCreateRequest(playerResult.Id, idx!));

		await arangoDB.Transaction.CommitAsync(transactionHandle);

		return new DBRef(int.Parse(obj.Key), time);
	}

	public async Task<DBRef> CreateRoomAsync(string name, SharpPlayer creator)
	{
		var time = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

		var obj = await arangoDB.Document.CreateAsync(handle, DatabaseConstants.objects,
			new SharpObjectCreateRequest(name, DatabaseConstants.typeRoom, [], time, time));
		var room = await arangoDB.Document.CreateAsync(handle, DatabaseConstants.rooms, new SharpRoomCreateRequest());

		await arangoDB.Document.CreateAsync(handle, DatabaseConstants.isObject,
			new SharpEdgeCreateRequest(room.Id, obj.Id));
		await arangoDB.Document.CreateAsync(handle, DatabaseConstants.hasObjectOwner,
			new SharpEdgeCreateRequest(room.Id, creator.Id!));

		return new DBRef(int.Parse(obj.Key), time);
	}

	public async Task<DBRef> CreateThingAsync(string name, AnySharpContainer location, SharpPlayer creator)
	{
		var time = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

		var obj = await arangoDB.Document.CreateAsync<SharpObjectCreateRequest, SharpObjectQueryResult>(handle,
			DatabaseConstants.objects,
			new SharpObjectCreateRequest(name, DatabaseConstants.typeThing, [], time, time));
		var thing = await arangoDB.Document.CreateAsync(handle, DatabaseConstants.things, new SharpThingCreateRequest([]));

		await arangoDB.Document.CreateAsync(handle, DatabaseConstants.isObject,
			new SharpEdgeCreateRequest(thing.Id, obj.Id));

		var idx = location.Id;

		await arangoDB.Document.CreateAsync(handle, DatabaseConstants.atLocation,
			new SharpEdgeCreateRequest(thing.Id, idx));
		await arangoDB.Document.CreateAsync(handle, DatabaseConstants.hasHome, new SharpEdgeCreateRequest(thing.Id, idx));
		await arangoDB.Document.CreateAsync(handle, DatabaseConstants.hasObjectOwner,
			new SharpEdgeCreateRequest(thing.Id, creator.Id!));

		return new DBRef(int.Parse(obj.Key), time);
	}

	public async Task<DBRef> CreateExitAsync(string name, AnySharpContainer location, SharpPlayer creator)
	{
		var time = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

		var obj = await arangoDB.Document.CreateAsync<SharpObjectCreateRequest, SharpObjectQueryResult>(handle,
			DatabaseConstants.objects,
			new SharpObjectCreateRequest(name, DatabaseConstants.typeExit, [], time, time));
		var exit = await arangoDB.Document.CreateAsync(handle, DatabaseConstants.exits, new SharpExitCreateRequest([]));

		var idx = location.Object()!.Id!;

		await arangoDB.Document.CreateAsync(handle, DatabaseConstants.isObject,
			new SharpEdgeCreateRequest(exit.Id, obj.Id));
		await arangoDB.Document.CreateAsync(handle, DatabaseConstants.hasHome, new SharpEdgeCreateRequest(exit.Id, idx));
		await arangoDB.Document.CreateAsync(handle, DatabaseConstants.hasObjectOwner,
			new SharpEdgeCreateRequest(exit.Id, idx));

		return new DBRef(int.Parse(obj.Key), time);
	}

	public AnyOptionalSharpObject GetObjectNode(DBRef dbref)
		=> GetObjectNodeAsync(dbref).Result;

	public AnyOptionalSharpObject GetObjectNode(string dbId)
		=> GetObjectNodeAsync(dbId).Result;

	private IEnumerable<SharpPower> GetPowers(string id)
	{
		var result = arangoDB.Query.ExecuteAsync<SharpPowerQueryResult>(handle,
			$"FOR v IN 1..1 OUTBOUND {id} GRAPH {DatabaseConstants.graphPowers} RETURN v").Result;
		return result.Select(x => new SharpPower()
		{
			Alias = x.Alias,
			Name = x.Alias,
			System = x.System,
			SetPermissions = x.SetPermissions,
			TypeRestrictions = x.TypeRestrictions,
			UnsetPermissions = x.UnsetPermissions,
			Id = x.Id
		});
	}

	private IEnumerable<SharpObjectFlag> GetFlags(string id)
		=> arangoDB.Query.ExecuteAsync<SharpObjectFlag>(handle,
			$"FOR v IN 1..1 OUTBOUND {id} GRAPH {DatabaseConstants.graphFlags} RETURN v").Result;

	private async Task<IEnumerable<SharpAttributeFlag>> GetAttributeFlagsAsync(string id)
	{
		var result = await arangoDB.Query.ExecuteAsync<SharpAttributeFlagQueryResult>(handle,
			$"FOR v in 1..1 OUTBOUND @startVertex GRAPH {DatabaseConstants.graphAttributeFlags} RETURN v",
			new Dictionary<string, object>() { { "startVertex", id } });
		return result.Select(x =>
			new SharpAttributeFlag()
			{
				Name = x.Name,
				Symbol = x.Symbol,
				System = x.System,
				Id = x.Id
			});
	}

	private IEnumerable<SharpAttributeFlag> GetAttributeFlags(string id) =>
		GetAttributeFlagsAsync(id).Result;

	private IEnumerable<SharpAttribute> GetAttributes(string id)
	{
		// This only works for when we get a non-attribute as our ID.
		// Adjustment is needed if we get an attribute ID.
		IEnumerable<SharpAttributeQueryResult> sharpAttributeResults;
		if (id.StartsWith(DatabaseConstants.attributes))
		{
			sharpAttributeResults = arangoDB.Query.ExecuteAsync<SharpAttributeQueryResult>(handle,
				$"FOR v IN 1..1 OUTBOUND @startVertex GRAPH {DatabaseConstants.graphAttributes} RETURN v",
				new Dictionary<string, object>() { { "startVertex", id } }).Result;
		}
		else
		{
			sharpAttributeResults = arangoDB.Query.ExecuteAsync<SharpAttributeQueryResult>(handle,
				$"LET start = FIRST(FOR v IN 1..1 INBOUND @startVertex GRAPH {DatabaseConstants.graphObjects} RETURN v) FOR v IN 1..1 OUTBOUND start GRAPH {DatabaseConstants.graphAttributes} RETURN v",
				new Dictionary<string, object>() { { "startVertex", id } }).Result;
		}

		var sharpAttributes = sharpAttributeResults.Select(x => new SharpAttribute()
		{
			Flags = () => GetAttributeFlags(x.Id),
			Name = x.Name,
			LongName = x.LongName,
			Owner = () => GetAttributeOwner(x.Id),
			Value = MarkupString.MarkupStringModule.single(x.Value), // TODO: Compose and Decompose
			Leaves = () => GetAttributes(x.Id),
			SharpAttributeEntry = () => null // TODO: Fix
		});

		return sharpAttributes;
	}

	private SharpPlayer GetObjectOwner(string id)
	{
		var owner = arangoDB.Query.ExecuteAsync<SharpPlayerQueryResult>(handle,
			$"FOR v IN 1..1 OUTBOUND {id} GRAPH {DatabaseConstants.graphObjectOwners} RETURN v").Result.First();

		var populatedOwner = GetObjectNodeAsync(owner.Id).Result;

		return populatedOwner.AsPlayer;
	}

	private SharpPlayer GetAttributeOwner(string id)
	{
		var owner = arangoDB.Query.ExecuteAsync<SharpObjectQueryResult>(handle,
			$"FOR v IN 1..1 OUTBOUND {id} GRAPH {DatabaseConstants.graphAttributeOwners} RETURN v").Result.First();

		var populatedOwner = GetObjectNodeAsync(owner.Id).Result;

		return populatedOwner.AsPlayer;
	}

	public SharpObject? GetParent(string id)
		=> arangoDB.Query.ExecuteAsync<SharpObject>(handle,
			$"FOR v IN 1..1 OUTBOUND {id} GRAPH {DatabaseConstants.graphParents} RETURN v").Result.FirstOrDefault();

	public IEnumerable<SharpObject> GetParents(string id)
		=> arangoDB.Query.ExecuteAsync<SharpObject>(handle,
			$"FOR v IN 1 OUTBOUND {id} GRAPH {DatabaseConstants.graphParents} RETURN v").Result;

	private AnySharpContainer GetHome(string id)
	{
		var homeId = arangoDB.Query.ExecuteAsync<string>(handle,
			$"FOR v IN 1..1 OUTBOUND {id} GRAPH {DatabaseConstants.graphHomes} RETURN v._id").Result.First();
		var homeObject = GetObjectNodeAsync(homeId).Result;

		return homeObject.Match<AnySharpContainer>(
			player => player,
			room => room,
			exit => throw new Exception("Invalid Location found"),
			thing => thing,
			none => throw new Exception("Invalid Location found"));
	}

	private AnySharpContainer GetLocation(string id)
	{
		var locationId = arangoDB.Query.ExecuteAsync<string>(handle,
			$"FOR v IN 1..1 OUTBOUND {id} GRAPH {DatabaseConstants.graphLocations} RETURN v._id").Result.First();
		var locationObject = GetObjectNodeAsync(locationId).Result;

		return locationObject.Match<AnySharpContainer>(
			player => player,
			room => room,
			exit => throw new Exception("Invalid Location found"),
			thing => thing,
			none => throw new Exception("Invalid Location found"));
	}

	public async Task<AnyOptionalSharpObject> GetObjectNodeAsync(DBRef dbref)
	{
		var obj = await arangoDB.Document.GetAsync<SharpObjectQueryResult>(handle, DatabaseConstants.objects,
			dbref.Number.ToString());

		if (obj is null) return new None();
		if (dbref.CreationMilliseconds is not null && obj.CreationTime != dbref.CreationMilliseconds) return new None();

		var startVertex = obj.Id;
		var res = (await arangoDB.Query.ExecuteAsync<dynamic>(handle,
			$"FOR v IN 1..1 INBOUND {startVertex} GRAPH {DatabaseConstants.graphObjects} RETURN v")).FirstOrDefault();

		if (res is null) return new None();

		string id = res._id;

		var convertObject = new SharpObject()
		{
			Name = obj.Name,
			Type = obj.Type,
			CreationTime = obj.CreationTime,
			ModifiedTime = obj.ModifiedTime,
			Locks = (obj.Locks ?? []).ToImmutableDictionary(),
			Id = obj.Id,
			Key = int.Parse(obj.Key),
			Flags = () => GetFlags(startVertex),
			Powers = () => GetPowers(startVertex),
			Attributes = () => GetAttributes(startVertex),
			Owner = () => GetObjectOwner(startVertex),
			Parent = () => GetParent(startVertex)
		};

		return obj.Type switch
		{
			DatabaseConstants.typeThing => new SharpThing
				{ Id = id, Object = convertObject, Location = () => GetLocation(id), Home = () => GetHome(id) },
			DatabaseConstants.typePlayer => new SharpPlayer
			{
				Id = id, Object = convertObject, Aliases = res.Aliases.ToObject<string[]>(), Location = () => GetLocation(id),
				Home = () => GetHome(id), PasswordHash = res.PasswordHash
			},
			DatabaseConstants.typeRoom => new SharpRoom { Id = id, Object = convertObject },
			DatabaseConstants.typeExit => new SharpExit
			{
				Id = id, Object = convertObject, Aliases = res.Aliases.ToObject<string[]>(), Location = () => GetLocation(id),
				Home = () => GetHome(id)
			},
			_ => throw new ArgumentException($"Invalid Object Type found: '{obj.Type}'"),
		};
	}

	private async Task<AnyOptionalSharpObject> GetObjectNodeAsync(string dbID)
	{
		ArangoList<dynamic>? query;
		if (dbID.StartsWith(DatabaseConstants.objects))
		{
			query = await arangoDB.Query.ExecuteAsync<dynamic>(handle,
				$"FOR v IN 0..1 INBOUND {dbID} GRAPH {DatabaseConstants.graphObjects} RETURN v");
			query.Reverse();
		}
		else
		{
			query = await arangoDB.Query.ExecuteAsync<dynamic>(handle,
				$"FOR v IN 0..1 OUTBOUND {dbID} GRAPH {DatabaseConstants.graphObjects} RETURN v");
		}

		var res = query.First();
		var obj = query.Last();

		string id = res._id;
		string objId = obj._id;
		string collection = id.Split("/")[0];
		var convertObject = new SharpObject()
		{
			Id = objId,
			Key = int.Parse((string)obj._key),
			Name = obj.Name,
			Type = obj.Type,
			CreationTime = obj.CreationTime,
			ModifiedTime = obj.ModifiedTime,
			Locks = ImmutableDictionary<string, string>
				.Empty, // FIX: ((Dictionary<string, string>?)obj.Locks ?? []).ToImmutableDictionary(),
			Flags = () => GetFlags(objId),
			Powers = () => GetPowers(objId),
			Attributes = () => GetAttributes(objId),
			Owner = () => GetObjectOwner(objId),
			Parent = () => GetParent(objId)
		};

		return collection switch
		{
			DatabaseConstants.things => new SharpThing
				{ Id = id, Object = convertObject, Location = () => GetLocation(id), Home = () => GetHome(id) },
			DatabaseConstants.players => new SharpPlayer
			{
				Id = id, Object = convertObject, Aliases = res.Aliases.ToObject<string[]>(), Location = () => GetLocation(id),
				Home = () => GetHome(id), PasswordHash = res.PasswordHash
			},
			DatabaseConstants.rooms => new SharpRoom { Id = id, Object = convertObject },
			DatabaseConstants.exits => new SharpExit
			{
				Id = id, Object = convertObject, Aliases = res.Aliases.ToObject<string[]>(), Location = () => GetLocation(id),
				Home = () => GetHome(id)
			},
			_ => throw new ArgumentException($"Invalid Object Type found: '{obj.Type}'"),
		};
	}

	public async Task<SharpObject?> GetBaseObjectNodeAsync(DBRef dbref)
	{
		var obj = await arangoDB.Document.GetAsync<SharpObjectQueryResult>(handle, DatabaseConstants.objects,
			dbref.Number.ToString());

		if (dbref.CreationMilliseconds.HasValue && obj.CreationTime != dbref.CreationMilliseconds)
		{
			return null;
		}

		return obj is null
			? null
			: new SharpObject()
			{
				Name = obj.Name,
				Type = obj.Type,
				Id = obj.Id,
				Key = int.Parse(obj.Key),
				Locks = (obj.Locks ?? []).ToImmutableDictionary(),
				CreationTime = obj.CreationTime,
				ModifiedTime = obj.ModifiedTime,
				Flags = () => GetFlags(obj.Id),
				Powers = () => GetPowers(obj.Id),
				Attributes = () => GetAttributes(obj.Id),
				Owner = () => GetObjectOwner(obj.Id),
				Parent = () => GetParent(obj.Id)
			};
	}

	public async Task<IEnumerable<SharpAttribute>?> GetAttributesAsync(DBRef dbref, string attribute_pattern)
	{
		var startVertex = $"{DatabaseConstants.objects}/{dbref.Number}";
		var result = await arangoDB.Query.ExecuteAsync<dynamic>(handle, $"RETURN DOCUMENT({startVertex})");
		var pattern = attribute_pattern.Replace("_", "\\_").Replace("%", "\\%").Replace("?", "_").Replace("*", "%");

		if (!result.Any())
		{
			return null;
		}

		// TODO: This is a lazy implementation and does not appropriately support the ` section of pattern matching for attribute trees.

		// OPTIONS { indexHint: "inverted_index_name", forceIndexHint: true }
		// This doesn't seem like it can be done on a GRAPH query?
		const string query =
			$"FOR v IN 1 OUTBOUND @startVertex GRAPH {DatabaseConstants.graphAttributes} FILTER v.LongName LIKE @pattern RETURN v";

		var result2 = await arangoDB.Query.ExecuteAsync<dynamic>(handle, query, new Dictionary<string, object>()
		{
			{ "startVertex", startVertex },
			{ "pattern", pattern }
		});

		return result2.Select(x => new SharpAttribute()
		{
			Flags = () => GetAttributeFlags(x._id),
			Name = x.Name,
			Value = x.Value,
			LongName = x.LongName,
			Leaves = () => GetAttributes(x._id),
			Owner = () => GetObjectOwner(x._id),
			SharpAttributeEntry = () => null // TODO: Fix
		});
	}

	public async Task<IEnumerable<SharpAttribute>?> GetAttributesRegexAsync(DBRef dbref, string attribute_pattern)
	{
		var startVertex = $"{DatabaseConstants.objects}/{dbref.Number}";
		var result = await arangoDB.Query.ExecuteAsync<dynamic>(handle, $"RETURN DOCUMENT({startVertex})");

		if (!result.Any())
		{
			return null;
		}

		// TODO: Create an Inverted Index on LongName.
		var query =
			$"FOR v IN 1 OUTBOUND @startVertex GRAPH {DatabaseConstants.graphAttributes} FILTER v.LongName =~ @pattern RETURN v";

		var result2 = await arangoDB.Query.ExecuteAsync<dynamic>(handle, query, new Dictionary<string, object>()
		{
			{ "startVertex", startVertex },
			{ "pattern", attribute_pattern }
		});

		return result2.Select(x => new SharpAttribute()
		{
			Flags = () => GetAttributeFlags(x._id),
			Name = x.Name,
			Value = x.Value,
			LongName = x.LongName,
			Leaves = () => GetAttributes(x._id),
			Owner = () => GetObjectOwner(x._id),
			SharpAttributeEntry = () => null // TODO: Fix
		}).ToArray();
	}

	public async Task SetLockAsync(SharpObject target, string lockName, string lockString)
		=> await arangoDB.Document.UpdateAsync(handle, DatabaseConstants.objects, new
		{
			target.Key,
			Locks = target.Locks.Add(lockName, lockString)
		}, mergeObjects: true);

	public async Task<IEnumerable<SharpAttribute>?> GetAttributeAsync(DBRef dbref, params string[] attribute)
	{
		var startVertex = $"{DatabaseConstants.objects}/{dbref.Number}";

		const string let =
			$"LET start = FIRST(FOR v IN 1..1 INBOUND @startVertex GRAPH {DatabaseConstants.graphObjects} RETURN v)";
		const string query =
			$"{let} FOR v,e,p IN 1..@max OUTBOUND start GRAPH {DatabaseConstants.graphAttributes} PRUNE condition = NTH(@attr,LENGTH(p.edges)-1) != v.Name FILTER !condition RETURN v";

		var result = await arangoDB.Query.ExecuteAsync<SharpAttributeQueryResult>(handle, query,
			new Dictionary<string, object>()
			{
				{ "attr", attribute.Select(x => x.ToUpper()) },
				{ "startVertex", startVertex },
				{ "max", attribute.Length }
			});

		if (result.Count < attribute.Length) return null;

		return result.Select(x => new SharpAttribute()
		{
			Name = x.Name,
			Flags = () => GetAttributeFlags(x.Id),
			Value = MarkupString.MarkupStringModule.single(x.Value), // TODO: Compose and Decompose
			LongName = x.LongName,
			Leaves = () => GetAttributes(x.Id),
			Owner = () => GetAttributeOwner(x.Id),
			SharpAttributeEntry = () => null // TODO: FIX
		}).ToArray();
	}

	public async Task<bool> SetAttributeAsync(DBRef dbref, string[] attribute, string value, SharpPlayer owner)
	{
		ArgumentException.ThrowIfNullOrEmpty(owner?.Id);

		var transactionHandle = await arangoDB.Transaction.BeginAsync(handle, new ArangoTransaction
		{
			LockTimeout = DatabaseBehaviorConstants.TransactionTimeout,
			WaitForSync = true,
			AllowImplicit = false,
			Collections = new ArangoTransactionScope
			{
				Write = [DatabaseConstants.attributes, DatabaseConstants.hasAttribute, DatabaseConstants.hasAttributeOwner],
				Read =
				[
					DatabaseConstants.attributes, DatabaseConstants.hasAttribute, DatabaseConstants.objects,
					DatabaseConstants.isObject, DatabaseConstants.players, DatabaseConstants.rooms, DatabaseConstants.things,
					DatabaseConstants.exits
				]
			}
		});

		var startVertex = $"{DatabaseConstants.objects}/{dbref.Number}";
		const string let1 =
			$"LET start = (FOR v IN 1..1 INBOUND @startVertex GRAPH {DatabaseConstants.graphObjects} RETURN v)";
		const string let2 =
			$"LET foundAttributes = (FOR v,e,p IN 1..@max OUTBOUND FIRST(start) GRAPH {DatabaseConstants.graphAttributes} PRUNE condition = NTH(@attr,LENGTH(p.edges)-1) != v.Name FILTER !condition RETURN v)";
		const string query = $"{let1} {let2} RETURN APPEND(start, foundAttributes)";

		var result = await arangoDB.Query.ExecuteAsync<dynamic[]>(handle, query, new Dictionary<string, object>
		{
			{ "attr", attribute.Select(x => x.ToUpper()) },
			{ "startVertex", startVertex },
			{ "max", attribute.Length }
		});

		var actualResult = result.First();

		var matches = actualResult.Length;
		var remaining = attribute.Skip(matches - 1).ToArray();
		var last = actualResult.Last();
		string lastId = last._id;

		foreach (var nextAttr in remaining.Select((attrName, i) => (value: attrName, i)))
		{
			var newOne = await arangoDB.Document.CreateAsync<SharpAttributeCreateRequest, SharpAttributeQueryResult>(
				transactionHandle, DatabaseConstants.attributes,
				new SharpAttributeCreateRequest(nextAttr.value.ToUpper(), [], string.Empty,
					string.Join('`', remaining.Take(nextAttr.i + 1).Select(x => x.ToUpper()))), waitForSync: true);

			await arangoDB.Document.CreateAsync<SharpEdgeCreateRequest, SharpEdgeQueryResult>(transactionHandle,
				DatabaseConstants.hasAttribute,
				new SharpEdgeCreateRequest(lastId, newOne.Id), waitForSync: true);

			await arangoDB.Document.CreateAsync<SharpEdgeCreateRequest, SharpEdgeQueryResult>(transactionHandle,
				DatabaseConstants.hasAttributeOwner,
				new SharpEdgeCreateRequest(newOne.Id, owner.Object.Id!), waitForSync: true);

			lastId = newOne.Id;
		}

		await arangoDB.Document.UpdateAsync(transactionHandle, DatabaseConstants.attributes, new
		{
			Key = lastId!.Split("/").Last(),
			Value = value,
			LongName = string.Join("`", attribute.Select(x => x.ToUpper()))
		}, mergeObjects: true, waitForSync: true);

		await arangoDB.Document.CreateAsync<SharpEdgeCreateRequest, SharpEdgeQueryResult>(transactionHandle,
			DatabaseConstants.hasAttributeOwner,
			new SharpEdgeCreateRequest(lastId, owner.Id!), mergeObjects: true, waitForSync: true);

		await arangoDB.Transaction.CommitAsync(transactionHandle);

		return true;
	}

	public Task<bool> ClearAttributeAsync(DBRef dbref, string[] attribute)
	{
		// Set the contents to empty.

		throw new NotImplementedException();
	}

	public Task<bool> WipeAttributeAsync(DBRef dbref, string[] attribute)
	{
		// Wipe a list of attributes. We assume the calling code figured out the permissions part.

		throw new NotImplementedException();
	}

	public Task<IEnumerable<AnySharpContent>> GetNearbyObjectsAsync(DBRef obj)
	{
		throw new NotImplementedException();
	}

	/// <summary>
	/// Gets the location of an object, at X depth, with 0 returning the same object, and -1 going until it can't go deeper.
	/// </summary>
	/// <param name="obj">Location</param>
	/// <param name="depth">Depth</param>
	/// <returns>The deepest findable object based on depth</returns>
	public async Task<AnyOptionalSharpContainer> GetLocationAsync(DBRef obj, int depth = 1)
	{
		var baseObject = await GetObjectNodeAsync(obj);
		if (baseObject.IsT4) return new None();

		var variableDepth = depth == -1 ? "0" : $"0..{depth}";
		var locationQuery =
			$"FOR v IN {variableDepth} OUTBOUND @startVertex GRAPH {DatabaseConstants.graphLocations} RETURN v";
		var query = await arangoDB.Query.ExecuteAsync<dynamic>(handle, locationQuery, new Dictionary<string, object>()
		{
			{ "startVertex", baseObject.Id()! }
		});
		var locationBaseObj = await GetObjectNodeAsync((string)query.Last()._id);
		var trueLocation = locationBaseObj.Match<AnyOptionalSharpContainer>(
			player => player,
			room => room,
			exit => throw new Exception("Invalid Location found"),
			thing => thing,
			none => throw new Exception("Invalid Location found"));

		return trueLocation;
	}

	public async Task<AnySharpContainer> GetLocationAsync(AnySharpObject obj, int depth = 1) =>
		(await GetLocationAsync(obj.Object().DBRef, depth)).WithoutNone();

	public async Task<IEnumerable<AnySharpContent>?> GetContentsAsync(DBRef obj)
	{
		var baseObject = await GetObjectNodeAsync(obj);
		if (baseObject.IsT4) return null;

		const string locationQuery =
			$"FOR v IN 1..1 INBOUND @startVertex GRAPH {DatabaseConstants.graphLocations} RETURN v";
		var query = await arangoDB.Query.ExecuteAsync<dynamic>(handle, $"{locationQuery}",
			new Dictionary<string, object>
			{
				{ "startVertex", baseObject.Object()!.Id! }
			});
		var result = query
			.Select(x => (string)x._id)
			.Select(GetObjectNodeAsync) // TODO: Optimize to make a single call.
			.Select(x => x.Result.Match<AnySharpContent>(
				player => player,
				room => throw new Exception("Invalid Contents found"),
				exit => exit,
				thing => thing,
				none => throw new Exception("Invalid Contents found")
			));

		return result;
	}

	public async Task<IEnumerable<AnySharpContent>?> GetContentsAsync(AnyOptionalSharpObject node)
	{
		var startVertex = node.Id();

		if (startVertex is null) return null;

		const string locationQuery =
			$"FOR v IN 1..1 INBOUND @startVertex GRAPH {DatabaseConstants.graphLocations} RETURN v";
		var query = await arangoDB.Query.ExecuteAsync<dynamic>(handle, $"{locationQuery}",
			new Dictionary<string, object>
			{
				{ "startVertex", startVertex! }
			});

		string[] ids = query.Select(x => (string)x._id).ToArray();
		var objects = await Task.WhenAll(ids.Select(async x => await GetObjectNodeAsync(x)));


		var result = objects.Select(x => x.Match<AnySharpContent>(
			player => player,
			room => throw new Exception("Invalid Contents found"),
			exit => exit,
			thing => thing,
			none => throw new Exception("Invalid Contents found")
		));

		return result;
	}

	public async Task<IEnumerable<SharpExit>?> GetExitsAsync(DBRef obj)
	{
		var baseObject = await GetObjectNodeAsync(obj);
		if (baseObject.IsT4) return null;

		const string exitQuery = $"FOR v IN 1..1 INBOUND @startVertex GRAPH {DatabaseConstants.graphExits} RETURN v";
		var query = await arangoDB.Query.ExecuteAsync<dynamic>(handle, $"{exitQuery}",
			new Dictionary<string, object>
			{
				{ "startVertex", baseObject.Object()!.Id! }
			});
		var result = query
			.Select(x => (string)x._id)
			.Select(GetObjectNodeAsync) // TODO: Optimize to make a single call.
			.Select(x => x.Result.Match(
				player => throw new Exception("Invalid Exit found"),
				room => throw new Exception("Invalid Exit found"),
				exit => exit,
				thing => throw new Exception("Invalid Exit found"),
				none => throw new Exception("Invalid Exit found")
			));

		return result;
	}

	public async Task<IEnumerable<SharpExit>?> GetExitsAsync(AnyOptionalSharpContainer node)
	{
		var startVertex = node.Id();
		if (startVertex is null) return null;

		const string exitQuery = $"FOR v IN 1..1 INBOUND @startVertex GRAPH {DatabaseConstants.graphExits} RETURN v";
		var query = await arangoDB.Query.ExecuteAsync<dynamic>(handle, $"{exitQuery}",
			new Dictionary<string, object>
			{
				{ "startVertex", startVertex! }
			});
		var result = query
			.Select(x => (string)x._id)
			.Select(GetObjectNodeAsync) // TODO: Optimize to make a single call.
			.Select(x => x.Result.Match(
				player => throw new Exception("Invalid Exit found"),
				room => throw new Exception("Invalid Exit found"),
				exit => exit,
				thing => throw new Exception("Invalid Exit found"),
				none => throw new Exception("Invalid Exit found")
			));

		return result;
	}

	public async Task<IEnumerable<SharpPlayer>> GetPlayerByNameAsync(string name)
	{
		// TODO: Look up by Alias.
		var query = await arangoDB.Query.ExecuteAsync<dynamic>(handle,
			$"FOR v IN {DatabaseConstants.objects} FILTER v.Type == @type && v.Name == @name RETURN v",
			bindVars: new Dictionary<string, object>
			{
				{ "name", name },
				{ "type", DatabaseConstants.typePlayer }
			});

		// TODO: Edit to return multiple players and let the above layer figure out which one it wants.
		var result = query.FirstOrDefault();
		if (result is null) return [];

		return query.Select(x => GetObjectNode((string)x._id).AsPlayer);
	}
}