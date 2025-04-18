﻿using Core.Arango;
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
							Name = DatabaseConstants.Things,
							Type = ArangoCollectionType.Document,
							WaitForSync = true,
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
							WaitForSync = true
						}
					},
					new()
					{
						Collection = new ArangoCollection
						{
							Name = DatabaseConstants.ObjectData,
							Type = ArangoCollectionType.Document,
							WaitForSync = true
						}
					},
					new()
					{
						Collection = new ArangoCollection
						{
							Name = DatabaseConstants.Exits,
							Type = ArangoCollectionType.Document,
							WaitForSync = true,
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
							WaitForSync = true,
							Schema = new ArangoSchema()
							{
								Rule = new {
									type = DatabaseConstants.TypeObject,
									properties = new
									{
										PasswordHash = new { type = DatabaseConstants.TypeString },
										Aliases = new { type = DatabaseConstants.TypeArray, items = new { type = DatabaseConstants.TypeString } }
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
							Name = DatabaseConstants.ObjectFlags,
							Type = ArangoCollectionType.Document,
							WaitForSync = true,
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
							WaitForSync = true,
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
							WaitForSync = true,
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
							WaitForSync = true,
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
							WaitForSync = true,
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
							WaitForSync = true,
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
							WaitForSync = true,
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
							WaitForSync = true,
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
							WaitForSync = true,
							Schema = new ArangoSchema
							{
								Rule = new {
									type = DatabaseConstants.TypeObject,
									properties = new
									{
										Name = new { type = DatabaseConstants.TypeString },
										Description = new { type = DatabaseConstants.TypeString },
										Privs = new { type = DatabaseConstants.TypeArray, items = new { type = DatabaseConstants.TypeString } },
										JoinLock = new { type = DatabaseConstants.TypeString },
										SpeakLock = new { type = DatabaseConstants.TypeString },
										SeeLock = new { type = DatabaseConstants.TypeString },
										HideLock = new { type = DatabaseConstants.TypeString },
										ModLock = new { type = DatabaseConstants.TypeString },
									},
									required = (string[])[nameof(SharpChannel.Name), nameof(SharpChannel.Privs)]
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
							WaitForSync = true
						}
					},
					new()
					{
						Collection = new ArangoCollection
						{
							Name = DatabaseConstants.HasObjectData,
							Type = ArangoCollectionType.Edge,
							WaitForSync = true
						}
					},
					new()
					{
						Collection = new ArangoCollection
						{
							Name = DatabaseConstants.HasHome,
							Type = ArangoCollectionType.Edge,
							WaitForSync = true
						}
					},
					new()
					{
						Collection = new ArangoCollection
						{
							Name = DatabaseConstants.HasExit,
							Type = ArangoCollectionType.Edge,
							WaitForSync = true
						}
					},
					new()
					{
						Collection = new ArangoCollection
						{
							Name = DatabaseConstants.HasFlags,
							Type = ArangoCollectionType.Edge,
							WaitForSync = true
						}
					},
					new()
					{
						Collection = new ArangoCollection
						{
							Name = DatabaseConstants.HasAttribute,
							Type = ArangoCollectionType.Edge,
							WaitForSync = true
						}
					},
					new()
					{
						Collection = new ArangoCollection
						{
							Name = DatabaseConstants.HasAttributeFlag,
							Type = ArangoCollectionType.Edge,
							WaitForSync = true
						}
					},
					new()
					{
						Collection = new ArangoCollection
						{
							Name = DatabaseConstants.HasObjectOwner,
							Type = ArangoCollectionType.Edge,
							WaitForSync = true
						}
					},
					new()
					{
						Collection = new ArangoCollection
						{
							Name = DatabaseConstants.HasAttributeOwner,
							Type = ArangoCollectionType.Edge,
							WaitForSync = true
						}
					}, 
					new()
					{
						Collection = new ArangoCollection
						{
							Name = DatabaseConstants.OnChannel,
							Type = ArangoCollectionType.Edge,
							WaitForSync = true,
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
										Title = new { type = DatabaseConstants.TypeBoolean }
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
							WaitForSync = true,
						}
					},
					new()
					{
						Collection = new ArangoCollection
						{
							Name = DatabaseConstants.OwnerOfChannel,
							Type = ArangoCollectionType.Edge,
							WaitForSync = true,
						}
					},
					new()
					{
						Collection = new ArangoCollection
						{
							Name = DatabaseConstants.ReceivedMail,
							Type = ArangoCollectionType.Edge,
							WaitForSync = true,
						}
					},
					new()
					{
						Collection = new ArangoCollection
						{
							Name = DatabaseConstants.HasHook,
							Type = ArangoCollectionType.Edge,
							WaitForSync = true,
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
			PasswordHash = string.Empty
		});

		var flags = await CreateInitialFlags(migrator, handle);
		var attributeFlags = await CreateInitialAttributeFlags(migrator, handle);
		var powers = await CreateInitialPowers(migrator, handle);
		var wizard = flags[18];

		await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.IsObject, new SharpEdge { From = roomTwoRoom.Id, To = roomTwoObj.Id });
		await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.IsObject, new SharpEdge { From = roomZeroRoom.Id, To = roomZeroObj.Id });
		await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.IsObject, new SharpEdge { From = playerOnePlayer.Id, To = playerOneObj.Id });
		await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.AtLocation, new SharpEdge { From = playerOnePlayer.Id, To = roomZeroRoom.Id });
		await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.HasHome, new SharpEdge { From = playerOnePlayer.Id, To = roomZeroRoom.Id });
		await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.HasObjectOwner, new SharpEdge { From = roomTwoObj.Id, To = playerOnePlayer.Id });
		await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.HasObjectOwner, new SharpEdge { From = roomZeroObj.Id, To = playerOnePlayer.Id });
		await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.HasObjectOwner, new SharpEdge { From = playerOneObj.Id, To = playerOnePlayer.Id });
		await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.HasFlags, new SharpEdge { From = playerOneObj.Id, To = wizard.Id });
	}

	private static async Task<List<ArangoUpdateResult<ArangoVoid>>> CreateInitialPowers(IArangoMigrator migrator,
		ArangoHandle handle) =>
	[
		await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.ObjectPowers,
			new
			{
				Name = "Boot",
				System = true,
				TypeRestrictions = DatabaseConstants.typesAll,
				SetPermissions = DatabaseConstants.permissionsWizard
					.Union(DatabaseConstants.permissionsLog),
				UnsetPermissions = DatabaseConstants.permissionsWizard
			}),
		await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.ObjectPowers,
			new
			{
				Name = "Builder",
				System = true,
				TypeRestrictions = DatabaseConstants.typesAll,
				SetPermissions = DatabaseConstants.permissionsWizard
					.Union(DatabaseConstants.permissionsLog),
				UnsetPermissions = DatabaseConstants.permissionsWizard
			}),
		await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.ObjectPowers,
			new
			{
				Name = "Can_Dark",
				System = true,
				TypeRestrictions = DatabaseConstants.typesPlayer,
				SetPermissions = DatabaseConstants.permissionsWizard
					.Union(DatabaseConstants.permissionsLog)
			}),
		await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.ObjectPowers,
			new
			{
				Name = "Can_HTTP",
				System = true,
				TypeRestrictions = DatabaseConstants.typesAll,
				SetPermissions = DatabaseConstants.permissionsWizard
					.Union(DatabaseConstants.permissionsLog)
			}),
		await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.ObjectPowers,
			new
			{
				Name = "Can_Spoof",
				System = true,
				Aliases = (string[])["Can_nspemit"],
				TypeRestrictions = DatabaseConstants.typesAll,
				SetPermissions = DatabaseConstants.permissionsWizard
					.Union(DatabaseConstants.permissionsLog),
				UnsetPermissions = DatabaseConstants.permissionsWizard
			}),
		await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.ObjectPowers,
			new
			{
				Name = "Chat_Privs",
				System = true,
				Aliases = (string[])["Can_nspemit"],
				TypeRestrictions = DatabaseConstants.typesAll,
				SetPermissions = DatabaseConstants.permissionsWizard
					.Union(DatabaseConstants.permissionsLog),
				UnsetPermissions = DatabaseConstants.permissionsWizard
			}),
		await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.ObjectPowers,
			new
			{
				Name = "Debit",
				System = true,
				Aliases = (string[])["Steal_Money"],
				TypeRestrictions = DatabaseConstants.typesAll,
				SetPermissions = DatabaseConstants.permissionsWizard
					.Union(DatabaseConstants.permissionsLog),
			}),
		await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.ObjectPowers,
			new
			{
				Name = "Functions",
				System = true,
				TypeRestrictions = DatabaseConstants.typesAll,
				SetPermissions = DatabaseConstants.permissionsWizard
					.Union(DatabaseConstants.permissionsLog),
				UnsetPermissions = DatabaseConstants.permissionsWizard
			}),
		await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.ObjectPowers,
			new
			{
				Name = "Guest",
				System = true,
				TypeRestrictions = DatabaseConstants.typesAll,
				SetPermissions = DatabaseConstants.permissionsWizard
					.Union(DatabaseConstants.permissionsLog),
				UnsetPermissions = DatabaseConstants.permissionsWizard
			}),
		await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.ObjectPowers,
			new
			{
				Name = "Halt",
				System = true,
				TypeRestrictions = DatabaseConstants.typesAll,
				SetPermissions = DatabaseConstants.permissionsWizard
					.Union(DatabaseConstants.permissionsLog),
				UnsetPermissions = DatabaseConstants.permissionsWizard
			}),
		await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.ObjectPowers,
			new
			{
				Name = "Hide",
				System = true,
				TypeRestrictions = DatabaseConstants.typesAll,
				SetPermissions = DatabaseConstants.permissionsWizard
					.Union(DatabaseConstants.permissionsLog),
				UnsetPermissions = DatabaseConstants.permissionsWizard
			}),
		await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.ObjectPowers,
			new
			{
				Name = "Hook",
				System = true,
				TypeRestrictions = DatabaseConstants.typesAll,
				SetPermissions = DatabaseConstants.permissionsWizard
					.Union(DatabaseConstants.permissionsLog)
			}),
		await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.ObjectPowers,
			new
			{
				Name = "Idle",
				System = true,
				TypeRestrictions = DatabaseConstants.typesAll,
				SetPermissions = DatabaseConstants.permissionsWizard
					.Union(DatabaseConstants.permissionsLog),
				UnsetPermissions = DatabaseConstants.permissionsWizard
			}),
		await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.ObjectPowers,
			new
			{
				Name = "Immortal",
				System = true,
				TypeRestrictions = DatabaseConstants.typesAll,
				SetPermissions = DatabaseConstants.permissionsWizard
					.Union(DatabaseConstants.permissionsLog),
				UnsetPermissions = DatabaseConstants.permissionsWizard
			}),
		await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.ObjectPowers,
			new
			{
				Name = "Link_Anywhere",
				System = true,
				TypeRestrictions = DatabaseConstants.typesAll,
				SetPermissions = DatabaseConstants.permissionsWizard
					.Union(DatabaseConstants.permissionsLog),
				UnsetPermissions = DatabaseConstants.permissionsWizard
			}),
		await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.ObjectPowers,
			new
			{
				Name = "Login",
				System = true,
				TypeRestrictions = DatabaseConstants.typesAll,
				SetPermissions = DatabaseConstants.permissionsWizard
					.Union(DatabaseConstants.permissionsLog),
				UnsetPermissions = DatabaseConstants.permissionsWizard
			}),
		await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.ObjectPowers,
			new
			{
				Name = "Long_Fingers",
				System = true,
				TypeRestrictions = DatabaseConstants.typesAll,
				SetPermissions = DatabaseConstants.permissionsWizard
					.Union(DatabaseConstants.permissionsLog),
				UnsetPermissions = DatabaseConstants.permissionsWizard
			}),
		await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.ObjectPowers,
			new
			{
				Name = "Many_Attribs",
				System = true,
				TypeRestrictions = DatabaseConstants.typesAll,
				SetPermissions = DatabaseConstants.permissionsWizard
					.Union(DatabaseConstants.permissionsLog)
			}),
		await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.ObjectPowers,
			new
			{
				Name = "No_Pay",
				System = true,
				TypeRestrictions = DatabaseConstants.typesAll,
				SetPermissions = DatabaseConstants.permissionsWizard
					.Union(DatabaseConstants.permissionsLog),
				UnsetPermissions = DatabaseConstants.permissionsWizard
			}),
		await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.ObjectPowers,
			new
			{
				Name = "No_Quota",
				System = true,
				Aliases = (string[])["Free_Quota"],
				TypeRestrictions = DatabaseConstants.typesAll,
				SetPermissions = DatabaseConstants.permissionsWizard
					.Union(DatabaseConstants.permissionsLog),
				UnsetPermissions = DatabaseConstants.permissionsWizard
			}),
		await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.ObjectPowers,
			new
			{
				Name = "Open_Anywhere",
				System = true,
				TypeRestrictions = DatabaseConstants.typesAll,
				SetPermissions = DatabaseConstants.permissionsWizard
					.Union(DatabaseConstants.permissionsLog),
				UnsetPermissions = DatabaseConstants.permissionsWizard
			}),
		await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.ObjectPowers,
			new
			{
				Name = "Pemit_All",
				System = true,
				TypeRestrictions = DatabaseConstants.typesAll,
				SetPermissions = DatabaseConstants.permissionsWizard
					.Union(DatabaseConstants.permissionsLog),
				UnsetPermissions = DatabaseConstants.permissionsWizard
			}),
		await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.ObjectPowers,
			new
			{
				Name = "Pick_DBRefs",
				System = true,
				TypeRestrictions = DatabaseConstants.typesAll,
				SetPermissions = DatabaseConstants.permissionsWizard
					.Union(DatabaseConstants.permissionsLog),
				UnsetPermissions = DatabaseConstants.permissionsWizard
			}),
		await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.ObjectPowers,
			new
			{
				Name = "Player_Create",
				System = true,
				TypeRestrictions = DatabaseConstants.typesAll,
				SetPermissions = DatabaseConstants.permissionsWizard
					.Union(DatabaseConstants.permissionsLog),
				UnsetPermissions = DatabaseConstants.permissionsWizard
			}),
		await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.ObjectPowers,
			new
			{
				Name = "Poll",
				System = true,
				TypeRestrictions = DatabaseConstants.typesAll,
				SetPermissions = DatabaseConstants.permissionsWizard
					.Union(DatabaseConstants.permissionsLog),
				UnsetPermissions = DatabaseConstants.permissionsWizard
			}),
		await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.ObjectPowers,
			new
			{
				Name = "Pueblo_Send",
				System = true,
				TypeRestrictions = DatabaseConstants.typesAll,
				SetPermissions = DatabaseConstants.permissionsWizard
					.Union(DatabaseConstants.permissionsLog),
				UnsetPermissions = DatabaseConstants.permissionsWizard
			}),
		await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.ObjectPowers,
			new
			{
				Name = "Queue",
				System = true,
				TypeRestrictions = DatabaseConstants.typesAll,
				SetPermissions = DatabaseConstants.permissionsWizard
					.Union(DatabaseConstants.permissionsLog),
				UnsetPermissions = DatabaseConstants.permissionsWizard
			}),
		await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.ObjectPowers,
			new
			{
				Name = "Search",
				System = true,
				TypeRestrictions = DatabaseConstants.typesAll,
				SetPermissions = DatabaseConstants.permissionsWizard
					.Union(DatabaseConstants.permissionsLog),
				UnsetPermissions = DatabaseConstants.permissionsWizard
			}),
		await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.ObjectPowers,
			new
			{
				Name = "See_All",
				System = true,
				TypeRestrictions = DatabaseConstants.typesAll,
				SetPermissions = DatabaseConstants.permissionsWizard
					.Union(DatabaseConstants.permissionsLog),
				UnsetPermissions = DatabaseConstants.permissionsWizard
			}),
		await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.ObjectPowers,
			new
			{
				Name = "See_Queue",
				System = true,
				TypeRestrictions = DatabaseConstants.typesAll,
				SetPermissions = DatabaseConstants.permissionsWizard
					.Union(DatabaseConstants.permissionsLog),
				UnsetPermissions = DatabaseConstants.permissionsWizard
			}),
		await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.ObjectPowers,
			new
			{
				Name = "See_OOB",
				System = true,
				TypeRestrictions = DatabaseConstants.typesAll,
				SetPermissions = DatabaseConstants.permissionsWizard
					.Union(DatabaseConstants.permissionsLog),
				UnsetPermissions = DatabaseConstants.permissionsWizard
			}),
		await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.ObjectPowers,
			new
			{
				Name = "SQL_OK",
				System = true,
				TypeRestrictions = DatabaseConstants.typesAll,
				SetPermissions = DatabaseConstants.permissionsWizard
					.Union(DatabaseConstants.permissionsLog),
				UnsetPermissions = DatabaseConstants.permissionsWizard
			}),
		await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.ObjectPowers,
			new
			{
				Name = "Tport_Anything",
				System = true,
				Aliases = (string[])["tel_anything"],
				TypeRestrictions = DatabaseConstants.typesAll,
				SetPermissions = DatabaseConstants.permissionsWizard
					.Union(DatabaseConstants.permissionsLog),
				UnsetPermissions = DatabaseConstants.permissionsWizard
			}),
		await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.ObjectPowers,
			new
			{
				Name = "Tport_Anywhere",
				System = true,
				Aliases = (string[])["tel_anywhere"],
				TypeRestrictions = DatabaseConstants.typesAll,
				SetPermissions = DatabaseConstants.permissionsWizard
					.Union(DatabaseConstants.permissionsLog),
				UnsetPermissions = DatabaseConstants.permissionsWizard
			}),
		await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.ObjectPowers,
			new
			{
				Name = "Unkillable",
				System = true,
				TypeRestrictions = DatabaseConstants.typesAll,
				SetPermissions = DatabaseConstants.permissionsWizard
					.Union(DatabaseConstants.permissionsLog),
				UnsetPermissions = DatabaseConstants.permissionsWizard
			})
	];

	private async static Task<List<ArangoUpdateResult<ArangoVoid>>> CreateInitialAttributeFlags(IArangoMigrator migrator,
		ArangoHandle handle) =>
	[
		await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.AttributeFlags,
			new
			{
				Name = "no_command",
				Symbol = "$",
				System = true,
				Inheritable = true
			}),
		await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.AttributeFlags,
			new
			{
				Name = "no_inherit",
				Symbol = "i",
				System = true,
				Inheritable = true
			}),
		await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.AttributeFlags,
			new
			{
				Name = "no_clone",
				Symbol = "c",
				System = true,
				Inheritable = true
			}),
		await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.AttributeFlags,
			new
			{
				Name = "mortal_dark",
				Symbol = "m",
				System = true,
				Inheritable = true
			}),
		await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.AttributeFlags,
			new
			{
				Name = "wizard",
				Symbol = "w",
				System = true,
				Inheritable = true
			}),
		await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.AttributeFlags,
			new
			{
				Name = "veiled",
				Symbol = "V",
				System = true,
				Inheritable = true
			}),
		await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.AttributeFlags,
			new
			{
				Name = "nearby",
				Symbol = "n",
				System = true,
				Inheritable = true
			}),
		await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.AttributeFlags,
			new
			{
				Name = "locked",
				Symbol = "+",
				System = true,
				Inheritable = true
			}),
		await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.AttributeFlags,
			new
			{
				Name = "safe",
				Symbol = "S",
				System = true,
				Inheritable = true
			}),
		await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.AttributeFlags,
			new
			{
				Name = "visual",
				Symbol = "v",
				System = true,
				Inheritable = false
			}),
		await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.AttributeFlags,
			new
			{
				Name = "public",
				Symbol = "p",
				System = true,
				Inheritable = false
			}),
		await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.AttributeFlags,
			new
			{
				Name = "debug",
				Symbol = "b",
				System = true,
				Inheritable = true
			}),
		await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.AttributeFlags,
			new
			{
				Name = "no_debug",
				Symbol = "B",
				System = true,
				Inheritable = true
			}),
		await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.AttributeFlags,
			new
			{
				Name = "regexp",
				Symbol = "R",
				System = true,
				Inheritable = false
			}),
		await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.AttributeFlags,
			new
			{
				Name = "case",
				Symbol = "C",
				System = true,
				Inheritable = false
			}),
		await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.AttributeFlags,
			new
			{
				Name = "nospace",
				Symbol = "s",
				System = true,
				Inheritable = true
			}),
		await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.AttributeFlags,
			new
			{
				Name = "noname",
				Symbol = "N",
				System = true,
				Inheritable = true
			}),
		await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.AttributeFlags,
			new
			{
				Name = "aahear",
				Symbol = "A",
				System = true,
				Inheritable = false
			}),
		await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.AttributeFlags,
			new
			{
				Name = "amhear",
				Symbol = "M",
				System = true,
				Inheritable = false
			}),
		await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.AttributeFlags,
			new
			{
				Name = "quiet",
				Symbol = "Q",
				System = true,
				Inheritable = false
			}),
		// TODO: Consider if this is needed for our purposes at all.
		await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.AttributeFlags,
			new
			{
				Name = "branch",
				Symbol = "`",
				System = true,
				Inheritable = false
			})
	];

	// Todo: Find a better way of doing this, so we can keep a proper async flow.
	private static async Task<List<ArangoUpdateResult<ArangoVoid>>> CreateInitialFlags(IArangoMigrator migrator,
		ArangoHandle handle) =>
	[
		await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.ObjectFlags, new
		{
			Name = "ABODE",
			Symbol = "A",
			System = true,
			TypeRestrictions = DatabaseConstants.typesRoom
		}),
		await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.ObjectFlags, new
		{
			Name = "ANSI",
			Symbol = "A",
			System = true,
			TypeRestrictions = DatabaseConstants.typesPlayer
		}),
		await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.ObjectFlags, new
		{
			Name = "CHOWN_OK",
			Symbol = "C",
			System = true,
			TypeRestrictions = DatabaseConstants.typesContainer
		}),
		await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.ObjectFlags, new
		{
			Name = "DARK",
			Symbol = "D",
			System = true,
			TypeRestrictions = DatabaseConstants.typesAll
		}),
		await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.ObjectFlags, new
		{
			Name = "FIXED",
			Symbol = "F",
			System = true,
			SetPermissions = DatabaseConstants.permissionsWizard,
			UnsetPermissions = DatabaseConstants.permissionsWizard,
			TypeRestrictions = DatabaseConstants.typesPlayer
		}),
		await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.ObjectFlags, new
		{
			Name = "FLOATING",
			Symbol = "F",
			System = true,
			TypeRestrictions = DatabaseConstants.typesRoom
		}),
		await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.ObjectFlags, new
		{
			Name = "HAVEN",
			Symbol = "H",
			System = true,
			TypeRestrictions = DatabaseConstants.typesPlayer
		}),
		await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.ObjectFlags, new
		{
			Name = "TRUST",
			Symbol = "I",
			System = true,
			Aliases = (string[])["INHERIT"],
			SetPermissions = DatabaseConstants.permissionsTrusted,
			UnsetPermissions = DatabaseConstants.permissionsTrusted,
			TypeRestrictions = DatabaseConstants.typesAll
		}),
		await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.ObjectFlags, new
		{
			Name = "JUDGE",
			Symbol = "J",
			System = true,
			SetPermissions = DatabaseConstants.permissionsRoyalty,
			UnsetPermissions = DatabaseConstants.permissionsRoyalty,
			TypeRestrictions = DatabaseConstants.typesPlayer
		}),
		await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.ObjectFlags, new
		{
			Name = "JUMP_OK",
			Symbol = "J",
			System = true,
			Aliases = (string[])["TEL-OK", "TEL_OK", "TELOK"],
			TypeRestrictions = DatabaseConstants.typesRoom
		}),
		await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.ObjectFlags, new
		{
			Name = "LINK_OK",
			Symbol = "L",
			System = true,
			TypeRestrictions = DatabaseConstants.typesAll
		}),
		await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.ObjectFlags, new
		{
			Name = "MONITOR",
			Symbol = "M",
			System = true,
			Aliases = (string[])["LISTENER", "WATCHER"],
			TypeRestrictions = DatabaseConstants.typesContainer
		}),
		await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.ObjectFlags, new
		{
			Name = "NO_LEAVE",
			Symbol = "N",
			System = true,
			Aliases = (string[])["NOLEAVE"],
			TypeRestrictions = DatabaseConstants.typesThing
		}),
		await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.ObjectFlags, new
		{
			Name = "NO_TEL",
			Symbol = "N",
			System = true,
			TypeRestrictions = DatabaseConstants.typesRoom
		}),
		await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.ObjectFlags, new
		{
			Name = "OPAQUE",
			Symbol = "O",
			System = true,
			TypeRestrictions = DatabaseConstants.typesAll
		}),
		await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.ObjectFlags, new
		{
			Name = "QUIET",
			Symbol = "Q",
			System = true,
			TypeRestrictions = DatabaseConstants.typesAll
		}),
		await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.ObjectFlags, new
		{
			Name = "UNFINDABLE",
			Symbol = "U",
			System = true,
			TypeRestrictions = DatabaseConstants.typesAll
		}),
		await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.ObjectFlags, new
		{
			Name = "VISUAL",
			Symbol = "V",
			System = true,
			TypeRestrictions = DatabaseConstants.typesAll
		}),
		await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.ObjectFlags, new
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
		}),
		await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.ObjectFlags, new
		{
			Name = "SAFE",
			Symbol = "X",
			System = true,
			TypeRestrictions = DatabaseConstants.typesAll
		}),
		await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.ObjectFlags, new
		{
			Name = "SHARED",
			Symbol = "Z",
			System = true,
			Aliases = (string[])["ZONE"],
			TypeRestrictions = DatabaseConstants.typesPlayer
		}),
		await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.ObjectFlags, new
		{
			Name = "Z_TEL",
			Symbol = "Z",
			System = true,
			TypeRestrictions = DatabaseConstants.typesRoom
				.Union(DatabaseConstants.typesThing)
		}),
		await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.ObjectFlags, new
		{
			Name = "LISTEN_PARENT",
			Symbol = "^",
			Aliases = (string[])["^"],
			System = true,
			TypeRestrictions = DatabaseConstants.typesPlayer
				.Union(DatabaseConstants.typesThing).Union(DatabaseConstants.typesRoom)
		}),
		await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.ObjectFlags, new
		{
			Name = "NOACCENTS",
			Symbol = "~",
			System = true,
			TypeRestrictions = DatabaseConstants.typesPlayer
				.Union(DatabaseConstants.typesThing).Union(DatabaseConstants.typesRoom)
		}),
		await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.ObjectFlags, new
		{
			Name = "UNREGISTERED",
			Symbol = "?",
			System = true,
			TypeRestrictions = DatabaseConstants.typesPlayer,
			SetPermissions = DatabaseConstants.permissionsRoyalty,
			UnsetPermissions = DatabaseConstants.permissionsRoyalty
		}),
		await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.ObjectFlags, new
		{
			Name = "NOSPOOF",
			Symbol = "\"",
			System = true,
			TypeRestrictions = DatabaseConstants.typesAll,
			SetPermissions = DatabaseConstants.permissionsODark,
			UnSetPermissions = DatabaseConstants.permissionsODark
		}),
		await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.ObjectFlags, new
		{
			Name = "AUDIBLE",
			Symbol = "a",
			System = true,
			TypeRestrictions = DatabaseConstants.typesAll
		}),
		await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.ObjectFlags, new
		{
			Name = "DEBUG",
			Aliases = (string[])["TRACE"],
			Symbol = "b",
			System = true,
			TypeRestrictions = DatabaseConstants.typesAll
		}),
		await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.ObjectFlags, new
		{
			Name = "DESTROY_OK",
			Aliases = (string[])["DEST_OK"],
			Symbol = "d",
			System = true,
			TypeRestrictions = DatabaseConstants.typesThing
		}),
		await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.ObjectFlags, new
		{
			Name = "ENTER_OK",
			Symbol = "e",
			System = true,
			TypeRestrictions = DatabaseConstants.typesAll
		}),
		await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.ObjectFlags, new
		{
			Name = "GAGGED",
			Symbol = "g",
			System = true,
			TypeRestrictions = DatabaseConstants.typesPlayer,
			SetPermissions = DatabaseConstants.permissionsWizard,
			UnSetPermissions = DatabaseConstants.permissionsWizard
		}),
		await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.ObjectFlags, new
		{
			Name = "HALT",
			Symbol = "h",
			System = true,
			TypeRestrictions = DatabaseConstants.typesAll
		}),
		await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.ObjectFlags, new
		{
			Name = "ORPHAN",
			Symbol = "i",
			System = true,
			TypeRestrictions = DatabaseConstants.typesAll
		}),
		await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.ObjectFlags, new
		{
			Name = "JURY_OK",
			Aliases = (string[])["JURYOK"],
			Symbol = "j",
			System = true,
			TypeRestrictions = DatabaseConstants.typesPlayer,
			SetPermissions = DatabaseConstants.permissionsRoyalty,
			UnSetPermissions = DatabaseConstants.permissionsRoyalty
		}),
		await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.ObjectFlags, new
		{
			Name = "KEEPALIVE",
			Symbol = "k",
			System = true,
			TypeRestrictions = DatabaseConstants.typesPlayer
		}),
		await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.ObjectFlags, new
		{
			Name = "LIGHT",
			Symbol = "l",
			System = true,
			TypeRestrictions = DatabaseConstants.typesAll
		}),
		await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.ObjectFlags, new
		{
			Name = "MISTRUST",
			Symbol = "m",
			System = true,
			TypeRestrictions = DatabaseConstants.typesContent,
			SetPermissions = DatabaseConstants.permissionsTrusted,
			UnSetPermissions = DatabaseConstants.permissionsTrusted
		}),
		await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.ObjectFlags, new
		{
			Name = "MISTRUST",
			Aliases = (string[])["MYOPIC"],
			Symbol = "m",
			System = true,
			TypeRestrictions = DatabaseConstants.typesContent,
			SetPermissions = DatabaseConstants.permissionsTrusted,
			UnSetPermissions = DatabaseConstants.permissionsTrusted
		}),
		await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.ObjectFlags, new
		{
			Name = "NO_COMMAND",
			Aliases = (string[])["NOCOMMAND"],
			Symbol = "n",
			System = true,
			TypeRestrictions = DatabaseConstants.typesAll
		}),
		await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.ObjectFlags, new
		{
			Name = "ON_VACATION",
			Aliases = (string[])["ONVACATION","ON-VACATION"],
			Symbol = "o",
			System = true,
			TypeRestrictions = DatabaseConstants.typesPlayer
		}),
		await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.ObjectFlags, new
		{
			Name = "PUPPET",
			Symbol = "P",
			System = true,
			TypeRestrictions = DatabaseConstants.typesThing
				.Union(DatabaseConstants.typesRoom)
		}),
		await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.ObjectFlags, new
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
		}),
		await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.ObjectFlags, new
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
		}),
		await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.ObjectFlags, new
		{
			Name = "TRANSPARENT",
			Symbol = "t",
			System = true,
			TypeRestrictions = DatabaseConstants.typesAll
		}),
		await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.ObjectFlags, new
		{
			Name = "VERBOSE",
			Symbol = "v",
			System = true,
			TypeRestrictions = DatabaseConstants.typesAll,
		}),
		await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.ObjectFlags, new
		{
			Name = "NO_WARN",
			Aliases = (string[])["NOWARN"],
			Symbol = "w",
			System = true,
			TypeRestrictions = DatabaseConstants.typesAll,
		}),
		await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.ObjectFlags, new
		{
			Name = "CLOUDY",
			Aliases = (string[])["TERSE"],
			Symbol = "x",
			System = true,
			TypeRestrictions = DatabaseConstants.typesExit,
		}),
		await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.ObjectFlags, new
		{
			Name = "CHAN_USEFIRSTMATCH",
			Aliases = (string[])["CHAN_FIRSTMATCH","CHAN_MATCHFIRST"],
			System = true,
			TypeRestrictions = DatabaseConstants.typesAll,
			SetPermissions = DatabaseConstants.permissionsTrusted,
			UnSetPermissions = DatabaseConstants.permissionsTrusted
		}),
		await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.ObjectFlags, new
		{
			Name = "HEAR_CONNECT",
			System = true,
			TypeRestrictions = DatabaseConstants.typesPlayer,
			SetPermissions = DatabaseConstants.permissionsRoyalty
		}),
		await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.ObjectFlags, new
		{
			Name = "HEAVY",
			System = true,
			TypeRestrictions = DatabaseConstants.typesAll,
			SetPermissions = DatabaseConstants.permissionsRoyalty
		}),
		await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.ObjectFlags, new
		{
			Name = "LOUD",
			System = true,
			TypeRestrictions = DatabaseConstants.typesPlayer,
			SetPermissions = DatabaseConstants.permissionsRoyalty
		}),
		await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.ObjectFlags, new
		{
			Name = "NO_LOG",
			System = true,
			TypeRestrictions = DatabaseConstants.typesPlayer,
			SetPermissions = DatabaseConstants.permissionsWizard
				.Union(DatabaseConstants.permissionsMDark)
				.Union(DatabaseConstants.permissionsLog),
			UnSetPermissions = DatabaseConstants.permissionsWizard
				.Union(DatabaseConstants.permissionsMDark)
		}),
		await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.ObjectFlags, new
		{
			Name = "PARANOID",
			System = true,
			TypeRestrictions = DatabaseConstants.typesPlayer,
			SetPermissions = DatabaseConstants.permissionsODark,
			UnSetPermissions = DatabaseConstants.permissionsODark
		}),
		await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.ObjectFlags, new
		{
			Name = "TRACK_MONEY",
			System = true,
			TypeRestrictions = DatabaseConstants.typesPlayer,
		}),
		await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.ObjectFlags, new
		{
			Name = "XTERM256",
			Aliases = (string[])["XTERM","COLOR256"],
			System = true,
			TypeRestrictions = DatabaseConstants.typesPlayer
		}),
		await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.ObjectFlags, new
		{
			Name = "MONIKER",
			System = true,
			TypeRestrictions = DatabaseConstants.typesAll,
			SetPermissions = DatabaseConstants.permissionsRoyalty,
			UnSetPermissions = DatabaseConstants.permissionsRoyalty
		}),
		await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.ObjectFlags, new
		{
			Name = "OPEN_OK",
			System = true,
			TypeRestrictions = DatabaseConstants.typesRoom
		}),
	];

	public Task Down(IArangoMigrator migrator, ArangoHandle handle)
	{
		throw new NotImplementedException();
	}
}