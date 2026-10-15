using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Markup;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Plugins.Scene.Commands;

/// <summary>
/// NOTICE: a message for one member of a scene, told to them and shown in their live scene view, and to
/// nobody else in it. The scene-safety package sends its anonymous pause signal this way: the member hears it
/// from the object running the command, never from the player who asked.
/// </summary>
public static class SceneNoticeHandlers
{
	public static async ValueTask<MString> Notice(
		IMUSHCodeParser parser,
		ISceneService sceneService,
		INotifyService notifyService,
		AnySharpObject executor,
		MString lhs,
		MString text)
	{
		// @scene/notice <sceneId>/<player>=<text>. The member's dbref carries no creation time, so it is looked up
		// for the object whose connections are told.
		var (sceneId, player) = SceneCommandHelper.SplitIdKey(lhs);
		if (string.IsNullOrEmpty(player))
		{
			await notifyService.Notify(executor, SceneCommandHelper.Notice("/notice needs <sceneId>/<player>=<text>.", NoticeKind.Warn));
			return MarkupText.Plain(SceneCommandHelper.BadArguments);
		}

		var playerDbref = await SceneLocate.PlayerOrSelf(parser, player);
		if (await sceneService.GetMemberAsync(sceneId, playerDbref) is not SceneMember { MemberDbref: { } member }
			|| !DBRef.TryParse(member, out var parsed) || parsed is not { } reference
			|| await parser.ServiceProvider.GetRequiredService<IMediator>().Send(new GetObjectNodeQuery(reference)) is not AnySharpObject recipient)
		{
			await notifyService.Notify(executor, SceneCommandHelper.Notice("No such scene or member.", NoticeKind.Warn));
			return MarkupText.Plain(SceneCommandHelper.NotFound);
		}

		await notifyService.Notify(recipient, text, executor);
		await SceneBroadcast.PublishNoticeAsync(parser, sceneId, member, text);
		return MarkupText.Plain(sceneId);
	}
}
