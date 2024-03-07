using Core.Arango;
using Core.Arango.Migration;
using Microsoft.Extensions.Logging;
using OneOf;
using SharpMUSH.Database.Types;

namespace SharpMUSH.Database
{
	// TODO: Unit of Work / Transaction around all of this!
	// TODO: A proper DBRef object that carries creationsecs on it optionally.
	public class ArangoDatabase(ILogger<ArangoDatabase> logger, IArangoContext arangodb, ArangoHandle handle) : ISharpDatabase
	{
		public async Task Migrate()
		{
			logger.LogInformation("Migrating Database");

			var migrator = new ArangoMigrator(arangodb)
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

			await arangodb.Document.CreateAsync(handle, "edge_is_object", new SharpEdge { From = player.Id, To = obj.Id });
			await arangodb.Document.CreateAsync(handle, "edge_has_object_owner", new SharpEdge { From = player.Id, To = player.Id! });

			var idx = location.Match(
				player => player.Id,
				room => room.Id,
				thing => thing.Id
				);

			await arangodb.Document.CreateAsync(handle, "edge_at_location", new SharpEdge { From = player.Id, To = idx! });
			await arangodb.Document.CreateAsync(handle, "edge_has_home", new SharpEdge { From = player.Id, To = idx! });

			return int.Parse(obj.Key);
		}

		public async Task<int> CreateRoom(string name, SharpPlayer creator)
		{
			var obj = await arangodb.Document.CreateAsync(handle, "node_objects", new SharpObject()
			{
				Name = name
			});

			var room = await arangodb.Document.CreateAsync(handle, "node_rooms", new SharpRoom()
			{
			});

			var _ = await arangodb.Document.CreateAsync(handle, "edge_is_object", new SharpEdge { From = room.Id, To = obj.Id });
			await arangodb.Document.CreateAsync(handle, "edge_has_object_owner", new SharpEdge { From = room.Id, To = creator.Id! });

			return int.Parse(obj.Key);
		}

		public async Task<int> CreateThing(string name, OneOf<SharpPlayer, SharpRoom, SharpThing> location, SharpPlayer creator)
		{
			var obj = await arangodb.Document.CreateAsync(handle, "node_objects", new SharpObject() { Name = name });
			var thing = await arangodb.Document.CreateAsync(handle, "node_things", new SharpThing() { });

			await arangodb.Document.CreateAsync(handle, "edge_is_object", new SharpEdge { From = thing.Id, To = obj.Id });

			var idx = location.Match(
				player => player.Id,
				room => room.Id,
				thing => thing.Id
				);

			await arangodb.Document.CreateAsync(handle, "edge_at_location", new SharpEdge { From = thing.Id, To = idx! });
			await arangodb.Document.CreateAsync(handle, "edge_has_home", new SharpEdge { From = thing.Id, To = idx! });
			await arangodb.Document.CreateAsync(handle, "edge_has_object_owner", new SharpEdge { From = thing.Id, To = creator.Id! });

			return int.Parse(obj.Key);
		}

		public async Task<int> CreateExit(string name, OneOf<SharpPlayer, SharpRoom, SharpThing> location, SharpPlayer creator)
		{
			var obj = await arangodb.Document.CreateAsync(handle, "node_objects", new SharpObject() { Name = name });
			var exit = await arangodb.Document.CreateAsync(handle, "node_exits", new SharpExit());

			await arangodb.Document.CreateAsync(handle, "edge_is_object", new SharpEdge { From = exit.Id, To = obj.Id });

			var idx = location.Match(
				player => player.Id,
				room => room.Id,
				thing => thing.Id
			);

			await arangodb.Document.CreateAsync(handle, "edge_has_home", new SharpEdge { From = exit.Id, To = idx! });
			await arangodb.Document.CreateAsync(handle, "edge_has_object_owner", new SharpEdge { From = exit.Id, To = idx! });

			return int.Parse(obj.Key);
		}

