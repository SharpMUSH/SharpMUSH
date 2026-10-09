using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Markup;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Plugins.Scene.Commands;

/// <summary>
/// Scene-level write switches: <c>@scene/create &lt;roomDbref&gt;,&lt;ownerDbref&gt;[,&lt;title&gt;]</c>
/// and <c>@scene/set &lt;sceneId&gt;/&lt;key&gt;=&lt;value&gt;</c>.
/// </summary>
public static class SceneWrite
{
	public static async ValueTask<MString> Create(
		IMUSHCodeParser parser,
		ISceneService sceneService,
		INotifyService notifyService,
		AnySharpObject executor,
		MString? args)
	{
		// <roomDbref>,<ownerDbref>[,<title>] — roomDbref empty → roomless scheduled scene.
		var fields = SceneCommandHelper.SplitFields(args ?? MarkupText.Empty, 3);
		// here/me/name resolve through the engine LocateService (room empty stays empty = roomless scene).
		var roomDbref = await SceneLocate.ObjectOrSelf(parser, fields[0]);
		var ownerDbref = await SceneLocate.PlayerOrSelf(parser, fields[1]);
		var title = fields[2];

		if (string.IsNullOrEmpty(ownerDbref))
		{
			await notifyService.Notify(executor, SceneCommandHelper.Notice("/create needs an owner dbref.", NoticeKind.Warn));
			return MarkupText.Plain(SceneCommandHelper.BadArguments);
		}

		var scene = await sceneService.CreateSceneAsync(roomDbref, ownerDbref, title);
		await notifyService.Notify(executor,
			SceneCommandHelper.Notice($"Created scene #{scene.Id} owned by {scene.OwnerName}.", NoticeKind.Ok));
		return MarkupText.Plain(scene.Id);
	}

	public static async ValueTask<MString> Set(
		IMUSHCodeParser parser,
		ISceneService sceneService,
		INotifyService notifyService,
		AnySharpObject executor,
		MString lhs,
		MString value)
	{
		// <sceneId>/<key>=<value>
		var (sceneId, key) = SceneCommandHelper.SplitIdKey(lhs);
		if (string.IsNullOrEmpty(key))
		{
			await notifyService.Notify(executor, SceneCommandHelper.Notice("/set needs <sceneId>/<key>=<value>.", NoticeKind.Warn));
			return MarkupText.Plain(SceneCommandHelper.BadArguments);
		}

		var roomBefore = await SceneRoomRefresh.RoomOfAsync(sceneService, sceneId);
		if (await sceneService.SetSceneMetaAsync(sceneId, key!, value.ToPlainText()) is not Contracts.Scene scene)
		{
			await notifyService.Notify(executor, SceneCommandHelper.Notice($"No scene '{sceneId}'.", NoticeKind.Warn));
			return MarkupText.Plain(SceneCommandHelper.NotFound);
		}

		await SceneRoomRefresh.AfterSetAsync(parser, key!, roomBefore, scene);

		await notifyService.Notify(executor, SceneCommandHelper.Notice($"#{scene.Id} {key} set.", NoticeKind.Ok));
		return MarkupText.Plain(scene.Id);
	}
}
