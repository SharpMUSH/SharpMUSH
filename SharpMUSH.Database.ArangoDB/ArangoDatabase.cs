using Core.Arango;
using Core.Arango.Migration;
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

// TODO: Unit of Work / Transaction around all of this!
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
		var obj = await arangoDB.Document.CreateAsync<SharpObjectCreateRequest, SharpObjectQueryResult>(handle, DatabaseConstants.objects, new SharpObjectCreateRequest(
			name,
			DatabaseConstants.typePlayer,
			[],
			time,
			time
		), returnNew: true);

		var newObject = obj.New;
		var hashedPassword = passwordService.HashPassword($"#{newObject.Key}:{newObject.CreationTime}", password);

		var playerResult = await arangoDB.Document.CreateAsync<SharpPlayerCreateRequest, SharpPlayerQueryResult>(handle, DatabaseConstants.players,
			new SharpPlayerCreateRequest([], hashedPassword));

		await arangoDB.Document.CreateAsync(handle, DatabaseConstants.isObject, new SharpEdgeCreateRequest(playerResult.Id, obj.Id));
		await arangoDB.Document.CreateAsync(handle, DatabaseConstants.hasObjectOwner, new SharpEdgeCreateRequest(playerResult.Id, playerResult.Id));

		var objectLocation = await GetObjectNodeAsync(location);

		var idx = objectLocation.Match(
			player => player.Id,
			room => room.Id,
			exit => throw new ArgumentException("An Exit is not a valid location to create a player!"),
			thing => thing.Id,
			none => throw new ArgumentException("A player must have a valid creation location!"));

		await arangoDB.Document.CreateAsync(handle, DatabaseConstants.atLocation, new SharpEdgeCreateRequest(playerResult.Id, idx!));
		await arangoDB.Document.CreateAsync(handle, DatabaseConstants.hasHome, new SharpEdgeCreateRequest(playerResult.Id, idx!));

		return new DBRef(int.Parse(obj.Key), time);
	}

	public async Task<DBRef> CreateRoomAsync(string name, SharpPlayer creator)
	{
		var time = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

		var obj = await arangoDB.Document.CreateAsync(handle, DatabaseConstants.objects,
			new SharpObjectCreateRequest(name, DatabaseConstants.typeRoom, [], time, time));
		var room = await arangoDB.Document.CreateAsync(handle, DatabaseConstants.rooms, new SharpRoomCreateRequest());

		await arangoDB.Document.CreateAsync(handle, DatabaseConstants.isObject, new SharpEdgeCreateRequest(room.Id, obj.Id));
		await arangoDB.Document.CreateAsync(handle, DatabaseConstants.hasObjectOwner, new SharpEdgeCreateRequest(room.Id, creator.Id!));

		return new DBRef(int.Parse(obj.Key), time);
	}

	public async Task<DBRef> CreateThingAsync(string name, AnySharpContainer location, SharpPlayer creator)
	{
		var time = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

		var obj = await arangoDB.Document.CreateAsync<SharpObjectCreateRequest, SharpObjectQueryResult>(handle, DatabaseConstants.objects,
			new SharpObjectCreateRequest(name, DatabaseConstants.typeThing, [], time, time));
		var thing = await arangoDB.Document.CreateAsync(handle, DatabaseConstants.things, new SharpThingCreateRequest([]));

		await arangoDB.Document.CreateAsync(handle, DatabaseConstants.isObject, new SharpEdgeCreateRequest(thing.Id, obj.Id));

		var idx = location.Object()?.Id!;

		await arangoDB.Document.CreateAsync(handle, DatabaseConstants.atLocation, new SharpEdgeCreateRequest(thing.Id, idx));
		await arangoDB.Document.CreateAsync(handle, DatabaseConstants.hasHome, new SharpEdgeCreateRequest(thing.Id, idx));
		await arangoDB.Document.CreateAsync(handle, DatabaseConstants.hasObjectOwner, new SharpEdgeCreateRequest(thing.Id, creator.Id!));

		return new DBRef(int.Parse(obj.Key), time);
	}

	public async Task<DBRef> CreateExitAsync(string name, AnySharpContainer location, SharpPlayer creator)
	{
		var time = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

		var obj = await arangoDB.Document.CreateAsync<SharpObjectCreateRequest, SharpObjectQueryResult>(handle, DatabaseConstants.objects,
			new SharpObjectCreateRequest(name, DatabaseConstants.typeExit, [], time, time));
		var exit = await arangoDB.Document.CreateAsync(handle, DatabaseConstants.exits, new SharpExitCreateRequest([]));

		var idx = location.Object()!.Id!;

		await arangoDB.Document.CreateAsync(handle, DatabaseConstants.isObject, new SharpEdgeCreateRequest(exit.Id, obj.Id));
		await arangoDB.Document.CreateAsync(handle, DatabaseConstants.hasHome, new SharpEdgeCreateRequest(exit.Id, idx));
		await arangoDB.Document.CreateAsync(handle, DatabaseConstants.hasObjectOwner, new SharpEdgeCreateRequest(exit.Id, idx));

		return new DBRef(int.Parse(obj.Key), time);
	}

	public AnyOptionalSharpObject GetObjectNode(DBRef dbref)
		=> GetObjectNodeAsync(dbref).Result;
	public AnyOptionalSharpObject GetObjectNode(string dbId)
		=> GetObjectNodeAsync(dbId).Result;

	private IQueryable<SharpPower> GetPowers(string id)
		=> arangoDB.Query.ExecuteAsync<SharpPower>(handle,
			$"FOR v IN 1..1 OUTBOUND {id} GRAPH {DatabaseConstants.graphPowers} RETURN v").Result.AsQueryable();

	private IQueryable<SharpObjectFlag> GetFlags(string id)
		=> arangoDB.Query.ExecuteAsync<SharpObjectFlag>(handle,
			$"FOR v IN 1..1 OUTBOUND {id} GRAPH {DatabaseConstants.graphFlags} RETURN v").Result.AsQueryable();

	private IQueryable<SharpAttribute> GetAttributes(string id)
		=> arangoDB.Query.ExecuteAsync<SharpAttribute>(handle,
			$"FOR v IN 1..1 OUTBOUND {id} GRAPH {DatabaseConstants.graphPowers} RETURN v").Result.AsQueryable();

	private SharpPlayer GetObjectOwner(string id)
	{
		var owner = arangoDB.Query.ExecuteAsync<SharpPlayerQueryResult>(handle,
			$"FOR v IN 1..1 OUTBOUND {id} GRAPH {DatabaseConstants.graphObjectOwners} RETURN v").Result.Single();

		var populatedOwner = GetObjectNodeAsync(owner.Id).Result;

		return populatedOwner.AsT0;
	}

	public IQueryable<SharpObject> GetParent(string id)
		=> arangoDB.Query.ExecuteAsync<SharpObject>(handle,
			$"FOR v IN 1..1 OUTBOUND {id} GRAPH {DatabaseConstants.graphParents} RETURN v").Result.AsQueryable();

	public IQueryable<SharpObject> GetParents(string id)
		=> arangoDB.Query.ExecuteAsync<SharpObject>(handle,
			$"FOR v IN 1 OUTBOUND {id} GRAPH {DatabaseConstants.graphParents} RETURN v").Result.AsQueryable();

	private AnySharpContainer GetHome(string id)
	{
		var homeId = arangoDB.Query.ExecuteAsync<string>(handle,
			$"FOR v IN 1..1 OUTBOUND {id} GRAPH {DatabaseConstants.graphHomes} RETURN v._id").Result.Single();
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
		// TODO: It can't do this conversion. It doesn't directly translate at all.
		// SharpRoom etc. should also populate with its correct values.
		var locationId = arangoDB.Query.ExecuteAsync<string>(handle,
			$"FOR v IN 1..1 OUTBOUND {id} GRAPH {DatabaseConstants.graphLocations} RETURN v._id").Result.Single();
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
		var obj = await arangoDB.Document.GetAsync<SharpObjectQueryResult>(handle, DatabaseConstants.objects, dbref.Number.ToString());

		if (obj == null) return new None();
		if (dbref.CreationMilliseconds != null && obj.CreationTime != dbref.CreationMilliseconds) return new None();

		var startVertex = obj.Id;
		var res = (await arangoDB.Query.ExecuteAsync<dynamic>(handle,
			$"FOR v IN 1..1 INBOUND {startVertex} GRAPH {DatabaseConstants.graphObjects} RETURN v")).SingleOrDefault();

		if (res == null) return new None();

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
			Flags = GetFlags(startVertex),
			Powers = GetPowers(startVertex),
			Attributes = GetAttributes(startVertex),
			Owner = () => GetObjectOwner(startVertex),
			Parent = GetParent(startVertex)
		};

		return obj.Type switch
		{
			DatabaseConstants.typeThing => new SharpThing { Id = id, Object = convertObject, Location = () => GetLocation(id), Home = () => GetHome(id) },
			DatabaseConstants.typePlayer => new SharpPlayer { Id = id, Object = convertObject, Aliases = res.Aliases.ToObject<string[]>(), Location = () => GetLocation(id), Home = () => GetHome(id), PasswordHash = res.PasswordHash },
			DatabaseConstants.typeRoom => new SharpRoom { Id = id, Object = convertObject },
			DatabaseConstants.typeExit => new SharpExit { Id = id, Object = convertObject, Aliases = res.Aliases.ToObject<string[]>(), Location = () => GetLocation(id), Home = () => GetHome(id) },
			_ => throw new ArgumentException($"Invalid Object Type found: '{obj.Type}'"),
		};
	}

	private async Task<AnyOptionalSharpObject> GetObjectNodeAsync(string dbID)
	{
		var query = await arangoDB.Query.ExecuteAsync<dynamic>(handle,
			$"FOR v IN 0..1 OUTBOUND {dbID} GRAPH {DatabaseConstants.graphObjects} RETURN v");

		if(query.Count() == 1)
		{
			// TODO: BE BETTER
			query = await arangoDB.Query.ExecuteAsync<dynamic>(handle,
				$"FOR v IN 0..1 INBOUND {dbID} GRAPH {DatabaseConstants.graphObjects} RETURN v");
			query.Reverse();
		}

		var res = query.First();
		var obj = query.Last();

		string id = res._id;
		string collection = id.Split("/")[0];
		var convertObject = new SharpObject()
		{
			Id = obj._id,
			Key = int.Parse((string)obj._key),
			Name = obj.Name,
			Type = obj.Type,
			CreationTime = obj.CreationTime,
			ModifiedTime = obj.ModifiedTime,
			Locks = ((Dictionary<string, string>?)obj.Locks ?? []).ToImmutableDictionary(),
			Flags = GetFlags(dbID),
			Powers = GetPowers(dbID),
			Attributes = GetAttributes(dbID),
			Owner = () => GetObjectOwner(dbID),
			Parent = GetParent(dbID)
		};

		return collection switch
		{
			DatabaseConstants.things => new SharpThing { Id = id, Object = convertObject, Location = () => GetLocation(id), Home = () => GetHome(id) },
			DatabaseConstants.players => new SharpPlayer { Id = id, Object = convertObject, Aliases = res.Aliases.ToObject<string[]>(), Location = () => GetLocation(id), Home = () => GetHome(id), PasswordHash = res.PasswordHash },
			DatabaseConstants.rooms => new SharpRoom { Id = id, Object = convertObject },
			DatabaseConstants.exits => new SharpExit { Id = id, Object = convertObject, Aliases = res.Aliases.ToObject<string[]>(), Location = () => GetLocation(id), Home = () => GetHome(id) },
			_ => throw new ArgumentException($"Invalid Object Type found: '{obj.Type}'"),
		};
	}

	public async Task<SharpObject?> GetBaseObjectNodeAsync(DBRef dbref)
	{
		var obj = await arangoDB.Document.GetAsync<SharpObjectQueryResult>(handle, DatabaseConstants.objects, dbref.Number.ToString());

		if (dbref.CreationMilliseconds.HasValue && obj.CreationTime != dbref.CreationMilliseconds)
		{
			return null;
		}

		return obj == null
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
				Flags = GetFlags(obj.Id),
				Powers = GetPowers(obj.Id),
				Attributes = GetAttributes(obj.Id),
				Owner = () => GetObjectOwner(obj.Id),
				Parent = GetParent(obj.Id)
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
		// TODO: Create an Inverted Index on LongName.

		// OPTIONS { indexHint: "inverted_index_name", forceIndexHint: true }
		const string query = $"FOR v IN 1 OUTBOUND @startVertex GRAPH {DatabaseConstants.graphAttributes} FILTER v.LongName LIKE @pattern RETURN v";

		var result2 = await arangoDB.Query.ExecuteAsync<dynamic>(handle, query, new Dictionary<string, object>()
		{
			{ "startVertex", startVertex },
			{ "pattern", pattern }
		});

		return result2.Select(x => new SharpAttribute()
		{
			Flags = x.Flags,
			Name = x.Name,
			Value = x.Value,
			LongName = x.LongName
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
		var query = $"FOR v IN 1 OUTBOUND @startVertex GRAPH {DatabaseConstants.graphAttributes} FILTER v.LongName =~ @pattern RETURN v";

		var result2 = await arangoDB.Query.ExecuteAsync<dynamic>(handle, query, new Dictionary<string, object>()
		{
			{ "startVertex", startVertex },
			{ "pattern", attribute_pattern }
		});

		return result2.Select(x => new SharpAttribute()
		{
			Flags = x.Flags,
			Name = x.Name,
			Value = x.Value,
			LongName = x.LongName
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
		const string let = $"LET start = FIRST(FOR v IN 1..1 INBOUND @startVertex GRAPH {DatabaseConstants.graphObjects} RETURN v)";
		const string query = $"{let} FOR v,e,p IN 1..@max OUTBOUND start GRAPH {DatabaseConstants.graphAttributes} PRUNE condition = NTH(@attr,LENGTH(p.edges)-1) != v.Name FILTER !condition RETURN v";

		var result = await arangoDB.Query.ExecuteAsync<SharpAttributeQueryResult>(handle, query, new Dictionary<string, object>()
		{
			{ "attr", attribute.Select(x => x.ToUpper()) },
			{ "startVertex", startVertex },
			{ "max", attribute.Length }
		});

		if (result.Count < attribute.Length) return null;

		return result.Select(x => new SharpAttribute() { Name = x.Name, Flags = x.Flags, Value = x.Value, Id = x.Id, LongName = x.LongName }).ToArray();
	}

	public async Task<bool> SetAttributeAsync(DBRef dbref, string[] attribute, string value, SharpPlayer owner)
	{
		ArgumentException.ThrowIfNullOrEmpty(owner?.Id);

		var startVertex = $"{DatabaseConstants.objects}/{dbref.Number}";
		const string let1 = $"LET start = (FOR v IN 1..1 INBOUND @startVertex GRAPH {DatabaseConstants.graphObjects} RETURN v)";
		const string let2 = $"LET foundAttributes = (FOR v,e,p IN 1..@max OUTBOUND FIRST(start) GRAPH {DatabaseConstants.graphAttributes} PRUNE condition = NTH(@attr,LENGTH(p.edges)-1) != v.Name FILTER !condition RETURN v)";
		const string query = $"{let1} {let2} RETURN APPEND(start, foundAttributes)";

		var result = await arangoDB.Query.ExecuteAsync<dynamic[]>(handle, query, new Dictionary<string, object>
		{
			{ "attr", attribute.Select(x => x.ToUpper()) },
			{ "startVertex", startVertex },
			{ "max", attribute.Length }
		});

		var actualResult = result.First();

		var matches = actualResult.Length;
		var remaining = attribute.Skip(matches - 1).ToList();
		var last = actualResult.Last();
		string lastId = last._id;

		foreach (var nextAttr in remaining.Select((attrName, i) => (value: attrName, i)))
		{
			var newOne = await arangoDB.Document.CreateAsync<SharpAttributeCreateRequest, SharpAttributeQueryResult>(handle, DatabaseConstants.attributes,
				new SharpAttributeCreateRequest(nextAttr.value.ToUpper(), [], string.Empty,
				string.Join('`', remaining.Take(nextAttr.i + 1).Select(x => x.ToUpper()))));
			await arangoDB.Document.CreateAsync<SharpEdgeCreateRequest, SharpEdgeQueryResult>(handle, DatabaseConstants.hasAttribute,
				new SharpEdgeCreateRequest(lastId, newOne.Id));
			lastId = newOne.Id;
		}

		await arangoDB.Document.UpdateAsync(handle, DatabaseConstants.attributes, new
		{
			Key = lastId!.Split("/").Last(),
			Value = value,
			LongName = string.Join("`", attribute.Select(x => x.ToUpper()))
		}, mergeObjects: true);
		await arangoDB.Document.CreateAsync<SharpEdgeCreateRequest, SharpEdgeQueryResult>(handle, DatabaseConstants.hasAttributeOwner,
			new SharpEdgeCreateRequest(lastId, owner.Id!), mergeObjects: true);

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
		var locationQuery = $"FOR v IN {variableDepth} OUTBOUND @startVertex GRAPH {DatabaseConstants.graphLocations} RETURN v";
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

	public async Task<AnySharpContainer> GetLocationAsync(AnySharpObject obj, int depth = 1) => (await GetLocationAsync(obj.Object().DBRef, depth)).WithoutNone();

	public async Task<IEnumerable<AnySharpContent>?> GetContentsAsync(DBRef obj)
	{
		var baseObject = await GetObjectNodeAsync(obj);
		if (baseObject.IsT4) return null;

		const string locationQuery = $"FOR v IN 1..1 INBOUND @startVertex GRAPH {DatabaseConstants.graphLocations} RETURN v";
		var query = await arangoDB.Query.ExecuteAsync<dynamic>(handle, $"{locationQuery}",
			new Dictionary<string, object>
			{
				{"startVertex", baseObject.Object()!.Id! }
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

		if (startVertex == null) return null;

		const string locationQuery = $"FOR v IN 1..1 INBOUND @startVertex GRAPH {DatabaseConstants.graphLocations} RETURN v";
		var query = await arangoDB.Query.ExecuteAsync<dynamic>(handle, $"{locationQuery}",
			new Dictionary<string, object>
			{
				{"startVertex", startVertex! }
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

	public async Task<IEnumerable<SharpExit>?> GetExitsAsync(DBRef obj)
	{
		var baseObject = await GetObjectNodeAsync(obj);
		if (baseObject.IsT4) return null;

		const string exitQuery = $"FOR v IN 1..1 INBOUND @startVertex GRAPH {DatabaseConstants.graphExits} RETURN v";
		var query = await arangoDB.Query.ExecuteAsync<dynamic>(handle, $"{exitQuery}",
			new Dictionary<string, object>
			{
				{"startVertex", baseObject.Object()!.Id! }
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
		if (startVertex == null) return null;

		const string exitQuery = $"FOR v IN 1..1 INBOUND @startVertex GRAPH {DatabaseConstants.graphExits} RETURN v";
		var query = await arangoDB.Query.ExecuteAsync<dynamic>(handle, $"{exitQuery}",
			new Dictionary<string, object>
			{
				{"startVertex", startVertex! }
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
		var query = await arangoDB.Query.ExecuteAsync<dynamic>(handle, $"FOR v IN {DatabaseConstants.objects} FILTER v.Type == @type && v.Name == @name RETURN v",
			bindVars: new Dictionary<string, object>
			{
				{ "name", name },
				{ "type", DatabaseConstants.typePlayer }
			});

		// TODO: Edit to return multiple players and let the above layer figure out which one it wants.
		var result = query.FirstOrDefault();
		if (result == null) return [];

		return query.Select(x => GetObjectNode((string)x._id).AsT0);
	}
}