		public async Task<OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing>?> GetObjectNode(int dbref, int? createdsecs = null, int? createdmsecs = null)
		{
			var obj = await arangodb.Document.GetAsync<SharpObject>(handle, "node_objects", dbref.ToString());
			var startVertex = obj.Id;

			// TODO: Version that cares about createdsecs / createdmsecs
			var query = await arangodb.Query.ExecuteAsync<dynamic>(handle,
				$"FOR v IN 1..1 INBOUND {startVertex} GRAPH graph_objects RETURN {{ \"id\": v._id, \"collection\": PARSE_IDENTIFIER( v._id ).collection, \"vertex\": v}}");

			var res = query.FirstOrDefault();

			if (res == null)
			{
				return null;
			}

			string id = res.id;
			string collection = res.collection;
			dynamic vertex = res.vertex;

			switch (collection)
			{
				case "node_things":
					return new SharpThing
					{
						Id = id,
						Object = obj
					};
				case "node_players":
					return new SharpPlayer
					{
						Id = id,
						PasswordHash = vertex.PasswordHash,
						Aliases = vertex.Aliases,
						Object = obj
					};
				case "node_rooms":
					return new SharpRoom
					{
						Id = id,
						Object = obj
					};
				case "node_exits":
					return new SharpThing
					{
						Id = id,
						Object = obj,
						Aliases = vertex.Aliases
					};
				default: throw new ArgumentException($"Invalid collection found: {collection}");
			}
		}

		public Task<SharpAttribute[]?> GetAttributes(int dbref, string[] attribute_pattern)
		{
			throw new NotImplementedException();
		}

		public async Task<SharpAttribute[]?> GetAttribute(int dbref, string[] attribute)
		{
			var startVertex = $"node_objects/{dbref}";
			var let = "LET start = FIRST(FOR v IN 1..1 INBOUND @startVertex GRAPH graph_objects RETURN v)";
			var query = $"{let} FOR v,e,p IN 1..@max OUTBOUND start GRAPH graph_attributes PRUNE condition = NTH(@attr,LENGTH(p.edges)-1) != v.Name FILTER !condition RETURN v";

			var result = await arangodb.Query.ExecuteAsync<dynamic>(handle, query, new Dictionary<string, object>()
			{
				{ "attr", attribute },
				{ "startVertex", startVertex },
				{ "max", attribute.Length }
			});

			// TODO: What if we did not find it?
			// TODO: What should the result be if we did not find it?
			return result.Select(x => new SharpAttribute() { Name = x.Name, Flags = x.Flags.ToObject<string[]>(), Value = x.Value, Id = x._id, LongName = x.LongName }).ToArray();
		}

		public async Task<bool> SetAttribute(int dbref, string[] attribute, string value, SharpPlayer owner)
		{
			if (owner is null) { throw new ArgumentNullException(nameof(owner)); }

			var startVertex = $"node_objects/{dbref}";
			var let1 = "LET start = (FOR v IN 1..1 INBOUND @startVertex GRAPH graph_objects RETURN v)";
			var let2 = $"LET foundAttributes = (FOR v,e,p IN 1..@max OUTBOUND FIRST(start) GRAPH graph_attributes PRUNE condition = NTH(@attr,LENGTH(p.edges)-1) != v.Name FILTER !condition RETURN v)";
			var query = $"{let1} {let2} RETURN APPEND(start, foundAttributes)";

			var result = await arangodb.Query.ExecuteAsync<dynamic[]>(handle, query, new Dictionary<string, object>()
			{
				{ "attr", attribute },
				{ "startVertex", startVertex },
				{ "max", attribute.Length }
			});

			var actualResult = result.First();

			var matches = actualResult.Length;
			var remaining = attribute.Skip(matches-1);
			var last = actualResult.Last();
			string lastId = last._id;

			foreach (var next in remaining)
			{
				var newOne = await arangodb.Document.CreateAsync(handle, "node_attributes", new SharpAttribute() { Name = next, Flags = [] });
				await arangodb.Document.CreateAsync(handle, "edge_has_attribute", new SharpEdge() { From = lastId, To = newOne.Id });
				lastId = newOne.Id;
			}

			await arangodb.Document.UpdateAsync(handle, "node_attributes", new { Key = lastId!.Split("/").Last(), Value = value, LongName = string.Join("`", attribute) }, mergeObjects: true);
			await arangodb.Document.CreateAsync(handle, "edge_has_attribute_owner", new SharpEdge { From = lastId, To = owner.Id!,  }, mergeObjects: true);

			return true;
		}
	}
}
