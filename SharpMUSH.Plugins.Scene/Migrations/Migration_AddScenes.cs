using Core.Arango;
using Core.Arango.Migration;
using Core.Arango.Protocol;
using SharpMUSH.Database;
using SharpMUSH.Plugins.Scene.Storage;

namespace SharpMUSH.Plugins.Scene.Migrations;

/// <summary>
/// Adds the Scene System (Area 7) — the graph-native <c>graph_sharp_sys_scene</c>
/// subsystem. Creates the four vertex DOCUMENT collections
/// (<c>node_sharp_sys_scene_scenes</c>, <c>…_poses</c>, <c>…_pose_edits</c>,
/// <c>…_plots</c>), all sixteen <c>edge_sharp_sys_scene_*</c> EDGE collections, the
/// named graph with edge definitions (including cross-collection edges into
/// <c>node_rooms</c>/<c>node_players</c>/<c>node_objects</c>), and persistent
/// indexes on <c>Scene.Status</c> / <c>Scene.ScheduledFor</c> / <c>Scene.IsPublic</c>.
///
/// <para>Phase 5: this migration was moved OUT of the engine's ArangoDB assembly into the
/// Scene plugin. The Scene plugin's <see cref="ScenePlugin"/> surfaces this assembly via
/// <c>IMigrationSource.ArangoMigrationAssembly</c>, so the host's <c>ArangoDatabase.Migrate()</c>
/// runs it in the same upgrade pass as the engine's built-in migrations. The <c>SCENE_ROOM</c>
/// flag is no longer seeded here — it is contributed by the plugin's <c>IFlagSource</c>.</para>
/// </summary>
public class Migration_AddScenes : IArangoMigration
{
	public long Id => 20260619_001;

	public string Name => "add_scenes";

