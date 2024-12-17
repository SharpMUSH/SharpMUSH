using Core.Arango;
using Core.Arango.Migration;
using Core.Arango.Protocol;
using MarkupString;
using Mediator;
using Microsoft.Extensions.Logging;
using OneOf.Types;
using SharpMUSH.Database.Models;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services;
using System.Collections.Immutable;

namespace SharpMUSH.Database.ArangoDB;

// TODO: Unit of Work / Transaction around all of this! Otherwise it risks the stability of the Database.
public class ArangoDatabase(
	ILogger<ArangoDatabase> logger,
	IArangoContext arangoDB,
	ArangoHandle handle,
	IMediator mediator,
	IPasswordService passwordService // TODO: This doesn't belong in the database layer
) : ISharpDatabase
{
	public async ValueTask Migrate()
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

	public async ValueTask<DBRef> CreatePlayerAsync(string name, string password, DBRef location)
	{
		var time = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
		var objectLocation = await GetObjectNodeAsync(location);

		var transaction = new ArangoTransaction()
		{
			LockTimeout = DatabaseBehaviorConstants.TransactionTimeout,
			WaitForSync = true,
			Collections = new ArangoTransactionScope
			{
				Exclusive =
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

	public async ValueTask<DBRef> CreateRoomAsync(string name, SharpPlayer creator)
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

	public async ValueTask<DBRef> CreateThingAsync(string name, AnySharpContainer location, SharpPlayer creator)
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

	public async ValueTask<DBRef> CreateExitAsync(string name, string[] aliases, AnySharpContainer location, SharpPlayer creator)
	{
		var time = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

		var obj = await arangoDB.Document.CreateAsync<SharpObjectCreateRequest, SharpObjectQueryResult>(handle,
			DatabaseConstants.objects,
			new SharpObjectCreateRequest(name, DatabaseConstants.typeExit, [], time, time));
		var exit = await arangoDB.Document.CreateAsync(handle, DatabaseConstants.exits, new SharpExitCreateRequest(aliases));

		var idx = location.Object()!.Id!;

		await arangoDB.Document.CreateAsync(handle, DatabaseConstants.isObject,
			new SharpEdgeCreateRequest(exit.Id, obj.Id));
		await arangoDB.Document.CreateAsync(handle, DatabaseConstants.hasHome, new SharpEdgeCreateRequest(exit.Id, idx));
		await arangoDB.Document.CreateAsync(handle, DatabaseConstants.hasObjectOwner,
			new SharpEdgeCreateRequest(exit.Id, idx));

		return new DBRef(int.Parse(obj.Key), time);
	}

	public async ValueTask<SharpObjectFlag?> GetObjectFlagAsync(string name)
		=> (await arangoDB.Query.ExecuteAsync<SharpObjectFlag>(
			handle,
			$"FOR v in @@C1 FILTER v.Name = @flag RETURN v",
			bindVars: new Dictionary<string, object>
			{
				{ "@C1", DatabaseConstants.objectFlags },
				{ "flag", name }
			},
			cache: true)).FirstOrDefault();

	public async ValueTask<IEnumerable<SharpObjectFlag>> GetObjectFlagsAsync()
		=> await arangoDB.Query.ExecuteAsync<SharpObjectFlag>(
			handle,
			$"FOR v in {DatabaseConstants.objectFlags:@} RETURN v",
			cache: true);

	public async ValueTask<bool> SetObjectFlagAsync(AnySharpObject target, SharpObjectFlag flag)
	{
		await arangoDB.Document.UpdateAsync(handle, DatabaseConstants.objects, new
		{
			target.Object().Key,
			Value = target.Object().Flags.Value.ToImmutableArray().Add(flag)
		});
		return true;
	}

	public async ValueTask<bool> UnsetObjectFlagAsync(AnySharpObject target, SharpObjectFlag flag)
	{
		await arangoDB.Document.UpdateAsync(handle, DatabaseConstants.objects, new
		{
			target.Object().Key,
			Value = target.Object().Flags.Value.ToImmutableArray().Remove(flag)
		});
		return true;
	}

	private async ValueTask<IEnumerable<SharpPower>> GetPowersAsync(string id)
	{
		var result = await arangoDB.Query.ExecuteAsync<SharpPowerQueryResult>(handle,
			$"FOR v IN 1..1 OUTBOUND {id} GRAPH {DatabaseConstants.graphPowers} RETURN v");

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

	public async ValueTask<IEnumerable<SharpObjectFlag>> GetObjectFlagsAsync(string id)
		=> await arangoDB.Query.ExecuteAsync<SharpObjectFlag>(handle,
			$"FOR v IN 1..1 OUTBOUND {id} GRAPH {DatabaseConstants.graphFlags} RETURN v");

	private async ValueTask<IEnumerable<SharpAttributeFlag>> GetAttributeFlagsAsync(string id)
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
				Inheritable = x.Inheritable,
				Id = x.Id
			});
	}

	private async ValueTask<IEnumerable<SharpAttribute>> GetAllAttributesAsync(string id)
	{
		// This only works for when we get a non-attribute as our ID.
		// Adjustment is needed if we get an attribute ID.
		IEnumerable<SharpAttributeQueryResult> sharpAttributeResults;
		if (id.StartsWith(DatabaseConstants.attributes))
		{
			sharpAttributeResults = await arangoDB.Query.ExecuteAsync<SharpAttributeQueryResult>(handle,
				$"FOR v IN 1..999 OUTBOUND @startVertex GRAPH {DatabaseConstants.graphAttributes} RETURN v",
				new Dictionary<string, object>() { { "startVertex", id } });
		}
		else
		{
			sharpAttributeResults = await arangoDB.Query.ExecuteAsync<SharpAttributeQueryResult>(handle,
				$"LET start = FIRST(FOR v IN 1..1 INBOUND @startVertex GRAPH {DatabaseConstants.graphObjects} RETURN v) FOR v IN 1..999 OUTBOUND start GRAPH {DatabaseConstants.graphAttributes} RETURN v",
				new Dictionary<string, object>() { { "startVertex", id } });
		}

		var sharpAttributes = sharpAttributeResults.Select(async x => 
			new SharpAttribute(
				Key:x.Key, 
				Name: x.Name, 
				Flags: await GetAttributeFlagsAsync(x.Id), 
				CommandListIndex: null, 
				LongName: x.LongName,
				Leaves: new(() => GetTopLevelAttributesAsync(x.Id).AsTask().Result), 
				Owner: new(() => GetAttributeOwnerAsync(x.Id).AsTask().Result), 
				SharpAttributeEntry: new(() => null))
		{
			Value = MarkupStringModule.deserialize(x.Value)
		});

		return await Task.WhenAll(sharpAttributes);
	}

	private async ValueTask<SharpPlayer> GetObjectOwnerAsync(string id)
	{
		var owner = (await arangoDB.Query.ExecuteAsync<SharpPlayerQueryResult>(handle,
			$"FOR v IN 1..1 OUTBOUND {id} GRAPH {DatabaseConstants.graphObjectOwners} RETURN v")).First();

		var populatedOwner = await GetObjectNodeAsync(owner.Id);

		return populatedOwner.AsPlayer;
	}

	private async ValueTask<SharpPlayer> GetAttributeOwnerAsync(string id)
	{
		var owner = (await arangoDB.Query.ExecuteAsync<SharpObjectQueryResult>(handle,
			$"FOR v IN 1..1 OUTBOUND {id} GRAPH {DatabaseConstants.graphAttributeOwners} RETURN v")).First();

		var populatedOwner = await GetObjectNodeAsync(owner.Id);

		return populatedOwner.AsPlayer;
	}

	public async ValueTask<SharpObject?> GetParentAsync(string id)
		=> (await arangoDB.Query.ExecuteAsync<SharpObject>(handle,
				$"FOR v IN 1..1 OUTBOUND {id} GRAPH {DatabaseConstants.graphParents} RETURN v", cache: true))
			.FirstOrDefault();

	public async ValueTask<IEnumerable<SharpObject>> GetParentsAsync(string id)
		=> await arangoDB.Query.ExecuteAsync<SharpObject>(handle,
			$"FOR v IN 1 OUTBOUND {id} GRAPH {DatabaseConstants.graphParents} RETURN v", cache: true);

	private async ValueTask<AnySharpContainer> GetHomeAsync(string id)
	{
		var homeId = (await arangoDB.Query.ExecuteAsync<string>(handle,
			$"FOR v IN 1..1 OUTBOUND {id} GRAPH {DatabaseConstants.graphHomes} RETURN v._id", cache: true)).First();
		var homeObject = await GetObjectNodeAsync(homeId);

		return homeObject.Match<AnySharpContainer>(
			player => player,
			room => room,
			exit => throw new Exception("Invalid Location found"),
			thing => thing,
			none => throw new Exception("Invalid Location found"));
	}

	public async ValueTask<AnyOptionalSharpObject> GetObjectNodeAsync(DBRef dbref)
	{
		var obj = await arangoDB.Document.GetAsync<SharpObjectQueryResult>(handle, DatabaseConstants.objects,
			dbref.Number.ToString());

		if (obj is null) return new None();
		if (dbref.CreationMilliseconds is not null && obj.CreationTime != dbref.CreationMilliseconds) return new None();

		var startVertex = obj.Id;
		var res = (await arangoDB.Query.ExecuteAsync<SharpObjectQueryResult>(handle,
				$"FOR v IN 1..1 INBOUND {startVertex} GRAPH {DatabaseConstants.graphObjects} RETURN v", cache: true))
			.FirstOrDefault();

		if (res is null) return new None();

		var id = res.Id;

		var convertObject = new SharpObject()
		{
			Name = obj.Name,
			Type = obj.Type,
			CreationTime = obj.CreationTime,
			ModifiedTime = obj.ModifiedTime,
			Locks = (obj.Locks ?? []).ToImmutableDictionary(),
			Id = obj.Id,
			Key = int.Parse(obj.Key),
			Flags = new(() => mediator.Send(new GetObjectFlagsQuery(startVertex)).AsTask().Result ?? []),
			Powers = new(() => GetPowersAsync(startVertex).AsTask().Result),
			Attributes = new(() => GetTopLevelAttributesAsync(startVertex).AsTask().Result),
			AllAttributes = new(() => GetAllAttributesAsync(startVertex).AsTask().Result),
			Owner = new(() => GetObjectOwnerAsync(startVertex).AsTask().Result),
			Parent = new(() => GetParentAsync(startVertex).AsTask().Result)
		};

		return obj.Type switch
		{
			DatabaseConstants.typeThing => new SharpThing
			{
				Id = id, Object = convertObject,
				Location = new Lazy<AnySharpContainer>(() => mediator.Send(new GetCertainLocationQuery(id)).AsTask().Result),
				Home = new Lazy<AnySharpContainer>(() => GetHomeAsync(id).AsTask().Result)
			},
			DatabaseConstants.typePlayer => new SharpPlayer
			{
				Id = id, Object = convertObject, Aliases = res.Aliases,
				Location = new Lazy<AnySharpContainer>(() => mediator.Send(new GetCertainLocationQuery(id)).AsTask().Result),
				Home = new Lazy<AnySharpContainer>(() => GetHomeAsync(id).AsTask().Result),
				PasswordHash = res.PasswordHash
			},
			DatabaseConstants.typeRoom => new SharpRoom { Id = id, Object = convertObject },
			DatabaseConstants.typeExit => new SharpExit
			{
				Id = id, Object = convertObject, Aliases = res.Aliases,
				Location = new Lazy<AnySharpContainer>(() => mediator.Send(new GetCertainLocationQuery(id)).AsTask().Result),
				Home = new Lazy<AnySharpContainer>(() => GetHomeAsync(id).AsTask().Result)
			},
			_ => throw new ArgumentException($"Invalid Object Type found: '{obj.Type}'"),
		};
	}

	private async ValueTask<AnyOptionalSharpObject> GetObjectNodeAsync(string dbID)
	{
		ArangoList<dynamic>? query;
		if (dbID.StartsWith(DatabaseConstants.objects))
		{
			query = await arangoDB.Query.ExecuteAsync<dynamic>(handle,
				$"FOR v IN 0..1 INBOUND {dbID} GRAPH {DatabaseConstants.graphObjects} RETURN v",
				cache: true);
			query.Reverse();
		}
		else
		{
			query = await arangoDB.Query.ExecuteAsync<dynamic>(handle,
				$"FOR v IN 0..1 OUTBOUND {dbID} GRAPH {DatabaseConstants.graphObjects} RETURN v", cache: true);
		}

		var res = query.First();
		var obj = query.Last();

		string id = res._id;
		string objId = obj._id;
		var collection = id.Split("/")[0];
		var convertObject = new SharpObject
		{
			Id = objId,
			Key = int.Parse((string)obj._key),
			Name = obj.Name,
			Type = obj.Type,
			CreationTime = obj.CreationTime,
			ModifiedTime = obj.ModifiedTime,
			Locks = ImmutableDictionary<string, string>.Empty, // FIX: ((Dictionary<string, string>?)obj.Locks ?? []).ToImmutableDictionary(),
			Flags = new(() => GetObjectFlagsAsync(objId).AsTask().Result),
			Powers = new(() => GetPowersAsync(objId).AsTask().Result),
			Attributes = new(() => GetTopLevelAttributesAsync(objId).AsTask().Result),
			AllAttributes = new(() => GetAllAttributesAsync(objId).AsTask().Result),
			Owner = new(() => GetObjectOwnerAsync(objId).AsTask().Result),
			Parent = new(() => GetParentAsync(objId).AsTask().Result)
		};

		return collection switch
		{
			DatabaseConstants.things => new SharpThing
			{
				Id = id, Object = convertObject,
				Location = new(() => mediator.Send(new GetCertainLocationQuery(id)).AsTask().Result),
				Home = new(() => GetHomeAsync(id).AsTask().Result)
			},
			DatabaseConstants.players => new SharpPlayer
			{
				Id = id, Object = convertObject, Aliases = res.Aliases.ToObject<string[]>(),
				Location = new(() => mediator.Send(new GetCertainLocationQuery(id)).AsTask().Result),
				Home = new(() => GetHomeAsync(id).AsTask().Result), PasswordHash = res.PasswordHash
			},
			DatabaseConstants.rooms => new SharpRoom { Id = id, Object = convertObject },
			DatabaseConstants.exits => new SharpExit
			{
				Id = id, Object = convertObject, Aliases = res.Aliases.ToObject<string[]>(),
				Location = new(() => mediator.Send(new GetCertainLocationQuery(id)).AsTask().Result),
				Home = new(() => GetHomeAsync(id).AsTask().Result)
			},
			_ => throw new ArgumentException($"Invalid Object Type found: '{obj.Type}'"),
		};
	}

	public async ValueTask<SharpObject?> GetBaseObjectNodeAsync(DBRef dbref)
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
				Flags = new(() => GetObjectFlagsAsync(obj.Id).AsTask().Result),
				Powers = new(() => GetPowersAsync(obj.Id).AsTask().Result),
				Attributes = new(() => GetTopLevelAttributesAsync(obj.Id).AsTask().Result),
				AllAttributes = new(() => GetAllAttributesAsync(obj.Id).AsTask().Result),
				Owner = new(() => GetObjectOwnerAsync(obj.Id).AsTask().Result),
				Parent = new(() => GetParentAsync(obj.Id).AsTask().Result)
			};
	}

	private async ValueTask<IEnumerable<SharpAttribute>> GetTopLevelAttributesAsync(string id)
	{
		// This only works for when we get a non-attribute as our ID.
		// Adjustment is needed if we get an attribute ID.
		IEnumerable<SharpAttributeQueryResult> sharpAttributeResults;
		if (id.StartsWith(DatabaseConstants.attributes))
		{
			sharpAttributeResults = await arangoDB.Query.ExecuteAsync<SharpAttributeQueryResult>(handle,
				$"FOR v IN 1..1 OUTBOUND @startVertex GRAPH {DatabaseConstants.graphAttributes} RETURN v",
				new Dictionary<string, object>() { { "startVertex", id } });
		}
		else
		{
			sharpAttributeResults = await arangoDB.Query.ExecuteAsync<SharpAttributeQueryResult>(handle,
				$"LET start = FIRST(FOR v IN 1..1 INBOUND @startVertex GRAPH {DatabaseConstants.graphObjects} RETURN v) FOR v IN 1..1 OUTBOUND start GRAPH {DatabaseConstants.graphAttributes} RETURN v",
				new Dictionary<string, object>() { { "startVertex", id } });
		}

		var sharpAttributes = sharpAttributeResults.Select(async x => new SharpAttribute(x.Key, x.Name, await GetAttributeFlagsAsync(x.Id), null, x.LongName, new(() => GetTopLevelAttributesAsync(x.Id).AsTask().Result), new(() => GetAttributeOwnerAsync(x.Id).AsTask().Result), new(() => null))
		{
			Value = MarkupStringModule.deserialize(x.Value)
		});

		return await Task.WhenAll(sharpAttributes);
	}

	public async ValueTask<IEnumerable<SharpAttribute>?> GetAttributesAsync(DBRef dbref, string attribute_pattern)
	{
		var startVertex = $"{DatabaseConstants.objects}/{dbref.Number}";
		var result = await arangoDB.Query.ExecuteAsync<SharpObjectQueryResult>(handle, $"RETURN DOCUMENT({startVertex})", cache: true);
		var pattern = attribute_pattern.Replace("_", "\\_").Replace("%", "\\%").Replace("?", "_").Replace("*", "%");

		if (!result.Any())
		{
			return null;
		}

		// TODO: This is a lazy implementation and does not appropriately support the ` section of pattern matching for attribute trees.
		// TODO: A pattern with a wildcard can match multiple levels of attributes.
		// This means it can also match attributes deeper in its structure that need to be reported on.
		// It already does this right now. But not in a sorted manner!

		// OPTIONS { indexHint: "inverted_index_name", forceIndexHint: true }
		// This doesn't seem like it can be done on a GRAPH query?
		const string query =
			$"FOR v IN 1 OUTBOUND @startVertex GRAPH {DatabaseConstants.graphAttributes} FILTER v.LongName LIKE @pattern RETURN v";

		var result2 = await arangoDB.Query.ExecuteAsync<SharpAttributeQueryResult>(handle, query,
			new Dictionary<string, object>()
			{
						{ "startVertex", startVertex },
						{ "pattern", pattern }
			});

		return await Task.WhenAll(result2.Select(async x => new SharpAttribute(x.Key, x.Name, await GetAttributeFlagsAsync(x.Id), null, x.LongName, new(() => GetTopLevelAttributesAsync(x.Id).AsTask().Result), new(() => GetObjectOwnerAsync(x.Id).AsTask().Result), new(() => null))
		{
			Value = MarkupStringModule.deserialize(x.Value)
		}));
	}

	public async ValueTask<IEnumerable<SharpAttribute>?> GetAttributesRegexAsync(DBRef dbref, string attribute_pattern)
	{
		var startVertex = $"{DatabaseConstants.objects}/{dbref.Number}";
		var result = await arangoDB.Query.ExecuteAsync<SharpObjectQueryResult>(handle, $"RETURN DOCUMENT({startVertex})", cache: true);

		if (!result.Any())
		{
			return null;
		}

		// TODO: Create an Inverted Index on LongName.
		var query =
			$"FOR v IN 1 OUTBOUND @startVertex GRAPH {DatabaseConstants.graphAttributes} FILTER v.LongName =~ @pattern RETURN v";

		var result2 = await arangoDB.Query.ExecuteAsync<SharpAttributeQueryResult>(handle, query,
			new Dictionary<string, object>()
			{
						{ "startVertex", startVertex },
						{ "pattern", attribute_pattern }
			});

		return await Task.WhenAll(result2.Select(async x => new SharpAttribute(x.Key, x.Name, await GetAttributeFlagsAsync(x.Id), null, x.LongName, new(() => GetTopLevelAttributesAsync(x.Id).AsTask().Result), new(() => GetObjectOwnerAsync(x.Id).AsTask().Result), new(() => null))
		{
			Value = MarkupStringModule.deserialize(x.Value)
		}));
	}

	public async ValueTask SetLockAsync(SharpObject target, string lockName, string lockString)
		=> await arangoDB.Document.UpdateAsync(handle, DatabaseConstants.objects, new
		{
			target.Key,
			Locks = target.Locks.Add(lockName, lockString)
		}, mergeObjects: true);

	public async ValueTask<IEnumerable<SharpAttribute>?> GetAttributeAsync(DBRef dbref, params string[] attribute)
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

		return await Task.WhenAll(result.Select(async x => new SharpAttribute(x.Key, x.Name, await GetAttributeFlagsAsync(x.Id), null, x.LongName, new(() => GetTopLevelAttributesAsync(x.Id).AsTask().Result), new(() => GetAttributeOwnerAsync(x.Id).AsTask().Result), new(() => null))
		{
			Value = MarkupStringModule.deserialize(x.Value)
		}));
	}

	public async ValueTask<bool> SetAttributeAsync(DBRef dbref, string[] attribute, MarkupStringModule.MarkupString value, SharpPlayer owner)
	{
		ArgumentException.ThrowIfNullOrEmpty(owner?.Id);

		var transactionHandle = await arangoDB.Transaction.BeginAsync(handle, new ArangoTransaction
		{
			LockTimeout = DatabaseBehaviorConstants.TransactionTimeout,
			WaitForSync = true,
			AllowImplicit = false,
			Collections = new ArangoTransactionScope
			{
				Exclusive = [DatabaseConstants.attributes, DatabaseConstants.hasAttribute, DatabaseConstants.hasAttributeOwner],
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

		// Create Path
		foreach (var nextAttr in remaining.Select((attrName, i) => (value: attrName, i)))
		{
			var newOne = await arangoDB.Document.CreateAsync<SharpAttributeCreateRequest, SharpAttributeQueryResult>(
				transactionHandle, DatabaseConstants.attributes,
				new SharpAttributeCreateRequest(nextAttr.value.ToUpper(), [],
					(nextAttr.i == remaining.Length - 1)
						? MarkupStringModule.serialize(value)
						: string.Empty,
					string.Join('`', attribute.SkipLast(remaining.Length - 1 - nextAttr.i).Select(x => x.ToUpper()))),
				waitForSync: true);

			await arangoDB.Graph.Edge.CreateAsync(transactionHandle, DatabaseConstants.graphAttributes,
				DatabaseConstants.hasAttribute,
				new SharpEdgeCreateRequest(lastId, newOne.Id), waitForSync: true);

			await arangoDB.Graph.Edge.CreateAsync(transactionHandle, DatabaseConstants.graphAttributeOwners,
				DatabaseConstants.hasAttributeOwner,
				new SharpEdgeCreateRequest(newOne.Id, owner.Object.Id!), waitForSync: true);

			lastId = newOne.Id;
		}

		// Update Path
		if (remaining.Length == 0)
		{
			await arangoDB.Document.UpdateAsync(transactionHandle, DatabaseConstants.attributes,
				new { Key = lastId.Split('/')[1], Value = MarkupStringModule.serialize(value) }, waitForSync: true, mergeObjects: true);

			await arangoDB.Graph.Edge.CreateAsync(transactionHandle, DatabaseConstants.graphAttributeOwners,
				DatabaseConstants.hasAttributeOwner,
				new SharpEdgeCreateRequest(lastId, owner.Object.Id!), waitForSync: true);
		}

		await arangoDB.Transaction.CommitAsync(transactionHandle);

		return true;
	}

	public async ValueTask<bool> SetAttributeFlagAsync(SharpObject dbref, string[] attribute, SharpAttributeFlag flag)
	{
		var attrInfo = await GetAttributeAsync(dbref.DBRef, attribute);
		if (attrInfo is null) return false;
		var attr = attrInfo.Last();

		await SetAttributeFlagAsync(attr, flag);
		return true;
	}

	public async ValueTask SetAttributeFlagAsync(SharpAttribute attr, SharpAttributeFlag flag) =>
		await arangoDB.Document.UpdateAsync(handle, DatabaseConstants.attributes, new
		{
			attr.Key,
			Value = attr.Flags.ToImmutableArray().Add(flag)
		});

	public async ValueTask<bool> UnsetAttributeFlagAsync(SharpObject dbref, string[] attribute, SharpAttributeFlag flag)
	{
		var attrInfo = await GetAttributeAsync(dbref.DBRef, attribute);
		if (attrInfo is null) return false;
		var attr = attrInfo.Last();

		await UnsetAttributeFlagAsync(attr, flag);
		return true;
	}

	public async ValueTask UnsetAttributeFlagAsync(SharpAttribute attr, SharpAttributeFlag flag) =>
		await arangoDB.Document.UpdateAsync(handle, DatabaseConstants.attributes, new
		{
			attr.Key,
			Value = attr.Flags.ToImmutableArray().Remove(flag)
		});

	public async ValueTask<SharpAttributeFlag?> GetAttributeFlagAsync(string flagName) =>
		(await arangoDB.Query.ExecuteAsync<SharpAttributeFlag>(handle,
			$"FOR v in @@C1 FILTER v.Name = @flag RETURN v",
			bindVars: new Dictionary<string, object>
			{
				{ "@C1", DatabaseConstants.attributeFlags },
				{ "flag", flagName }
			}, cache: true)).FirstOrDefault();

	public async ValueTask<IEnumerable<SharpAttributeFlag>> GetAttributeFlagsAsync() =>
		await arangoDB.Query.ExecuteAsync<SharpAttributeFlag>(handle,
			$"FOR v in {DatabaseConstants.attributeFlags:@} RETURN v",
			cache: true);

	public ValueTask<bool> ClearAttributeAsync(DBRef dbref, string[] attribute)
	{
		// Set the contents to empty.

		throw new NotImplementedException();
	}

	public ValueTask<bool> WipeAttributeAsync(DBRef dbref, string[] attribute)
	{
		// Wipe a list of attributes. We assume the calling code figured out the permissions part.

		throw new NotImplementedException();
	}

	public async ValueTask<IEnumerable<AnySharpObject>> GetNearbyObjectsAsync(DBRef obj)
	{
		var self = (await GetObjectNodeAsync(obj)).WithoutNone();
		var location = self.Where;

		return
		[
			self,
			.. (await GetContentsAsync(self.Object().DBRef))!.Select(x => x.WithRoomOption()),
			.. (await GetContentsAsync(location.Object().DBRef))!.Select(x => x.WithRoomOption()),
		];
	}

	public async ValueTask<IEnumerable<AnySharpObject>> GetNearbyObjectsAsync(AnySharpObject obj)
	{
		var location = obj.Where;

		return
		[
			obj,
			.. (await GetContentsAsync(obj.Object().DBRef))!.Select(x => x.WithRoomOption()),
			.. (await GetContentsAsync(location.Object().DBRef))!.Select(x => x.WithRoomOption()),
		];
	}

	/// <summary>
	/// Gets the location of an object, at X depth, with 0 returning the same object, and -1 going until it can't go deeper.
	/// </summary>
	/// <param name="obj">Location</param>
	/// <param name="depth">Depth</param>
	/// <returns>The deepest findable object based on depth</returns>
	public async ValueTask<AnyOptionalSharpContainer> GetLocationAsync(DBRef obj, int depth = 1)
	{
		var baseObject = await GetObjectNodeAsync(obj);
		if (baseObject.IsNone) return new None();

		var variableDepth = depth == -1 ? "0" : $"0..{depth}";
		var locationQuery =
			$"FOR v IN {variableDepth} OUTBOUND @startVertex GRAPH {DatabaseConstants.graphLocations} RETURN v._id";
		var query = await arangoDB.Query.ExecuteAsync<string>(handle, locationQuery, new Dictionary<string, object>()
		{
			{ "startVertex", baseObject.Id()! }
		});
		var locationBaseObj = await GetObjectNodeAsync((string)query.Last());
		var trueLocation = locationBaseObj.Match<AnyOptionalSharpContainer>(
			player => player,
			room => room,
			exit => throw new Exception("Invalid Location found"),
			thing => thing,
			none => throw new Exception("Invalid Location found"));

		return trueLocation;
	}

	/// <summary>
	/// Gets the location of an object, at X depth, with 0 returning the same object, and -1 going until it can't go deeper.
	/// </summary>
	/// <param name="id">Location ID</param>
	/// <param name="depth">Depth</param>
	/// <returns>The deepest findable object based on depth</returns>
	public async ValueTask<AnySharpContainer> GetLocationAsync(string id, int depth = 1)
	{
		var variableDepth = depth == -1 ? "0" : $"0..{depth}";
		var locationQuery =
			$"FOR v IN {variableDepth} OUTBOUND @startVertex GRAPH {DatabaseConstants.graphLocations} RETURN v._id";
		var query = await arangoDB.Query.ExecuteAsync<string>(handle, locationQuery, new Dictionary<string, object>()
		{
			{ "startVertex", id }
		});
		var locationBaseObj = await GetObjectNodeAsync(query.Last());
		var trueLocation = locationBaseObj.Match<AnySharpContainer>(
			player => player,
			room => room,
			exit => throw new Exception("Invalid Location found"),
			thing => thing,
			none => throw new Exception("Invalid Location found"));

		return trueLocation;
	}

	public async ValueTask<AnySharpContainer> GetLocationAsync(AnySharpObject obj, int depth = 1) =>
		(await GetLocationAsync(obj.Object().DBRef, depth)).WithoutNone();

	public async ValueTask<IEnumerable<AnySharpContent>?> GetContentsAsync(DBRef obj)
	{
		var baseObject = await GetObjectNodeAsync(obj);
		if (baseObject.IsNone) return null;

		const string locationQuery =
			$"FOR v IN 1..1 INBOUND @startVertex GRAPH {DatabaseConstants.graphLocations} RETURN v._id";
		var query = await arangoDB.Query.ExecuteAsync<string>(handle, $"{locationQuery}",
			new Dictionary<string, object>
			{
				{ "startVertex", baseObject.Object()!.Id! }
			});
		var result = query
			.Select(x => x)
			.Select(GetObjectNodeAsync) // TODO: Optimize to make a single call.
			.Select(async x => (await x).Match<AnySharpContent>(
				player => player,
				room => throw new Exception("Invalid Contents found"),
				exit => exit,
				thing => thing,
				none => throw new Exception("Invalid Contents found")
			));

		return await Task.WhenAll(result);
	}

	public async ValueTask<IEnumerable<AnySharpContent>?> GetContentsAsync(AnySharpContainer node)
	{
		var startVertex = node.Id;

		const string locationQuery =
			$"FOR v IN 1..1 INBOUND @startVertex GRAPH {DatabaseConstants.graphLocations} RETURN v";
		var query = await arangoDB.Query.ExecuteAsync<dynamic>(handle, $"{locationQuery}",
			new Dictionary<string, object>
			{
				{ "startVertex", startVertex }
			});

		var ids = query.Select(x => (string)x._id).ToArray();
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

	public async ValueTask<IEnumerable<SharpExit>?> GetExitsAsync(DBRef obj)
	{
		var baseObject = await GetObjectNodeAsync(obj);
		if (baseObject.IsT4) return null;

		const string exitQuery = $"FOR v IN 1..1 INBOUND @startVertex GRAPH {DatabaseConstants.graphExits} RETURN v";
		var query = await arangoDB.Query.ExecuteAsync<SharpObjectQueryResult>(handle, $"{exitQuery}",
			new Dictionary<string, object>
			{
				{ "startVertex", baseObject.Object()!.Id! }
			});
		var result = query
			.Select(x => x.Id)
			.Select(GetObjectNodeAsync) // TODO: Optimize to make a single call.
			.Select(async x => (await x).Match(
				player => throw new Exception("Invalid Exit found"),
				room => throw new Exception("Invalid Exit found"),
				exit => exit,
				thing => throw new Exception("Invalid Exit found"),
				none => throw new Exception("Invalid Exit found")
			));

		return await Task.WhenAll(result);
	}

	public async ValueTask<IEnumerable<SharpExit>> GetExitsAsync(AnySharpContainer node)
	{
		var startVertex = node.Id;

		const string exitQuery = $"FOR v IN 1..1 INBOUND @startVertex GRAPH {DatabaseConstants.graphExits} RETURN v";
		var query = await arangoDB.Query.ExecuteAsync<SharpObjectQueryResult>(handle, $"{exitQuery}",
			new Dictionary<string, object>
			{
				{ "startVertex", startVertex! }
			});
		var result = query
			.Select(x => x.Id)
			.Select(GetObjectNodeAsync) // TODO: Optimize to make a single call.
			.Select(async x => (await x).Match(
				player => throw new Exception("Invalid Exit found"),
				room => throw new Exception("Invalid Exit found"),
				exit => exit,
				thing => throw new Exception("Invalid Exit found"),
				none => throw new Exception("Invalid Exit found")
			));

		return await Task.WhenAll(result);
	}

	public async ValueTask<IEnumerable<SharpPlayer>> GetPlayerByNameAsync(string name)
	{
		// TODO: Look up by Alias.
		var query = await arangoDB.Query.ExecuteAsync<string>(handle,
			$"FOR v IN {DatabaseConstants.objects} FILTER v.Type == @type && v.Name == @name RETURN v._id",
			bindVars: new Dictionary<string, object>
			{
				{ "name", name },
				{ "type", DatabaseConstants.typePlayer }
			});

		// TODO: Edit to return multiple players and let the above layer figure out which one it wants.
		var result = query.FirstOrDefault();
		if (result is null) return [];

		return await Task.WhenAll(query.Select(GetObjectNodeAsync).Select(async x => (await x).AsPlayer));
	}

	public async ValueTask MoveObjectAsync(AnySharpContent enactorObj, AnySharpContainer destination)
	{
		var oldLocation = enactorObj.Location();
		var newLocation = destination;
		var edge = (await arangoDB.Query.ExecuteAsync<SharpEdgeQueryResult>(handle,
				$"FOR v,e IN 1..1 OUTBOUND {oldLocation.Object().Id} GRAPH {DatabaseConstants.graphLocations} RETURN e"))
			.Single();

		await arangoDB.Graph.Edge.UpdateAsync(handle,
			DatabaseConstants.graphLocations,
			DatabaseConstants.atLocation,
			edge.Key,
			new
			{
				From = enactorObj.Object().Id,
				To = newLocation.Object().Id
			},
			waitForSync: true);
	}

		ValueTask<bool> ISharpDatabase.UnsetAttributeFlagAsync(SharpAttribute attr, SharpAttributeFlag flag)
		{
				throw new NotImplementedException();
		}
}