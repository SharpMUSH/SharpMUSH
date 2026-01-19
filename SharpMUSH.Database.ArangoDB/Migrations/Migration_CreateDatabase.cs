using Core.Arango;
using Core.Arango.Migration;
using Core.Arango.Protocol;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Database.ArangoDB.Migrations;

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

	// Helper to determine if we're in test mode for performance optimizations
	private static bool IsTestMode => Environment.GetEnvironmentVariable("SHARPMUSH_FAST_MIGRATION") == "true";

	// Helper method to batch insert documents for better performance
	private static async Task<List<ArangoUpdateResult<ArangoVoid>>> BatchCreateDocuments<T>(
		IArangoMigrator migrator,
		ArangoHandle handle,
		string collection,
		List<T> documents)
	{
		if (IsTestMode && documents.Count > 1)
		{
			// Use batch insert for test mode
			return await migrator.Context.Document.CreateManyAsync(handle, collection, documents);
		}
		else
		{
			// Fall back to individual inserts for production to maintain exact compatibility
			var results = new List<ArangoUpdateResult<ArangoVoid>>();
			foreach (var doc in documents)
			{
				results.Add(await migrator.Context.Document.CreateAsync(handle, collection, doc));
			}
			return results;
		}
	}

	public async Task Up(IArangoMigrator migrator, ArangoHandle handle)
	{
		await migrator.ApplyStructureAsync(handle, new ArangoStructure()
			{
				Collections =
				[
					new() {
						Collection = new ArangoCollection
						{
							Name = DatabaseConstants.Objects,
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
									type = DatabaseConstants.TypeObject,
									properties = new {
										Name = new { type = DatabaseConstants.TypeString },
										Type = new { type = DatabaseConstants.TypeString },
										Locks = new { type = DatabaseConstants.TypeObject },
										CreationTime = new { type = DatabaseConstants.TypeNumber },
										ModifiedTime = new { type = DatabaseConstants.TypeNumber },
									},
									required = (string[])[nameof(SharpObject.Name), nameof(SharpObject.Type)],
									additionalProperties = true
								}
							},
							WaitForSync = !IsTestMode
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
							Name = DatabaseConstants.Things,
							Type = ArangoCollectionType.Document,
							WaitForSync = !IsTestMode,
							Schema = new ArangoSchema()
							{
								Rule = new {
									type = DatabaseConstants.TypeObject,
									properties = new
									{
										Aliases = new { type = DatabaseConstants.TypeArray, 
											items = new { type = DatabaseConstants.TypeString } },
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
							Name = DatabaseConstants.Rooms,
							Type = ArangoCollectionType.Document,
							WaitForSync = !IsTestMode
						}
					},
					new()
					{
						Collection = new ArangoCollection
						{
							Name = DatabaseConstants.ServerData,
							Type = ArangoCollectionType.Document,
							WaitForSync = !IsTestMode
						}
					},
					new()
					{
						Collection = new ArangoCollection
						{
							Name = DatabaseConstants.ObjectData,
							Type = ArangoCollectionType.Document,
							WaitForSync = !IsTestMode
						}
					},
					new()
					{
						Collection = new ArangoCollection
						{
							Name = DatabaseConstants.Exits,
							Type = ArangoCollectionType.Document,
							WaitForSync = !IsTestMode,
							Schema = new ArangoSchema()
							{
								Rule = new {
									type = DatabaseConstants.TypeObject,
									properties = new
									{
										Aliases = new { type = DatabaseConstants.TypeArray, 
											items = new { type = DatabaseConstants.TypeString } }
									}
								}
							}
						}
					},
					new()
					{
						Collection = new ArangoCollection
						{
							Name = DatabaseConstants.Players,
							Type = ArangoCollectionType.Document,
							WaitForSync = !IsTestMode,
							Schema = new ArangoSchema()
							{
								Rule = new {
									type = DatabaseConstants.TypeObject,
									properties = new
									{
										PasswordHash = new { type = DatabaseConstants.TypeString },
										Aliases = new { type = DatabaseConstants.TypeArray, items = new { type = DatabaseConstants.TypeString } },
										Quota = new { type = DatabaseConstants.TypeNumber, multipleOf = 1 }
									},
									required = (string[])[nameof(SharpPlayer.PasswordHash), nameof(SharpPlayer.Quota)]
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
							Name = DatabaseConstants.ObjectFlags,
							Type = ArangoCollectionType.Document,
							WaitForSync = !IsTestMode,
							Schema = new ArangoSchema()
							{
								Rule = new {
									type = DatabaseConstants.TypeObject,
									properties = new
									{
										Name = new { type = DatabaseConstants.TypeString },
										Symbol = new { type = DatabaseConstants.TypeString, multipleOf = 1 },
										Aliases = new { type = DatabaseConstants.TypeArray, items = new { type = DatabaseConstants.TypeString } },
										System = new {type = DatabaseConstants.TypeBoolean },
										SetPermissions = new { type = DatabaseConstants.TypeArray, items = new { type = DatabaseConstants.TypeString } },
										UnsetPermissions = new { type = DatabaseConstants.TypeArray, items = new { type = DatabaseConstants.TypeString } },
										TypeRestrictions = new { type = DatabaseConstants.TypeArray, items = new { type = DatabaseConstants.TypeString } }
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
							Name = DatabaseConstants.AttributeFlags,
							Type = ArangoCollectionType.Document,
							WaitForSync = !IsTestMode,
							Schema = new ArangoSchema()
							{
								Rule = new {
									type = DatabaseConstants.TypeObject,
									properties = new
									{
										Name = new { type = DatabaseConstants.TypeString },
										Symbol = new { type = DatabaseConstants.TypeString, multipleOf = 1 },
										System = new {type = DatabaseConstants.TypeBoolean },
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
							Name = DatabaseConstants.ObjectPowers,
							Type = ArangoCollectionType.Document,
							WaitForSync = !IsTestMode,
							Schema = new ArangoSchema()
							{
								Rule = new {
									type = DatabaseConstants.TypeObject,
									properties = new
									{
										Name = new { type = DatabaseConstants.TypeString },
										Symbol = new { type = DatabaseConstants.TypeString, multipleOf = 1 },
										System = new { type = DatabaseConstants.TypeBoolean },
										SetPermissions = new { type = DatabaseConstants.TypeArray, items = new { type = DatabaseConstants.TypeString } },
										UnsetPermissions = new { type = DatabaseConstants.TypeArray, items = new { type = DatabaseConstants.TypeString } },
										TypeRestrictions = new { type = DatabaseConstants.TypeArray, items = new { type = DatabaseConstants.TypeString } }
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
							Name = DatabaseConstants.Attributes,
							Type = ArangoCollectionType.Document,
							WaitForSync = !IsTestMode,
							Schema = new ArangoSchema()
							{
								Rule = new {
									type = DatabaseConstants.TypeObject,
									properties = new
									{
										Name = new { type = DatabaseConstants.TypeString },
										LongName = new { type = DatabaseConstants.TypeString },
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
							Name = DatabaseConstants.AttributeEntries,
							Type = ArangoCollectionType.Document,
							WaitForSync = !IsTestMode,
							Schema = new ArangoSchema()
							{
								Rule = new {
									type = DatabaseConstants.TypeObject,
									properties = new
									{
										Name = new { type = DatabaseConstants.TypeString },
										DefaultFlags = new { type = DatabaseConstants.TypeArray, items = new { type = DatabaseConstants.TypeString } },
										Limit = new { type = DatabaseConstants.TypeString },
										Enum = new { type = DatabaseConstants.TypeArray, items = new { type = DatabaseConstants.TypeString } }
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
							Name = DatabaseConstants.Functions,
							Type = ArangoCollectionType.Document,
							WaitForSync = !IsTestMode,
							Schema = new ArangoSchema()
							{
								Rule = new
								{
									type = DatabaseConstants.TypeObject,
									properties = new {
										Name = new { type = DatabaseConstants.TypeString },
										Alias = new { type = DatabaseConstants.TypeString },
										RestrictedErrorMessage = new { type = DatabaseConstants.TypeString },
										Traits = new { type = DatabaseConstants.TypeArray, items = new { type = DatabaseConstants.TypeString } },
										MinArgs = new { type = DatabaseConstants.TypeNumber, multipleOf = 1 },
										MaxArgs = new { type = DatabaseConstants.TypeNumber, multipleOf = 1 },
										Restrictions = new { type = DatabaseConstants.TypeArray, items = new { type = DatabaseConstants.TypeString } }
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
							Name = DatabaseConstants.Commands,
							Type = ArangoCollectionType.Document,
							WaitForSync = !IsTestMode,
							Schema = new ArangoSchema
							{
								Rule = new {
									type = DatabaseConstants.TypeObject,
									properties = new
									{
										Name = new { type = DatabaseConstants.TypeString },
										Aliases = new { type = DatabaseConstants.TypeArray, items = new { type = DatabaseConstants.TypeString } },
										Limit = new { type = DatabaseConstants.TypeString },
										Enum = new { type = DatabaseConstants.TypeArray, items = new { type = DatabaseConstants.TypeString } }
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
							Name = DatabaseConstants.Mails,
							Type = ArangoCollectionType.Document,
							WaitForSync = !IsTestMode,
							Schema = new ArangoSchema
							{
								Rule = new {
									type = DatabaseConstants.TypeObject,
									properties = new
									{
										DateSent = new { type = DatabaseConstants.TypeNumber },
										Fresh = new { type = DatabaseConstants.TypeBoolean },
										Read = new { type = DatabaseConstants.TypeBoolean },
										Tagged = new { type = DatabaseConstants.TypeBoolean },
										Forwarded = new { type = DatabaseConstants.TypeBoolean },
										Urgent = new { type = DatabaseConstants.TypeBoolean },
										Cleared = new { type = DatabaseConstants.TypeBoolean },
										Folder = new { type = DatabaseConstants.TypeString },
										Content = new { type = DatabaseConstants.TypeString },
										Subject = new { type = DatabaseConstants.TypeString },
									},
									required = (string[])[
										nameof(SharpMail.DateSent), 
										nameof(SharpMail.Fresh),
										nameof(SharpMail.Read),
										nameof(SharpMail.Tagged),
										nameof(SharpMail.Forwarded),
										nameof(SharpMail.Urgent),
										nameof(SharpMail.Cleared),
										nameof(SharpMail.Folder),
										nameof(SharpMail.Content),
										nameof(SharpMail.Subject)
									]
								}
							}
						},
						Indices =
						[
							new() { Fields = [nameof(SharpMail.Folder)] },
						]
					},
					
					new()
					{
						Collection = new ArangoCollection
						{
							Name = DatabaseConstants.Channels,
							Type = ArangoCollectionType.Document,
							WaitForSync = !IsTestMode,
							Schema = new ArangoSchema
							{
								Rule = new {
									type = DatabaseConstants.TypeObject,
									properties = new
									{
										Name = new { type = DatabaseConstants.TypeString },
										MarkedUpName = new { type = DatabaseConstants.TypeString },
										Description = new { type = DatabaseConstants.TypeString },
										Privs = new { type = DatabaseConstants.TypeArray, items = new { type = DatabaseConstants.TypeString } },
										JoinLock = new { type = DatabaseConstants.TypeString },
										SpeakLock = new { type = DatabaseConstants.TypeString },
										SeeLock = new { type = DatabaseConstants.TypeString },
										HideLock = new { type = DatabaseConstants.TypeString },
										ModLock = new { type = DatabaseConstants.TypeString },
									},
									required = (string[])[nameof(SharpChannel.Name), "MarkedUpName", nameof(SharpChannel.Privs)]
								}
							}
						},
						Indices =
						[
							new() { Fields = [nameof(SharpChannel.Name)] },
						]
					},
					new()
					{
						Collection = new ArangoCollection
						{
							Name = DatabaseConstants.AtLocation,
							Type = ArangoCollectionType.Edge,
							WaitForSync = !IsTestMode
						}
					},
					new()
					{
						Collection = new ArangoCollection
						{
							Name = DatabaseConstants.HasObjectData,
							Type = ArangoCollectionType.Edge,
							WaitForSync = !IsTestMode
						}
					},
					new()
					{
						Collection = new ArangoCollection
						{
							Name = DatabaseConstants.HasHome,
							Type = ArangoCollectionType.Edge,
							WaitForSync = !IsTestMode
						}
					},
					new()
					{
						Collection = new ArangoCollection
						{
							Name = DatabaseConstants.HasExit,
							Type = ArangoCollectionType.Edge,
							WaitForSync = !IsTestMode
						}
					},
					new()
					{
						Collection = new ArangoCollection
						{
							Name = DatabaseConstants.HasFlags,
							Type = ArangoCollectionType.Edge,
							WaitForSync = !IsTestMode
						}
					},
					new()
					{
						Collection = new ArangoCollection
						{
							Name = DatabaseConstants.HasAttribute,
							Type = ArangoCollectionType.Edge,
							WaitForSync = !IsTestMode
						}
					},
					new()
					{
						Collection = new ArangoCollection
						{
							Name = DatabaseConstants.HasAttributeFlag,
							Type = ArangoCollectionType.Edge,
							WaitForSync = !IsTestMode
						}
					},
					new()
					{
						Collection = new ArangoCollection
						{
							Name = DatabaseConstants.HasAttributeEntry,
							Type = ArangoCollectionType.Edge,
							WaitForSync = !IsTestMode
						}
					},
					new()
					{
						Collection = new ArangoCollection
						{
							Name = DatabaseConstants.HasObjectOwner,
							Type = ArangoCollectionType.Edge,
							WaitForSync = !IsTestMode
						}
					},
					new()
					{
						Collection = new ArangoCollection
						{
							Name = DatabaseConstants.HasAttributeOwner,
							Type = ArangoCollectionType.Edge,
							WaitForSync = !IsTestMode
						}
					}, 
					new()
					{
						Collection = new ArangoCollection
						{
							Name = DatabaseConstants.OnChannel,
							Type = ArangoCollectionType.Edge,
							WaitForSync = !IsTestMode,
							Schema = new ArangoSchema()
							{
								Rule = new
								{
									properties = new
									{
										Gagged = new { type = DatabaseConstants.TypeBoolean },
										Mute = new { type = DatabaseConstants.TypeBoolean },
										Hide = new { type = DatabaseConstants.TypeBoolean },
										Combine = new { type = DatabaseConstants.TypeBoolean },
										Title = new { type = DatabaseConstants.TypeString }
									}
								}
							}
						}
					},
					new()
					{
						Collection = new ArangoCollection
						{
							Name = DatabaseConstants.SenderOfMail,
							Type = ArangoCollectionType.Edge,
							WaitForSync = !IsTestMode,
						}
					},
					new()
					{
						Collection = new ArangoCollection
						{
							Name = DatabaseConstants.OwnerOfChannel,
							Type = ArangoCollectionType.Edge,
							WaitForSync = !IsTestMode,
						}
					},
					new()
					{
						Collection = new ArangoCollection
						{
							Name = DatabaseConstants.ReceivedMail,
							Type = ArangoCollectionType.Edge,
							WaitForSync = !IsTestMode,
						}
					},
					new()
					{
						Collection = new ArangoCollection
						{
							Name = DatabaseConstants.HasHook,
							Type = ArangoCollectionType.Edge,
							WaitForSync = !IsTestMode,
							Schema = new ArangoSchema()
							{
								Rule = new {
									properties = new
									{
										Type = new { type = DatabaseConstants.TypeString }
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
								Collection = DatabaseConstants.IsObject,
								To = [DatabaseConstants.Objects],
								From = [.. DatabaseConstants.verticesAll]
							}
						],
						Name = DatabaseConstants.GraphObjects
					},
					new()
					{
						EdgeDefinitions =
						[
							new ArangoEdgeDefinition()
							{
								Collection = DatabaseConstants.HasPowers,
								To = [DatabaseConstants.ObjectPowers],
								From = [
									DatabaseConstants.Objects
								]
							}
						],
						Name = DatabaseConstants.GraphPowers
					},
					new()
					{
						EdgeDefinitions =
						[
							new ArangoEdgeDefinition()
							{
								Collection = DatabaseConstants.HasFlags,
								To = [DatabaseConstants.ObjectFlags],
								From = [
									DatabaseConstants.Objects
								]
							}
						],
						Name = DatabaseConstants.GraphFlags
					},
					new()
					{
						EdgeDefinitions =
						[
							new ArangoEdgeDefinition()
							{
								Collection = DatabaseConstants.HasObjectData,
								To = [DatabaseConstants.ObjectData],
								From = [
									DatabaseConstants.Objects
								]
							}
						],
						Name = DatabaseConstants.GraphObjectData
					},
					new()
					{
						EdgeDefinitions =
						[
							new ArangoEdgeDefinition()
							{
								Collection = DatabaseConstants.HasAttribute,
								To = [DatabaseConstants.Attributes],
								From = [
									.. DatabaseConstants.verticesAll,
									DatabaseConstants.Attributes
								]
							}
						],
						Name = DatabaseConstants.GraphAttributes
					},
					new()
					{
						EdgeDefinitions =
						[
							new ArangoEdgeDefinition()
							{
								Collection = DatabaseConstants.HasAttributeFlag,
								To = [DatabaseConstants.AttributeFlags],
								From = [
									DatabaseConstants.Attributes
								]
							}
						],
						Name = DatabaseConstants.GraphAttributeFlags
					},
					new()
					{
						EdgeDefinitions =
						[
							new ArangoEdgeDefinition()
							{
								Collection = DatabaseConstants.HasAttributeEntry,
								To = [DatabaseConstants.AttributeEntries],
								From = [
									DatabaseConstants.Attributes
								]
							}
						],
						Name = DatabaseConstants.GraphAttributeEntries
					},
					new()
					{
						EdgeDefinitions =
						[
							new ArangoEdgeDefinition()
							{
								Collection = DatabaseConstants.AtLocation,
								To = [.. DatabaseConstants.verticesContainer],
								From = [.. DatabaseConstants.verticesContent]
							}
						],
						Name = DatabaseConstants.GraphLocations,
					},
					new()
					{
						EdgeDefinitions =
						[
							new ArangoEdgeDefinition()
							{
								Collection = DatabaseConstants.HasHome,
								To = [.. DatabaseConstants.verticesContainer],
								From = [.. DatabaseConstants.verticesContent]
							}
						],
						Name = DatabaseConstants.GraphHomes
					},
					new()
					{
						EdgeDefinitions =
						[
							new ArangoEdgeDefinition()
							{
								Collection = DatabaseConstants.HasExit,
								To = [
									DatabaseConstants.Exits,
								],
								From = [.. DatabaseConstants.verticesContainer]
							}
						],
						Name = DatabaseConstants.GraphExits
					},
					new()
					{
						EdgeDefinitions =
						[
							new ArangoEdgeDefinition()
							{
								Collection = DatabaseConstants.HasObjectOwner,
								To = [
									DatabaseConstants.Players
								],
								From = [
									DatabaseConstants.Objects
								]
							}
						],
						Name = DatabaseConstants.GraphObjectOwners
					},
					new()
					{
						EdgeDefinitions =
						[
							new ArangoEdgeDefinition()
							{
								Collection = DatabaseConstants.HasAttributeOwner,
								To = [
									DatabaseConstants.Players
								],
								From = [
									DatabaseConstants.Attributes
								]
							}
						],
						Name = DatabaseConstants.GraphAttributeOwners
					},
					new()
					{
						EdgeDefinitions =
						[
							new ArangoEdgeDefinition()
							{
								Collection = DatabaseConstants.HasParent,
								To = [
									DatabaseConstants.Objects
								],
								From = [
									DatabaseConstants.Objects
								]
							}
						],
						Name = DatabaseConstants.GraphParents
					},
					new()
					{
						EdgeDefinitions =
						[
							new ArangoEdgeDefinition()
							{
								Collection = DatabaseConstants.OwnerOfChannel,
								To = [
									DatabaseConstants.Players
								],
								From = [
									DatabaseConstants.Channels
								]
							},
							new ArangoEdgeDefinition()
							{
								Collection = DatabaseConstants.OnChannel,
								To = [
									DatabaseConstants.Channels
								],
								From = [
									DatabaseConstants.Objects
								]
							}
						],
						Name = DatabaseConstants.GraphChannels
					},
					new()
					{
						EdgeDefinitions =
						[
							new ArangoEdgeDefinition()
							{
								Collection = DatabaseConstants.ReceivedMail,
								To = [
									DatabaseConstants.Mails
								],
								From = [
									DatabaseConstants.Players
								]
							},
							new ArangoEdgeDefinition()
							{
								Collection = DatabaseConstants.SenderOfMail,
								To = [
									DatabaseConstants.Objects
								],
								From = [
									DatabaseConstants.Mails
								]
							}
						],
						Name = DatabaseConstants.GraphMail
					},
					new()
					{
						EdgeDefinitions =
						[
							new ArangoEdgeDefinition()
							{
								Collection = DatabaseConstants.HasZone,
								To = [
									DatabaseConstants.Objects
								],
								From = [
									DatabaseConstants.Objects
								]
							}
						],
						Name = DatabaseConstants.GraphZones
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
		var roomZeroObj = await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.Objects, new
		{
			_key = 0.ToString(),
			Name = "Room Zero",
			Type = DatabaseConstants.TypeRoom,
			CreationTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
			ModifiedTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
		});
		var roomZeroRoom = await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.Rooms, new { });

		/* Create Player One */
		var playerOneObj = await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.Objects, new
		{
			_key = 1.ToString(),
			Name = "God",
			Type = DatabaseConstants.TypePlayer,
			CreationTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
			ModifiedTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
		});

		/* Create Room Zero */
		var roomTwoObj = await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.Objects, new
		{
			_key = 2.ToString(),
			Name = "Master Room",
			Type = DatabaseConstants.TypeRoom,
			CreationTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
			ModifiedTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
		});
		var roomTwoRoom = await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.Rooms, new { });

		var playerOnePlayer = await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.Players, new
		{
			Aliases = Array.Empty<string>(),
			PasswordHash = string.Empty,
			Quota = 999999 // God has unlimited quota
		});

		var flags = await CreateInitialFlags(migrator, handle);
		var attributeFlags = await CreateInitialAttributeFlags(migrator, handle);
		var powers = await CreateInitialPowers(migrator, handle);
		var attributeEntries = await CreateInitialSharpAttributeEntries(migrator, handle);
		var wizard = flags[0];

		// Batch edge creation for better performance
		var isObjectEdges = new List<SharpEdge>
		{
			new SharpEdge { From = roomTwoRoom.Id, To = roomTwoObj.Id },
			new SharpEdge { From = roomZeroRoom.Id, To = roomZeroObj.Id },
			new SharpEdge { From = playerOnePlayer.Id, To = playerOneObj.Id }
		};
		await migrator.Context.Document.CreateManyAsync(handle, DatabaseConstants.IsObject, isObjectEdges);

		var locationEdges = new List<SharpEdge>
		{
			new SharpEdge { From = playerOnePlayer.Id, To = roomZeroRoom.Id }
		};
		await migrator.Context.Document.CreateManyAsync(handle, DatabaseConstants.AtLocation, locationEdges);

		var homeEdges = new List<SharpEdge>
		{
			new SharpEdge { From = playerOnePlayer.Id, To = roomZeroRoom.Id }
		};
		await migrator.Context.Document.CreateManyAsync(handle, DatabaseConstants.HasHome, homeEdges);

		var ownerEdges = new List<SharpEdge>
		{
			new SharpEdge { From = roomTwoObj.Id, To = playerOnePlayer.Id },
			new SharpEdge { From = roomZeroObj.Id, To = playerOnePlayer.Id },
			new SharpEdge { From = playerOneObj.Id, To = playerOnePlayer.Id }
		};
		await migrator.Context.Document.CreateManyAsync(handle, DatabaseConstants.HasObjectOwner, ownerEdges);

		var flagEdges = new List<SharpEdge>
		{
			new SharpEdge { From = playerOneObj.Id, To = wizard.Id }
		};
		await migrator.Context.Document.CreateManyAsync(handle, DatabaseConstants.HasFlags, flagEdges);
	}

	private static async Task<List<ArangoUpdateResult<ArangoVoid>>> CreateInitialSharpAttributeEntries(
IArangoMigrator migrator, ArangoHandle handle)
{
// Batch create all attribute entries for better performance
var attributeEntries = new object[]
{
			new
			{
				Name = "ABUY",
				DefaultFlags = (string[])["no_command","prefixmatch"]
			},
			new
			{
				Name = "ACLONE",
				DefaultFlags = (string[])["no_command","prefixmatch"]
			},
			new
			{
				Name = "ACONNECT",
				DefaultFlags = (string[])["no_command","prefixmatch"]
			},
			new
			{
				Name = "ADEATH",
				DefaultFlags = (string[])["no_command","prefixmatch"]
			},
			new
			{
				Name = "ADESCRIBE",
				DefaultFlags = (string[])["no_command","prefixmatch"]
			},
			new
			{
				Name = "ADESTROY",
				DefaultFlags = (string[])["no_inherit","no_clone","wizard","prefixmatch"]
			},
			new
			{
				Name = "ADISCONNECT",
				DefaultFlags = (string[])["no_command","prefixmatch"]
			},
			new
			{
				Name = "ADROP",
				DefaultFlags = (string[])["no_command","prefixmatch"]
			},
			new
			{
				Name = "AEFAIL",
				DefaultFlags = (string[])["no_command","prefixmatch"]
			},
			new
			{
				Name = "AENTER",
				DefaultFlags = (string[])["no_command","prefixmatch"]
			},
			new
			{
				Name = "AFAILURE",
				DefaultFlags = (string[])["no_command","prefixmatch"]
			},
			new
			{
				Name = "AFOLLOW",
				DefaultFlags = (string[])["no_command","prefixmatch"]
			},
			new
			{
				Name = "AGIVE",
				DefaultFlags = (string[])["no_command","prefixmatch"]
			},
			new
			{
				Name = "AHEAR",
				DefaultFlags = (string[])["no_command","prefixmatch"]
			},
			new
			{
				Name = "AIDESCRIBE",
				DefaultFlags = (string[])["no_command","prefixmatch"]
			},
			new
			{
				Name = "ALEAVE",
				DefaultFlags = (string[])["no_command","prefixmatch"]
			},
			new
			{
				Name = "ALFAIL",
				DefaultFlags = (string[])["no_command","prefixmatch"]
			},
			new
			{
				Name = "ALIAS",
				DefaultFlags = (string[])["no_command","visual","prefixmatch"]
			},
			new
			{
				Name = "AMAIL",
				DefaultFlags = (string[])["wizard","prefixmatch"]
			},
			new
			{
				Name = "AMHEAR",
				DefaultFlags = (string[])["no_command","prefixmatch"]
			},
			new
			{
				Name = "AMOVE",
				DefaultFlags = (string[])["no_command","prefixmatch"]
			},
			new
			{
				Name = "ANAME",
				DefaultFlags = (string[])["no_command","prefixmatch"]
			},
			new
			{
				Name = "APAYMENT",
				DefaultFlags = (string[])["no_command","prefixmatch"]
			},
			new
			{
				Name = "ARECEIVE",
				DefaultFlags = (string[])["no_command","prefixmatch"]
			},
			new
			{
				Name = "ASUCCESS",
				DefaultFlags = (string[])["no_command","prefixmatch"]
			},
			new
			{
				Name = "ATPORT",
				DefaultFlags = (string[])["no_command","prefixmatch"]
			},
			new
			{
				Name = "AUFAIL",
				DefaultFlags = (string[])["no_command","prefixmatch"]
			},
			new
			{
				Name = "AUNFOLLOW",
				DefaultFlags = (string[])["no_command","prefixmatch"]
			},
			new
			{
				Name = "AUSE",
				DefaultFlags = (string[])["no_command","prefixmatch"]
			},
			new
			{
				Name = "AWAY",
				DefaultFlags = (string[])["no_command","prefixmatch"]
			},
			new
			{
				Name = "AZENTER",
				DefaultFlags = (string[])["no_command","prefixmatch"]
			},
			new
			{
				Name = "AZLEAVE",
				DefaultFlags = (string[])["no_command","prefixmatch"]
			},
			new
			{
				Name = "BUY",
				DefaultFlags = (string[])["no_command","prefixmatch"]
			},
			new
			{
				Name = "CHANALIAS",
				DefaultFlags = (string[])["no_command"]
			},
			new
			{
				Name = "CHARGES",
				DefaultFlags = (string[])["no_command","prefixmatch"]
			},
			new
			{
				Name = "CHATFORMAT",
				DefaultFlags = (string[])["no_command","prefixmatch"]
			},
			new
			{
				Name = "COMMENT",
				DefaultFlags = (string[])["no_command","no_clone","wizard","mortal_dark","prefixmatch"]
			},
			new
			{
				Name = "CONFORMAT",
				DefaultFlags = (string[])["no_command","prefixmatch"]
			},
			new
			{
				Name = "COST",
				DefaultFlags = (string[])["no_command","prefixmatch"]
			},
			new
			{
				Name = "DEATH",
				DefaultFlags = (string[])["no_command","prefixmatch"]
			},
			new
			{
				Name = "DEBUGFORWARDLIST",
				DefaultFlags = (string[])["no_command","no_inherit","prefixmatch"]
			},
			new
			{
				Name = "DESCFORMAT",
				DefaultFlags = (string[])["no_command","prefixmatch"]
			},
			new
			{
				Name = "DESCRIBE",
				DefaultFlags = (string[])["no_command","visual","prefixmatch","public","nearby"]
			},
			new
			{
				Name = "DESTINATION",
				DefaultFlags = (string[])["no_command"]
			},
			new
			{
				Name = "DOING",
				DefaultFlags = (string[])["no_command","no_inherit","visual","public"]
			},
			new
			{
				Name = "DROP",
				DefaultFlags = (string[])["no_command","prefixmatch"]
			},
			new
			{
				Name = "EALIAS",
				DefaultFlags = (string[])["no_command","prefixmatch"]
			},
			new
			{
				Name = "EFAIL",
				DefaultFlags = (string[])["no_command","prefixmatch"]
			},
			new
			{
				Name = "ENTER",
				DefaultFlags = (string[])["no_command","prefixmatch"]
			},
			new
			{
				Name = "EXITFORMAT",
				DefaultFlags = (string[])["no_command","prefixmatch"]
			},
			new
			{
				Name = "EXITTO",
				DefaultFlags = (string[])["no_command","prefixmatch"]
			},
			new
			{
				Name = "FAILURE",
				DefaultFlags = (string[])["no_command","prefixmatch"]
			},
			new
			{
				Name = "FILTER",
				DefaultFlags = (string[])["no_command","prefixmatch"]
			},
			new
			{
				Name = "FOLLOW",
				DefaultFlags = (string[])["no_command","prefixmatch"]
			},
			new
			{
				Name = "FOLLOWERS",
				DefaultFlags = (string[])["no_command","no_inherit","no_clone","wizard","prefixmatch"]
			},
			new
			{
				Name = "FOLLOWING",
				DefaultFlags = (string[])["no_command","no_inherit","no_clone","wizard","prefixmatch"]
			},
			new
			{
				Name = "FORWARDLIST",
				DefaultFlags = (string[])["no_command","no_inherit","prefixmatch"]
			},
			new
			{
				Name = "GIVE",
				DefaultFlags = (string[])["no_command","prefixmatch"]
			},
			new
			{
				Name = "HAVEN",
				DefaultFlags = (string[])["no_command","prefixmatch"]
			},
			new
			{
				Name = "IDESCFORMAT",
				DefaultFlags = (string[])["no_command","prefixmatch"]
			},
			new
			{
				Name = "IDESCRIBE",
				DefaultFlags = (string[])["no_command","prefixmatch"]
			},
			new
			{
				Name = "IDLE",
				DefaultFlags = (string[])["no_command","prefixmatch"]
			},
			new
			{
				Name = "INFILTER",
				DefaultFlags = (string[])["no_command","prefixmatch"]
			},
			new
			{
				Name = "INPREFIX",
				DefaultFlags = (string[])["no_command","prefixmatch"]
			},
			new
			{
				Name = "INVFORMAT",
				DefaultFlags = (string[])["no_command","prefixmatch"]
			},
			new
			{
				Name = "LALIAS",
				DefaultFlags = (string[])["no_command","prefixmatch"]
			},
			new
			{
				Name = "LAST",
				DefaultFlags = (string[])["no_clone","wizard","visual","locked","prefixmatch"]
			},
			new
			{
				Name = "LASTFAILED",
				DefaultFlags = (string[])["no_clone","wizard","locked","prefixmatch"]
			},
			new
			{
				Name = "LASTIP",
				DefaultFlags = (string[])["no_clone","wizard","locked","prefixmatch"]
			},
			new
			{
				Name = "LASTLOGOUT",
				DefaultFlags = (string[])["no_clone","wizard","locked","prefixmatch"]
			},
			new
			{
				Name = "LASTPAGED",
				DefaultFlags = (string[])["no_clone","wizard","locked","prefixmatch"]
			},
			new
			{
				Name = "LASTSITE",
				DefaultFlags = (string[])["no_clone","wizard","locked","prefixmatch"]
			},
			new
			{
				Name = "LEAVE",
				DefaultFlags = (string[])["no_command","prefixmatch"]
			},
			new
			{
				Name = "LFAIL",
				DefaultFlags = (string[])["no_command","prefixmatch"]
			},
			new
			{
				Name = "LISTEN",
				DefaultFlags = (string[])["no_command","prefixmatch"]
			},
			new
			{
				Name = "MAILCURF",
				DefaultFlags = (string[])["no_command","no_clone","wizard","locked","prefixmatch"]
			},
			new
			{
				Name = "MAILFILTER",
				DefaultFlags = (string[])["no_command","prefixmatch"]
			},
			new
			{
				Name = "MAILFILTERS",
				DefaultFlags = (string[])["no_command","no_clone","wizard","locked","prefixmatch"]
			},
			new
			{
				Name = "MAILFOLDERS",
				DefaultFlags = (string[])["no_command","no_clone","wizard","locked","prefixmatch"]
			},
			new
			{
				Name = "MAILFORWARDLIST",
				DefaultFlags = (string[])["no_command","prefixmatch"]
			},
			new
			{
				Name = "MAILQUOTA",
				DefaultFlags = (string[])["no_command","no_clone","wizard","locked"]
			},
			new
			{
				Name = "MAILSIGNATURE",
				DefaultFlags = (string[])["no_command","prefixmatch"]
			},
			new
			{
				Name = "MONIKER",
				DefaultFlags = (string[])["no_command","wizard","visual","locked"]
			},
			new
			{
				Name = "MOVE",
				DefaultFlags = (string[])["no_command","prefixmatch"]
			},
			new
			{
				Name = "NAMEACCENT",
				DefaultFlags = (string[])["no_command","visual","prefixmatch"]
			},
			new
			{
				Name = "NAMEFORMAT",
				DefaultFlags = (string[])["no_command","prefixmatch"]
			},
			new
			{
				Name = "OBUY",
				DefaultFlags = (string[])["no_command","prefixmatch"]
			},
			new
			{
				Name = "ODEATH",
				DefaultFlags = (string[])["no_command","prefixmatch"]
			},
			new
			{
				Name = "ODESCRIBE",
				DefaultFlags = (string[])["no_command","prefixmatch"]
			},
			new
			{
				Name = "ODROP",
				DefaultFlags = (string[])["no_command","prefixmatch"]
			},
			new
			{
				Name = "OEFAIL",
				DefaultFlags = (string[])["no_command","prefixmatch"]
			},
			new
			{
				Name = "OENTER",
				DefaultFlags = (string[])["no_command","prefixmatch"]
			},
			new
			{
				Name = "OFAILURE",
				DefaultFlags = (string[])["no_command","prefixmatch"]
			},
			new
			{
				Name = "OFOLLOW",
				DefaultFlags = (string[])["no_command","prefixmatch"]
			},
			new
			{
				Name = "OGIVE",
				DefaultFlags = (string[])["no_command","prefixmatch"]
			},
			new
			{
				Name = "OIDESCRIBE",
				DefaultFlags = (string[])["no_command","prefixmatch"]
			},
			new
			{
				Name = "OLEAVE",
				DefaultFlags = (string[])["no_command","prefixmatch"]
			},
			new
			{
				Name = "OLFAIL",
				DefaultFlags = (string[])["no_command","prefixmatch"]
			},
			new
			{
				Name = "OMOVE",
				DefaultFlags = (string[])["no_command","prefixmatch"]
			},
			new
			{
				Name = "ONAME",
				DefaultFlags = (string[])["no_command","prefixmatch"]
			},
			new
			{
				Name = "OPAYMENT",
				DefaultFlags = (string[])["no_command","prefixmatch"]
			},
			new
			{
				Name = "ORECEIVE",
				DefaultFlags = (string[])["no_command","prefixmatch"]
			},
			new
			{
				Name = "OSUCCESS",
				DefaultFlags = (string[])["no_command","prefixmatch"]
			},
			new
			{
				Name = "OTPORT",
				DefaultFlags = (string[])["no_command","prefixmatch"]
			},
			new
			{
				Name = "OUFAIL",
				DefaultFlags = (string[])["no_command","prefixmatch"]
			},
			new
			{
				Name = "OUNFOLLOW",
				DefaultFlags = (string[])["no_command","prefixmatch"]
			},
			new
			{
				Name = "OUSE",
				DefaultFlags = (string[])["no_command","prefixmatch"]
			},
			new
			{
				Name = "OUTPAGEFORMAT",
				DefaultFlags = (string[])["no_command","prefixmatch"]
			},
			new
			{
				Name = "OXENTER",
				DefaultFlags = (string[])["no_command","prefixmatch"]
			},
			new
			{
				Name = "OXLEAVE",
				DefaultFlags = (string[])["no_command","prefixmatch"]
			},
			new
			{
				Name = "OXMOVE",
				DefaultFlags = (string[])["no_command","prefixmatch"]
			},
			new
			{
				Name = "OXTPORT",
				DefaultFlags = (string[])["no_command","prefixmatch"]
			},
			new
			{
				Name = "OZENTER",
				DefaultFlags = (string[])["no_command","prefixmatch"]
			},
			new
			{
				Name = "OZLEAVE",
				DefaultFlags = (string[])["no_command","prefixmatch"]
			},
			new
			{
				Name = "PAGEFORMAT",
				DefaultFlags = (string[])["no_command","prefixmatch"]
			},
			new
			{
				Name = "PAYMENT",
				DefaultFlags = (string[])["no_command","prefixmatch"]
			},
			new
			{
				Name = "PREFIX",
				DefaultFlags = (string[])["no_command","prefixmatch"]
			},
			new
			{
				Name = "PRICELIST",
				DefaultFlags = (string[])["no_command","prefixmatch"]
			},
			new
			{
				Name = "QUEUE",
				DefaultFlags = (string[])["no_inherit","no_clone","wizard"]
			},
			new
			{
				Name = "RECEIVE",
				DefaultFlags = (string[])["no_command","prefixmatch"]
			},
			new
			{
				Name = "REGISTERED_EMAIL",
				DefaultFlags = (string[])["no_inherit","no_clone","wizard","locked"]
			},
			new
			{
				Name = "RQUOTA",
				DefaultFlags = (string[])["mortal_dark","locked"]
			},
			new
			{
				Name = "RUNOUT",
				DefaultFlags = (string[])["no_command","prefixmatch"]
			},
			new
			{
				Name = "SEMAPHORE",
				DefaultFlags = (string[])["no_inherit","no_clone","locked"]
			},
			new
			{
				Name = "SEX",
				DefaultFlags = (string[])["no_command","visual","prefixmatch"]
			},
			new
			{
				Name = "SPEECHMOD",
				DefaultFlags = (string[])["no_command","prefixmatch"]
			},
			new
			{
				Name = "STARTUP",
				DefaultFlags = (string[])["no_command","prefixmatch"]
			},
			new
			{
				Name = "SUCCESS",
				DefaultFlags = (string[])["no_command","prefixmatch"]
			},
			new
			{
				Name = "TFPREFIX",
				DefaultFlags = (string[])["no_command","no_inherit","no_clone","prefixmatch"]
			},
			new
			{
				Name = "TPORT",
				DefaultFlags = (string[])["no_command","prefixmatch"]
			},
			new
			{
				Name = "TZ",
				DefaultFlags = (string[])["no_command","visual"]
			},
			new
			{
				Name = "UFAIL",
				DefaultFlags = (string[])["no_command","prefixmatch"]
			},
			new
			{
				Name = "UNFOLLOW",
				DefaultFlags = (string[])["no_command","prefixmatch"]
			},
			new
			{
				Name = "USE",
				DefaultFlags = (string[])["no_command","prefixmatch"]
			},
			new
			{
				Name = "VA",
				DefaultFlags = (string[])[]
			},
			new
			{
				Name = "VB",
				DefaultFlags = (string[])[]
			},
			new
			{
				Name = "VC",
				DefaultFlags = (string[])[]
			},
			new
			{
				Name = "VD",
				DefaultFlags = (string[])[]
			},
			new
			{
				Name = "VE",
				DefaultFlags = (string[])[]
			},
			new
			{
				Name = "VF",
				DefaultFlags = (string[])[]
			},
			new
			{
				Name = "VG",
				DefaultFlags = (string[])[]
			},
			new
			{
				Name = "VH",
				DefaultFlags = (string[])[]
			},
			new
			{
				Name = "VI",
				DefaultFlags = (string[])[]
			},
			new
			{
				Name = "VJ",
				DefaultFlags = (string[])[]
			},
			new
			{
				Name = "VK",
				DefaultFlags = (string[])[]
			},
			new
			{
				Name = "VL",
				DefaultFlags = (string[])[]
			},
			new
			{
				Name = "VM",
				DefaultFlags = (string[])[]
			},
			new
			{
				Name = "VN",
				DefaultFlags = (string[])[]
			},
			new
			{
				Name = "VO",
				DefaultFlags = (string[])[]
			},
			new
			{
				Name = "VP",
				DefaultFlags = (string[])[]
			},
			new
			{
				Name = "VQ",
				DefaultFlags = (string[])[]
			},
			new
			{
				Name = "VR",
				DefaultFlags = (string[])[]
			},
			new
			{
				Name = "VRML_URL",
				DefaultFlags = (string[])["no_command","prefixmatch"]
			},
			new
			{
				Name = "VS",
				DefaultFlags = (string[])[]
			},
			new
			{
				Name = "VT",
				DefaultFlags = (string[])[]
			},
			new
			{
				Name = "VU",
				DefaultFlags = (string[])[]
			},
			new
			{
				Name = "VV",
				DefaultFlags = (string[])[]
			},
			new
			{
				Name = "VW",
				DefaultFlags = (string[])[]
			},
			new
			{
				Name = "VX",
				DefaultFlags = (string[])[]
			},
			new
			{
				Name = "VY",
				DefaultFlags = (string[])[]
			},
			new
			{
				Name = "VZ",
				DefaultFlags = (string[])[]
			},
			new
			{
				Name = "WA",
				DefaultFlags = (string[])[]
			},
			new
			{
				Name = "WB",
				DefaultFlags = (string[])[]
			},
			new
			{
				Name = "WC",
				DefaultFlags = (string[])[]
			},
			new
			{
				Name = "WD",
				DefaultFlags = (string[])[]
			},
			new
			{
				Name = "WE",
				DefaultFlags = (string[])[]
			},
			new
			{
				Name = "WF",
				DefaultFlags = (string[])[]
			},
			new
			{
				Name = "WG",
				DefaultFlags = (string[])[]
			},
			new
			{
				Name = "WH",
				DefaultFlags = (string[])[]
			},
			new
			{
				Name = "WI",
				DefaultFlags = (string[])[]
			},
			new
			{
				Name = "WJ",
				DefaultFlags = (string[])[]
			},
			new
			{
				Name = "WK",
				DefaultFlags = (string[])[]
			},
			new
			{
				Name = "WL",
				DefaultFlags = (string[])[]
			},
			new
			{
				Name = "WM",
				DefaultFlags = (string[])[]
			},
			new
			{
				Name = "WN",
				DefaultFlags = (string[])[]
			},
			new
			{
				Name = "WO",
				DefaultFlags = (string[])[]
			},
			new
			{
				Name = "WP",
				DefaultFlags = (string[])[]
			},
			new
			{
				Name = "WQ",
				DefaultFlags = (string[])[]
			},
			new
			{
				Name = "WR",
				DefaultFlags = (string[])[]
			},
			new
			{
				Name = "WS",
				DefaultFlags = (string[])[]
			},
			new
			{
				Name = "WT",
				DefaultFlags = (string[])[]
			},
			new
			{
				Name = "WU",
				DefaultFlags = (string[])[]
			},
			new
			{
				Name = "WV",
				DefaultFlags = (string[])[]
			},
			new
			{
				Name = "WW",
				DefaultFlags = (string[])[]
			},
			new
			{
				Name = "WX",
				DefaultFlags = (string[])[]
			},
			new
			{
				Name = "WY",
				DefaultFlags = (string[])[]
			},
			new
			{
				Name = "WZ",
				DefaultFlags = (string[])[]
			},
			new
			{
				Name = "XA",
				DefaultFlags = (string[])[]
			},
			new
			{
				Name = "XB",
				DefaultFlags = (string[])[]
			},
			new
			{
				Name = "XC",
				DefaultFlags = (string[])[]
			},
			new
			{
				Name = "XD",
				DefaultFlags = (string[])[]
			},
			new
			{
				Name = "XE",
				DefaultFlags = (string[])[]
			},
			new
			{
				Name = "XF",
				DefaultFlags = (string[])[]
			},
			new
			{
				Name = "XG",
				DefaultFlags = (string[])[]
			},
			new
			{
				Name = "XH",
				DefaultFlags = (string[])[]
			},
			new
			{
				Name = "XI",
				DefaultFlags = (string[])[]
			},
			new
			{
				Name = "XJ",
				DefaultFlags = (string[])[]
			},
			new
			{
				Name = "XK",
				DefaultFlags = (string[])[]
			},
			new
			{
				Name = "XL",
				DefaultFlags = (string[])[]
			},
			new
			{
				Name = "XM",
				DefaultFlags = (string[])[]
			},
			new
			{
				Name = "XN",
				DefaultFlags = (string[])[]
			},
			new
			{
				Name = "XO",
				DefaultFlags = (string[])[]
			},
			new
			{
				Name = "XP",
				DefaultFlags = (string[])[]
			},
			new
			{
				Name = "XQ",
				DefaultFlags = (string[])[]
			},
			new
			{
				Name = "XR",
				DefaultFlags = (string[])[]
			},
			new
			{
				Name = "XS",
				DefaultFlags = (string[])[]
			},
			new
			{
				Name = "XT",
				DefaultFlags = (string[])[]
			},
			new
			{
				Name = "XU",
				DefaultFlags = (string[])[]
			},
			new
			{
				Name = "XV",
				DefaultFlags = (string[])[]
			},
			new
			{
				Name = "XW",
				DefaultFlags = (string[])[]
			},
			new
			{
				Name = "XX",
				DefaultFlags = (string[])[]
			},
			new
			{
				Name = "XY",
				DefaultFlags = (string[])[]
			},
			new
			{
				Name = "XZ",
				DefaultFlags = (string[])[]
			},
			new
			{
				Name = "ZENTER",
				DefaultFlags = (string[])["no_command","prefixmatch"]
			}
};

var results = await migrator.Context.Document.CreateManyAsync(
handle, DatabaseConstants.AttributeEntries, attributeEntries);
return results.ToList();
}
	
	private static async Task<List<ArangoUpdateResult<ArangoVoid>>> CreateInitialPowers(IArangoMigrator migrator,
ArangoHandle handle)
{
// Batch create all powers for better performance
var powers = new object[]
{
			new
			{Name = "Announce",
				System = true,
				TypeRestrictions = DatabaseConstants.typesAll,
				SetPermissions = DatabaseConstants.permissionsWizard
					.Union(DatabaseConstants.permissionsLog),
				UnsetPermissions = DatabaseConstants.permissionsWizard},
			new
			{Name = "Boot",
				System = true,
				TypeRestrictions = DatabaseConstants.typesAll,
				SetPermissions = DatabaseConstants.permissionsWizard
					.Union(DatabaseConstants.permissionsLog),
				UnsetPermissions = DatabaseConstants.permissionsWizard},
			new
			{Name = "Builder",
				System = true,
				TypeRestrictions = DatabaseConstants.typesAll,
				SetPermissions = DatabaseConstants.permissionsWizard
					.Union(DatabaseConstants.permissionsLog),
				UnsetPermissions = DatabaseConstants.permissionsWizard},
			new
			{Name = "Can_Dark",
				System = true,
				TypeRestrictions = DatabaseConstants.typesPlayer,
				SetPermissions = DatabaseConstants.permissionsWizard
					.Union(DatabaseConstants.permissionsLog)},
			new
			{Name = "Can_HTTP",
				System = true,
				TypeRestrictions = DatabaseConstants.typesAll,
				SetPermissions = DatabaseConstants.permissionsWizard
					.Union(DatabaseConstants.permissionsLog)},
			new
			{Name = "Can_Spoof",
				System = true,
				Aliases = (string[])["Can_nspemit"],
				TypeRestrictions = DatabaseConstants.typesAll,
				SetPermissions = DatabaseConstants.permissionsWizard
					.Union(DatabaseConstants.permissionsLog),
				UnsetPermissions = DatabaseConstants.permissionsWizard},
			new
			{Name = "Chat_Privs",
				System = true,
				Aliases = (string[])["Can_nspemit"],
				TypeRestrictions = DatabaseConstants.typesAll,
				SetPermissions = DatabaseConstants.permissionsWizard
					.Union(DatabaseConstants.permissionsLog),
				UnsetPermissions = DatabaseConstants.permissionsWizard},
			new
			{Name = "Debit",
				System = true,
				Aliases = (string[])["Steal_Money"],
				TypeRestrictions = DatabaseConstants.typesAll,
				SetPermissions = DatabaseConstants.permissionsWizard
					.Union(DatabaseConstants.permissionsLog),},
			new
			{Name = "Functions",
				System = true,
				TypeRestrictions = DatabaseConstants.typesAll,
				SetPermissions = DatabaseConstants.permissionsWizard
					.Union(DatabaseConstants.permissionsLog),
				UnsetPermissions = DatabaseConstants.permissionsWizard},
			new
			{Name = "Guest",
				System = true,
				TypeRestrictions = DatabaseConstants.typesAll,
				SetPermissions = DatabaseConstants.permissionsWizard
					.Union(DatabaseConstants.permissionsLog),
				UnsetPermissions = DatabaseConstants.permissionsWizard},
			new
			{Name = "Halt",
				System = true,
				TypeRestrictions = DatabaseConstants.typesAll,
				SetPermissions = DatabaseConstants.permissionsWizard
					.Union(DatabaseConstants.permissionsLog),
				UnsetPermissions = DatabaseConstants.permissionsWizard},
			new
			{Name = "Hide",
				System = true,
				TypeRestrictions = DatabaseConstants.typesAll,
				SetPermissions = DatabaseConstants.permissionsWizard
					.Union(DatabaseConstants.permissionsLog),
				UnsetPermissions = DatabaseConstants.permissionsWizard},
			new
			{Name = "Hook",
				System = true,
				TypeRestrictions = DatabaseConstants.typesAll,
				SetPermissions = DatabaseConstants.permissionsWizard
					.Union(DatabaseConstants.permissionsLog)},
			new
			{Name = "Idle",
				System = true,
				TypeRestrictions = DatabaseConstants.typesAll,
				SetPermissions = DatabaseConstants.permissionsWizard
					.Union(DatabaseConstants.permissionsLog),
				UnsetPermissions = DatabaseConstants.permissionsWizard},
			new
			{Name = "Immortal",
				System = true,
				TypeRestrictions = DatabaseConstants.typesAll,
				SetPermissions = DatabaseConstants.permissionsWizard
					.Union(DatabaseConstants.permissionsLog),
				UnsetPermissions = DatabaseConstants.permissionsWizard},
			new
			{Name = "Link_Anywhere",
				System = true,
				TypeRestrictions = DatabaseConstants.typesAll,
				SetPermissions = DatabaseConstants.permissionsWizard
					.Union(DatabaseConstants.permissionsLog),
				UnsetPermissions = DatabaseConstants.permissionsWizard},
			new
			{Name = "Login",
				System = true,
				TypeRestrictions = DatabaseConstants.typesAll,
				SetPermissions = DatabaseConstants.permissionsWizard
					.Union(DatabaseConstants.permissionsLog),
				UnsetPermissions = DatabaseConstants.permissionsWizard},
			new
			{Name = "Long_Fingers",
				System = true,
				TypeRestrictions = DatabaseConstants.typesAll,
				SetPermissions = DatabaseConstants.permissionsWizard
					.Union(DatabaseConstants.permissionsLog),
				UnsetPermissions = DatabaseConstants.permissionsWizard},
			new
			{Name = "Many_Attribs",
				System = true,
				TypeRestrictions = DatabaseConstants.typesAll,
				SetPermissions = DatabaseConstants.permissionsWizard
					.Union(DatabaseConstants.permissionsLog)},
			new
			{Name = "No_Pay",
				System = true,
				TypeRestrictions = DatabaseConstants.typesAll,
				SetPermissions = DatabaseConstants.permissionsWizard
					.Union(DatabaseConstants.permissionsLog),
				UnsetPermissions = DatabaseConstants.permissionsWizard},
			new
			{Name = "No_Quota",
				System = true,
				Aliases = (string[])["Free_Quota"],
				TypeRestrictions = DatabaseConstants.typesAll,
				SetPermissions = DatabaseConstants.permissionsWizard
					.Union(DatabaseConstants.permissionsLog),
				UnsetPermissions = DatabaseConstants.permissionsWizard},
			new
			{Name = "Open_Anywhere",
				System = true,
				TypeRestrictions = DatabaseConstants.typesAll,
				SetPermissions = DatabaseConstants.permissionsWizard
					.Union(DatabaseConstants.permissionsLog),
				UnsetPermissions = DatabaseConstants.permissionsWizard},
			new
			{Name = "Pemit_All",
				System = true,
				TypeRestrictions = DatabaseConstants.typesAll,
				SetPermissions = DatabaseConstants.permissionsWizard
					.Union(DatabaseConstants.permissionsLog),
				UnsetPermissions = DatabaseConstants.permissionsWizard},
			new
			{Name = "Pick_DBRefs",
				System = true,
				TypeRestrictions = DatabaseConstants.typesAll,
				SetPermissions = DatabaseConstants.permissionsWizard
					.Union(DatabaseConstants.permissionsLog),
				UnsetPermissions = DatabaseConstants.permissionsWizard},
			new
			{Name = "Player_Create",
				System = true,
				TypeRestrictions = DatabaseConstants.typesAll,
				SetPermissions = DatabaseConstants.permissionsWizard
					.Union(DatabaseConstants.permissionsLog),
				UnsetPermissions = DatabaseConstants.permissionsWizard},
			new
			{Name = "Poll",
				System = true,
				TypeRestrictions = DatabaseConstants.typesAll,
				SetPermissions = DatabaseConstants.permissionsWizard
					.Union(DatabaseConstants.permissionsLog),
				UnsetPermissions = DatabaseConstants.permissionsWizard},
			new
			{Name = "Pueblo_Send",
				System = true,
				TypeRestrictions = DatabaseConstants.typesAll,
				SetPermissions = DatabaseConstants.permissionsWizard
					.Union(DatabaseConstants.permissionsLog),
				UnsetPermissions = DatabaseConstants.permissionsWizard},
			new
			{Name = "Queue",
				System = true,
				TypeRestrictions = DatabaseConstants.typesAll,
				SetPermissions = DatabaseConstants.permissionsWizard
					.Union(DatabaseConstants.permissionsLog),
				UnsetPermissions = DatabaseConstants.permissionsWizard},
			new
			{Name = "Search",
				System = true,
				TypeRestrictions = DatabaseConstants.typesAll,
				SetPermissions = DatabaseConstants.permissionsWizard
					.Union(DatabaseConstants.permissionsLog),
				UnsetPermissions = DatabaseConstants.permissionsWizard},
			new
			{Name = "See_All",
				System = true,
				TypeRestrictions = DatabaseConstants.typesAll,
				SetPermissions = DatabaseConstants.permissionsWizard
					.Union(DatabaseConstants.permissionsLog),
				UnsetPermissions = DatabaseConstants.permissionsWizard},
			new
			{Name = "See_Queue",
				System = true,
				TypeRestrictions = DatabaseConstants.typesAll,
				SetPermissions = DatabaseConstants.permissionsWizard
					.Union(DatabaseConstants.permissionsLog),
				UnsetPermissions = DatabaseConstants.permissionsWizard},
			new
			{Name = "See_OOB",
				System = true,
				TypeRestrictions = DatabaseConstants.typesAll,
				SetPermissions = DatabaseConstants.permissionsWizard
					.Union(DatabaseConstants.permissionsLog),
				UnsetPermissions = DatabaseConstants.permissionsWizard},
			new
			{Name = "SQL_OK",
				System = true,
				TypeRestrictions = DatabaseConstants.typesAll,
				SetPermissions = DatabaseConstants.permissionsWizard
					.Union(DatabaseConstants.permissionsLog),
				UnsetPermissions = DatabaseConstants.permissionsWizard},
			new
			{Name = "Tport_Anything",
				System = true,
				Aliases = (string[])["tel_anything"],
				TypeRestrictions = DatabaseConstants.typesAll,
				SetPermissions = DatabaseConstants.permissionsWizard
					.Union(DatabaseConstants.permissionsLog),
				UnsetPermissions = DatabaseConstants.permissionsWizard},
			new
			{Name = "Tport_Anywhere",
				System = true,
				Aliases = (string[])["tel_anywhere"],
				TypeRestrictions = DatabaseConstants.typesAll,
				SetPermissions = DatabaseConstants.permissionsWizard
					.Union(DatabaseConstants.permissionsLog),
				UnsetPermissions = DatabaseConstants.permissionsWizard}
};

var results = await migrator.Context.Document.CreateManyAsync(
handle, DatabaseConstants.ObjectPowers, powers);
return results.ToList();
}

		private async static Task<List<ArangoUpdateResult<ArangoVoid>>> CreateInitialAttributeFlags(IArangoMigrator migrator,
ArangoHandle handle)
{
// Batch create all attribute flags for better performance
var attributeFlags = new object[]
{
			new
			{
				Name = "no_command",
				Symbol = "$",
				System = true,
				Inheritable = true
			},
			new
			{
				Name = "no_inherit",
				Symbol = "i",
				System = true,
				Inheritable = true
			},
			new
			{
				Name = "no_clone",
				Symbol = "c",
				System = true,
				Inheritable = true
			},
			new
			{
				Name = "mortal_dark",
				Symbol = "m",
				System = true,
				Inheritable = true
			},
			new
			{
				Name = "wizard",
				Symbol = "w",
				System = true,
				Inheritable = true
			},
			new
			{
				Name = "veiled",
				Symbol = "V",
				System = true,
				Inheritable = true
			},
			new
			{
				Name = "nearby",
				Symbol = "n",
				System = true,
				Inheritable = true
			},
			new
			{
				Name = "locked",
				Symbol = "+",
				System = true,
				Inheritable = true
			},
			new
			{
				Name = "safe",
				Symbol = "S",
				System = true,
				Inheritable = true
			},
			new
			{
				Name = "visual",
				Symbol = "v",
				System = true,
				Inheritable = false
			},
			new
			{
				Name = "public",
				Symbol = "p",
				System = true,
				Inheritable = false
			},
			new
			{
				Name = "debug",
				Symbol = "b",
				System = true,
				Inheritable = true
			},
			new
			{
				Name = "no_debug",
				Symbol = "B",
				System = true,
				Inheritable = true
			},
			new
			{
				Name = "regexp",
				Symbol = "R",
				System = true,
				Inheritable = false
			},
			new
			{
				Name = "case",
				Symbol = "C",
				System = true,
				Inheritable = false
			},
			new
			{
				Name = "nospace",
				Symbol = "s",
				System = true,
				Inheritable = true
			},
			new
			{
				Name = "noname",
				Symbol = "N",
				System = true,
				Inheritable = true
			},
			new
			{
				Name = "aahear",
				Symbol = "A",
				System = true,
				Inheritable = false
			},
			new
			{
				Name = "amhear",
				Symbol = "M",
				System = true,
				Inheritable = false
			},
			new
			{
				Name = "quiet",
				Symbol = "Q",
				System = true,
				Inheritable = false
			}
};

var results = await migrator.Context.Document.CreateManyAsync(
handle, DatabaseConstants.AttributeFlags, attributeFlags);
return results.ToList();
}

		// Todo: Find a better way of doing this, so we can keep a proper async flow.
	private static async Task<List<ArangoUpdateResult<ArangoVoid>>> CreateInitialFlags(IArangoMigrator migrator,
		ArangoHandle handle)
	{
		// Batch create all flags for better performance
		var flags = new object[]
		{
			new
			{Name = "WIZARD",
			Symbol = "W",
			System = true,
			TypeRestrictions = DatabaseConstants.typesAll,
			SetPermissions = DatabaseConstants.permissionsTrusted
				.Union(DatabaseConstants.permissionsWizard)
				.Union(DatabaseConstants.permissionsLog),
			UnsetPermissions = DatabaseConstants.permissionsTrusted
				.Union(DatabaseConstants.permissionsWizard),},
			new
			{Name = "ABODE",
			Symbol = "A",
			System = true,
			TypeRestrictions = DatabaseConstants.typesRoom},
			new
			{Name = "ANSI",
			Symbol = "A",
			System = true,
			TypeRestrictions = DatabaseConstants.typesPlayer},
			new
			{Name = "CHOWN_OK",
			Symbol = "C",
			System = true,
			TypeRestrictions = DatabaseConstants.typesContainer},
			new
			{Name = "DARK",
			Symbol = "D",
			System = true,
			TypeRestrictions = DatabaseConstants.typesAll},
			new
			{Name = "FIXED",
			Symbol = "F",
			System = true,
			SetPermissions = DatabaseConstants.permissionsWizard,
			UnsetPermissions = DatabaseConstants.permissionsWizard,
			TypeRestrictions = DatabaseConstants.typesPlayer},
			new
			{Name = "FLOATING",
			Symbol = "F",
			System = true,
			TypeRestrictions = DatabaseConstants.typesRoom},
			new
			{Name = "HAVEN",
			Symbol = "H",
			System = true,
			TypeRestrictions = DatabaseConstants.typesPlayer},
			new
			{Name = "TRUST",
			Symbol = "I",
			System = true,
			Aliases = (string[])["INHERIT"],
			SetPermissions = DatabaseConstants.permissionsTrusted,
			UnsetPermissions = DatabaseConstants.permissionsTrusted,
			TypeRestrictions = DatabaseConstants.typesAll},
			new
			{Name = "JUDGE",
			Symbol = "J",
			System = true,
			SetPermissions = DatabaseConstants.permissionsRoyalty,
			UnsetPermissions = DatabaseConstants.permissionsRoyalty,
			TypeRestrictions = DatabaseConstants.typesPlayer},
			new
			{Name = "JUMP_OK",
			Symbol = "J",
			System = true,
			Aliases = (string[])["TEL-OK", "TEL_OK", "TELOK"],
			TypeRestrictions = DatabaseConstants.typesRoom},
			new
			{Name = "LINK_OK",
			Symbol = "L",
			System = true,
			TypeRestrictions = DatabaseConstants.typesAll},
			new
			{Name = "MONITOR",
			Symbol = "M",
			System = true,
			Aliases = (string[])["LISTENER", "WATCHER"],
			TypeRestrictions = DatabaseConstants.typesContainer},
			new
			{Name = "NO_LEAVE",
			Symbol = "N",
			System = true,
			Aliases = (string[])["NOLEAVE"],
			TypeRestrictions = DatabaseConstants.typesThing},
			new
			{Name = "NO_TEL",
			Symbol = "N",
			System = true,
			TypeRestrictions = DatabaseConstants.typesRoom},
			new
			{Name = "OPAQUE",
			Symbol = "O",
			System = true,
			TypeRestrictions = DatabaseConstants.typesAll},
			new
			{Name = "QUIET",
			Symbol = "Q",
			System = true,
			TypeRestrictions = DatabaseConstants.typesAll},
			new
			{Name = "UNFINDABLE",
			Symbol = "U",
			System = true,
			TypeRestrictions = DatabaseConstants.typesAll},
			new
			{Name = "VISUAL",
			Symbol = "V",
			System = true,
			TypeRestrictions = DatabaseConstants.typesAll},
			new
			{Name = "SAFE",
			Symbol = "X",
			System = true,
			TypeRestrictions = DatabaseConstants.typesAll},
			new
			{Name = "SHARED",
			Symbol = "Z",
			System = true,
			Aliases = (string[])["ZONE"],
			TypeRestrictions = DatabaseConstants.typesPlayer},
			new
			{Name = "Z_TEL",
			Symbol = "Z",
			System = true,
			TypeRestrictions = DatabaseConstants.typesRoom
				.Union(DatabaseConstants.typesThing)},
			new
			{Name = "LISTEN_PARENT",
			Symbol = "^",
			Aliases = (string[])["^"],
			System = true,
			TypeRestrictions = DatabaseConstants.typesPlayer
				.Union(DatabaseConstants.typesThing).Union(DatabaseConstants.typesRoom)},
			new
			{Name = "NOACCENTS",
			Symbol = "~",
			System = true,
			TypeRestrictions = DatabaseConstants.typesPlayer
				.Union(DatabaseConstants.typesThing).Union(DatabaseConstants.typesRoom)},
			new
			{Name = "UNREGISTERED",
			Symbol = "?",
			System = true,
			TypeRestrictions = DatabaseConstants.typesPlayer,
			SetPermissions = DatabaseConstants.permissionsRoyalty,
			UnsetPermissions = DatabaseConstants.permissionsRoyalty},
			new
			{Name = "NOSPOOF",
			Symbol = "\"",
			System = true,
			TypeRestrictions = DatabaseConstants.typesAll,
			SetPermissions = DatabaseConstants.permissionsODark,
			UnSetPermissions = DatabaseConstants.permissionsODark},
			new
			{Name = "AUDIBLE",
			Symbol = "a",
			System = true,
			TypeRestrictions = DatabaseConstants.typesAll},
			new
			{Name = "DEBUG",
			Aliases = (string[])["TRACE"],
			Symbol = "b",
			System = true,
			TypeRestrictions = DatabaseConstants.typesAll},
			new
			{Name = "DESTROY_OK",
			Aliases = (string[])["DEST_OK"],
			Symbol = "d",
			System = true,
			TypeRestrictions = DatabaseConstants.typesThing},
			new
			{Name = "ENTER_OK",
			Symbol = "e",
			System = true,
			TypeRestrictions = DatabaseConstants.typesAll},
			new
			{Name = "GAGGED",
			Symbol = "g",
			System = true,
			TypeRestrictions = DatabaseConstants.typesPlayer,
			SetPermissions = DatabaseConstants.permissionsWizard,
			UnSetPermissions = DatabaseConstants.permissionsWizard},
			new
			{Name = "HALT",
			Symbol = "h",
			System = true,
			TypeRestrictions = DatabaseConstants.typesAll},
			new
			{Name = "ORPHAN",
			Symbol = "i",
			System = true,
			TypeRestrictions = DatabaseConstants.typesAll},
			new
			{Name = "JURY_OK",
			Aliases = (string[])["JURYOK"],
			Symbol = "j",
			System = true,
			TypeRestrictions = DatabaseConstants.typesPlayer,
			SetPermissions = DatabaseConstants.permissionsRoyalty,
			UnSetPermissions = DatabaseConstants.permissionsRoyalty},
			new
			{Name = "KEEPALIVE",
			Symbol = "k",
			System = true,
			TypeRestrictions = DatabaseConstants.typesPlayer},
			new
			{Name = "LIGHT",
			Symbol = "l",
			System = true,
			TypeRestrictions = DatabaseConstants.typesAll},
			new
			{Name = "MISTRUST",
			Symbol = "m",
			System = true,
			TypeRestrictions = DatabaseConstants.typesContent,
			SetPermissions = DatabaseConstants.permissionsTrusted,
			UnSetPermissions = DatabaseConstants.permissionsTrusted},
			new
			{Name = "MISTRUST",
			Aliases = (string[])["MYOPIC"],
			Symbol = "m",
			System = true,
			TypeRestrictions = DatabaseConstants.typesContent,
			SetPermissions = DatabaseConstants.permissionsTrusted,
			UnSetPermissions = DatabaseConstants.permissionsTrusted},
			new
			{Name = "NO_COMMAND",
			Aliases = (string[])["NOCOMMAND"],
			Symbol = "n",
			System = true,
			TypeRestrictions = DatabaseConstants.typesAll},
			new
			{Name = "ON_VACATION",
			Aliases = (string[])["ONVACATION","ON-VACATION"],
			Symbol = "o",
			System = true,
			TypeRestrictions = DatabaseConstants.typesPlayer},
			new
			{Name = "PUPPET",
			Symbol = "P",
			System = true,
			TypeRestrictions = DatabaseConstants.typesThing
				.Union(DatabaseConstants.typesRoom)},
			new
			{Name = "ROYALTY",
			Symbol = "r",
			System = true,
			TypeRestrictions = DatabaseConstants.typesAll,
			SetPermissions = DatabaseConstants.permissionsTrusted
				.Union(DatabaseConstants.permissionsRoyalty)
				.Union(DatabaseConstants.permissionsLog),
			UnSetPermissions = DatabaseConstants.permissionsTrusted
				.Union(DatabaseConstants.permissionsRoyalty)},
			new
			{Name = "SUSPECT",
			Symbol = "s",
			System = true,
			TypeRestrictions = DatabaseConstants.typesAll,
			SetPermissions = DatabaseConstants.permissionsWizard
				.Union(DatabaseConstants.permissionsMDark)
				.Union(DatabaseConstants.permissionsLog),
			UnSetPermissions = DatabaseConstants.permissionsWizard
				.Union(DatabaseConstants.permissionsMDark)},
			new
			{Name = "TRANSPARENT",
			Symbol = "t",
			System = true,
			TypeRestrictions = DatabaseConstants.typesAll},
			new
			{Name = "VERBOSE",
			Symbol = "v",
			System = true,
			TypeRestrictions = DatabaseConstants.typesAll,},
			new
			{Name = "NO_WARN",
			Aliases = (string[])["NOWARN"],
			Symbol = "w",
			System = true,
			TypeRestrictions = DatabaseConstants.typesAll,},
			new
			{Name = "CLOUDY",
			Aliases = (string[])["TERSE"],
			Symbol = "x",
			System = true,
			TypeRestrictions = DatabaseConstants.typesExit,},
			new
			{Name = "CHAN_USEFIRSTMATCH",
			Aliases = (string[])["CHAN_FIRSTMATCH","CHAN_MATCHFIRST"],
			System = true,
			TypeRestrictions = DatabaseConstants.typesAll,
			SetPermissions = DatabaseConstants.permissionsTrusted,
			UnSetPermissions = DatabaseConstants.permissionsTrusted},
			new
			{Name = "HEAR_CONNECT",
			System = true,
			TypeRestrictions = DatabaseConstants.typesPlayer,
			SetPermissions = DatabaseConstants.permissionsRoyalty},
			new
			{Name = "HEAVY",
			System = true,
			TypeRestrictions = DatabaseConstants.typesAll,
			SetPermissions = DatabaseConstants.permissionsRoyalty},
			new
			{Name = "LOUD",
			System = true,
			TypeRestrictions = DatabaseConstants.typesPlayer,
			SetPermissions = DatabaseConstants.permissionsRoyalty},
			new
			{Name = "NO_LOG",
			System = true,
			TypeRestrictions = DatabaseConstants.typesPlayer,
			SetPermissions = DatabaseConstants.permissionsWizard
				.Union(DatabaseConstants.permissionsMDark)
				.Union(DatabaseConstants.permissionsLog),
			UnSetPermissions = DatabaseConstants.permissionsWizard
				.Union(DatabaseConstants.permissionsMDark)},
			new
			{Name = "PARANOID",
			System = true,
			TypeRestrictions = DatabaseConstants.typesPlayer,
			SetPermissions = DatabaseConstants.permissionsODark,
			UnSetPermissions = DatabaseConstants.permissionsODark},
			new
			{Name = "TRACK_MONEY",
			System = true,
			TypeRestrictions = DatabaseConstants.typesPlayer,},
			new
			{Name = "XTERM256",
			Aliases = (string[])["XTERM","COLOR256"],
			System = true,
			TypeRestrictions = DatabaseConstants.typesPlayer},
			new
			{Name = "MONIKER",
			System = true,
			TypeRestrictions = DatabaseConstants.typesAll,
			SetPermissions = DatabaseConstants.permissionsRoyalty,
			UnSetPermissions = DatabaseConstants.permissionsRoyalty},
			new
			{Name = "OPEN_OK",
			System = true,
			TypeRestrictions = DatabaseConstants.typesRoom}
		};
		
		var results = await migrator.Context.Document.CreateManyAsync(
			handle, DatabaseConstants.ObjectFlags, flags);
		return results.ToList();
	}

		public Task Down(IArangoMigrator migrator, ArangoHandle handle)
	{
		throw new NotSupportedException();
	}
}