	public async Task Up(IArangoMigrator migrator, ArangoHandle handle)
	{
		// ── Vertex DOCUMENT collections ─────────────────────────────────────────
		if (!await migrator.Context.Collection.ExistAsync(handle, SceneArangoConstants.SharpScenes))
		{
			await migrator.Context.Collection.CreateAsync(handle, new ArangoCollection
			{
				Name = SceneArangoConstants.SharpScenes,
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

			await migrator.Context.Index.CreateAsync(handle, SceneArangoConstants.SharpScenes, new ArangoIndex
			{
				Fields = ["Status"],
				Type = ArangoIndexType.Persistent
			});

			await migrator.Context.Index.CreateAsync(handle, SceneArangoConstants.SharpScenes, new ArangoIndex
			{
				Fields = ["ScheduledFor"],
				Type = ArangoIndexType.Persistent
			});

			await migrator.Context.Index.CreateAsync(handle, SceneArangoConstants.SharpScenes, new ArangoIndex
			{
				Fields = ["IsPublic"],
				Type = ArangoIndexType.Persistent
			});
		}

		if (!await migrator.Context.Collection.ExistAsync(handle, SceneArangoConstants.SharpScenePoses))
		{
			await migrator.Context.Collection.CreateAsync(handle, new ArangoCollection
			{
				Name = SceneArangoConstants.SharpScenePoses,
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

		if (!await migrator.Context.Collection.ExistAsync(handle, SceneArangoConstants.SharpScenePoseEdits))
		{
			await migrator.Context.Collection.CreateAsync(handle, new ArangoCollection
			{
				Name = SceneArangoConstants.SharpScenePoseEdits,
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

		if (!await migrator.Context.Collection.ExistAsync(handle, SceneArangoConstants.SharpScenePlots))
		{
			await migrator.Context.Collection.CreateAsync(handle, new ArangoCollection
			{
				Name = SceneArangoConstants.SharpScenePlots,
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
			SceneArangoConstants.SceneFirstPose,
			SceneArangoConstants.SceneLastPose,
			SceneArangoConstants.ScenePoseNext,
			SceneArangoConstants.ScenePoseInScene,
			SceneArangoConstants.SceneFirstEdit,
			SceneArangoConstants.SceneCurrentEdit,
			SceneArangoConstants.SceneNextEdit,
			SceneArangoConstants.ScenePlotIncludes,
			SceneArangoConstants.SceneMember,
			SceneArangoConstants.SceneInRoom,
			SceneArangoConstants.SceneOwner,
			SceneArangoConstants.SceneStarter,
			SceneArangoConstants.ScenePoseAuthor,
			SceneArangoConstants.ScenePoseOrigin,
			SceneArangoConstants.SceneEditEditor,
			SceneArangoConstants.ScenePlotOwner
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
		if (!sceneGraphs.Any(g => g.Name == SceneArangoConstants.GraphScene))
		{
			await migrator.Context.Graph.CreateAsync(handle, new ArangoGraph
			{
				Name = SceneArangoConstants.GraphScene,
				EdgeDefinitions =
				[
						new ArangoEdgeDefinition
						{
							Collection = SceneArangoConstants.SceneFirstPose,
							From = [SceneArangoConstants.SharpScenes],
							To = [SceneArangoConstants.SharpScenePoses]
						},
						new ArangoEdgeDefinition
						{
							Collection = SceneArangoConstants.SceneLastPose,
							From = [SceneArangoConstants.SharpScenes],
							To = [SceneArangoConstants.SharpScenePoses]
						},
						new ArangoEdgeDefinition
						{
							Collection = SceneArangoConstants.ScenePoseNext,
							From = [SceneArangoConstants.SharpScenePoses],
							To = [SceneArangoConstants.SharpScenePoses]
						},
						new ArangoEdgeDefinition
						{
							Collection = SceneArangoConstants.ScenePoseInScene,
							From = [SceneArangoConstants.SharpScenePoses],
							To = [SceneArangoConstants.SharpScenes]
						},
						new ArangoEdgeDefinition
						{
							Collection = SceneArangoConstants.SceneFirstEdit,
							From = [SceneArangoConstants.SharpScenePoses],
							To = [SceneArangoConstants.SharpScenePoseEdits]
						},
						new ArangoEdgeDefinition
						{
							Collection = SceneArangoConstants.SceneCurrentEdit,
							From = [SceneArangoConstants.SharpScenePoses],
							To = [SceneArangoConstants.SharpScenePoseEdits]
						},
						new ArangoEdgeDefinition
						{
							Collection = SceneArangoConstants.SceneNextEdit,
							From = [SceneArangoConstants.SharpScenePoseEdits],
							To = [SceneArangoConstants.SharpScenePoseEdits]
						},
						new ArangoEdgeDefinition
						{
							Collection = SceneArangoConstants.ScenePlotIncludes,
							From = [SceneArangoConstants.SharpScenePlots],
							To = [SceneArangoConstants.SharpScenes]
						},
						new ArangoEdgeDefinition
						{
							Collection = SceneArangoConstants.SceneMember,
							From = [DatabaseConstants.Players],
							To = [SceneArangoConstants.SharpScenes]
						},
						new ArangoEdgeDefinition
						{
							Collection = SceneArangoConstants.SceneInRoom,
							From = [SceneArangoConstants.SharpScenes],
							To = [DatabaseConstants.Rooms]
						},
						new ArangoEdgeDefinition
						{
							Collection = SceneArangoConstants.SceneOwner,
							From = [SceneArangoConstants.SharpScenes],
							To = [DatabaseConstants.Objects]
						},
						new ArangoEdgeDefinition
						{
							Collection = SceneArangoConstants.SceneStarter,
							From = [SceneArangoConstants.SharpScenes],
							To = [DatabaseConstants.Players]
						},
						new ArangoEdgeDefinition
						{
							Collection = SceneArangoConstants.ScenePoseAuthor,
							From = [SceneArangoConstants.SharpScenePoses],
							To = [DatabaseConstants.Players]
						},
						new ArangoEdgeDefinition
						{
							Collection = SceneArangoConstants.ScenePoseOrigin,
							From = [SceneArangoConstants.SharpScenePoses],
							To = [DatabaseConstants.Rooms]
						},
						new ArangoEdgeDefinition
						{
							Collection = SceneArangoConstants.SceneEditEditor,
							From = [SceneArangoConstants.SharpScenePoseEdits],
							To = [DatabaseConstants.Players]
						},
						new ArangoEdgeDefinition
						{
							Collection = SceneArangoConstants.ScenePlotOwner,
							From = [SceneArangoConstants.SharpScenePlots],
							To = [DatabaseConstants.Objects]
						}
				]
			});
		}
	}

	public Task Down(IArangoMigrator migrator, ArangoHandle handle) => Task.CompletedTask;
}
