using OneOf.Types;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Plugins.Scene.Commands;

/// <summary>
/// Pose-scoped switches. All take a globally-unique <c>&lt;poseId&gt;</c> (poses live in
/// their own collection): ADDPOSE, SETPOSE, EDITPOSE, UNDO, REDO, MOVE, DELETE.
/// </summary>
public static class ScenePoseHandlers
{
	public static async ValueTask<MString> AddPose(
		IMUSHCodeParser parser,
		ISceneService sceneService,
		INotifyService notifyService,
		AnySharpObject executor,
		MString sceneIdArg,
		MString rest)
	{
		// @scene/addpose <sceneId>=<authorDbref>,<showAs>,<originDbref>,<source>,<tags>,<content>
		var sceneId = SceneCommandHelper.Plain(sceneIdArg);
		var fields = SceneCommandHelper.SplitFields(rest, 6);
		// author/origin resolve through the engine LocateService (here/me/name -> dbref).
		var authorDbref = await SceneLocate.PlayerOrSelf(parser, fields[0]);
		var showAs = fields[1];
		var originDbref = await SceneLocate.ObjectOrSelf(parser, fields[2]);
		var source = fields[3];
		var tagsRaw = fields[4];
		var content = fields[5];

		if (string.IsNullOrEmpty(authorDbref))
		{
			await notifyService.Notify(executor, "SCENE: /addpose needs an author dbref.");
			return MModule.single(SceneCommandHelper.BadArguments);
		}

		var tags = tagsRaw.Length == 0
			? Array.Empty<string>()
			: tagsRaw.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

		var result = await sceneService.AddPoseAsync(sceneId, authorDbref, showAs, originDbref, source, tags, content);

		return await result.Match(
			async pose =>
			{
				await notifyService.Notify(executor, $"SCENE: Added pose #{pose.Id} to scene #{sceneId}.");
				await SceneBroadcast.PublishSceneEventAsync(parser, sceneId, "pose", pose);
				return MModule.single(pose.Id);
			},
			async _ =>
			{
				await notifyService.Notify(executor, $"SCENE: No scene '{sceneId}'.");
				return MModule.single(SceneCommandHelper.NotFound);
			},
			async err =>
			{
				await notifyService.Notify(executor, $"SCENE: {err.Value}");
				return MModule.single($"#-1 {err.Value}");
			});
	}

	public static async ValueTask<MString> SetPose(
		IMUSHCodeParser parser,
		ISceneService sceneService,
		INotifyService notifyService,
		AnySharpObject executor,
		MString lhs,
		MString value)
	{
		// @scene/setpose <poseId>/<key>=<value>
		var (poseId, key) = SceneCommandHelper.SplitIdKey(lhs);
		if (string.IsNullOrEmpty(key))
		{
			await notifyService.Notify(executor, "SCENE: /setpose needs <poseId>/<key>=<value>.");
			return MModule.single(SceneCommandHelper.BadArguments);
		}

		var result = await sceneService.SetPoseMetaAsync(poseId, key!, value.ToPlainText());
		return await PoseResult(notifyService, executor, poseId, result, $"#{poseId} {key} set.");
	}

	public static async ValueTask<MString> EditPose(
		IMUSHCodeParser parser,
		ISceneService sceneService,
		INotifyService notifyService,
		AnySharpObject executor,
		MString poseIdArg,
		MString rest)
	{
		// @scene/editpose <poseId>=<editorDbref>,<content>
		var poseId = SceneCommandHelper.Plain(poseIdArg);
		var fields = SceneCommandHelper.SplitFields(rest, 2);
		var editorDbref = await SceneLocate.PlayerOrSelf(parser, fields[0]);
		var content = fields[1];

		if (string.IsNullOrEmpty(editorDbref))
		{
			await notifyService.Notify(executor, "SCENE: /editpose needs an editor dbref.");
			return MModule.single(SceneCommandHelper.BadArguments);
		}

		var result = await sceneService.EditPoseAsync(poseId, editorDbref, content);
		return await PoseResult(notifyService, executor, poseId, result, $"#{poseId} edited.",
			parser, "edit");
	}

