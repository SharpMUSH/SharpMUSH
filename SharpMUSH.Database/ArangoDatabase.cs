using Core.Arango;
using Core.Arango.Migration;
using Microsoft.Extensions.Logging;
using OneOf;
using OneOf.Types;
using Serilog;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Database
{
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

		public async Task<int> CreatePlayerAsync(string name, string password, DBRef location)
		{
			var obj = await arangoDB.Document.CreateAsync<dynamic, dynamic>(handle, DatabaseConstants.objects, new
			{
				Name = name,
				Type = DatabaseConstants.typePlayer,
				CreationTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
				ModifiedTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
			}, returnNew: true);

			var newObject = obj.New;
			var hashedPassword = passwordService.HashPassword($"#{newObject.Key}:{newObject.CreationTime}", password);

			var player = await arangoDB.Document.CreateAsync<dynamic, dynamic>(handle, DatabaseConstants.players, new
			{
				PasswordHash = hashedPassword
			});

			await arangoDB.Document.CreateAsync(handle, DatabaseConstants.isObject, new SharpEdge { From = player.Id, To = obj.Id });
			await arangoDB.Document.CreateAsync(handle, DatabaseConstants.hasObjectOwner, new SharpEdge { From = player.Id, To = player.Id! });

			var objectLocation = await GetObjectNodeAsync(location);

			var idx = objectLocation.Match(
				player => player.Id,
				room => room.Id,
				exit => throw new ArgumentException("An Exit is not a valid location to create a player!"),
				thing => thing.Id,
				none => throw new ArgumentException("A player must have a valid creation location!"));

			await arangoDB.Document.CreateAsync(handle, DatabaseConstants.atLocation, new SharpEdge { From = player.Id, To = idx! });
			await arangoDB.Document.CreateAsync(handle, DatabaseConstants.hasHome, new SharpEdge { From = player.Id, To = idx! });

			return int.Parse(obj.Key);
		}

		public async Task<int> CreateRoomAsync(string name, SharpPlayer creator)
		{
			var obj = await arangoDB.Document.CreateAsync(handle, DatabaseConstants.objects, new SharpObject()
			{
				Type = DatabaseConstants.typeRoom,
				Name = name
			});

			var room = await arangoDB.Document.CreateAsync(handle, DatabaseConstants.rooms, new SharpRoom()
			{
			});

			var _ = await arangoDB.Document.CreateAsync(handle, DatabaseConstants.isObject, new SharpEdge { From = room.Id, To = obj.Id });
			await arangoDB.Document.CreateAsync(handle, DatabaseConstants.hasObjectOwner, new SharpEdge { From = room.Id, To = creator.Id! });

			return int.Parse(obj.Key);
		}

		public async Task<int> CreateThingAsync(string name, OneOf<SharpPlayer, SharpRoom, SharpThing> location, SharpPlayer creator)
		{
			var obj = await arangoDB.Document.CreateAsync(handle, DatabaseConstants.objects, new SharpObject()
			{
				Type = DatabaseConstants.typeThing,
				Name = name
			});
			var thing = await arangoDB.Document.CreateAsync(handle, DatabaseConstants.things, new SharpThing() { });

			await arangoDB.Document.CreateAsync(handle, DatabaseConstants.isObject, new SharpEdge { From = thing.Id, To = obj.Id });

			var idx = location.Object()?.Id;

			await arangoDB.Document.CreateAsync(handle, DatabaseConstants.atLocation, new SharpEdge { From = thing.Id, To = idx! });
			await arangoDB.Document.CreateAsync(handle, DatabaseConstants.hasHome, new SharpEdge { From = thing.Id, To = idx! });
			await arangoDB.Document.CreateAsync(handle, DatabaseConstants.hasObjectOwner, new SharpEdge { From = thing.Id, To = creator.Id! });

			return int.Parse(obj.Key);
		}

		public async Task<int> CreateExitAsync(string name, OneOf<SharpPlayer, SharpRoom, SharpThing> location, SharpPlayer creator)
		{
			var obj = await arangoDB.Document.CreateAsync(handle, DatabaseConstants.objects, new SharpObject()
			{
				Type = DatabaseConstants.typeExit,
				Name = name
			});
			var exit = await arangoDB.Document.CreateAsync(handle, DatabaseConstants.exits, new SharpExit());

			await arangoDB.Document.CreateAsync(handle, DatabaseConstants.isObject, new SharpEdge { From = exit.Id, To = obj.Id });

			var idx = location.Object()!.Id!;

			await arangoDB.Document.CreateAsync(handle, DatabaseConstants.hasHome, new SharpEdge { From = exit.Id, To = idx });
			await arangoDB.Document.CreateAsync(handle, DatabaseConstants.hasObjectOwner, new SharpEdge { From = exit.Id, To = idx });

			return int.Parse(obj.Key);
		}

		public OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing, None> GetObjectNode(DBRef dbref) 
			=> GetObjectNodeAsync(dbref).Result;

		public async Task<OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing, None>> GetObjectNodeAsync(DBRef dbref)
		{
			Log.Logger.Information("Test");
			// TODO: Version that cares about CreatedMilliseconds 
			var obj = await arangoDB.Document.GetAsync<dynamic>(handle, DatabaseConstants.objects, dbref.Number.ToString());
			if (obj == null) return new None();

			var startVertex = obj._id;

			var query = await arangoDB.Query.ExecuteAsync<dynamic>(handle,
				$"FOR v IN 1..1 INBOUND {startVertex} GRAPH {DatabaseConstants.graphObjects} RETURN {{ \"id\": v._id, \"collection\": PARSE_IDENTIFIER( v._id ).collection, \"vertex\": v}}");

			var res = query.SingleOrDefault();
			if (res == null) return new None();

			string id = res.id;
			string collection = res.collection;
			dynamic vertex = res.vertex;
			var convertObject = new SharpObject()
			{
				Name = obj.Name,
				Type = obj.Type,
				CreationTime = obj.CreationTime,
				ModifiedTime = obj.ModifiedTime,
				Flags = obj.Flags,
				Locks = obj.Locks,
				Id = obj._id,
				Key = obj._key,
				Powers = obj.Powers
			};

			return collection switch
			{
				DatabaseConstants.things => new SharpThing { Id = id, Object = convertObject },
				DatabaseConstants.players => new SharpPlayer { Id = id, PasswordHash = vertex.PasswordHash, Aliases = vertex.Aliases, Object = convertObject },
				DatabaseConstants.rooms => new SharpRoom { Id = id, Object = convertObject },
				DatabaseConstants.exits => new SharpThing { Id = id, Object = convertObject, Aliases = vertex.Aliases },
				_ => throw new ArgumentException($"Invalid collection found: {collection}"),
			};
		}

		private async Task<OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing>> GetObjectNodeAsync(string dbID)
		{
			var startVertex = dbID;

			var query = await arangoDB.Query.ExecuteAsync<dynamic>(handle,
				$"FOR v IN 0..1 OUTBOUND {startVertex} GRAPH {DatabaseConstants.graphObjects} RETURN {{ \"id\": v._id, \"collection\": PARSE_IDENTIFIER( v._id ).collection, \"vertex\": v}}");

			var obj = query.Last().vertex;
			var res = query.First();

			string id = res.id;
			string collection = res.collection;
			dynamic vertex = res.vertex;
			var convertObject = new SharpObject()
			{
				Name = obj.Name,
				Type = obj.Type,
				CreationTime = obj.CreationTime,
				ModifiedTime = obj.ModifiedTime,
				Flags = obj.Flags,
				Locks = obj.Locks,
				Id = obj._id,
				Key = obj._key,
				Powers = obj.Powers
			};

			return collection switch
			{
				DatabaseConstants.things => new SharpThing { Id = id, Object = convertObject },
				DatabaseConstants.players => new SharpPlayer { Id = id, PasswordHash = vertex.PasswordHash, Aliases = vertex.Aliases, Object = convertObject },
				DatabaseConstants.rooms => new SharpRoom { Id = id, Object = convertObject },
				DatabaseConstants.exits => new SharpThing { Id = id, Object = convertObject, Aliases = vertex.Aliases },
				_ => throw new ArgumentException($"Invalid collection found: {collection}"),
			};
		}

		public async Task<OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing, None>> PopulateObjectNodeAsync(OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing> node)
		{
			var startVertex = node!.Id();
			if (startVertex == null)
				return new None();

			// TODO: This is doing too much work. It should assume that the object is populated, and should just put the SharpObject into it.
			var query = await arangoDB.Query.ExecuteAsync<dynamic>(handle,
				$"FOR v IN 1..1 OUTBOUND {startVertex} GRAPH {DatabaseConstants.graphObjects} RETURN {{ \"id\": v._id, \"collection\": PARSE_IDENTIFIER( v._id ).collection, \"vertex\": v}}");

			var obj = query.Single()!.vertex;
			if (obj == null) { return new None(); }

			var convertObject = new SharpObject()
			{
				Name = obj.Name,
				Type = obj.Type,
				CreationTime = obj.CreationTime,
				ModifiedTime = obj.ModifiedTime,
				Flags = obj.Flags,
				Locks = obj.Locks,
				Id = obj._id,
				Key = obj._key,
				Powers = obj.Powers
			};

			return node.Match<OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing, None>>(
				player => { player.Object = convertObject; return player; },
				room => { room.Object = convertObject; return room; },
				exit => { exit.Object = convertObject; return exit; },
				thing => { thing.Object = convertObject; return thing; }
				);
		}

		public async Task<SharpObject?> GetBaseObjectNodeAsync(DBRef dbref)
		{
			// TODO: Version that cares about CreatedMilliseconds
			var obj = await arangoDB.Document.GetAsync<dynamic>(handle, DatabaseConstants.objects, dbref.Number.ToString());

			return obj == null
				? null
				: new SharpObject()
				{
					Name = obj.Name,
					Type = obj.Type,
					Id = obj._id,
					Key = obj._key,
					Flags = obj.Flags,
					Powers = obj.Powers,
					Locks = obj.Locks,
					CreationTime = obj.CreationTime,
					ModifiedTime = obj.ModifiedTime
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
			var result = await PopulateObjectNodeAsync(locationBaseObj);

			return result;
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
					thing => thing
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
					thing => thing
				));

			return result;
		}

		public async Task<SharpPlayer?> GetPlayerByNameAsync(string name)
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
			if (result == null) return null;

			var playerQuery = $"FOR v IN 1..1 INBOUND @startVertex GRAPH {DatabaseConstants.graphObjects} RETURN v";
			var playerQueryResult = await arangoDB.Query.ExecuteAsync<dynamic>(handle, playerQuery,
				bindVars: new Dictionary<string, object>
				{
					{ "startVertex", result._id },
				});
			var playerQueryFirstResult = playerQueryResult.First();

			return new SharpPlayer()
			{
				PasswordHash = playerQueryFirstResult.PasswordHash,
				Object = new SharpObject()
				{
					Name = result.Name,
					Type = result.Type,
					CreationTime = result.CreationTime,
					Flags = result.Flags,
					Id = result._id,
					Key = result._key,
					ModifiedTime = result.ModifiedTime,
					Locks = result.Locks,
					Owner = result.Owner,
					Parent = result.Parent,
					Powers = result.Powers,
				}
			};
		}
	}
}
