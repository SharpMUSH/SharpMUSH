using Core.Arango;
using Core.Arango.Migration;
using Microsoft.Extensions.Logging;
using OneOf;
using OneOf.Types;
using SharpMUSH.Database.Models;
using SharpMUSH.Library;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services;
using System.Collections.Immutable;

namespace SharpMUSH.Database;

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

		// load all migrations from assembly
		migrator.AddMigrations(typeof(ArangoDatabase).Assembly);

		// apply all migrations up to latest
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

		var player = await arangoDB.Document.CreateAsync<SharpPlayerCreateRequest, SharpPlayerQueryResult>(handle, DatabaseConstants.players,
			new SharpPlayerCreateRequest([], hashedPassword));

		await arangoDB.Document.CreateAsync(handle, DatabaseConstants.isObject, new SharpEdgeCreateRequest(player.Id, obj.Id));
		await arangoDB.Document.CreateAsync(handle, DatabaseConstants.hasObjectOwner, new SharpEdgeCreateRequest(player.Id, player.Id));

		var objectLocation = await GetObjectNodeAsync(location);

		var idx = objectLocation.Match(
			player => player.Id,
			room => room.Id,
			exit => throw new ArgumentException("An Exit is not a valid location to create a player!"),
			thing => thing.Id,
			none => throw new ArgumentException("A player must have a valid creation location!"));

		await arangoDB.Document.CreateAsync(handle, DatabaseConstants.atLocation, new SharpEdgeCreateRequest(player.Id, idx!));
		await arangoDB.Document.CreateAsync(handle, DatabaseConstants.hasHome, new SharpEdgeCreateRequest(player.Id, idx!));

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

	public async Task<DBRef> CreateThingAsync(string name, OneOf<SharpPlayer, SharpRoom, SharpThing> location, SharpPlayer creator)
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

	public async Task<DBRef> CreateExitAsync(string name, OneOf<SharpPlayer, SharpRoom, SharpThing> location, SharpPlayer creator)
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

	public OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing, None> GetObjectNode(DBRef dbref)
		=> GetObjectNodeAsync(dbref).Result;

	private IQueryable<SharpPower> GetPowers(string id)
		=> arangoDB.Query.ExecuteAsync<SharpPower>(handle,
			$"FOR v IN 1..1 OUTBOUND {id} GRAPH {DatabaseConstants.graphPowers} RETURN v").Result.AsQueryable();

	private IQueryable<SharpObjectFlag> GetFlags(string id)
		=> arangoDB.Query.ExecuteAsync<SharpObjectFlag>(handle,
			$"FOR v IN 1..1 OUTBOUND {id} GRAPH {DatabaseConstants.graphFlags} RETURN v").Result.AsQueryable();

	private IQueryable<SharpAttribute> GetAttributes(string id)
		=> arangoDB.Query.ExecuteAsync<SharpAttribute>(handle,
			$"FOR v IN 1..1 OUTBOUND {id} GRAPH {DatabaseConstants.graphPowers} RETURN v").Result.AsQueryable();

	private IQueryable<SharpPlayer> GetObjectOwner(string id)
		=> arangoDB.Query.ExecuteAsync<SharpPlayer>(handle,
			$"FOR v IN 1..1 OUTBOUND {id} GRAPH {DatabaseConstants.graphObjectOwners} RETURN v").Result.AsQueryable();

	private IQueryable<SharpObject> GetParent(string id)
		=> arangoDB.Query.ExecuteAsync<SharpObject>(handle,
			$"FOR v IN 1..1 OUTBOUND {id} GRAPH {DatabaseConstants.graphParents} RETURN v").Result.AsQueryable();

	private OneOf<SharpPlayer, SharpRoom, SharpThing> GetHome(string id)
		=> arangoDB.Query.ExecuteAsync<OneOf<SharpPlayer, SharpRoom, SharpThing>>(handle,
			$"FOR v IN 1..1 OUTBOUND {id} GRAPH {DatabaseConstants.graphParents} RETURN v").Result.Single();

	private OneOf<SharpPlayer, SharpRoom, SharpThing> GetLocation(string id)
		=> arangoDB.Query.ExecuteAsync<OneOf<SharpPlayer, SharpRoom, SharpThing>>(handle,
			$"FOR v IN 1..1 OUTBOUND {id} GRAPH {DatabaseConstants.graphParents} RETURN v").Result.Single();

	public async Task<OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing, None>> GetObjectNodeAsync(DBRef dbref)
	{
		var obj = await arangoDB.Document.GetAsync<SharpObjectQueryResult>(handle, DatabaseConstants.objects, dbref.Number.ToString());

		if (obj == null) return new None();
		if (dbref.CreationMilliseconds != null && obj.CreationTime != dbref.CreationMilliseconds) return new None();

		var startVertex = obj.Id;
		var res = (await arangoDB.Query.ExecuteAsync<dynamic>(handle,
			$"FOR v IN 1..1 INBOUND {startVertex} GRAPH {DatabaseConstants.graphObjects} RETURN v")).SingleOrDefault();

		if (res == null) return new None();

		string id = res._id;
		dynamic vertex = res;

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
			Owner = GetObjectOwner(startVertex),
			Parent = GetParent(startVertex)
		};

		return obj.Type switch
		{
			DatabaseConstants.typeThing => new SharpThing { Id = id, Object = convertObject },
			DatabaseConstants.typePlayer => new SharpPlayer { Id = id, Object = convertObject, Aliases = vertex.Aliases.ToObject<string[]>(), Location = () => GetLocation(id), Home = () => GetHome(id), PasswordHash = vertex.PasswordHash },
			DatabaseConstants.typeRoom => new SharpRoom { Id = id, Object = convertObject },
			DatabaseConstants.typeExit => new SharpThing { Id = id, Object = convertObject, Aliases = vertex.Aliases },
			_ => throw new ArgumentException($"Invalid Object Type found: '{obj.Type}'"),
		};
	}

	private async Task<OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing, None>> GetObjectNodeAsync(string dbID)
	{
		var startVertex = dbID;

		var query = await arangoDB.Query.ExecuteAsync<dynamic>(handle,
			$"FOR v IN 0..1 OUTBOUND {startVertex} GRAPH {DatabaseConstants.graphObjects} RETURN v");

		var obj = (SharpObjectQueryResult)query.Last().vertex;
		var res = query.First();

		string id = res.id;
		string collection = res.collection;
		dynamic vertex = res.vertex;
		var convertObject = new SharpObject()
		{
			Id = obj.Id,
			Key = int.Parse(obj.Key),
			Name = obj.Name,
			Type = obj.Type,
			CreationTime = obj.CreationTime,
			ModifiedTime = obj.ModifiedTime,
			Locks = (obj.Locks ?? []).ToImmutableDictionary(),
			Flags = GetFlags(startVertex),
			Powers = GetPowers(startVertex),
			Attributes = GetAttributes(startVertex),
			Owner = GetObjectOwner(startVertex),
			Parent = GetParent(startVertex)
		};

		return obj.Type switch
		{
			DatabaseConstants.typeThing => new SharpThing { Id = id, Object = convertObject },
			DatabaseConstants.typePlayer => new SharpPlayer { Id = id, Object = convertObject, Aliases = vertex.Aliases, Location = () => GetLocation(id), Home = () => GetHome(id), PasswordHash = vertex.PasswordHash },
			DatabaseConstants.typeRoom => new SharpRoom { Id = id, Object = convertObject },
			DatabaseConstants.typeExit => new SharpThing { Id = id, Object = convertObject, Aliases = vertex.Aliases },
			_ => throw new ArgumentException($"Invalid Object Type found: '{obj.Type}'"),
		};
	}

	public async Task<SharpObject?> GetBaseObjectNodeAsync(DBRef dbref)
	{
		// TODO: Version that cares about CreatedMilliseconds
		var obj = await arangoDB.Document.GetAsync<SharpObjectQueryResult>(handle, DatabaseConstants.objects, dbref.Number.ToString());

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
				Owner = GetObjectOwner(obj.Id),
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

		var query = $"FOR v IN 1 OUTBOUND @startVertex GRAPH {DatabaseConstants.graphAttributes} FILTER v.LongName LIKE @pattern RETURN v";

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

	public async Task<IEnumerable<SharpAttribute>?> GetAttributeAsync(DBRef dbref, string[] attribute)
	{
		var startVertex = $"{DatabaseConstants.objects}/{dbref.Number}";
		var let = $"LET start = FIRST(FOR v IN 1..1 INBOUND @startVertex GRAPH {DatabaseConstants.graphObjects} RETURN v)";
		var query = $"{let} FOR v,e,p IN 1..@max OUTBOUND start GRAPH {DatabaseConstants.graphAttributes} PRUNE condition = NTH(@attr,LENGTH(p.edges)-1) != v.Name FILTER !condition RETURN v";

		var result = await arangoDB.Query.ExecuteAsync<dynamic>(handle, query, new Dictionary<string, object>()
		{
			{ "attr", attribute.Select(x => x.ToUpper()) },
			{ "startVertex", startVertex },
			{ "max", attribute.Length }
		});

		// TODO: What if we did not find it?
		// TODO: What should the result be if we did not find it?
		// TODO: This doesn't handle Inheritance - so we may need to pass back the player it was found on as well either way.
		return result.Select(x => new SharpAttribute() { Name = x.Name, Flags = x.Flags.ToObject<string[]>(), Value = x.Value, Id = x._id, LongName = x.LongName }).ToArray();
	}

	public async Task<bool> SetAttributeAsync(DBRef dbref, string[] attribute, string value, SharpPlayer owner)
	{
		ArgumentNullException.ThrowIfNull(owner);

		var startVertex = $"{DatabaseConstants.objects}/{dbref.Number}";
		var let1 = $"LET start = (FOR v IN 1..1 INBOUND @startVertex GRAPH {DatabaseConstants.graphObjects} RETURN v)";
		var let2 = $"LET foundAttributes = (FOR v,e,p IN 1..@max OUTBOUND FIRST(start) GRAPH {DatabaseConstants.graphAttributes} PRUNE condition = NTH(@attr,LENGTH(p.edges)-1) != v.Name FILTER !condition RETURN v)";
		var query = $"{let1} {let2} RETURN APPEND(start, foundAttributes)";

		var result = await arangoDB.Query.ExecuteAsync<dynamic[]>(handle, query, new Dictionary<string, object>()
		{
			{ "attr", attribute.Select(x => x.ToUpper()) },
			{ "startVertex", startVertex },
			{ "max", attribute.Length }
		});

		var actualResult = result.First();

		var matches = actualResult.Length;
		var remaining = attribute.Skip(matches - 1);
		var last = actualResult.Last();
		string lastId = last._id;

		foreach (var next in remaining)
		{
			var newOne = await arangoDB.Document.CreateAsync(handle, DatabaseConstants.attributes, new SharpAttribute() { Name = next.ToUpper(), Flags = [] });
			await arangoDB.Document.CreateAsync(handle, DatabaseConstants.hasAttribute, new SharpEdge() { From = lastId, To = newOne.Id });
			lastId = newOne.Id;
		}

		await arangoDB.Document.UpdateAsync(handle, DatabaseConstants.attributes, new { Key = lastId!.Split("/").Last(), Value = value, LongName = string.Join("`", attribute.Select(x => x.ToUpper())) }, mergeObjects: true);
		await arangoDB.Document.CreateAsync(handle, DatabaseConstants.hasAttributeOwner, new SharpEdge { From = lastId, To = owner.Id!, }, mergeObjects: true);

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

	public Task<IEnumerable<OneOf<SharpPlayer, SharpExit, SharpThing>>> GetNearbyObjectsAsync(DBRef obj)
	{

		throw new NotImplementedException();
	}

	/// <summary>
	/// Gets the location of an object, at X depth, with 0 returning the same object, and -1 going until it can't go deeper.
	/// </summary>
	/// <param name="obj">Location</param>
	/// <param name="depth">Depth</param>
	/// <returns>The deepest findable object based on depth</returns>
	public async Task<OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing, None>> GetLocationAsync(DBRef obj, int depth = 1)
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

		return locationBaseObj;
	}

	public async Task<IEnumerable<OneOf<SharpPlayer, SharpExit, SharpThing, None>>?> GetContentsAsync(DBRef obj)
	{
		var baseObject = await GetObjectNodeAsync(obj);
		if (baseObject.IsT4) return null;

		var locationQuery = $"FOR v IN 1..1 INBOUND @startVertex GRAPH {DatabaseConstants.graphLocations} RETURN v";
		var query = await arangoDB.Query.ExecuteAsync<dynamic>(handle, $"{locationQuery}",
			new Dictionary<string, object>
			{
				{"startVertex", baseObject.Object()!.Id! }
			});
		var result = query
			.Select(x => (string)x._id)
			.Select(GetObjectNodeAsync) // TODO: Optimize to make a single call.
			.Select(x => x.Result.Match<OneOf<SharpPlayer, SharpExit, SharpThing, None>>(
				player => player,
				room => new None(),
				exit => exit,
				thing => thing,
				none => none
			));

		return result;
	}

	public async Task<IEnumerable<OneOf<SharpPlayer, SharpExit, SharpThing, None>>?> GetContentsAsync(OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing, None> node)
	{
		var startVertex = node.Id();

		if (startVertex == null) return null;

		var locationQuery = $"FOR v IN 1..1 INBOUND @startVertex GRAPH {DatabaseConstants.graphLocations} RETURN v";
		var query = await arangoDB.Query.ExecuteAsync<dynamic>(handle, $"{locationQuery}",
			new Dictionary<string, object>
			{
				{"startVertex", startVertex! }
			});
		var result = query
			.Select(x => (string)x._id)
			.Select(GetObjectNodeAsync) // TODO: Optimize to make a single call.
			.Select(x => x.Result.Match<OneOf<SharpPlayer, SharpExit, SharpThing, None>>(
				player => player,
				room => new None(),
				exit => exit,
				thing => thing,
				none => none
			));

		return result;
	}

	public async Task<IEnumerable<SharpPlayer>> GetPlayerByNameAsync(string name)
	{
		// Todo: Look up by Alias.
		var query = await arangoDB.Query.ExecuteAsync<dynamic>(handle, $"FOR v IN {DatabaseConstants.objects} FILTER v.Type == @type && v.Name == @name RETURN v",
			bindVars: new Dictionary<string, object>
			{
				{ "name", name },
				{ "type", DatabaseConstants.typePlayer }
			});

		// Todo: Edit to return multiple players and let the above layer figure out which one it wants.
		var result = query.FirstOrDefault();
		if (result == null) return [];

		// This will instead have to be a query on isObject to get multiple player:object pairs.
		var playerQuery = $"FOR v,e,p IN 1..1 INBOUND @startVertex GRAPH {DatabaseConstants.graphObjects} RETURN p";
		var playerQueryResult = await arangoDB.Query.ExecuteAsync<dynamic>(handle, playerQuery,
			bindVars: new Dictionary<string, object>
			{
				{ "startVertex", result._id },
			});


		return playerQueryResult.Select(path =>
		{
			var curObject = (path.edges[0] as SharpObjectQueryResult)!;
			var curPlayer = (path.edges[1] as SharpPlayerQueryResult)!;
			return new SharpPlayer()
			{
				Object = new SharpObject()
				{
					Name = curObject.Name,
					Type = curObject.Type,
					Id = curObject.Id,
					Key = int.Parse(curObject.Key),
					Locks = (curObject.Locks ?? []).ToImmutableDictionary(),
					CreationTime = curObject.CreationTime,
					ModifiedTime = curObject.ModifiedTime,
					Flags = GetFlags(curObject.Id),
					Powers = GetPowers(curObject.Id),
					Attributes = GetAttributes(curObject.Id),
					Owner = GetObjectOwner(curObject.Id),
					Parent = GetParent(curObject.Id)
				},
				Location = () => GetLocation(curPlayer.Id),
				Home = () => GetHome(curPlayer.Id),
				PasswordHash = curPlayer.PasswordHash,
				Aliases = curPlayer.Aliases,
				Id = curPlayer.Id
			};
		});
	}
}
