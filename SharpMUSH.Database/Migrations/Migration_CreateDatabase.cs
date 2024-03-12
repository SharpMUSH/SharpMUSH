﻿using Core.Arango.Migration;
using Core.Arango;
using Core.Arango.Protocol;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Database.Migrations
{
	/// <summary>
	/// Creates the basic database, containing Player #1 (God), Room 0, and Room 2.
	/// This should not use the Sharp class definitions, because it should be unmarried to their definitions.
	/// That way, if things change, the Migration doesn't fail, and instead we use new migrations to get over the 
	/// hurdle of breaking changes and database upgrades.
	/// </summary>
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
									LongName = new { type = "string" },
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
										MinArgs = new { type = "number", multipleOf = 1 },
										MaxArgs = new { type = "number", multipleOf = 1 },
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
						Name = "edge_at_location",
						Type = ArangoCollectionType.Edge,
						WaitForSync = true
					}
				},
				new()
				{
					Collection = new ArangoCollection
					{
						Name = "edge_has_home",
						Type = ArangoCollectionType.Edge,
						WaitForSync = true
					}
				},
				new()
				{
					Collection = new ArangoCollection
					{
						Name = "edge_has_flags",
						Type = ArangoCollectionType.Edge,
						WaitForSync = true
					}
				},
				new()
				{
					Collection = new ArangoCollection
					{
						Name = "edge_has_attribute",
						Type = ArangoCollectionType.Edge,
						WaitForSync = true
					}
				},
				new()
				{
					Collection = new ArangoCollection
					{
						Name = "edge_has_object_owner",
						Type = ArangoCollectionType.Edge,
						WaitForSync = true
					}
				},
				new()
				{
					Collection = new ArangoCollection
					{
						Name = "edge_has_attribute_owner",
						Type = ArangoCollectionType.Edge,
						WaitForSync = true
					}
				},
				new()
				{
					Collection = new ArangoCollection
					{
						Name = "edge_has_hook",
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
					new()
					{
						EdgeDefinitions = 
						[
							new ArangoEdgeDefinition()
							{
								Collection = "edge_is_object",
								To = ["node_objects"],
								From = ["node_things", "node_players", "node_rooms", "node_exits"]
							}
						],
						Name = "graph_objects"
					},
					new()
					{
						EdgeDefinitions =
						[
							new ArangoEdgeDefinition()
							{
								Collection = "edge_has_attribute",
								To = ["node_attributes"],
								From = ["node_attributes", "node_things", "node_players", "node_rooms", "node_exits"]
							}
						],
						Name = "graph_attributes"
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

			var playerOnePlayer = await migrator.Context.Document.CreateAsync(handle, "node_players", new 
			{
				PasswordHash = ""
			});

			await migrator.Context.Document.CreateAsync(handle, "edge_is_object", new SharpEdge { From = roomTwoRoom.Id, To = roomTwoObj.Id });
			await migrator.Context.Document.CreateAsync(handle, "edge_is_object", new SharpEdge { From = roomZeroRoom.Id, To = roomZeroObj.Id });
			await migrator.Context.Document.CreateAsync(handle, "edge_is_object", new SharpEdge { From = playerOnePlayer.Id, To = playerOneObj.Id });
			await migrator.Context.Document.CreateAsync(handle, "edge_at_location", new SharpEdge { From = playerOnePlayer.Id, To = roomZeroRoom.Id });
			await migrator.Context.Document.CreateAsync(handle, "edge_has_home", new SharpEdge { From = playerOnePlayer.Id, To = roomZeroRoom.Id });
			await migrator.Context.Document.CreateAsync(handle, "edge_has_object_owner", new SharpEdge { From = roomTwoRoom.Id, To = playerOnePlayer.Id });
			await migrator.Context.Document.CreateAsync(handle, "edge_has_object_owner", new SharpEdge { From = roomZeroRoom.Id, To = playerOnePlayer.Id });
			await migrator.Context.Document.CreateAsync(handle, "edge_has_object_owner", new SharpEdge { From = playerOnePlayer.Id, To = playerOneObj.Id });
		}

		public Task Down(IArangoMigrator migrator, ArangoHandle handle)
		{
			throw new NotImplementedException();
		}
	}
}