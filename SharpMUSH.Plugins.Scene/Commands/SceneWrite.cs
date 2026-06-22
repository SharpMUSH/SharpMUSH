using SharpMUSH.Library.DiscriminatedUnions;
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
		var fields = SceneCommandHelper.SplitFields(args ?? MModule.single(string.Empty), 3);
		// here/me/name resolve through the engine LocateService (room empty stays empty = roomless scene).
		var roomDbref = await SceneLocate.ObjectOrSelf(parser, fields[0]);
		var ownerDbref = await SceneLocate.PlayerOrSelf(parser, fields[1]);
		var title = fields[2];

		if (string.IsNullOrEmpty(ownerDbref))
		{
			await notifyService.Notify(executor, "SCENE: /create needs an owner dbref.");
			return MModule.single(SceneCommandHelper.BadArguments);
		}

		var scene = await sceneService.CreateSceneAsync(roomDbref, ownerDbref, title);
		await notifyService.Notify(executor,
			$"SCENE: Created scene #{scene.Id} owned by {scene.OwnerName}.");
		return MModule.single(scene.Id);
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
			await notifyService.Notify(executor, "SCENE: /set needs <sceneId>/<key>=<value>.");
			return MModule.single(SceneCommandHelper.BadArguments);
		}

		var result = await sceneService.SetSceneMetaAsync(sceneId, key!, value.ToPlainText());

		return await result.Match(
			async scene =>
			{
				await notifyService.Notify(executor, $"SCENE: #{scene.Id} {key} set.");
				return MModule.single(scene.Id);
			},
			async _ =>
			{
				await notifyService.Notify(executor, $"SCENE: No scene '{sceneId}'.");
				return MModule.single(SceneCommandHelper.NotFound);
			});
	}
}
