using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Markup;
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
			await notifyService.Notify(executor, SceneCommandHelper.Notice("/member needs <sceneId>/<role>=<playerDbref>.", NoticeKind.Warn));
			return MarkupText.Plain(SceneCommandHelper.BadArguments);
		}

		var playerDbref = await SceneLocate.PlayerOrSelf(parser, playerArg.ToPlainText().Trim());
		if (await sceneService.AddMemberAsync(sceneId, playerDbref, role!) is not SceneMember member)
		{
			await notifyService.Notify(executor, SceneCommandHelper.Notice($"No scene '{sceneId}'.", NoticeKind.Warn));
			return MarkupText.Plain(SceneCommandHelper.NotFound);
		}

		await SceneRoomRefresh.AfterMembershipAsync(parser, sceneService, sceneId);
		await notifyService.Notify(executor,
			SceneCommandHelper.Notice($"{member.MemberName} is now '{member.Role}' in scene #{sceneId}.", NoticeKind.Ok));
		return MarkupText.Plain(sceneId);
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
		if (await sceneService.RemoveMemberAsync(sceneId, playerDbref) is NotFound)
		{
			await notifyService.Notify(executor, SceneCommandHelper.Notice($"No scene '{sceneId}'.", NoticeKind.Warn));
			return MarkupText.Plain(SceneCommandHelper.NotFound);
		}

		await SceneRoomRefresh.AfterMembershipAsync(parser, sceneService, sceneId);
		await notifyService.Notify(executor, SceneCommandHelper.Notice($"Removed {playerDbref} from scene #{sceneId}.", NoticeKind.Ok));
		return MarkupText.Plain(sceneId);
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
		var focusBefore = await SceneRoomRefresh.FocusOfAsync(sceneService, playerDbref);
		if (await sceneService.SetFocusAsync(playerDbref, string.IsNullOrEmpty(sceneId) ? null : sceneId) is NotFound)
		{
			await notifyService.Notify(executor, SceneCommandHelper.Notice($"No scene '{sceneId}'.", NoticeKind.Warn));
			return MarkupText.Plain(SceneCommandHelper.NotFound);
		}

		await SceneRoomRefresh.AfterFocusAsync(parser, sceneService, playerDbref, focusBefore,
			await SceneRoomRefresh.FocusOfAsync(sceneService, playerDbref));

		await notifyService.Notify(executor, string.IsNullOrEmpty(sceneId)
			? SceneCommandHelper.Notice($"Cleared focus for {playerDbref}.", NoticeKind.Ok)
			: SceneCommandHelper.Notice($"{playerDbref} now focused on scene #{sceneId}.", NoticeKind.Ok));
		return MarkupText.Plain(sceneId);
	}

	public static async ValueTask<MString> ShowAs(
		IMUSHCodeParser parser,
		ISceneService sceneService,
		INotifyService notifyService,
		AnySharpObject executor,
		MString lhs,
		MString nameArg)
	{
		// @scene/showas <sceneId>/<playerDbref>=<name>; an empty name clears the persona.
		var (sceneId, playerDbref) = SceneCommandHelper.SplitIdKey(lhs);
		if (string.IsNullOrEmpty(playerDbref))
		{
			await notifyService.Notify(executor, SceneCommandHelper.Notice("/showas needs <sceneId>/<playerDbref>=<name>.", NoticeKind.Warn));
			return MarkupText.Plain(SceneCommandHelper.BadArguments);
		}

		var resolvedPlayer = await SceneLocate.PlayerOrSelf(parser, playerDbref!);
		if (await sceneService.SetShowAsAsync(sceneId, resolvedPlayer, nameArg.ToPlainText()) is not SceneMember member)
		{
			await notifyService.Notify(executor, SceneCommandHelper.Notice("No such scene or member.", NoticeKind.Warn));
			return MarkupText.Plain(SceneCommandHelper.NotFound);
		}

		await notifyService.Notify(executor, member.ShowAs.Length == 0
			? SceneCommandHelper.Notice($"{member.MemberName} shown under their own name in scene #{sceneId}.", NoticeKind.Ok)
			: SceneCommandHelper.Notice($"{member.MemberName} now shown as '{member.ShowAs}' in scene #{sceneId}.", NoticeKind.Ok));
		return MarkupText.Plain(sceneId);
	}
}
