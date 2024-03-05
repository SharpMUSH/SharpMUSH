using Core.Arango.Migration;
using Core.Arango;
using Core.Arango.Protocol;
using SharpMUSH.Database.Types;

namespace SharpMUSH.Database.Migrations
{

	// sample migration / downgrades not yet supported
	public class Migration_CreateDatabase : IArangoMigration
	{
		public long Id => 20240304_001; // sortable unique id
		
		public string Name => "create_database";

		public async Task Up(IArangoMigrator migrator, ArangoHandle handle)
		{
			await migrator.ApplyStructureAsync(handle, new ArangoStructure()
			{
				Collections = new List<ArangoCollectionIndices>
				{
					new() {
						Collection = new ArangoCollection
						{
							Name = "node_objects",
							Type = ArangoCollectionType.Document,
							KeyOptions = new ArangoKeyOptions()
							{
								AllowUserKeys = true,
								Type = ArangoKeyType.Traditional
							},
							Schema = new ArangoSchema()
							{
								Rule = new {
									DBRef = new { type = "number", multipleOf = 1 },
									Name = new { type = "string" },
									Locks = new { type = "object" },
									CreationTime = new { type = "number" },
									Powers = new { type = "array", items = "string" },
									required = (string[])[nameof(SharpObject.DBRef), nameof(SharpObject.Name)]
								}
							},
							WaitForSync = true
						},
						Indices = new ArangoIndex[]
						{
							new()
							{
								Fields = [nameof(SharpObject.Name)]
							}
						}
					},
					new() {
						Collection = new ArangoCollection
						{
							Name = "node_things",
							Type = ArangoCollectionType.Document,
							WaitForSync = true,
							Schema = new ArangoSchema()
							{
								Rule = new {
									Aliases = new { type = "array", items = "string" },
								}
							}
						},
						Indices = new ArangoIndex[]
						{
							new()
							{
								Fields = [nameof(SharpThing.Aliases)]
							}
						}
					},
					new() {
						Collection = new ArangoCollection
						{
							Name = "node_rooms",
							Type = ArangoCollectionType.Document,
							WaitForSync = true
						}
					},
					new() {
						Collection = new ArangoCollection
						{
							Name = "node_exits",
							Type = ArangoCollectionType.Document,
							WaitForSync = true,
							Schema = new ArangoSchema()
							{
								Rule = new {
									Aliases = new { type = "array", items = "string" }
								}
							}
						}
					},
					new() {
						Collection = new ArangoCollection
						{
							Name = "node_players",
							Type = ArangoCollectionType.Document,
							WaitForSync = true,
							Schema = new ArangoSchema()
							{
								Rule = new {
									PasswordHash = new { type = "string" },
									Aliases = new { type = "array", items = "string" },
									required = (string[])[nameof(SharpPlayer.PasswordHash)]
								}
							}
						},
						Indices = new ArangoIndex[]
						{
							new()
							{
								Fields = [nameof(SharpPlayer.Aliases)]
							}
						}
					},
					new() {
						Collection = new ArangoCollection
						{
							Name = "node_object_flags",
							Type = ArangoCollectionType.Document,
							WaitForSync = true,
							Schema = new ArangoSchema()
							{
								Rule = new {
									Name = new { type = "string" },
									Symbol = new { type = "string", multipleOf = 1 },
									Aliases = new { type = "array", items = "string" },
									SetPermissions = new { type = "array", items = "string" },
									UnsetPermissions = new { type = "array", items = "string" },
									TypeRestrictions = new { type = "array", items = "string" },
									required = (string[])[
										nameof(SharpObjectFlag.Name),
										nameof(SharpObjectFlag.Symbol),
										nameof(SharpObjectFlag.SetPermissions),
										nameof(SharpObjectFlag.UnsetPermissions),
										nameof(SharpObjectFlag.TypeRestrictions)
										]
								}
							}
						}
					},
					new() {
						Collection = new ArangoCollection
						{
							Name = "node_attributes",
							Type = ArangoCollectionType.Document,
							WaitForSync = true,
							Schema = new ArangoSchema()
							{
								Rule = new {
									Name = new { type = "string" },
									Flags = new { type = "array", items = "string" },
									required = (string[])[nameof(SharpAttribute.Name)]
								}
							}
						}
					},
					new() {
						Collection = new ArangoCollection
						{
							Name = "node_attribute_entries",
							Type = ArangoCollectionType.Document,
							WaitForSync = true,
							Schema = new ArangoSchema()
							{
								Rule = new {
									Name = new { type = "string" },
									DefaultFlags = new { type = "array", items = "string" },
									Limit = new { type = "string" },
									Enum = new { type = "array", items = "string" },
									required = (string[])[nameof(SharpAttributeEntry.Name)]
								}
							}
						},
						Indices = new ArangoIndex[]
						{
							new()
							{
								Fields = [nameof(SharpAttributeEntry.Name)]
							}
						}
					},
					new() {
						Collection = new ArangoCollection
						{
							Name = "node_functions",
							Type = ArangoCollectionType.Document,
							WaitForSync = true,
							Schema = new ArangoSchema()
							{
								Rule = new {
									Name = new { type = "string" },
									Alias = new { type = "string" },
									RestrictedErrorMessage = new { type = "string" },
									Traits = new { type = "array", items = "string" },
									MinArgs = new { type = "number", multipleOf = "1" },
									MaxArgs = new { type = "number", multipleOf = "1" },
									Restrictions = new { type = "array", items = "string" },
									required = (string[])[
										nameof(SharpFunction.Name),
										nameof(SharpFunction.MinArgs),
										nameof(SharpFunction.MaxArgs),
										nameof(SharpFunction.Restrictions),
										nameof(SharpFunction.Traits)
										]
								}
							}
						},
						Indices = new ArangoIndex[]
						{
							new() { Fields = [ nameof(SharpFunction.Name) ] },
							new() { Fields = [ nameof(SharpFunction.Traits) ] },
							new() { Fields = [ nameof(SharpFunction.Alias) ] },
							new() { Fields = [ nameof(SharpFunction.Enabled) ] },
						}
					},
					new() {
						Collection = new ArangoCollection
						{
							Name = "node_commands",
							Type = ArangoCollectionType.Document,
							WaitForSync = true,
							Schema = new ArangoSchema()
							{
								Rule = new {
									Name = new { type = "string" },
									Alias = new { type = "string" },
									Limit = new { type = "string" },
									Enum = new { type = "array", items = "string" },
									required = (string[])[nameof(SharpCommand.Name)]
								}
							}
						},
						Indices = new ArangoIndex[]
						{
							new()
							{
								Fields = [nameof(SharpCommand.Name)]
							},
							new()
							{
								Fields = [nameof(SharpCommand.Alias)]
							}
						}
					},
					new() {
						Collection = new ArangoCollection
						{
							Name = "edge_atlocation",
							Type = ArangoCollectionType.Edge,
							WaitForSync = true
						}
					},
					new() {
						Collection = new ArangoCollection
						{
							Name = "edge_hashome",
							Type = ArangoCollectionType.Edge,
							WaitForSync = true
						}
					},
					new() {
						Collection = new ArangoCollection
						{
							Name = "edge_hasflags",
							Type = ArangoCollectionType.Edge,
							WaitForSync = true
						}
					},
					new() {
						Collection = new ArangoCollection
						{
							Name = "edge_isobject",
							Type = ArangoCollectionType.Edge,
							WaitForSync = true
						}
					},
					new() {
						Collection = new ArangoCollection
						{
							Name = "edge_hashook",
							Type = ArangoCollectionType.Edge,
							WaitForSync = true,
							Schema = new ArangoSchema()
							{
								Rule = new {
									Type = new { type = "string" },
									required = (string[])[nameof(SharpHookEdge.Type)]
								}
							}
						}
					}
				}
			}, new ArangoMigrationOptions { DryRun = false, Notify = x => Console.WriteLine("Migration Change: {0}: {1} - {2}", x.Name, x.Object, x.State) }); ;

			/* Create Room Zero */
			var roomZeroObj = await migrator.Context.Document.CreateAsync(handle, "node_objects", new SharpObject
			{
				Name = "Room Zero",
				DBRef = 0,
				CreationTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
			});
			var roomZeroRoom = await migrator.Context.Document.CreateAsync(handle, "node_rooms", new SharpRoom { });
			await migrator.Context.Document.CreateAsync(handle, "edge_isobject", new { _from = roomZeroRoom.Id, _to = roomZeroObj.Id });

			/* Create Room Zero */
			var roomTwoObj = await migrator.Context.Document.CreateAsync(handle, "node_objects", new SharpObject
			{
				Name = "Master Room",
				DBRef = 2,
				CreationTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
			});
			var roomTwoRoom = await migrator.Context.Document.CreateAsync(handle, "node_rooms", new SharpRoom { });
			await migrator.Context.Document.CreateAsync(handle, "edge_isobject", new { _from = roomTwoRoom.Id, _to = roomTwoObj.Id });

			/* Create Player One */
			var playerOneObj = await migrator.Context.Document.CreateAsync(handle, "node_objects", new SharpObject
			{
				Name = "God",
				DBRef = 1,
				CreationTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
			});
			var playerOnePlayer = await migrator.Context.Document.CreateAsync(handle, "node_players", new SharpPlayer
			{
				PasswordHash = string.Empty
			});
			await migrator.Context.Document.CreateAsync(handle, "edge_isobject", new SharpEdge { From = playerOnePlayer.Id, To = playerOneObj.Id });
			await migrator.Context.Document.CreateAsync(handle, "edge_atlocation", new SharpEdge { From = playerOnePlayer.Id, To = roomZeroRoom.Id });
			await migrator.Context.Document.CreateAsync(handle, "edge_hashome", new SharpEdge { From = playerOnePlayer.Id, To = roomZeroRoom.Id });
		}

		public Task Down(IArangoMigrator migrator, ArangoHandle handle)
		{
			throw new NotImplementedException();
		}
	}
}