	public static async ValueTask<MString> Undo(
		IMUSHCodeParser parser,
		ISceneService sceneService,
		INotifyService notifyService,
		AnySharpObject executor,
		MString poseIdArg)
	{
		var poseId = SceneCommandHelper.Plain(poseIdArg);
		var result = await sceneService.UndoPoseAsync(poseId);
		return await PoseResultWithError(notifyService, executor, poseId, result, $"#{poseId} undone.");
	}

	public static async ValueTask<MString> Redo(
		IMUSHCodeParser parser,
		ISceneService sceneService,
		INotifyService notifyService,
		AnySharpObject executor,
		MString poseIdArg)
	{
		var poseId = SceneCommandHelper.Plain(poseIdArg);
		var result = await sceneService.RedoPoseAsync(poseId);
		return await PoseResultWithError(notifyService, executor, poseId, result, $"#{poseId} redone.");
	}

	public static async ValueTask<MString> Move(
		IMUSHCodeParser parser,
		ISceneService sceneService,
		INotifyService notifyService,
		AnySharpObject executor,
		MString poseIdArg,
		MString? afterArg)
	{
		// @scene/move <poseId>=<afterPoseId> (empty = to front)
		var poseId = SceneCommandHelper.Plain(poseIdArg);
		var afterPoseId = SceneCommandHelper.Plain(afterArg);
		var result = await sceneService.MovePoseAsync(poseId, afterPoseId);
		return await PoseResultWithError(notifyService, executor, poseId, result, $"#{poseId} moved.",
			parser, "move");
	}

	public static async ValueTask<MString> Delete(
		IMUSHCodeParser parser,
		ISceneService sceneService,
		INotifyService notifyService,
		AnySharpObject executor,
		MString poseIdArg)
	{
		// @scene/delete <poseId> (soft-delete)
		var poseId = SceneCommandHelper.Plain(poseIdArg);
		var result = await sceneService.DeletePoseAsync(poseId);
		return await PoseResult(notifyService, executor, poseId, result, $"#{poseId} deleted.",
			parser, "delete");
	}

	private static async ValueTask<MString> PoseResult(
		INotifyService notifyService,
		AnySharpObject executor,
		string poseId,
		OneOf.OneOf<Contracts.ScenePose, NotFound> result,
		string successMessage,
		IMUSHCodeParser? parser = null,
		string? eventType = null)
		=> await result.Match(
			async pose =>
			{
				await notifyService.Notify(executor, $"SCENE: {successMessage}");
				if (parser is not null && eventType is not null)
					await SceneBroadcast.PublishSceneEventAsync(parser, pose.SceneId, eventType, pose);
				return MModule.single(pose.Id);
			},
			async _ =>
			{
				await notifyService.Notify(executor, $"SCENE: No pose '{poseId}'.");
				return MModule.single(SceneCommandHelper.NotFound);
			});

	private static async ValueTask<MString> PoseResultWithError(
		INotifyService notifyService,
		AnySharpObject executor,
		string poseId,
		OneOf.OneOf<Contracts.ScenePose, NotFound, Error<string>> result,
		string successMessage,
		IMUSHCodeParser? parser = null,
		string? eventType = null)
		=> await result.Match(
			async pose =>
			{
				await notifyService.Notify(executor, $"SCENE: {successMessage}");
				if (parser is not null && eventType is not null)
					await SceneBroadcast.PublishSceneEventAsync(parser, pose.SceneId, eventType, pose);
				return MModule.single(pose.Id);
			},
			async _ =>
			{
				await notifyService.Notify(executor, $"SCENE: No pose '{poseId}'.");
				return MModule.single(SceneCommandHelper.NotFound);
			},
			async err =>
			{
				await notifyService.Notify(executor, $"SCENE: {err.Value}");
				return MModule.single($"#-1 {err.Value}");
			});
}
