using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Plugins.Scene.Commands;

/// <summary>
/// Membership / focus switches: MEMBER, UNMEMBER, FOCUS, SHOWAS.
/// </summary>
public static class SceneMemberHandlers
{
	public static async ValueTask<MString> AddMember(
		IMUSHCodeParser parser,
		ISceneService sceneService,
		INotifyService notifyService,
		AnySharpObject executor,
		MString lhs,
		MString playerArg)
	{
		// @scene/member <sceneId>/<role>=<playerDbref>
		var (sceneId, role) = SceneCommandHelper.SplitIdKey(lhs);
		if (string.IsNullOrEmpty(role))
		{
			await notifyService.Notify(executor, "SCENE: /member needs <sceneId>/<role>=<playerDbref>.");
			return MModule.single(SceneCommandHelper.BadArguments);
		}

		var playerDbref = await SceneLocate.PlayerOrSelf(parser, playerArg.ToPlainText().Trim());
		var result = await sceneService.AddMemberAsync(sceneId, playerDbref, role!);

		return await result.Match(
			async member =>
			{
				await notifyService.Notify(executor,
					$"SCENE: {member.MemberName} is now '{member.Role}' in scene #{sceneId}.");
				return MModule.single(sceneId);
			},
			async _ =>
			{
				await notifyService.Notify(executor, $"SCENE: No scene '{sceneId}'.");
				return MModule.single(SceneCommandHelper.NotFound);
			});
	}

	public static async ValueTask<MString> RemoveMember(
		IMUSHCodeParser parser,
		ISceneService sceneService,
		INotifyService notifyService,
		AnySharpObject executor,
		MString sceneIdArg,
		MString playerArg)
	{
		// @scene/unmember <sceneId>=<playerDbref> (drops all the player's roles)
		var sceneId = SceneCommandHelper.Plain(sceneIdArg);
		var playerDbref = await SceneLocate.PlayerOrSelf(parser, playerArg.ToPlainText().Trim());
		var result = await sceneService.RemoveMemberAsync(sceneId, playerDbref);

		return await result.Match(
			async _ =>
			{
				await notifyService.Notify(executor, $"SCENE: Removed {playerDbref} from scene #{sceneId}.");
				return MModule.single(sceneId);
			},
			async _ =>
			{
				await notifyService.Notify(executor, $"SCENE: No scene '{sceneId}'.");
				return MModule.single(SceneCommandHelper.NotFound);
			});
	}

	public static async ValueTask<MString> Focus(
		IMUSHCodeParser parser,
		ISceneService sceneService,
		INotifyService notifyService,
		AnySharpObject executor,
		MString playerArg,
		MString? sceneArg)
	{
		// @scene/focus <playerDbref>=<sceneId> (empty = clear)
		var playerDbref = await SceneLocate.PlayerOrSelf(parser, playerArg.ToPlainText().Trim());
		var sceneId = SceneCommandHelper.Plain(sceneArg);
		var result = await sceneService.SetFocusAsync(playerDbref, string.IsNullOrEmpty(sceneId) ? null : sceneId);

		return await result.Match(
			async _ =>
			{
				await notifyService.Notify(executor, string.IsNullOrEmpty(sceneId)
					? $"SCENE: Cleared focus for {playerDbref}."
					: $"SCENE: {playerDbref} now focused on scene #{sceneId}.");
				return MModule.single(sceneId);
			},
			async _ =>
			{
				await notifyService.Notify(executor, $"SCENE: No scene '{sceneId}'.");
				return MModule.single(SceneCommandHelper.NotFound);
			});
	}

	public static async ValueTask<MString> ShowAs(
		IMUSHCodeParser parser,
		ISceneService sceneService,
		INotifyService notifyService,
		AnySharpObject executor,
		MString lhs,
		MString nameArg)
	{
		// @scene/showas <sceneId>/<playerDbref>=<name>
		var (sceneId, playerDbref) = SceneCommandHelper.SplitIdKey(lhs);
		if (string.IsNullOrEmpty(playerDbref))
		{
			await notifyService.Notify(executor, "SCENE: /showas needs <sceneId>/<playerDbref>=<name>.");
			return MModule.single(SceneCommandHelper.BadArguments);
		}

		var resolvedPlayer = await SceneLocate.PlayerOrSelf(parser, playerDbref!);
		var result = await sceneService.SetShowAsAsync(sceneId, resolvedPlayer, nameArg.ToPlainText());

		return await result.Match(
			async member =>
			{
				await notifyService.Notify(executor,
					$"SCENE: {member.MemberName} now shown as '{member.ShowAs}' in scene #{sceneId}.");
				return MModule.single(sceneId);
			},
			async _ =>
			{
				await notifyService.Notify(executor, $"SCENE: No such scene or member.");
				return MModule.single(SceneCommandHelper.NotFound);
			});
	}
}
