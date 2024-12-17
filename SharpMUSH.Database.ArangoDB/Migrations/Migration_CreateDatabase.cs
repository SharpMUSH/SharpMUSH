using Core.Arango;
using Core.Arango.Migration;
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
									System = new {type = DatabaseConstants.typeBoolean },
									SetPermissions = new { type = DatabaseConstants.typeArray, items = new { type = DatabaseConstants.typeString } },
									UnsetPermissions = new { type = DatabaseConstants.typeArray, items = new { type = DatabaseConstants.typeString } },
									TypeRestrictions = new { type = DatabaseConstants.typeArray, items = new { type = DatabaseConstants.typeString } }
								},
								required = (string[])[
									nameof(SharpObjectFlag.Name),
									nameof(SharpObjectFlag.System),
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
						Name = DatabaseConstants.attributeFlags,
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
									System = new {type = DatabaseConstants.typeBoolean },
								},
								required = (string[])[
									nameof(SharpObjectFlag.Name),
									nameof(SharpObjectFlag.System),
									nameof(SharpObjectFlag.Symbol)
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
									Symbol = new { type = DatabaseConstants.typeString, multipleOf = 1 },
									System = new { type = DatabaseConstants.typeBoolean },
									SetPermissions = new { type = DatabaseConstants.typeArray, items = new { type = DatabaseConstants.typeString } },
									UnsetPermissions = new { type = DatabaseConstants.typeArray, items = new { type = DatabaseConstants.typeString } },
									TypeRestrictions = new { type = DatabaseConstants.typeArray, items = new { type = DatabaseConstants.typeString } }
								},
								required = (string[])[
									nameof(SharpPower.Name),
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
						Schema = new ArangoSchema
						{
							Rule = new {
								type = DatabaseConstants.typeObject,
								properties = new
								{
									Name = new { type = DatabaseConstants.typeString },
									Aliases = new { type = DatabaseConstants.typeArray, items = new { type = DatabaseConstants.typeString } },
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
						Name = DatabaseConstants.hasAttributeFlag,
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
									DatabaseConstants.players,
									DatabaseConstants.things,
									DatabaseConstants.exits,
									DatabaseConstants.rooms,
									DatabaseConstants.attributes
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
								Collection = DatabaseConstants.hasAttributeFlag,
								To = [DatabaseConstants.attributeFlags],
								From = [
									DatabaseConstants.attributes
								]
							}
						],
						Name = DatabaseConstants.graphAttributeFlags
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

			var flags = CreateInitialFlags(migrator, handle);
			var attributeFlags = CreateInitialAttributeFlags(migrator, handle);
			var powers = CreateInitialPowers(migrator, handle);
			var wizard = flags[18];

			await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.isObject, new SharpEdge { From = roomTwoRoom.Id, To = roomTwoObj.Id });
			await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.isObject, new SharpEdge { From = roomZeroRoom.Id, To = roomZeroObj.Id });
			await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.isObject, new SharpEdge { From = playerOnePlayer.Id, To = playerOneObj.Id });
			await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.atLocation, new SharpEdge { From = playerOnePlayer.Id, To = roomZeroRoom.Id });
			await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.hasHome, new SharpEdge { From = playerOnePlayer.Id, To = roomZeroRoom.Id });
			await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.hasObjectOwner, new SharpEdge { From = roomTwoObj.Id, To = playerOnePlayer.Id });
			await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.hasObjectOwner, new SharpEdge { From = roomZeroObj.Id, To = playerOnePlayer.Id });
			await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.hasObjectOwner, new SharpEdge { From = playerOneObj.Id, To = playerOnePlayer.Id });
			await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.hasFlags, new SharpEdge { From = playerOneObj.Id, To = wizard.Id });
		}

		private static List<ArangoUpdateResult<ArangoVoid>> CreateInitialPowers(IArangoMigrator migrator,
						ArangoHandle handle) =>
		[
			migrator.Context.Document.CreateAsync(handle, DatabaseConstants.objectPowers,
							new
							{
											Name = "Boot",
											System = true,
											TypeRestrictions = DatabaseConstants.typesAll,
											SetPermissions = DatabaseConstants.permissionsWizard
															.Union(DatabaseConstants.permissionsLog),
											UnsetPermissions = DatabaseConstants.permissionsWizard
							}).Result,
			migrator.Context.Document.CreateAsync(handle, DatabaseConstants.objectPowers,
							new
							{
											Name = "Builder",
											System = true,
											TypeRestrictions = DatabaseConstants.typesAll,
											SetPermissions = DatabaseConstants.permissionsWizard
															.Union(DatabaseConstants.permissionsLog),
											UnsetPermissions = DatabaseConstants.permissionsWizard
							}).Result,
			migrator.Context.Document.CreateAsync(handle, DatabaseConstants.objectPowers,
							new
							{
											Name = "Can_Dark",
											System = true,
											TypeRestrictions = DatabaseConstants.typesPlayer,
											SetPermissions = DatabaseConstants.permissionsWizard
															.Union(DatabaseConstants.permissionsLog)
							}).Result,
			migrator.Context.Document.CreateAsync(handle, DatabaseConstants.objectPowers,
							new
							{
											Name = "Can_HTTP",
											System = true,
											TypeRestrictions = DatabaseConstants.typesAll,
											SetPermissions = DatabaseConstants.permissionsWizard
															.Union(DatabaseConstants.permissionsLog)
							}).Result,
			migrator.Context.Document.CreateAsync(handle, DatabaseConstants.objectPowers,
							new
							{
											Name = "Can_Spoof",
											System = true,
											Aliases = (string[])["Can_nspemit"],
											TypeRestrictions = DatabaseConstants.typesAll,
											SetPermissions = DatabaseConstants.permissionsWizard
															.Union(DatabaseConstants.permissionsLog),
											UnsetPermissions = DatabaseConstants.permissionsWizard
							}).Result,
			migrator.Context.Document.CreateAsync(handle, DatabaseConstants.objectPowers,
							new
							{
											Name = "Chat_Privs",
											System = true,
											Aliases = (string[])["Can_nspemit"],
											TypeRestrictions = DatabaseConstants.typesAll,
											SetPermissions = DatabaseConstants.permissionsWizard
															.Union(DatabaseConstants.permissionsLog),
											UnsetPermissions = DatabaseConstants.permissionsWizard
							}).Result,
			migrator.Context.Document.CreateAsync(handle, DatabaseConstants.objectPowers,
							new
							{
											Name = "Debit",
											System = true,
											Aliases = (string[])["Steal_Money"],
											TypeRestrictions = DatabaseConstants.typesAll,
											SetPermissions = DatabaseConstants.permissionsWizard
															.Union(DatabaseConstants.permissionsLog),
							}).Result,
			migrator.Context.Document.CreateAsync(handle, DatabaseConstants.objectPowers,
							new
							{
											Name = "Functions",
											System = true,
											TypeRestrictions = DatabaseConstants.typesAll,
											SetPermissions = DatabaseConstants.permissionsWizard
															.Union(DatabaseConstants.permissionsLog),
											UnsetPermissions = DatabaseConstants.permissionsWizard
							}).Result,
			migrator.Context.Document.CreateAsync(handle, DatabaseConstants.objectPowers,
							new
							{
											Name = "Guest",
											System = true,
											TypeRestrictions = DatabaseConstants.typesAll,
											SetPermissions = DatabaseConstants.permissionsWizard
															.Union(DatabaseConstants.permissionsLog),
											UnsetPermissions = DatabaseConstants.permissionsWizard
							}).Result,
			migrator.Context.Document.CreateAsync(handle, DatabaseConstants.objectPowers,
							new
							{
											Name = "Halt",
											System = true,
											TypeRestrictions = DatabaseConstants.typesAll,
											SetPermissions = DatabaseConstants.permissionsWizard
															.Union(DatabaseConstants.permissionsLog),
											UnsetPermissions = DatabaseConstants.permissionsWizard
							}).Result,
			migrator.Context.Document.CreateAsync(handle, DatabaseConstants.objectPowers,
							new
							{
											Name = "Hide",
											System = true,
											TypeRestrictions = DatabaseConstants.typesAll,
											SetPermissions = DatabaseConstants.permissionsWizard
															.Union(DatabaseConstants.permissionsLog),
											UnsetPermissions = DatabaseConstants.permissionsWizard
							}).Result,
			migrator.Context.Document.CreateAsync(handle, DatabaseConstants.objectPowers,
							new
							{
											Name = "Hook",
											System = true,
											TypeRestrictions = DatabaseConstants.typesAll,
											SetPermissions = DatabaseConstants.permissionsWizard
															.Union(DatabaseConstants.permissionsLog)
							}).Result,
			migrator.Context.Document.CreateAsync(handle, DatabaseConstants.objectPowers,
							new
							{
											Name = "Idle",
											System = true,
											TypeRestrictions = DatabaseConstants.typesAll,
											SetPermissions = DatabaseConstants.permissionsWizard
															.Union(DatabaseConstants.permissionsLog),
											UnsetPermissions = DatabaseConstants.permissionsWizard
							}).Result,
			migrator.Context.Document.CreateAsync(handle, DatabaseConstants.objectPowers,
							new
							{
											Name = "Immortal",
											System = true,
											TypeRestrictions = DatabaseConstants.typesAll,
											SetPermissions = DatabaseConstants.permissionsWizard
															.Union(DatabaseConstants.permissionsLog),
											UnsetPermissions = DatabaseConstants.permissionsWizard
							}).Result,
			migrator.Context.Document.CreateAsync(handle, DatabaseConstants.objectPowers,
							new
							{
											Name = "Link_Anywhere",
											System = true,
											TypeRestrictions = DatabaseConstants.typesAll,
											SetPermissions = DatabaseConstants.permissionsWizard
															.Union(DatabaseConstants.permissionsLog),
											UnsetPermissions = DatabaseConstants.permissionsWizard
							}).Result,
			migrator.Context.Document.CreateAsync(handle, DatabaseConstants.objectPowers,
							new
							{
											Name = "Login",
											System = true,
											TypeRestrictions = DatabaseConstants.typesAll,
											SetPermissions = DatabaseConstants.permissionsWizard
															.Union(DatabaseConstants.permissionsLog),
											UnsetPermissions = DatabaseConstants.permissionsWizard
							}).Result,
			migrator.Context.Document.CreateAsync(handle, DatabaseConstants.objectPowers,
							new
							{
											Name = "Long_Fingers",
											System = true,
											TypeRestrictions = DatabaseConstants.typesAll,
											SetPermissions = DatabaseConstants.permissionsWizard
															.Union(DatabaseConstants.permissionsLog),
											UnsetPermissions = DatabaseConstants.permissionsWizard
							}).Result,
			migrator.Context.Document.CreateAsync(handle, DatabaseConstants.objectPowers,
							new
							{
											Name = "Many_Attribs",
											System = true,
											TypeRestrictions = DatabaseConstants.typesAll,
											SetPermissions = DatabaseConstants.permissionsWizard
															.Union(DatabaseConstants.permissionsLog)
							}).Result,
			migrator.Context.Document.CreateAsync(handle, DatabaseConstants.objectPowers,
							new
							{
											Name = "No_Pay",
											System = true,
											TypeRestrictions = DatabaseConstants.typesAll,
											SetPermissions = DatabaseConstants.permissionsWizard
															.Union(DatabaseConstants.permissionsLog),
											UnsetPermissions = DatabaseConstants.permissionsWizard
							}).Result,
			migrator.Context.Document.CreateAsync(handle, DatabaseConstants.objectPowers,
							new
							{
											Name = "No_Quota",
											System = true,
											Aliases = (string[])["Free_Quota"],
											TypeRestrictions = DatabaseConstants.typesAll,
											SetPermissions = DatabaseConstants.permissionsWizard
															.Union(DatabaseConstants.permissionsLog),
											UnsetPermissions = DatabaseConstants.permissionsWizard
							}).Result,
			migrator.Context.Document.CreateAsync(handle, DatabaseConstants.objectPowers,
							new
							{
											Name = "Open_Anywhere",
											System = true,
											TypeRestrictions = DatabaseConstants.typesAll,
											SetPermissions = DatabaseConstants.permissionsWizard
															.Union(DatabaseConstants.permissionsLog),
											UnsetPermissions = DatabaseConstants.permissionsWizard
							}).Result,
			migrator.Context.Document.CreateAsync(handle, DatabaseConstants.objectPowers,
							new
							{
											Name = "Pemit_All",
											System = true,
											TypeRestrictions = DatabaseConstants.typesAll,
											SetPermissions = DatabaseConstants.permissionsWizard
															.Union(DatabaseConstants.permissionsLog),
											UnsetPermissions = DatabaseConstants.permissionsWizard
							}).Result,
			migrator.Context.Document.CreateAsync(handle, DatabaseConstants.objectPowers,
							new
							{
											Name = "Pick_DBRefs",
											System = true,
											TypeRestrictions = DatabaseConstants.typesAll,
											SetPermissions = DatabaseConstants.permissionsWizard
															.Union(DatabaseConstants.permissionsLog),
											UnsetPermissions = DatabaseConstants.permissionsWizard
							}).Result,
			migrator.Context.Document.CreateAsync(handle, DatabaseConstants.objectPowers,
							new
							{
											Name = "Player_Create",
											System = true,
											TypeRestrictions = DatabaseConstants.typesAll,
											SetPermissions = DatabaseConstants.permissionsWizard
															.Union(DatabaseConstants.permissionsLog),
											UnsetPermissions = DatabaseConstants.permissionsWizard
							}).Result,
			migrator.Context.Document.CreateAsync(handle, DatabaseConstants.objectPowers,
							new
							{
											Name = "Poll",
											System = true,
											TypeRestrictions = DatabaseConstants.typesAll,
											SetPermissions = DatabaseConstants.permissionsWizard
															.Union(DatabaseConstants.permissionsLog),
											UnsetPermissions = DatabaseConstants.permissionsWizard
							}).Result,
			migrator.Context.Document.CreateAsync(handle, DatabaseConstants.objectPowers,
							new
							{
											Name = "Pueblo_Send",
											System = true,
											TypeRestrictions = DatabaseConstants.typesAll,
											SetPermissions = DatabaseConstants.permissionsWizard
															.Union(DatabaseConstants.permissionsLog),
											UnsetPermissions = DatabaseConstants.permissionsWizard
							}).Result,
			migrator.Context.Document.CreateAsync(handle, DatabaseConstants.objectPowers,
							new
							{
											Name = "Queue",
											System = true,
											TypeRestrictions = DatabaseConstants.typesAll,
											SetPermissions = DatabaseConstants.permissionsWizard
															.Union(DatabaseConstants.permissionsLog),
											UnsetPermissions = DatabaseConstants.permissionsWizard
							}).Result,
			migrator.Context.Document.CreateAsync(handle, DatabaseConstants.objectPowers,
							new
							{
											Name = "Search",
											System = true,
											TypeRestrictions = DatabaseConstants.typesAll,
											SetPermissions = DatabaseConstants.permissionsWizard
															.Union(DatabaseConstants.permissionsLog),
											UnsetPermissions = DatabaseConstants.permissionsWizard
							}).Result,
			migrator.Context.Document.CreateAsync(handle, DatabaseConstants.objectPowers,
							new
							{
											Name = "See_All",
											System = true,
											TypeRestrictions = DatabaseConstants.typesAll,
											SetPermissions = DatabaseConstants.permissionsWizard
															.Union(DatabaseConstants.permissionsLog),
											UnsetPermissions = DatabaseConstants.permissionsWizard
							}).Result,
			migrator.Context.Document.CreateAsync(handle, DatabaseConstants.objectPowers,
							new
							{
											Name = "See_Queue",
											System = true,
											TypeRestrictions = DatabaseConstants.typesAll,
											SetPermissions = DatabaseConstants.permissionsWizard
															.Union(DatabaseConstants.permissionsLog),
											UnsetPermissions = DatabaseConstants.permissionsWizard
							}).Result,
			migrator.Context.Document.CreateAsync(handle, DatabaseConstants.objectPowers,
							new
							{
											Name = "See_OOB",
											System = true,
											TypeRestrictions = DatabaseConstants.typesAll,
											SetPermissions = DatabaseConstants.permissionsWizard
															.Union(DatabaseConstants.permissionsLog),
											UnsetPermissions = DatabaseConstants.permissionsWizard
							}).Result,
			migrator.Context.Document.CreateAsync(handle, DatabaseConstants.objectPowers,
							new
							{
											Name = "SQL_OK",
											System = true,
											TypeRestrictions = DatabaseConstants.typesAll,
											SetPermissions = DatabaseConstants.permissionsWizard
															.Union(DatabaseConstants.permissionsLog),
											UnsetPermissions = DatabaseConstants.permissionsWizard
							}).Result,
			migrator.Context.Document.CreateAsync(handle, DatabaseConstants.objectPowers,
							new
							{
											Name = "Tport_Anything",
											System = true,
											Aliases = (string[])["tel_anything"],
											TypeRestrictions = DatabaseConstants.typesAll,
											SetPermissions = DatabaseConstants.permissionsWizard
															.Union(DatabaseConstants.permissionsLog),
											UnsetPermissions = DatabaseConstants.permissionsWizard
							}).Result,
			migrator.Context.Document.CreateAsync(handle, DatabaseConstants.objectPowers,
							new
							{
											Name = "Tport_Anywhere",
											System = true,
											Aliases = (string[])["tel_anywhere"],
											TypeRestrictions = DatabaseConstants.typesAll,
											SetPermissions = DatabaseConstants.permissionsWizard
															.Union(DatabaseConstants.permissionsLog),
											UnsetPermissions = DatabaseConstants.permissionsWizard
							}).Result,
			migrator.Context.Document.CreateAsync(handle, DatabaseConstants.objectPowers,
							new
							{
											Name = "Unkillable",
											System = true,
											TypeRestrictions = DatabaseConstants.typesAll,
											SetPermissions = DatabaseConstants.permissionsWizard
															.Union(DatabaseConstants.permissionsLog),
											UnsetPermissions = DatabaseConstants.permissionsWizard
							}).Result
		];

		private static List<ArangoUpdateResult<ArangoVoid>> CreateInitialAttributeFlags(IArangoMigrator migrator,
			ArangoHandle handle) =>
		[
			migrator.Context.Document.CreateAsync(handle, DatabaseConstants.attributeFlags,
				new
				{
					Name = "no_command",
					Symbol = "$",
					System = true,
					Inheritable = true
				}).Result,
				migrator.Context.Document.CreateAsync(handle, DatabaseConstants.attributeFlags,
				new
				{
					Name = "no_inherit",
					Symbol = "i",
					System = true,
					Inheritable = true
				}).Result,
				migrator.Context.Document.CreateAsync(handle, DatabaseConstants.attributeFlags,
				new
				{
					Name = "no_clone",
					Symbol = "c",
					System = true,
					Inheritable = true
				}).Result,
				migrator.Context.Document.CreateAsync(handle, DatabaseConstants.attributeFlags,
				new
				{
					Name = "mortal_dark",
					Symbol = "m",
					System = true,
					Inheritable = true
				}).Result,
				migrator.Context.Document.CreateAsync(handle, DatabaseConstants.attributeFlags,
				new
				{
					Name = "wizard",
					Symbol = "w",
					System = true,
					Inheritable = true
				}).Result,
				migrator.Context.Document.CreateAsync(handle, DatabaseConstants.attributeFlags,
				new
				{
					Name = "veiled",
					Symbol = "V",
					System = true,
					Inheritable = true
				}).Result,
				migrator.Context.Document.CreateAsync(handle, DatabaseConstants.attributeFlags,
				new
				{
					Name = "nearby",
					Symbol = "n",
					System = true,
					Inheritable = true
				}).Result,
				migrator.Context.Document.CreateAsync(handle, DatabaseConstants.attributeFlags,
				new
				{
					Name = "locked",
					Symbol = "+",
					System = true,
					Inheritable = true
				}).Result,
				migrator.Context.Document.CreateAsync(handle, DatabaseConstants.attributeFlags,
				new
				{
					Name = "safe",
					Symbol = "S",
					System = true,
					Inheritable = true
				}).Result,
				migrator.Context.Document.CreateAsync(handle, DatabaseConstants.attributeFlags,
				new
				{
					Name = "visual",
					Symbol = "v",
					System = true,
					Inheritable = false
				}).Result,
				migrator.Context.Document.CreateAsync(handle, DatabaseConstants.attributeFlags,
				new
				{
					Name = "public",
					Symbol = "p",
					System = true,
					Inheritable = false
				}).Result,
				migrator.Context.Document.CreateAsync(handle, DatabaseConstants.attributeFlags,
				new
				{
					Name = "debug",
					Symbol = "b",
					System = true,
					Inheritable = true
				}).Result,
				migrator.Context.Document.CreateAsync(handle, DatabaseConstants.attributeFlags,
				new
				{
					Name = "no_debug",
					Symbol = "B",
					System = true,
					Inheritable = true
				}).Result,
				migrator.Context.Document.CreateAsync(handle, DatabaseConstants.attributeFlags,
				new
				{
					Name = "regexp",
					Symbol = "R",
					System = true,
					Inheritable = false
				}).Result,
				migrator.Context.Document.CreateAsync(handle, DatabaseConstants.attributeFlags,
				new
				{
					Name = "case",
					Symbol = "C",
					System = true,
					Inheritable = false
				}).Result,
				migrator.Context.Document.CreateAsync(handle, DatabaseConstants.attributeFlags,
				new
				{
					Name = "nospace",
					Symbol = "s",
					System = true,
					Inheritable = true
				}).Result,
				migrator.Context.Document.CreateAsync(handle, DatabaseConstants.attributeFlags,
				new
				{
					Name = "noname",
					Symbol = "N",
					System = true,
					Inheritable = true
				}).Result,
				migrator.Context.Document.CreateAsync(handle, DatabaseConstants.attributeFlags,
				new
				{
					Name = "aahear",
					Symbol = "A",
					System = true,
					Inheritable = false
				}).Result,
				migrator.Context.Document.CreateAsync(handle, DatabaseConstants.attributeFlags,
				new
				{
					Name = "amhear",
					Symbol = "M",
					System = true,
					Inheritable = false
				}).Result,
				migrator.Context.Document.CreateAsync(handle, DatabaseConstants.attributeFlags,
				new
				{
					Name = "quiet",
					Symbol = "Q",
					System = true,
					Inheritable = false
				}).Result,
				// TODO: Consider if this is needed for our purposes at all.
				migrator.Context.Document.CreateAsync(handle, DatabaseConstants.attributeFlags,
				new
				{
					Name = "branch",
					Symbol = "`",
					System = true,
					Inheritable = false
				}).Result
		];

		// Todo: Find a better way of doing this, so we can keep a proper async flow.
		private static List<ArangoUpdateResult<ArangoVoid>> CreateInitialFlags(IArangoMigrator migrator,
			ArangoHandle handle) =>
		[
			migrator.Context.Document.CreateAsync(handle, DatabaseConstants.objectFlags, new
			{
				Name = "ABODE",
				Symbol = "A",
				System = true,
				TypeRestrictions = DatabaseConstants.typesRoom
			}).Result,
			migrator.Context.Document.CreateAsync(handle, DatabaseConstants.objectFlags, new
			{
				Name = "ANSI",
				Symbol = "A",
				System = true,
				TypeRestrictions = DatabaseConstants.typesPlayer
			}).Result,
			migrator.Context.Document.CreateAsync(handle, DatabaseConstants.objectFlags, new
			{
				Name = "CHOWN_OK",
				Symbol = "C",
				System = true,
				TypeRestrictions = DatabaseConstants.typesContainer
			}).Result,
			migrator.Context.Document.CreateAsync(handle, DatabaseConstants.objectFlags, new
			{
				Name = "DARK",
				Symbol = "D",
				System = true,
				TypeRestrictions = DatabaseConstants.typesAll
			}).Result,
			migrator.Context.Document.CreateAsync(handle, DatabaseConstants.objectFlags, new
			{
				Name = "FIXED",
				Symbol = "F",
				System = true,
				SetPermissions = DatabaseConstants.permissionsWizard,
				UnsetPermissions = DatabaseConstants.permissionsWizard,
				TypeRestrictions = DatabaseConstants.typesPlayer
			}).Result,
			migrator.Context.Document.CreateAsync(handle, DatabaseConstants.objectFlags, new
			{
				Name = "FLOATING",
				Symbol = "F",
				System = true,
				TypeRestrictions = DatabaseConstants.typesRoom
			}).Result,
			migrator.Context.Document.CreateAsync(handle, DatabaseConstants.objectFlags, new
			{
				Name = "HAVEN",
				Symbol = "H",
				System = true,
				TypeRestrictions = DatabaseConstants.typesPlayer
			}).Result,
			migrator.Context.Document.CreateAsync(handle, DatabaseConstants.objectFlags, new
			{
				Name = "TRUST",
				Symbol = "I",
				System = true,
				Aliases = (string[])["INHERIT"],
				SetPermissions = DatabaseConstants.permissionsTrusted,
				UnsetPermissions = DatabaseConstants.permissionsTrusted,
				TypeRestrictions = DatabaseConstants.typesAll
			}).Result,
			migrator.Context.Document.CreateAsync(handle, DatabaseConstants.objectFlags, new
			{
				Name = "JUDGE",
				Symbol = "J",
				System = true,
				SetPermissions = DatabaseConstants.permissionsRoyalty,
				UnsetPermissions = DatabaseConstants.permissionsRoyalty,
				TypeRestrictions = DatabaseConstants.typesPlayer
			}).Result,
			migrator.Context.Document.CreateAsync(handle, DatabaseConstants.objectFlags, new
			{
				Name = "JUMP_OK",
				Symbol = "J",
				System = true,
				Aliases = (string[])["TEL-OK", "TEL_OK", "TELOK"],
				TypeRestrictions = DatabaseConstants.typesRoom
			}).Result,
			migrator.Context.Document.CreateAsync(handle, DatabaseConstants.objectFlags, new
			{
				Name = "LINK_OK",
				Symbol = "L",
				System = true,
				TypeRestrictions = DatabaseConstants.typesAll
			}).Result,
			migrator.Context.Document.CreateAsync(handle, DatabaseConstants.objectFlags, new
			{
				Name = "MONITOR",
				Symbol = "M",
				System = true,
				Aliases = (string[])["LISTENER", "WATCHER"],
				TypeRestrictions = DatabaseConstants.typesContainer
			}).Result,
			migrator.Context.Document.CreateAsync(handle, DatabaseConstants.objectFlags, new
			{
				Name = "NO_LEAVE",
				Symbol = "N",
				System = true,
				Aliases = (string[])["NOLEAVE"],
				TypeRestrictions = DatabaseConstants.typesThing
			}).Result,
			migrator.Context.Document.CreateAsync(handle, DatabaseConstants.objectFlags, new
			{
				Name = "NO_TEL",
				Symbol = "N",
				System = true,
				TypeRestrictions = DatabaseConstants.typesRoom
			}).Result,
			migrator.Context.Document.CreateAsync(handle, DatabaseConstants.objectFlags, new
			{
				Name = "OPAQUE",
				Symbol = "O",
				System = true,
				TypeRestrictions = DatabaseConstants.typesAll
			}).Result,
			migrator.Context.Document.CreateAsync(handle, DatabaseConstants.objectFlags, new
			{
				Name = "QUIET",
				Symbol = "Q",
				System = true,
				TypeRestrictions = DatabaseConstants.typesAll
			}).Result,
			migrator.Context.Document.CreateAsync(handle, DatabaseConstants.objectFlags, new
			{
				Name = "UNFINDABLE",
				Symbol = "U",
				System = true,
				TypeRestrictions = DatabaseConstants.typesAll
			}).Result,
			migrator.Context.Document.CreateAsync(handle, DatabaseConstants.objectFlags, new
			{
				Name = "VISUAL",
				Symbol = "V",
				System = true,
				TypeRestrictions = DatabaseConstants.typesAll
			}).Result,
			migrator.Context.Document.CreateAsync(handle, DatabaseConstants.objectFlags, new
			{
				Name = "WIZARD",
				Symbol = "W",
				System = true,
				TypeRestrictions = DatabaseConstants.typesAll,
				SetPermissions = DatabaseConstants.permissionsTrusted
					.Union(DatabaseConstants.permissionsWizard)
					.Union(DatabaseConstants.permissionsLog),
				UnsetPermissions = DatabaseConstants.permissionsTrusted
					.Union(DatabaseConstants.permissionsWizard),
			}).Result,
			migrator.Context.Document.CreateAsync(handle, DatabaseConstants.objectFlags, new
			{
				Name = "SAFE",
				Symbol = "X",
				System = true,
				TypeRestrictions = DatabaseConstants.typesAll
			}).Result,
			migrator.Context.Document.CreateAsync(handle, DatabaseConstants.objectFlags, new
			{
				Name = "SHARED",
				Symbol = "Z",
				System = true,
				Aliases = (string[])["ZONE"],
				TypeRestrictions = DatabaseConstants.typesPlayer
			}).Result,
			migrator.Context.Document.CreateAsync(handle, DatabaseConstants.objectFlags, new
			{
				Name = "Z_TEL",
				Symbol = "Z",
				System = true,
				TypeRestrictions = DatabaseConstants.typesRoom
					.Union(DatabaseConstants.typesThing)
			}).Result,
			migrator.Context.Document.CreateAsync(handle, DatabaseConstants.objectFlags, new
			{
				Name = "LISTEN_PARENT",
				Symbol = "^",
				Aliases = (string[])["^"],
				System = true,
				TypeRestrictions = DatabaseConstants.typesPlayer
					.Union(DatabaseConstants.typesThing).Union(DatabaseConstants.typesRoom)
			}).Result,
			migrator.Context.Document.CreateAsync(handle, DatabaseConstants.objectFlags, new
			{
				Name = "NOACCENTS",
				Symbol = "~",
				System = true,
				TypeRestrictions = DatabaseConstants.typesPlayer
					.Union(DatabaseConstants.typesThing).Union(DatabaseConstants.typesRoom)
			}).Result,
			migrator.Context.Document.CreateAsync(handle, DatabaseConstants.objectFlags, new
			{
				Name = "UNREGISTERED",
				Symbol = "?",
				System = true,
				TypeRestrictions = DatabaseConstants.typesPlayer,
				SetPermissions = DatabaseConstants.permissionsRoyalty,
				UnsetPermissions = DatabaseConstants.permissionsRoyalty
			}).Result,
			migrator.Context.Document.CreateAsync(handle, DatabaseConstants.objectFlags, new
			{
				Name = "NOSPOOF",
				Symbol = "\"",
				System = true,
				TypeRestrictions = DatabaseConstants.typesAll,
				SetPermissions = DatabaseConstants.permissionsODark,
				UnSetPermissions = DatabaseConstants.permissionsODark
			}).Result,
			migrator.Context.Document.CreateAsync(handle, DatabaseConstants.objectFlags, new
			{
				Name = "AUDIBLE",
				Symbol = "a",
				System = true,
				TypeRestrictions = DatabaseConstants.typesAll
			}).Result,
			migrator.Context.Document.CreateAsync(handle, DatabaseConstants.objectFlags, new
			{
				Name = "DEBUG",
				Aliases = (string[])["TRACE"],
				Symbol = "b",
				System = true,
				TypeRestrictions = DatabaseConstants.typesAll
			}).Result,
			migrator.Context.Document.CreateAsync(handle, DatabaseConstants.objectFlags, new
			{
				Name = "DESTROY_OK",
				Aliases = (string[])["DEST_OK"],
				Symbol = "d",
				System = true,
				TypeRestrictions = DatabaseConstants.typesThing
			}).Result,
			migrator.Context.Document.CreateAsync(handle, DatabaseConstants.objectFlags, new
			{
				Name = "ENTER_OK",
				Symbol = "e",
				System = true,
				TypeRestrictions = DatabaseConstants.typesAll
			}).Result,
			migrator.Context.Document.CreateAsync(handle, DatabaseConstants.objectFlags, new
			{
				Name = "GAGGED",
				Symbol = "g",
				System = true,
				TypeRestrictions = DatabaseConstants.typesPlayer,
				SetPermissions = DatabaseConstants.permissionsWizard,
				UnSetPermissions = DatabaseConstants.permissionsWizard
			}).Result,
			migrator.Context.Document.CreateAsync(handle, DatabaseConstants.objectFlags, new
			{
				Name = "HALT",
				Symbol = "h",
				System = true,
				TypeRestrictions = DatabaseConstants.typesAll
			}).Result,
			migrator.Context.Document.CreateAsync(handle, DatabaseConstants.objectFlags, new
			{
				Name = "ORPHAN",
				Symbol = "i",
				System = true,
				TypeRestrictions = DatabaseConstants.typesAll
			}).Result,
			migrator.Context.Document.CreateAsync(handle, DatabaseConstants.objectFlags, new
			{
				Name = "JURY_OK",
				Aliases = (string[])["JURYOK"],
				Symbol = "j",
				System = true,
				TypeRestrictions = DatabaseConstants.typesPlayer,
				SetPermissions = DatabaseConstants.permissionsRoyalty,
				UnSetPermissions = DatabaseConstants.permissionsRoyalty
			}).Result,
			migrator.Context.Document.CreateAsync(handle, DatabaseConstants.objectFlags, new
			{
				Name = "KEEPALIVE",
				Symbol = "k",
				System = true,
				TypeRestrictions = DatabaseConstants.typesPlayer
			}).Result,
			migrator.Context.Document.CreateAsync(handle, DatabaseConstants.objectFlags, new
			{
				Name = "LIGHT",
				Symbol = "l",
				System = true,
				TypeRestrictions = DatabaseConstants.typesAll
			}).Result,
			migrator.Context.Document.CreateAsync(handle, DatabaseConstants.objectFlags, new
			{
				Name = "MISTRUST",
				Symbol = "m",
				System = true,
				TypeRestrictions = DatabaseConstants.typesContent,
				SetPermissions = DatabaseConstants.permissionsTrusted,
				UnSetPermissions = DatabaseConstants.permissionsTrusted
			}).Result,
			migrator.Context.Document.CreateAsync(handle, DatabaseConstants.objectFlags, new
			{
				Name = "MISTRUST",
				Aliases = (string[])["MYOPIC"],
				Symbol = "m",
				System = true,
				TypeRestrictions = DatabaseConstants.typesContent,
				SetPermissions = DatabaseConstants.permissionsTrusted,
				UnSetPermissions = DatabaseConstants.permissionsTrusted
			}).Result,
			migrator.Context.Document.CreateAsync(handle, DatabaseConstants.objectFlags, new
			{
				Name = "NO_COMMAND",
				Aliases = (string[])["NOCOMMAND"],
				Symbol = "n",
				System = true,
				TypeRestrictions = DatabaseConstants.typesAll
			}).Result,
			migrator.Context.Document.CreateAsync(handle, DatabaseConstants.objectFlags, new
			{
				Name = "ON_VACATION",
				Aliases = (string[])["ONVACATION","ON-VACATION"],
				Symbol = "o",
				System = true,
				TypeRestrictions = DatabaseConstants.typesPlayer
			}).Result,
			migrator.Context.Document.CreateAsync(handle, DatabaseConstants.objectFlags, new
			{
				Name = "PUPPET",
				Symbol = "P",
				System = true,
				TypeRestrictions = DatabaseConstants.typesThing
					.Union(DatabaseConstants.typesRoom)
			}).Result,
			migrator.Context.Document.CreateAsync(handle, DatabaseConstants.objectFlags, new
			{
				Name = "ROYALTY",
				Symbol = "r",
				System = true,
				TypeRestrictions = DatabaseConstants.typesAll,
				SetPermissions = DatabaseConstants.permissionsTrusted
					.Union(DatabaseConstants.permissionsRoyalty)
					.Union(DatabaseConstants.permissionsLog),
				UnSetPermissions = DatabaseConstants.permissionsTrusted
					.Union(DatabaseConstants.permissionsRoyalty)
			}).Result,
			migrator.Context.Document.CreateAsync(handle, DatabaseConstants.objectFlags, new
			{
				Name = "SUSPECT",
				Symbol = "s",
				System = true,
				TypeRestrictions = DatabaseConstants.typesAll,
				SetPermissions = DatabaseConstants.permissionsWizard
					.Union(DatabaseConstants.permissionsMDark)
					.Union(DatabaseConstants.permissionsLog),
				UnSetPermissions = DatabaseConstants.permissionsWizard
					.Union(DatabaseConstants.permissionsMDark)
			}).Result,
			migrator.Context.Document.CreateAsync(handle, DatabaseConstants.objectFlags, new
			{
				Name = "TRANSPARENT",
				Symbol = "t",
				System = true,
				TypeRestrictions = DatabaseConstants.typesAll
			}).Result,
			migrator.Context.Document.CreateAsync(handle, DatabaseConstants.objectFlags, new
			{
				Name = "VERBOSE",
				Symbol = "v",
				System = true,
				TypeRestrictions = DatabaseConstants.typesAll,
			}).Result,
			migrator.Context.Document.CreateAsync(handle, DatabaseConstants.objectFlags, new
			{
				Name = "NO_WARN",
				Aliases = (string[])["NOWARN"],
				Symbol = "w",
				System = true,
				TypeRestrictions = DatabaseConstants.typesAll,
			}).Result,
			migrator.Context.Document.CreateAsync(handle, DatabaseConstants.objectFlags, new
			{
				Name = "CLOUDY",
				Aliases = (string[])["TERSE"],
				Symbol = "x",
				System = true,
				TypeRestrictions = DatabaseConstants.typesExit,
			}).Result,
			migrator.Context.Document.CreateAsync(handle, DatabaseConstants.objectFlags, new
			{
				Name = "CHAN_USEFIRSTMATCH",
				Aliases = (string[])["CHAN_FIRSTMATCH","CHAN_MATCHFIRST"],
				System = true,
				TypeRestrictions = DatabaseConstants.typesAll,
				SetPermissions = DatabaseConstants.permissionsTrusted,
				UnSetPermissions = DatabaseConstants.permissionsTrusted
			}).Result,
			migrator.Context.Document.CreateAsync(handle, DatabaseConstants.objectFlags, new
			{
				Name = "HEAR_CONNECT",
				System = true,
				TypeRestrictions = DatabaseConstants.typesPlayer,
				SetPermissions = DatabaseConstants.permissionsRoyalty
			}).Result,
			migrator.Context.Document.CreateAsync(handle, DatabaseConstants.objectFlags, new
			{
				Name = "HEAVY",
				System = true,
				TypeRestrictions = DatabaseConstants.typesAll,
				SetPermissions = DatabaseConstants.permissionsRoyalty
			}).Result,
			migrator.Context.Document.CreateAsync(handle, DatabaseConstants.objectFlags, new
			{
				Name = "LOUD",
				System = true,
				TypeRestrictions = DatabaseConstants.typesPlayer,
				SetPermissions = DatabaseConstants.permissionsRoyalty
			}).Result,
			migrator.Context.Document.CreateAsync(handle, DatabaseConstants.objectFlags, new
			{
				Name = "NO_LOG",
				System = true,
				TypeRestrictions = DatabaseConstants.typesPlayer,
				SetPermissions = DatabaseConstants.permissionsWizard
					.Union(DatabaseConstants.permissionsMDark)
					.Union(DatabaseConstants.permissionsLog),
				UnSetPermissions = DatabaseConstants.permissionsWizard
					.Union(DatabaseConstants.permissionsMDark)
			}).Result,
			migrator.Context.Document.CreateAsync(handle, DatabaseConstants.objectFlags, new
			{
				Name = "PARANOID",
				System = true,
				TypeRestrictions = DatabaseConstants.typesPlayer,
				SetPermissions = DatabaseConstants.permissionsODark,
				UnSetPermissions = DatabaseConstants.permissionsODark
			}).Result,
			migrator.Context.Document.CreateAsync(handle, DatabaseConstants.objectFlags, new
			{
				Name = "TRACK_MONEY",
				System = true,
				TypeRestrictions = DatabaseConstants.typesPlayer,
			}).Result,
			migrator.Context.Document.CreateAsync(handle, DatabaseConstants.objectFlags, new
			{
				Name = "XTERM256",
				Aliases = (string[])["XTERM","COLOR256"],
				System = true,
				TypeRestrictions = DatabaseConstants.typesPlayer
			}).Result,
			migrator.Context.Document.CreateAsync(handle, DatabaseConstants.objectFlags, new
			{
				Name = "MONIKER",
				System = true,
				TypeRestrictions = DatabaseConstants.typesAll,
				SetPermissions = DatabaseConstants.permissionsRoyalty,
				UnSetPermissions = DatabaseConstants.permissionsRoyalty
			}).Result,
			migrator.Context.Document.CreateAsync(handle, DatabaseConstants.objectFlags, new
			{
				Name = "OPEN_OK",
				System = true,
				TypeRestrictions = DatabaseConstants.typesRoom
			}).Result,
		];

		public Task Down(IArangoMigrator migrator, ArangoHandle handle)
		{
			throw new NotImplementedException();
		}
	}
}