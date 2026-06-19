using Core.Arango;
using Core.Arango.Migration;
using Core.Arango.Protocol;

namespace SharpMUSH.Database.ArangoDB.Migrations;

/// <summary>
/// Adds the Scene System (Area 7) — the graph-native <c>graph_sharp_sys_scene</c>
/// subsystem. Creates the four vertex DOCUMENT collections
/// (<c>node_sharp_sys_scene_scenes</c>, <c>…_poses</c>, <c>…_pose_edits</c>,
/// <c>…_plots</c>), all sixteen <c>edge_sharp_sys_scene_*</c> EDGE collections, the
/// named graph with edge definitions (including cross-collection edges into
/// <c>node_rooms</c>/<c>node_players</c>/<c>node_objects</c>), and persistent
/// indexes on <c>Scene.Status</c> / <c>Scene.ScheduledFor</c> / <c>Scene.IsPublic</c>.
/// Also seeds the informational <c>SCENE_ROOM</c> object flag.
/// </summary>
public class Migration_AddScenes : IArangoMigration
{
	public long Id => 20260619_001;

	public string Name => "add_scenes";

	public async Task Up(IArangoMigrator migrator, ArangoHandle handle)
	{
		// ── Vertex DOCUMENT collections ─────────────────────────────────────────
		if (!await migrator.Context.Collection.ExistAsync(handle, DatabaseConstants.SharpScenes))
		{
			await migrator.Context.Collection.CreateAsync(handle, new ArangoCollection
			{
				Name = DatabaseConstants.SharpScenes,
				Type = ArangoCollectionType.Document,
				WaitForSync = true,
				Schema = new ArangoSchema
				{
					Rule = new
					{
						type = DatabaseConstants.TypeObject,
						properties = new
						{
							Status = new { type = DatabaseConstants.TypeString }
							// Other fields (IsPublic/IsTempRoom/ScheduledFor/StartedAt/LastActivityAt/
							// PoseCount/OwnerName/StarterName/RoomName/Meta) are intentionally left
							// undeclared so ScheduledFor may be explicitly null (cleared) without
							// tripping JSON-schema type validation. additionalProperties allows them.
						},
						required = (string[])["Status"],
						additionalProperties = true
					}
				}
			});

			await migrator.Context.Index.CreateAsync(handle, DatabaseConstants.SharpScenes, new ArangoIndex
			{
				Fields = ["Status"],
				Type = ArangoIndexType.Persistent
			});

			await migrator.Context.Index.CreateAsync(handle, DatabaseConstants.SharpScenes, new ArangoIndex
			{
				Fields = ["ScheduledFor"],
				Type = ArangoIndexType.Persistent
			});

			await migrator.Context.Index.CreateAsync(handle, DatabaseConstants.SharpScenes, new ArangoIndex
			{
				Fields = ["IsPublic"],
				Type = ArangoIndexType.Persistent
			});
		}

		if (!await migrator.Context.Collection.ExistAsync(handle, DatabaseConstants.SharpScenePoses))
		{
			await migrator.Context.Collection.CreateAsync(handle, new ArangoCollection
			{
				Name = DatabaseConstants.SharpScenePoses,
				Type = ArangoCollectionType.Document,
				WaitForSync = true,
				Schema = new ArangoSchema
				{
					Rule = new
					{
						type = DatabaseConstants.TypeObject,
						properties = new
						{
							Source = new { type = DatabaseConstants.TypeString },
							Tags = new { type = DatabaseConstants.TypeArray, items = new { type = DatabaseConstants.TypeString } },
							Meta = new { type = DatabaseConstants.TypeObject },
							CreatedAt = new { type = DatabaseConstants.TypeNumber },
							IsDeleted = new { type = DatabaseConstants.TypeBoolean },
							AuthorName = new { type = DatabaseConstants.TypeString },
							ShowAsName = new { type = DatabaseConstants.TypeString },
							OriginName = new { type = DatabaseConstants.TypeString }
						},
						// No required properties — ArangoDB rejects an empty "required" array,
						// and these collections only need type-validation of declared fields.
						additionalProperties = true
					}
				}
			});
		}

		if (!await migrator.Context.Collection.ExistAsync(handle, DatabaseConstants.SharpScenePoseEdits))
		{
			await migrator.Context.Collection.CreateAsync(handle, new ArangoCollection
			{
				Name = DatabaseConstants.SharpScenePoseEdits,
				Type = ArangoCollectionType.Document,
				WaitForSync = true,
				Schema = new ArangoSchema
				{
					Rule = new
					{
						type = DatabaseConstants.TypeObject,
						properties = new
						{
							Content = new { type = DatabaseConstants.TypeString },
							Markup = new { type = DatabaseConstants.TypeString },
							EditedAt = new { type = DatabaseConstants.TypeNumber },
							EditorName = new { type = DatabaseConstants.TypeString }
						},
						// No required properties — ArangoDB rejects an empty "required" array,
						// and these collections only need type-validation of declared fields.
						additionalProperties = true
					}
				}
			});
		}

		if (!await migrator.Context.Collection.ExistAsync(handle, DatabaseConstants.SharpScenePlots))
		{
			await migrator.Context.Collection.CreateAsync(handle, new ArangoCollection
			{
				Name = DatabaseConstants.SharpScenePlots,
				Type = ArangoCollectionType.Document,
				WaitForSync = true,
				Schema = new ArangoSchema
				{
					Rule = new
					{
						type = DatabaseConstants.TypeObject,
						properties = new
						{
							Title = new { type = DatabaseConstants.TypeString },
							Description = new { type = DatabaseConstants.TypeString },
							OwnerName = new { type = DatabaseConstants.TypeString },
							CreatedAt = new { type = DatabaseConstants.TypeNumber },
							UpdatedAt = new { type = DatabaseConstants.TypeNumber }
						},
						// No required properties — ArangoDB rejects an empty "required" array,
						// and these collections only need type-validation of declared fields.
						additionalProperties = true
					}
				}
			});
		}

		// ── Edge collections ────────────────────────────────────────────────────
		string[] edgeCollections =
		[
			DatabaseConstants.SceneFirstPose,
			DatabaseConstants.SceneLastPose,
			DatabaseConstants.ScenePoseNext,
			DatabaseConstants.ScenePoseInScene,
			DatabaseConstants.SceneFirstEdit,
			DatabaseConstants.SceneCurrentEdit,
			DatabaseConstants.SceneNextEdit,
			DatabaseConstants.ScenePlotIncludes,
			DatabaseConstants.SceneMember,
			DatabaseConstants.SceneInRoom,
			DatabaseConstants.SceneOwner,
			DatabaseConstants.SceneStarter,
			DatabaseConstants.ScenePoseAuthor,
			DatabaseConstants.ScenePoseOrigin,
			DatabaseConstants.SceneEditEditor,
			DatabaseConstants.ScenePlotOwner
		];

		foreach (var edge in edgeCollections)
		{
			if (!await migrator.Context.Collection.ExistAsync(handle, edge))
			{
				await migrator.Context.Collection.CreateAsync(handle, new ArangoCollection
				{
					Name = edge,
					Type = ArangoCollectionType.Edge,
					WaitForSync = true
				});
			}
		}

		// ── Named graph with edge definitions ─────────────────────────────────────
		// Create the graph directly (NOT via ApplyStructureAsync) — that calls
		// GetStructureAsync, which fails to deserialize some existing collections' index
		// metadata on ArangoDB 3.10+ (same reason Migration_AddAccounts/AddRoles do this).
		var sceneGraphs = await migrator.Context.Graph.ListAsync(handle);
		if (!sceneGraphs.Any(g => g.Name == DatabaseConstants.GraphScene))
		{
			await migrator.Context.Graph.CreateAsync(handle, new ArangoGraph
			{
				Name = DatabaseConstants.GraphScene,
				EdgeDefinitions =
				[
						new ArangoEdgeDefinition
						{
							Collection = DatabaseConstants.SceneFirstPose,
							From = [DatabaseConstants.SharpScenes],
							To = [DatabaseConstants.SharpScenePoses]
						},
						new ArangoEdgeDefinition
						{
							Collection = DatabaseConstants.SceneLastPose,
							From = [DatabaseConstants.SharpScenes],
							To = [DatabaseConstants.SharpScenePoses]
						},
						new ArangoEdgeDefinition
						{
							Collection = DatabaseConstants.ScenePoseNext,
							From = [DatabaseConstants.SharpScenePoses],
							To = [DatabaseConstants.SharpScenePoses]
						},
						new ArangoEdgeDefinition
						{
							Collection = DatabaseConstants.ScenePoseInScene,
							From = [DatabaseConstants.SharpScenePoses],
							To = [DatabaseConstants.SharpScenes]
						},
						new ArangoEdgeDefinition
						{
							Collection = DatabaseConstants.SceneFirstEdit,
							From = [DatabaseConstants.SharpScenePoses],
							To = [DatabaseConstants.SharpScenePoseEdits]
						},
						new ArangoEdgeDefinition
						{
							Collection = DatabaseConstants.SceneCurrentEdit,
							From = [DatabaseConstants.SharpScenePoses],
							To = [DatabaseConstants.SharpScenePoseEdits]
						},
						new ArangoEdgeDefinition
						{
							Collection = DatabaseConstants.SceneNextEdit,
							From = [DatabaseConstants.SharpScenePoseEdits],
							To = [DatabaseConstants.SharpScenePoseEdits]
						},
						new ArangoEdgeDefinition
						{
							Collection = DatabaseConstants.ScenePlotIncludes,
							From = [DatabaseConstants.SharpScenePlots],
							To = [DatabaseConstants.SharpScenes]
						},
						new ArangoEdgeDefinition
						{
							Collection = DatabaseConstants.SceneMember,
							From = [DatabaseConstants.Players],
							To = [DatabaseConstants.SharpScenes]
						},
						new ArangoEdgeDefinition
						{
							Collection = DatabaseConstants.SceneInRoom,
							From = [DatabaseConstants.SharpScenes],
							To = [DatabaseConstants.Rooms]
						},
						new ArangoEdgeDefinition
						{
							Collection = DatabaseConstants.SceneOwner,
							From = [DatabaseConstants.SharpScenes],
							To = [DatabaseConstants.Objects]
						},
						new ArangoEdgeDefinition
						{
							Collection = DatabaseConstants.SceneStarter,
							From = [DatabaseConstants.SharpScenes],
							To = [DatabaseConstants.Players]
						},
						new ArangoEdgeDefinition
						{
							Collection = DatabaseConstants.ScenePoseAuthor,
							From = [DatabaseConstants.SharpScenePoses],
							To = [DatabaseConstants.Players]
						},
						new ArangoEdgeDefinition
						{
							Collection = DatabaseConstants.ScenePoseOrigin,
							From = [DatabaseConstants.SharpScenePoses],
							To = [DatabaseConstants.Rooms]
						},
						new ArangoEdgeDefinition
						{
							Collection = DatabaseConstants.SceneEditEditor,
							From = [DatabaseConstants.SharpScenePoseEdits],
							To = [DatabaseConstants.Players]
						},
						new ArangoEdgeDefinition
						{
							Collection = DatabaseConstants.ScenePlotOwner,
							From = [DatabaseConstants.SharpScenePlots],
							To = [DatabaseConstants.Objects]
						}
				]
			});
		}

		// ── SCENE_ROOM informational object flag ──────────────────────────────────
		var sceneRoomExists = await migrator.Context.Query.ExecuteAsync<int>(handle,
			"FOR f IN @@c FILTER f.Name == @name COLLECT WITH COUNT INTO cnt RETURN cnt",
			new Dictionary<string, object>
			{
				{ "@c", DatabaseConstants.ObjectFlags },
				{ "name", "SCENE_ROOM" }
			});

		if (sceneRoomExists.FirstOrDefault() == 0)
		{
			await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.ObjectFlags, new
			{
				Name = "SCENE_ROOM",
				Symbol = "S",
				System = true,
				SetPermissions = DatabaseConstants.permissionsWizard,
				UnsetPermissions = DatabaseConstants.permissionsWizard,
				TypeRestrictions = DatabaseConstants.typesRoom
			});
		}
	}

	public Task Down(IArangoMigrator migrator, ArangoHandle handle) => Task.CompletedTask;
}
