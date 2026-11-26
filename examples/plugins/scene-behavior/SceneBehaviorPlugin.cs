using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Plugins;
using SharpMUSH.Plugins.Scene.Models;
using SharpMUSH.Plugins.Scene.Storage;
using SceneModels = SharpMUSH.Plugins.Scene.Models;

namespace SceneBehaviorPlugin;

/// <summary>
/// A plugin that adds a behavior to the Scene System from outside it. plugin.json names <c>scene</c> under
/// <c>dependencies</c>, so the Scene plugin loads first, its services are registered first, and this plugin
/// resolves the Scene plugin's assembly from the Scene plugin's own load context.
/// </summary>
[SharpPlugin]
public sealed class SceneBehaviorPlugin : PluginBase, IServiceRegistrar
{
	public override string Id => "scene-untitled";

	public override IReadOnlyList<string> Dependencies => ["scene"];

	/// <summary>Finds the builder the Scene plugin registered and adds <see cref="UntitledSceneBehavior"/> to it.</summary>
	public void RegisterServices(IServiceCollection services)
	{
		var builder = services
			.Where(d => d.ServiceType == typeof(ISceneSystemBuilder))
			.Select(d => d.ImplementationInstance)
			.OfType<ISceneSystemBuilder>()
			.Single();
		builder.AddBehavior<UntitledSceneBehavior>();
	}
}

/// <summary>
/// Calls a scene created without a title "Untitled scene". Every other call passes through to the next
/// <see cref="ISceneService"/> in the chain unchanged.
/// </summary>
public sealed class UntitledSceneBehavior(ISceneService inner) : ISceneServiceBehavior
{
	public const string DefaultTitle = "Untitled scene";

	public Task<SceneModels.Scene> CreateSceneAsync(string roomDbref, string ownerDbref, string title = "", string pitch = "",
		long? scheduledFor = null, bool isPublic = true, string? joinPolicy = null) =>
		inner.CreateSceneAsync(roomDbref, ownerDbref, string.IsNullOrWhiteSpace(title) ? DefaultTitle : title, pitch,
			scheduledFor, isPublic, joinPolicy);

	public Task<Found<SceneModels.Scene>> GetSceneAsync(string sceneId) =>
		inner.GetSceneAsync(sceneId);

	public Task<FoundResult<SceneModels.Scene>> SetSceneMetaAsync(string sceneId, string key, string value) =>
		inner.SetSceneMetaAsync(sceneId, key, value);

	public Task<IReadOnlyList<SceneModels.Scene>> ListScenesAsync(string filter, string? viewerDbref = null, long? fromUtcMillis = null, long? toUtcMillis = null, int count = 50) =>
		inner.ListScenesAsync(filter, viewerDbref, fromUtcMillis, toUtcMillis, count);

	public Task<Found<SceneModels.Scene>> GetActiveSceneInRoomAsync(string roomDbref) =>
		inner.GetActiveSceneInRoomAsync(roomDbref);

	public Task<IReadOnlyList<SceneModels.Scene>> ListActiveScenesInRoomAsync(string roomDbref) =>
		inner.ListActiveScenesInRoomAsync(roomDbref);

	public Task<FoundResult<ScenePose>> AddPoseAsync(string sceneId, string authorDbref, string showAs, string originDbref, string type, string source, IReadOnlyList<string> tags, string content) =>
		inner.AddPoseAsync(sceneId, authorDbref, showAs, originDbref, type, source, tags, content);

	public Task<Found<ScenePose>> GetPoseAsync(string poseId) =>
		inner.GetPoseAsync(poseId);

	public Task<Found<IReadOnlyList<ScenePose>>> GetPosesAsync(string sceneId, string? authorDbref = null, int? count = null) =>
		inner.GetPosesAsync(sceneId, authorDbref, count);

	public Task<Found<ScenePosePage>> GetPosePageAsync(string sceneId, long? after, int take) =>
		inner.GetPosePageAsync(sceneId, after, take);

