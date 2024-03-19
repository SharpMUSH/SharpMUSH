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
							Name = DatabaseConstants.objects,
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
									type = DatabaseConstants.typeObject,
									properties = new {
										Name = new { type = DatabaseConstants.typeString },
										Locks = new { type = DatabaseConstants.typeObject },
										CreationTime = new { type = DatabaseConstants.typeNumber },
										ModifiedTime = new { type = DatabaseConstants.typeNumber },
										Powers = new { type = DatabaseConstants.typeArray, items = new { type = DatabaseConstants.typeString } }
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
						Name = DatabaseConstants.things,
						Type = ArangoCollectionType.Document,
						WaitForSync = true,
						Schema = new ArangoSchema()
						{
							Rule = new {
								type = DatabaseConstants.typeObject,
								properties = new
								{
									Aliases = new { type = DatabaseConstants.typeArray, items = new { type = DatabaseConstants.typeString } },
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
						Name = DatabaseConstants.rooms,
						Type = ArangoCollectionType.Document,
						WaitForSync = true
					}
				},
				new()
				{
					Collection = new ArangoCollection
					{
						Name = DatabaseConstants.exits,
						Type = ArangoCollectionType.Document,
						WaitForSync = true,
						Schema = new ArangoSchema()
						{
							Rule = new {
								type = DatabaseConstants.typeObject,
								properties = new
								{
									Aliases = new { type = DatabaseConstants.typeArray, items = new { type = DatabaseConstants.typeString } }
								}
							}
						}
					}
				},
				new()
				{
					Collection = new ArangoCollection
					{
						Name = DatabaseConstants.players,
						Type = ArangoCollectionType.Document,
						WaitForSync = true,
						Schema = new ArangoSchema()
						{
							Rule = new {
								type = DatabaseConstants.typeObject,
								properties = new
								{
									PasswordHash = new { type = DatabaseConstants.typeString },
									Aliases = new { type = DatabaseConstants.typeArray, items = new { type = DatabaseConstants.typeString } } 
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
						Name = DatabaseConstants.objectFlags,
						Type = ArangoCollectionType.Document,
						WaitForSync = true,
						Schema = new ArangoSchema()
						{
							Rule = new {
								type = DatabaseConstants.typeObject,
								properties = new
								{
									Name = new { type = DatabaseConstants.typeString },
									Symbol = new { type = DatabaseConstants.typeString, multipleOf = 1 },
									Aliases = new { type = DatabaseConstants.typeArray, items = new { type = DatabaseConstants.typeString } },
									SetPermissions = new { type = DatabaseConstants.typeArray, items = new { type = DatabaseConstants.typeString } },
									UnsetPermissions = new { type = DatabaseConstants.typeArray, items = new { type = DatabaseConstants.typeString } },
									TypeRestrictions = new { type = DatabaseConstants.typeArray, items = new { type = DatabaseConstants.typeString } }
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
						Name = DatabaseConstants.attributes,
						Type = ArangoCollectionType.Document,
						WaitForSync = true,
						Schema = new ArangoSchema()
						{
							Rule = new {
								type = DatabaseConstants.typeObject,
								properties = new
								{
									Name = new { type = DatabaseConstants.typeString },
									LongName = new { type = DatabaseConstants.typeString },
									Flags = new { type = DatabaseConstants.typeArray, items = new { type = DatabaseConstants.typeString } }
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
						Name = DatabaseConstants.attributeEntries,
						Type = ArangoCollectionType.Document,
						WaitForSync = true,
						Schema = new ArangoSchema()
						{
							Rule = new {
								type = DatabaseConstants.typeObject,
								properties = new
								{
									Name = new { type = DatabaseConstants.typeString },
									DefaultFlags = new { type = DatabaseConstants.typeArray, items = new { type = DatabaseConstants.typeString } },
									Limit = new { type = DatabaseConstants.typeString },
									Enum = new { type = DatabaseConstants.typeArray, items = new { type = DatabaseConstants.typeString } }
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
						Name = DatabaseConstants.functions,
						Type = ArangoCollectionType.Document,
						WaitForSync = true,
						Schema = new ArangoSchema()
						{
							Rule = new
							{
									type = DatabaseConstants.typeObject,
									properties = new {
										Name = new { type = DatabaseConstants.typeString },
										Alias = new { type = DatabaseConstants.typeString },
										RestrictedErrorMessage = new { type = DatabaseConstants.typeString },
										Traits = new { type = DatabaseConstants.typeArray, items = new { type = DatabaseConstants.typeString } },
										MinArgs = new { type = DatabaseConstants.typeNumber, multipleOf = 1 },
										MaxArgs = new { type = DatabaseConstants.typeNumber, multipleOf = 1 },
										Restrictions = new { type = DatabaseConstants.typeArray, items = new { type = DatabaseConstants.typeString } }
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
						Name = DatabaseConstants.commands,
						Type = ArangoCollectionType.Document,
						WaitForSync = true,
						Schema = new ArangoSchema()
						{
							Rule = new {
								type = DatabaseConstants.typeObject,
								properties = new
								{
									Name = new { type = DatabaseConstants.typeString },
									Alias = new { type = DatabaseConstants.typeString },
									Limit = new { type = DatabaseConstants.typeString },
									Enum = new { type = DatabaseConstants.typeArray, items = new { type = DatabaseConstants.typeString } }
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
						Name = DatabaseConstants.atLocation,
						Type = ArangoCollectionType.Edge,
						WaitForSync = true
					}
				},
				new()
				{
					Collection = new ArangoCollection
					{
						Name = DatabaseConstants.hasHome,
						Type = ArangoCollectionType.Edge,
						WaitForSync = true
					}
				},
				new()
				{
					Collection = new ArangoCollection
					{
						Name = DatabaseConstants.hasFlags,
						Type = ArangoCollectionType.Edge,
						WaitForSync = true
					}
				},
				new()
				{
					Collection = new ArangoCollection
					{
						Name = DatabaseConstants.hasAttribute,
						Type = ArangoCollectionType.Edge,
						WaitForSync = true
					}
				},
				new()
				{
					Collection = new ArangoCollection
					{
						Name = DatabaseConstants.hasObjectOwner,
						Type = ArangoCollectionType.Edge,
						WaitForSync = true
					}
				},
				new()
				{
					Collection = new ArangoCollection
					{
						Name = DatabaseConstants.hasAttributeOwner,
						Type = ArangoCollectionType.Edge,
						WaitForSync = true
					}
				},
				new()
				{
					Collection = new ArangoCollection
					{
						Name = DatabaseConstants.hasHook,
						Type = ArangoCollectionType.Edge,
						WaitForSync = true,
						Schema = new ArangoSchema()
						{
							Rule = new {
								properties = new
								{
									Type = new { type = DatabaseConstants.typeString }
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
								Collection = DatabaseConstants.isObject,
								To = [DatabaseConstants.objects],
								From = [
									DatabaseConstants.things, 
									DatabaseConstants.players, 
									DatabaseConstants.rooms, 
									DatabaseConstants.exits
									]
							}
						],
						Name = DatabaseConstants.graphObjects
					},
					new()
					{
						EdgeDefinitions =
						[
							new ArangoEdgeDefinition()
							{
								Collection = DatabaseConstants.hasAttribute,
								To = [DatabaseConstants.attributes],
								From = [
									DatabaseConstants.attributes, 
									DatabaseConstants.things, 
									DatabaseConstants.players, 
									DatabaseConstants.rooms,
									DatabaseConstants.exits
									]
							}
						],
						Name = DatabaseConstants.graphAttributes
					}
				}
			}, new ArangoMigrationOptions { DryRun = false, Notify = x => Console.WriteLine("Migration Change: {0}: {1} - {2}", x.Name, x.Object, x.State) }); ;


			/// The stuff below this is not running for some reason.
			/// Seems like a deadlock coming from an Exception.
			/// The exception is a Schema Failure.

			/* Create Room Zero */
			var roomZeroObj = await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.objects, new 
			{
				_key = "0",
				Name = "Room Zero",
				CreationTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
				ModifiedTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
			});
			var roomZeroRoom = await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.rooms, new SharpRoom { });

			/* Create Player One */
			var playerOneObj = await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.objects, new 
			{
				_key = "1",
				Name = "God",
				CreationTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
				ModifiedTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
			});

			/* Create Room Zero */
			var roomTwoObj = await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.objects, new 
			{
				_key = "2",
				Name = "Master Room",
				CreationTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
				ModifiedTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
			});
			var roomTwoRoom = await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.rooms, new SharpRoom { });

			var playerOnePlayer = await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.players, new 
			{
				PasswordHash = ""
			});

			await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.isObject, new SharpEdge { From = roomTwoRoom.Id, To = roomTwoObj.Id });
			await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.isObject, new SharpEdge { From = roomZeroRoom.Id, To = roomZeroObj.Id });
			await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.isObject, new SharpEdge { From = playerOnePlayer.Id, To = playerOneObj.Id });
			await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.atLocation, new SharpEdge { From = playerOnePlayer.Id, To = roomZeroRoom.Id });
			await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.hasHome, new SharpEdge { From = playerOnePlayer.Id, To = roomZeroRoom.Id });
			await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.hasObjectOwner, new SharpEdge { From = roomTwoRoom.Id, To = playerOnePlayer.Id });
			await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.hasObjectOwner, new SharpEdge { From = roomZeroRoom.Id, To = playerOnePlayer.Id });
			await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.hasObjectOwner, new SharpEdge { From = playerOnePlayer.Id, To = playerOneObj.Id });
		}

		public Task Down(IArangoMigrator migrator, ArangoHandle handle)
		{
			throw new NotImplementedException();
		}
	}
}