using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models.Scene;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Plugins.Scene.Commands;

/// <summary>
/// Read switches: <c>@scene &lt;sceneId&gt;</c> (bare display), <c>@scene/list [&lt;status&gt;]</c>,
/// <c>@scene/get &lt;sceneId&gt;[/&lt;key&gt;]</c>.
/// </summary>
public static class SceneRead
{
	public static async ValueTask<MString> List(
		IMUSHCodeParser parser,
		ISceneService sceneService,
		INotifyService notifyService,
		AnySharpObject executor,
		MString? statusArg)
	{
		var filter = SceneCommandHelper.Plain(statusArg);
		var scenes = await sceneService.ListScenesAsync(filter, viewerDbref: executor.Object().DBRef.ToString());

		if (scenes.Count == 0)
		{
			await notifyService.Notify(executor, "SCENE: No scenes match.");
			return MModule.single(string.Empty);
		}

		await notifyService.Notify(executor, $"SCENE: {scenes.Count} scene(s).");
		foreach (var scene in scenes)
		{
			await notifyService.Notify(executor, FormatSummary(scene));
		}

		// Fire-and-forget command: hand back the space-separated id list for convenience.
		return MModule.single(string.Join(' ', scenes.Select(s => s.Id)));
	}

	public static async ValueTask<MString> Get(
		IMUSHCodeParser parser,
		ISceneService sceneService,
		INotifyService notifyService,
		AnySharpObject executor,
		MString refArg)
	{
		var (sceneId, key) = SceneCommandHelper.SplitIdKey(refArg);
		var lookup = await sceneService.GetSceneAsync(sceneId);

		return await lookup.Match(
			async scene =>
			{
				if (string.IsNullOrEmpty(key))
				{
					await notifyService.Notify(executor, FormatSummary(scene));
					return MModule.single(scene.Id);
				}

				var value = ReadKey(scene, key!);
				await notifyService.Notify(executor, $"SCENE: {scene.Id}/{key} = {value}");
				return MModule.single(value);
			},
			async _ =>
			{
				await notifyService.Notify(executor, $"SCENE: No scene '{sceneId}'.");
				return MModule.single(SceneCommandHelper.NotFound);
			});
	}

	public static async ValueTask<MString> Display(
		IMUSHCodeParser parser,
		ISceneService sceneService,
		INotifyService notifyService,
		AnySharpObject executor,
		MString sceneIdArg)
	{
		var sceneId = SceneCommandHelper.Plain(sceneIdArg);
		var lookup = await sceneService.GetSceneAsync(sceneId);

		return await lookup.Match(
			async scene =>
			{
				await notifyService.Notify(executor, FormatSummary(scene));
				var members = await sceneService.GetMembersAsync(scene.Id);
				if (members.IsT0)
				{
					foreach (var member in members.AsT0)
					{
						await notifyService.Notify(executor,
							$"  [{member.Role}] {member.MemberName}" +
							(string.IsNullOrEmpty(member.ShowAs) ? string.Empty : $" (as {member.ShowAs})") +
							(member.IsCurrent ? " *" : string.Empty));
					}
				}

				return MModule.single(scene.Id);
			},
			async _ =>
			{
				await notifyService.Notify(executor, $"SCENE: No scene '{sceneId}'.");
				return MModule.single(SceneCommandHelper.NotFound);
			});
	}

	private static string FormatSummary(Library.Models.Scene.Scene scene)
		=> $"SCENE #{scene.Id} [{scene.Status}] {(scene.Meta.TryGetValue("title", out var t) ? t : "(untitled)")}" +
		   $" — owner {scene.OwnerName}, room {(string.IsNullOrEmpty(scene.RoomName) ? "(roomless)" : scene.RoomName)}," +
		   $" {scene.PoseCount} pose(s)";

	private static string ReadKey(Library.Models.Scene.Scene scene, string key)
		=> key.ToLowerInvariant() switch
		{
			"status" => scene.Status,
			"public" => scene.IsPublic ? "1" : "0",
			"istemp" => scene.IsTempRoom ? "1" : "0",
			"scheduledfor" => scene.ScheduledFor?.ToString() ?? string.Empty,
			"room" => scene.RoomName,
			"owner" => scene.OwnerName,
			"starter" => scene.StarterName,
			"posecount" => scene.PoseCount.ToString(),
			"startedat" => scene.StartedAt.ToString(),
			"lastactivityat" => scene.LastActivityAt.ToString(),
			_ => scene.Meta.TryGetValue(key, out var v) ? v : string.Empty,
		};
}
