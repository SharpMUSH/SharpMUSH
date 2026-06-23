namespace SharpMUSH.Plugins.Scene.Storage;

/// <summary>
/// Scene-System collection, edge, and named-graph names for the ArangoDB backend. These moved OUT of the
/// engine's <c>DatabaseConstants</c> into the Scene plugin (Phase 8): removing the plugin DLL leaves core
/// with no knowledge of the scene graph. Both the relocated <see cref="ArangoSceneStorage"/> and the
/// plugin's <c>Migration_AddScenes</c> reference these from here.
/// </summary>
public static class SceneArangoConstants
{
	// ── Vertex DOCUMENT collections ─────────────────────────────────────────────
	public const string SharpScenes = "node_sharp_sys_scene_scenes";
	public const string SharpScenePoses = "node_sharp_sys_scene_poses";
	public const string SharpScenePoseEdits = "node_sharp_sys_scene_pose_edits";
	public const string SharpScenePlots = "node_sharp_sys_scene_plots";

	// ── EDGE collections (comments note from -> to vertex collections) ──────────
	public const string SceneFirstPose = "edge_sharp_sys_scene_first_pose";       // SharpScenes -> SharpScenePoses
	public const string SceneLastPose = "edge_sharp_sys_scene_last_pose";         // SharpScenes -> SharpScenePoses
	public const string ScenePoseNext = "edge_sharp_sys_scene_pose_next";         // SharpScenePoses -> SharpScenePoses
	public const string ScenePoseInScene = "edge_sharp_sys_scene_pose_in_scene";  // SharpScenePoses -> SharpScenes
	public const string SceneFirstEdit = "edge_sharp_sys_scene_first_edit";       // SharpScenePoses -> SharpScenePoseEdits
	public const string SceneCurrentEdit = "edge_sharp_sys_scene_current_edit";   // SharpScenePoses -> SharpScenePoseEdits
	public const string SceneNextEdit = "edge_sharp_sys_scene_next_edit";         // SharpScenePoseEdits -> SharpScenePoseEdits
	public const string ScenePlotIncludes = "edge_sharp_sys_scene_plot_includes"; // SharpScenePlots -> SharpScenes
	public const string SceneMember = "edge_sharp_sys_scene_member";              // Players -> SharpScenes
	public const string SceneInRoom = "edge_sharp_sys_scene_in_room";             // SharpScenes -> Rooms
	public const string SceneOwner = "edge_sharp_sys_scene_owner";                // SharpScenes -> Objects
	public const string SceneStarter = "edge_sharp_sys_scene_starter";            // SharpScenes -> Players
	public const string ScenePoseAuthor = "edge_sharp_sys_scene_author";          // SharpScenePoses -> Players
	public const string ScenePoseOrigin = "edge_sharp_sys_scene_origin";          // SharpScenePoses -> Rooms
	public const string SceneEditEditor = "edge_sharp_sys_scene_editor";          // SharpScenePoseEdits -> Players
	public const string ScenePlotOwner = "edge_sharp_sys_scene_plotowner";        // SharpScenePlots -> Objects

	// ── Named graph ─────────────────────────────────────────────────────────────
	public const string GraphScene = "graph_sharp_sys_scene";
}
