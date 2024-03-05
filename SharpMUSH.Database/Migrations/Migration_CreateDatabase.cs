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
								Type = ArangoKeyType.Autoincrement,
								Increment = 1,
								Offset = 0
							},
							Schema = new ArangoSchema()
							{
								Rule = new {
									type = "object",
									properties = new {
										Name = new { type = "string" },
										Locks = new { type = "object" },
										CreationTime = new { type = "number" },
										Powers = new { type = "array", items = new { type = "string" } }
									},
									required = (string[])[nameof(SharpObject.Name)],
									additionalProperties = true
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
				new()
				{
					Collection = new ArangoCollection
					{
						Name = "node_things",
						Type = ArangoCollectionType.Document,
						WaitForSync = true,
						Schema = new ArangoSchema()
						{
							Rule = new {
								type = "object",
								properties = new
								{
									Aliases = new { type = "array", items = new { type = "string" } },
								}
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
				new()
				{
					Collection = new ArangoCollection
					{
						Name = "node_rooms",
						Type = ArangoCollectionType.Document,
						WaitForSync = true
					}
				},
				new()
				{
					Collection = new ArangoCollection
					{
						Name = "node_exits",
						Type = ArangoCollectionType.Document,
						WaitForSync = true,
						Schema = new ArangoSchema()
						{
							Rule = new {
								type = "object",
								properties = new
								{
									Aliases = new { type = "array", items = new { type = "string" } }
								}
							}
						}
					}
				},
				new()
				{
					Collection = new ArangoCollection
					{
						Name = "node_players",
						Type = ArangoCollectionType.Document,
						WaitForSync = true,
						Schema = new ArangoSchema()
						{
							Rule = new {
								type = "object",
								properties = new
								{
									PasswordHash = new { type = "string" },
									Aliases = new { type = "array", items = new { type = "string" } } 
								},
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
				new()
				{
					Collection = new ArangoCollection
					{
						Name = "node_object_flags",
						Type = ArangoCollectionType.Document,
						WaitForSync = true,
						Schema = new ArangoSchema()
						{
							Rule = new {
								type = "object",
								properties = new
								{
									Name = new { type = "string" },
									Symbol = new { type = "string", multipleOf = 1 },
									Aliases = new { type = "array", items = new { type = "string" } },
									SetPermissions = new { type = "array", items = new { type = "string" } },
									UnsetPermissions = new { type = "array", items = new { type = "string" } },
									TypeRestrictions = new { type = "array", items = new { type = "string" } }
								},
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
				new()
				{
					Collection = new ArangoCollection
					{
						Name = "node_attributes",
						Type = ArangoCollectionType.Document,
						WaitForSync = true,
						Schema = new ArangoSchema()
						{
							Rule = new {
								type = "object",
								properties = new
								{
									Name = new { type = "string" },
									Flags = new { type = "array", items = new { type = "string" } }
								},
								required = (string[])[nameof(SharpAttribute.Name)]
							}
						}
					}
				},
				new()
				{
					Collection = new ArangoCollection
					{
						Name = "node_attribute_entries",
						Type = ArangoCollectionType.Document,
						WaitForSync = true,
						Schema = new ArangoSchema()
						{
							Rule = new {
								type = "object",
								properties = new
								{
									Name = new { type = "string" },
									DefaultFlags = new { type = "array", items = new { type = "string" } },
									Limit = new { type = "string" },
									Enum = new { type = "array", items = new { type = "string" } }
								},
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
				new()
				{
					Collection = new ArangoCollection
					{
						Name = "node_functions",
						Type = ArangoCollectionType.Document,
						WaitForSync = true,
						Schema = new ArangoSchema()
						{
							Rule = new
							{
									type = "object",
									properties = new {
										Name = new { type = "string" },
										Alias = new { type = "string" },
										RestrictedErrorMessage = new { type = "string" },
										Traits = new { type = "array", items = new { type = "string" } },
										MinArgs = new { type = "number", multipleOf = "1" },
										MaxArgs = new { type = "number", multipleOf = "1" },
										Restrictions = new { type = "array", items = new { type = "string" } }
									},
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
				new()
				{
					Collection = new ArangoCollection
					{
						Name = "node_commands",
						Type = ArangoCollectionType.Document,
						WaitForSync = true,
						Schema = new ArangoSchema()
						{
							Rule = new {
								type = "object",
								properties = new
								{
									Name = new { type = "string" },
									Alias = new { type = "string" },
									Limit = new { type = "string" },
									Enum = new { type = "array", items = new { type = "string" } }
								},
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
				new()
				{
					Collection = new ArangoCollection
					{
						Name = "edge_atlocation",
						Type = ArangoCollectionType.Edge,
						WaitForSync = true
					}
				},
				new()
				{
					Collection = new ArangoCollection
					{
						Name = "edge_hashome",
						Type = ArangoCollectionType.Edge,
						WaitForSync = true
					}
				},
				new()
				{
					Collection = new ArangoCollection
					{
						Name = "edge_hasflags",
						Type = ArangoCollectionType.Edge,
						WaitForSync = true
					}
				},
				new()
				{
					Collection = new ArangoCollection
					{
						Name = "edge_isobject",
						Type = ArangoCollectionType.Edge,
						WaitForSync = true
					}
				},
				new()
				{
					Collection = new ArangoCollection
					{
						Name = "edge_hashook",
						Type = ArangoCollectionType.Edge,
						WaitForSync = true,
						Schema = new ArangoSchema()
						{
							Rule = new {
								properties = new
								{
									Type = new { type = "string" }
								},
								required = (string[])[nameof(SharpHookEdge.Type)]
							}
						}
					}
				}
			},
				Graphs = new ArangoGraph[]
				{
					new ArangoGraph()
					{
						EdgeDefinitions = [new ArangoEdgeDefinition()
						{
							Collection = "edge_isobject",
							To = ["node_objects"],
							From = ["node_things", "node_players", "node_rooms", "node_exits"]
						},
						new ArangoEdgeDefinition()
						{
							Collection = "edge_isobject",
							From = ["node_things", "node_players", "node_rooms", "node_exits"],
							To = ["node_objects"]
						}],
						Name = "graph_objects"
					}
				}
			}, new ArangoMigrationOptions { DryRun = false, Notify = x => Console.WriteLine("Migration Change: {0}: {1} - {2}", x.Name, x.Object, x.State) }); ;


			/// The stuff below this is not running for some reason.
			/// Seems like a deadlock coming from an Exception.
			/// The exception is a Schema Failure.

			/* Create Room Zero */
			var roomZeroObj = await migrator.Context.Document.CreateAsync(handle, "node_objects", new 
			{
				_key = "0",
				Name = "Room Zero",
				CreationTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
			});
			var roomZeroRoom = await migrator.Context.Document.CreateAsync(handle, "node_rooms", new SharpRoom { });

			/* Create Player One */
			var playerOneObj = await migrator.Context.Document.CreateAsync(handle, "node_objects", new 
			{
				_key = "1",
				Name = "God",
				CreationTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
			});

			/* Create Room Zero */
			var roomTwoObj = await migrator.Context.Document.CreateAsync(handle, "node_objects", new 
			{
				_key = "2",
				Name = "Master Room",
				CreationTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
			});
			var roomTwoRoom = await migrator.Context.Document.CreateAsync(handle, "node_rooms", new SharpRoom { });

			var playerOnePlayer = await migrator.Context.Document.CreateAsync(handle, "node_players", new SharpPlayer
			{
				PasswordHash = string.Empty
			});

			await migrator.Context.Document.CreateAsync(handle, "edge_isobject", new SharpEdge { From = roomTwoRoom.Id, To = roomTwoObj.Id });
			await migrator.Context.Document.CreateAsync(handle, "edge_isobject", new SharpEdge { From = roomZeroRoom.Id, To = roomZeroObj.Id });
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