	public Task<Found<ScenePose>> SetPoseMetaAsync(string poseId, string key, string value) =>
		inner.SetPoseMetaAsync(poseId, key, value);

	public Task<FoundResult<ScenePose>> EditPoseAsync(string poseId, string editorDbref, string content) =>
		inner.EditPoseAsync(poseId, editorDbref, content);

	public Task<FoundResult<ScenePose>> UndoPoseAsync(string poseId) =>
		inner.UndoPoseAsync(poseId);

	public Task<FoundResult<ScenePose>> RedoPoseAsync(string poseId) =>
		inner.RedoPoseAsync(poseId);

	public Task<FoundResult<ScenePose>> MovePoseAsync(string poseId, string afterPoseId) =>
		inner.MovePoseAsync(poseId, afterPoseId);

	public Task<Found<ScenePose>> DeletePoseAsync(string poseId) =>
		inner.DeletePoseAsync(poseId);

	public Task<Found<IReadOnlyList<ScenePoseEdit>>> GetPoseEditsAsync(string poseId) =>
		inner.GetPoseEditsAsync(poseId);

	public Task<FoundResult<None>> InvitePlayerAsync(string sceneId, string playerDbref) =>
		inner.InvitePlayerAsync(sceneId, playerDbref);

	public Task<FoundResult<None>> UninvitePlayerAsync(string sceneId, string playerDbref) =>
		inner.UninvitePlayerAsync(sceneId, playerDbref);

	public Task<Found<IReadOnlyList<SceneInvitation>>> GetInvitationsAsync(string sceneId) =>
		inner.GetInvitationsAsync(sceneId);

	public Task<FoundResult<SceneMember>> JoinSceneAsync(string sceneId, string playerDbref) =>
		inner.JoinSceneAsync(sceneId, playerDbref);

	public Task<FoundResult<SceneMember>> AddMemberAsync(string sceneId, string playerDbref, string role) =>
		inner.AddMemberAsync(sceneId, playerDbref, role);

	public Task<Found<None>> RemoveMemberAsync(string sceneId, string playerDbref) =>
		inner.RemoveMemberAsync(sceneId, playerDbref);

	public Task<Found<IReadOnlyList<SceneMember>>> GetMembersAsync(string sceneId, string? role = null) =>
		inner.GetMembersAsync(sceneId, role);

	public Task<Found<SceneMember>> GetMemberAsync(string sceneId, string playerDbref) =>
		inner.GetMemberAsync(sceneId, playerDbref);

	public Task<Found<None>> SetFocusAsync(string playerDbref, string? sceneId = null) =>
		inner.SetFocusAsync(playerDbref, sceneId);

	public Task<Found<SceneModels.Scene>> GetCurrentSceneAsync(string playerDbref) =>
		inner.GetCurrentSceneAsync(playerDbref);

	public Task<Found<SceneMember>> SetShowAsAsync(string sceneId, string playerDbref, string showAs) =>
		inner.SetShowAsAsync(sceneId, playerDbref, showAs);

	public Task<ScenePlot> UpsertPlotAsync(string? plotId, string title, string description, string ownerDbref) =>
		inner.UpsertPlotAsync(plotId, title, description, ownerDbref);

	public Task<Found<ScenePlot>> GetPlotAsync(string plotId) =>
		inner.GetPlotAsync(plotId);

	public Task<Found<None>> LinkSceneToPlotAsync(string plotId, string sceneId) =>
		inner.LinkSceneToPlotAsync(plotId, sceneId);

	public Task<Found<None>> UnlinkSceneFromPlotAsync(string plotId, string sceneId) =>
		inner.UnlinkSceneFromPlotAsync(plotId, sceneId);

	public Task<Found<IReadOnlyList<string>>> GetTagsAsync(string sceneId) =>
		inner.GetTagsAsync(sceneId);

	public Task<Found<IReadOnlyList<string>>> GetCastAsync(string sceneId) =>
		inner.GetCastAsync(sceneId);
}
