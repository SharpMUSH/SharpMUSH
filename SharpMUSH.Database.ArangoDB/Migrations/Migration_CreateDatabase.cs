using Core.Arango.Migration;
using Core.Arango;
using Core.Arango.Protocol;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Database.ArangoDB.Migrations
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
				Collections =
				[
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
										Type = new { type = DatabaseConstants.typeString },
										Locks = new { type = DatabaseConstants.typeObject },
										CreationTime = new { type = DatabaseConstants.typeNumber },
										ModifiedTime = new { type = DatabaseConstants.typeNumber },
										Powers = new { type = DatabaseConstants.typeArray, items = new { type = DatabaseConstants.typeString } }
									},
									required = (string[])[nameof(SharpObject.Name), nameof(SharpObject.Type)],
									additionalProperties = true
								}
							},
							WaitForSync = true
						},
						Indices =
						[
							new()
							{
								Fields = [nameof(SharpObject.Name)]
							}
						]
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
					Indices =
						[
							new()
							{
								Fields = [nameof(SharpThing.Aliases)]
							}
						]
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
					Indices =
						[
							new()
							{
								Fields = [nameof(SharpPlayer.Aliases)]
							}
						]
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
						Name = DatabaseConstants.objectPowers,
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
									SetPermissions = new { type = DatabaseConstants.typeArray, items = new { type = DatabaseConstants.typeString } },
									UnsetPermissions = new { type = DatabaseConstants.typeArray, items = new { type = DatabaseConstants.typeString } },
									TypeRestrictions = new { type = DatabaseConstants.typeArray, items = new { type = DatabaseConstants.typeString } }
								},
								required = (string[])[
									nameof(SharpPower.Name),
									nameof(SharpPower.Alias),
									nameof(SharpPower.SetPermissions),
									nameof(SharpPower.UnsetPermissions),
									nameof(SharpPower.TypeRestrictions)
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
					},
					Indices =
						[
							new()
							{
								Fields = [nameof(SharpAttribute.LongName)],
								Type = ArangoIndexType.Inverted
							},
							new()
							{
								Fields = [nameof(SharpAttribute.LongName)],
								Type = ArangoIndexType.Persistent
							}
						]
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
					Indices =
						[
							new()
							{
								Fields = [nameof(SharpAttributeEntry.Name)]
							}
						]
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
					Indices =
						[
							new() { Fields = [ nameof(SharpFunction.Name) ] },
							new() { Fields = [ nameof(SharpFunction.Traits) ] },
							new() { Fields = [ nameof(SharpFunction.Alias) ] },
							new() { Fields = [ nameof(SharpFunction.Enabled) ] },
						]
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
					Indices =
						[
							new() { Fields = [nameof(SharpCommand.Name)] },
							new() { Fields = [nameof(SharpCommand.Alias)] }
						]
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
						Name = DatabaseConstants.hasExit,
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
			],
				Graphs =
				[
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
								Collection = DatabaseConstants.hasPowers,
								To = [DatabaseConstants.objectPowers],
								From = [
									DatabaseConstants.objects
									]
							}
						],
						Name = DatabaseConstants.graphPowers
					},
					new()
					{
						EdgeDefinitions =
						[
							new ArangoEdgeDefinition()
							{
								Collection = DatabaseConstants.hasFlags,
								To = [DatabaseConstants.objectFlags],
								From = [
									DatabaseConstants.objects
									]
							}
						],
						Name = DatabaseConstants.graphFlags
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
									DatabaseConstants.objects
									]
							}
						],
						Name = DatabaseConstants.graphAttributes
					},
					new()
					{
						EdgeDefinitions =
						[
							new ArangoEdgeDefinition()
							{
								Collection = DatabaseConstants.atLocation,
								To = [
									DatabaseConstants.rooms,
									DatabaseConstants.things,
									DatabaseConstants.players
									],
								From = [
									DatabaseConstants.exits,
									DatabaseConstants.things,
									DatabaseConstants.players
									]
							}
						],
						Name = DatabaseConstants.graphLocations,
					},
					new()
					{
						EdgeDefinitions =
						[
							new ArangoEdgeDefinition()
							{
								Collection = DatabaseConstants.hasHome,
								To = [
									DatabaseConstants.rooms,
									DatabaseConstants.things,
									DatabaseConstants.players
								],
								From = [
									DatabaseConstants.exits,
									DatabaseConstants.things,
									DatabaseConstants.players
								]
							}
						],
						Name = DatabaseConstants.graphHomes
					},
					new()
					{
						EdgeDefinitions =
						[
							new ArangoEdgeDefinition()
							{
								Collection = DatabaseConstants.hasExit,
								To = [
									DatabaseConstants.exits,
								],
								From = [
									DatabaseConstants.rooms,
									DatabaseConstants.things,
									DatabaseConstants.players
								]
							}
						],
						Name = DatabaseConstants.graphExits
					},
					new()
					{
						EdgeDefinitions =
						[
							new ArangoEdgeDefinition()
							{
								Collection = DatabaseConstants.hasObjectOwner,
								To = [
									DatabaseConstants.players
									],
								From = [
									DatabaseConstants.objects
									]
							}
						],
						Name = DatabaseConstants.graphObjectOwners
					},
					new()
					{
						EdgeDefinitions =
						[
							new ArangoEdgeDefinition()
							{
								Collection = DatabaseConstants.hasAttributeOwner,
								To = [
									DatabaseConstants.objects
									],
								From = [
									DatabaseConstants.attributes
									]
							}
						],
						Name = DatabaseConstants.graphAttributeOwners
					},
					new()
					{
						EdgeDefinitions =
						[
							new ArangoEdgeDefinition()
							{
								Collection = DatabaseConstants.hasParent,
								To = [
									DatabaseConstants.objects
									],
								From = [
									DatabaseConstants.objects
									]
							}
						],
						Name = DatabaseConstants.graphParents
					}
				]
			},
			new ArangoMigrationOptions
			{
				DryRun = false,
				Notify = x => Console.WriteLine("Migration Change: {0}: {1} - {2}", x.Name, x.Object, x.State)
			});


			/// The stuff below this is not running for some reason.
			/// Seems like a deadlock coming from an Exception.
			/// The exception is a Schema Failure.

			/* Create Room Zero */
			var roomZeroObj = await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.objects, new
			{
				_key = 0.ToString(),
				Name = "Room Zero",
				Type = DatabaseConstants.typeRoom,
				CreationTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
				ModifiedTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
			});
			var roomZeroRoom = await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.rooms, new { });

			/* Create Player One */
			var playerOneObj = await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.objects, new
			{
				_key = 1.ToString(),
				Name = "God",
				Type = DatabaseConstants.typePlayer,
				CreationTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
				ModifiedTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
			});

			/* Create Room Zero */
			var roomTwoObj = await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.objects, new
			{
				_key = 2.ToString(),
				Name = "Master Room",
				Type = DatabaseConstants.typeRoom,
				CreationTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
				ModifiedTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
			});
			var roomTwoRoom = await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.rooms, new { });

			var playerOnePlayer = await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.players, new
			{
				Aliases = Array.Empty<string>(),
				PasswordHash = string.Empty
			});

			await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.isObject, new SharpEdge { From = roomTwoRoom.Id, To = roomTwoObj.Id });
			await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.isObject, new SharpEdge { From = roomZeroRoom.Id, To = roomZeroObj.Id });
			await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.isObject, new SharpEdge { From = playerOnePlayer.Id, To = playerOneObj.Id });
			await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.atLocation, new SharpEdge { From = playerOnePlayer.Id, To = roomZeroRoom.Id });
			await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.hasHome, new SharpEdge { From = playerOnePlayer.Id, To = roomZeroRoom.Id });
			await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.hasObjectOwner, new SharpEdge { From = roomTwoObj.Id, To = playerOnePlayer.Id });
			await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.hasObjectOwner, new SharpEdge { From = roomZeroObj.Id, To = playerOnePlayer.Id });
			await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.hasObjectOwner, new SharpEdge { From = playerOneObj.Id, To = playerOnePlayer.Id });
		}

		public Task Down(IArangoMigrator migrator, ArangoHandle handle)
		{
			throw new NotImplementedException();
		}
	}
}