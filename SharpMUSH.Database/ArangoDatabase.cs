using Core.Arango;
using Core.Arango.Migration;
using Microsoft.Extensions.Logging;
using OneOf;
using SharpMUSH.Database.Types;

namespace SharpMUSH.Database
{
	// TODO: Unit of Work / Transaction around all of this!
	public class ArangoDatabase(ILogger<ArangoDatabase> logger, IArangoContext arangodb, ArangoHandle handle) : ISharpDatabase
	{
		public async Task Migrate()
		{
			logger.LogInformation("Migrating Database");

			var migrator = new ArangoMigrator(arangodb)
			{
				HistoryCollection = "MigrationHistory"
			};

			// load all migrations from assembly
			migrator.AddMigrations(typeof(ArangoDatabase).Assembly);

			// apply all migrations up to latest
			await migrator.UpgradeAsync(handle);

			logger.LogInformation("Migration Completed.");
		}

		public async Task<int> CreatePlayer(string name, string passwordHash, OneOf<SharpPlayer, SharpRoom, SharpThing> location)
		{
			var obj = await arangodb.Document.CreateAsync(handle, "node_objects", new SharpObject()
			{
				Name = name
			});

			var player = await arangodb.Document.CreateAsync(handle, "node_players", new SharpPlayer()
			{
				PasswordHash = passwordHash
			});

			var _ = await arangodb.Document.CreateAsync(handle, "edge_isobject", new SharpEdge { From = player.Id, To = obj.Id });

			var idx = location.Match(
				player => player.Id,
				room => room.Id,
				thing => thing.Id
				);

			var _1 = await arangodb.Document.CreateAsync(handle, "edge_atlocation", new SharpEdge { From = player.Id, To = idx! });
			var _2 = await arangodb.Document.CreateAsync(handle, "edge_hashome", new SharpEdge { From = player.Id, To = idx! });

			return int.Parse(obj.Key);
		}

		public async Task<int> CreateRoom(string name)
		{
			var obj = await arangodb.Document.CreateAsync(handle, "node_objects", new SharpObject()
			{
				Name = name
			});

			var room = await arangodb.Document.CreateAsync(handle, "node_rooms", new SharpRoom()
			{
			});

			var _ = await arangodb.Document.CreateAsync(handle, "edge_isobject", new SharpEdge { From = room.Id, To = obj.Id });

			return int.Parse(obj.Key);
		}

		public async Task<int> CreateThing(string name, OneOf<SharpPlayer, SharpRoom, SharpThing> location)
		{
			var obj = await arangodb.Document.CreateAsync(handle, "node_objects", new SharpObject()
			{
				Name = name
			});

			var thing = await arangodb.Document.CreateAsync(handle, "node_things", new SharpThing()
			{
			});


			var _0 = await arangodb.Document.CreateAsync(handle, "edge_isobject", new SharpEdge { From = thing.Id, To = obj.Id });

			var idx = location.Match(
				player => player.Id,
				room => room.Id,
				thing => thing.Id
				);

			var _1 = await arangodb.Document.CreateAsync(handle, "edge_atlocation", new SharpEdge { From = thing.Id, To = idx! });
			var _2 = await arangodb.Document.CreateAsync(handle, "edge_hashome", new SharpEdge { From = thing.Id, To = idx! });

			return int.Parse(obj.Key);
		}

		public async Task<int> CreateExit(string name, OneOf<SharpPlayer, SharpRoom, SharpThing> source)
		{
			var obj = await arangodb.Document.CreateAsync(handle, "node_objects", new SharpObject()
			{
				Name = name
			});

			var exit = await arangodb.Document.CreateAsync(handle, "node_exits", new SharpExit()
			{
				
			});

			var _ = await arangodb.Document.CreateAsync(handle, "edge_isobject", new SharpEdge { From = exit.Id, To = obj.Id });

			var idx = source.Match(
				player => player.Id,
				room => room.Id,
				thing => thing.Id
				);

			var _1 = await arangodb.Document.CreateAsync(handle, "edge_hashome", new SharpEdge { From = exit.Id, To = idx! });

			return int.Parse(obj.Key);
		}

		public async Task<SharpAttribute[]?> GetAttribute(int dbref, string[] attribute)
		{
			// TODO: Don't care about the type we get back. Just do the search!
			
			var startVertex = $"node_objects/{dbref}";
			var length = $"2..{attribute.Length}"; // Skip the hop to the object definition.
			var let = $"LET attributes = {attribute}";
			var query = $"{let} FOR v,e IN {length} OUTBOUND {startVertex} GRAPH graph_attributes PRUNE v.Name == LAST(attributes) RETURN {{attribute = e, pop = POP(attributes)}}";

			var result = await arangodb.Query.ExecuteAsync<SharpAttribute>(handle, $"{query}");

			// TODO: What if we did not find it?
			// We return the whole path so that the system can inspect the Flags and see if the permissions are A-OK.
			return result.ToArray();
		}

		public async Task<OneOf<SharpPlayer,SharpRoom,SharpExit,SharpThing>?> GetObjectNode(int dbref, int? createdsecs = null, int? createdmsecs = null)
		{
			var obj = await arangodb.Document.GetAsync<SharpObject>(handle, "node_objects", dbref.ToString());
			var startVertex = obj.Id;

			// TODO: Version that cares about createdsecs / createdmsecs
			var query = await arangodb.Query.ExecuteAsync<(string id, string collection, string vertex)>(handle, 
				$"FOR v IN 1..1 INBOUND {startVertex} GRAPH graph_objects RETURN {{ \"id\": v._id, \"collection\": PARSE_COLLECTION( v_.id ), \"vertex\": v}}");
			
			(string id, string collection, dynamic vertex) = query.First();

			switch (collection)
			{
				case "node_things":
					return new SharpThing { Id = id, Object = obj };
				case "node_players":
					return new SharpPlayer{ Id = id, PasswordHash = vertex.PasswordHash, Aliases = vertex.Aliases, Object = obj };
				case "node_rooms":
					return new SharpRoom { Id = id, Object = obj };
				case "node_exits":
					return new SharpThing { Id = id, Object = obj, Aliases = vertex.Aliases };
				default: throw new ArgumentException($"Invalid collection found: {collection}");
			}
		}
	}
}
