using Core.Arango;
using Core.Arango.Migration;
using Microsoft.Extensions.Logging;
using OneOf;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Database
{
	// TODO: Unit of Work / Transaction around all of this!
	public class ArangoDatabase(ILogger<ArangoDatabase> logger, IArangoContext arangoDB, ArangoHandle handle, IPasswordService passwordService) : ISharpDatabase
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

		public async Task<int> CreatePlayer(string name, string password, DBRef location)
		{
			var obj = await arangoDB.Document.CreateAsync<dynamic, dynamic>(handle, DatabaseConstants.objects, new
			{
				Name = name,
				CreationTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
			}, returnNew: true);

			var newObject = obj.New;
			var hashedPassword = passwordService.HashPassword($"#{newObject.Key}:{newObject.CreationTime}", password);

			var player = await arangoDB.Document.CreateAsync<dynamic, dynamic>(handle, DatabaseConstants.players, new
			{
				PasswordHash = hashedPassword
			});

			await arangoDB.Document.CreateAsync(handle, DatabaseConstants.isObject, new SharpEdge { From = player.Id, To = obj.Id });
			await arangoDB.Document.CreateAsync(handle, DatabaseConstants.hasObjectOwner, new SharpEdge { From = player.Id, To = player.Id! });

			var objectLocation = await GetObjectNode(location);

			var idx = objectLocation.Value.Match(
				player => player.Id,
				room => room.Id,
				exit => throw new ArgumentException("An Exit is not a valid location to create a player!"),
				thing => thing.Id);

			await arangoDB.Document.CreateAsync(handle, DatabaseConstants.atLocation, new SharpEdge { From = player.Id, To = idx! });
			await arangoDB.Document.CreateAsync(handle, DatabaseConstants.hasHome, new SharpEdge { From = player.Id, To = idx! });

			return int.Parse(obj.Key);
		}

		public async Task<int> CreateRoom(string name, SharpPlayer creator)
		{
			var obj = await arangoDB.Document.CreateAsync(handle, DatabaseConstants.objects, new SharpObject()
			{
				Name = name
			});

			var room = await arangoDB.Document.CreateAsync(handle, DatabaseConstants.rooms, new SharpRoom()
			{
			});

			var _ = await arangoDB.Document.CreateAsync(handle, DatabaseConstants.isObject, new SharpEdge { From = room.Id, To = obj.Id });
			await arangoDB.Document.CreateAsync(handle, DatabaseConstants.hasObjectOwner, new SharpEdge { From = room.Id, To = creator.Id! });

			return int.Parse(obj.Key);
		}

		public async Task<int> CreateThing(string name, OneOf<SharpPlayer, SharpRoom, SharpThing> location, SharpPlayer creator)
		{
			var obj = await arangoDB.Document.CreateAsync(handle, DatabaseConstants.objects, new SharpObject() { Name = name });
			var thing = await arangoDB.Document.CreateAsync(handle, DatabaseConstants.things, new SharpThing() { });

			await arangoDB.Document.CreateAsync(handle, DatabaseConstants.isObject, new SharpEdge { From = thing.Id, To = obj.Id });

			var idx = location.Match(
				player => player.Id,
				room => room.Id,
				thing => thing.Id
				);

			await arangoDB.Document.CreateAsync(handle, DatabaseConstants.atLocation, new SharpEdge { From = thing.Id, To = idx! });
			await arangoDB.Document.CreateAsync(handle, DatabaseConstants.hasHome, new SharpEdge { From = thing.Id, To = idx! });
			await arangoDB.Document.CreateAsync(handle, DatabaseConstants.hasObjectOwner, new SharpEdge { From = thing.Id, To = creator.Id! });

			return int.Parse(obj.Key);
		}

		public async Task<int> CreateExit(string name, OneOf<SharpPlayer, SharpRoom, SharpThing> location, SharpPlayer creator)
		{
			var obj = await arangoDB.Document.CreateAsync(handle, DatabaseConstants.objects, new SharpObject() { Name = name });
			var exit = await arangoDB.Document.CreateAsync(handle, DatabaseConstants.exits, new SharpExit());

			await arangoDB.Document.CreateAsync(handle, DatabaseConstants.isObject, new SharpEdge { From = exit.Id, To = obj.Id });

			var idx = location.Match(
				player => player.Id,
				room => room.Id,
				thing => thing.Id
			);

			await arangoDB.Document.CreateAsync(handle, DatabaseConstants.hasHome, new SharpEdge { From = exit.Id, To = idx! });
			await arangoDB.Document.CreateAsync(handle, DatabaseConstants.hasObjectOwner, new SharpEdge { From = exit.Id, To = idx! });

			return int.Parse(obj.Key);
		}

		public async Task<OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing>?> GetObjectNode(DBRef dbref)
		{
			var obj = await arangoDB.Document.GetAsync<dynamic>(handle, DatabaseConstants.objects, dbref.Number.ToString());
			var startVertex = obj._id;

			// TODO: Version that cares about CreatedMilliseconds
			var query = await arangoDB.Query.ExecuteAsync<dynamic>(handle,
				$"FOR v IN 1..1 INBOUND {startVertex} GRAPH {DatabaseConstants.graphObjects} RETURN {{ \"id\": v._id, \"collection\": PARSE_IDENTIFIER( v._id ).collection, \"vertex\": v}}");

			var res = query.FirstOrDefault();

			if (res == null)
			{
				return null;
			}

			string id = res.id;
			string collection = res.collection;
			dynamic vertex = res.vertex;
			var convertObject = new SharpObject()
			{
				Name = obj.Name,
				CreationTime = obj.CreationTime,
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

		public Task<SharpAttribute[]?> GetAttributes(DBRef dbref, string attribute_pattern)
		{
			// Step 1: Get Object.
			// Step 2: Find all attributes that belong to that Object.
			// Step 3: Filter down.

			// We cannot expect that attribute_pattern has been translated from GLOB to Arango LIKE.
			// "foo"  LIKE  "f%"          // true

			throw new NotImplementedException();
		}

		public Task<SharpAttribute[]?> GetAttributesRegex(DBRef dbref, string attribute_pattern)
		{
			// Step 1: Get Object.
			// Step 2: Find all attributes that belong to that Object.
			// Step 3: Filter down.

			// Technically a (largely useful) subset of Regex is supported by ArangoDB.
			// But it is annoying that ArangoDB and C# have different levels of Regex available to them.
			//  "foo"  =~  "^f[o].$"       // true
			throw new NotImplementedException();
		}

		public async Task<SharpAttribute[]?> GetAttribute(DBRef dbref, string[] attribute)
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

		public async Task<bool> SetAttribute(DBRef dbref, string[] attribute, string value, SharpPlayer owner)
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

		public Task<bool> ClearAttribute(DBRef dbref, string[] attribute)
		{
			throw new NotImplementedException();
		}
	}
}
