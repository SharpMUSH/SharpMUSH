using Mediator;
using SharpMUSH.Library.Common;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Implementation.Commands.ChannelCommand;

/// <summary>
/// <c>@channel/who</c> — PennMUSH <c>do_channel_who</c> (<c>src/extchat.c:2955-2985</c>): a THING always,
/// a player while connected, and a member hiding on the channel only to a viewer with <c>Priv_Who</c>.
/// <c>Chanuser_Hide</c> is the whole of what <c>@channel/hide</c> does, and this is one of its two
/// readers.
/// </summary>
public static class ChannelWho
{
	public static async ValueTask<CallState> Handle(IMUSHCodeParser parser, ILocateService locateService,
		IPermissionService permissionService, IMediator mediator, INotifyService notifyService,
		IConnectionService connectionService, MString channelName)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(mediator);
		// extchat.c:1210 gates the WHO branch of do_channel on Chan_Can_See before it will list a member.
		return await ChannelHelper.GetVisibleChannelOrError(permissionService, mediator,
			notifyService, executor, channelName, notify: true) switch
		{
			SharpChannel channel => await ListMembersAsync(notifyService, connectionService, executor, channel),
			Error<CallState> error => error.Value
		};
	}

	private static async ValueTask<CallState> ListMembersAsync(INotifyService notifyService,
		IConnectionService connectionService, AnySharpObject executor, SharpChannel channel)
	{
		var privilegedWho = await ChannelHelper.PrivilegedWho(executor);

		var listed = (await ChannelHelper.ChannelMembers(connectionService, channel))
			.Where(x => x.ListedAsOn(privilegedWho))
			.ToList();

		if (listed.Count == 0)
		{
			await notifyService.Notify(executor, ErrorMessages.Notifications.ChatNoConnectedPlayersOnChannel, executor);
			return new CallState(ErrorMessages.Notifications.ChatNoConnectedPlayersOnChannel);
		}

		// extchat.c:2969-2977 — a THING carries its dbref, and a member's own hide/gag state is annotated
		// (only a Priv_Who viewer ever reaches the "(hidden)" branch, since nobody else is shown them).
		var names = listed
			.Select(MString (member) => MarkupText.Plain(
				member.Object.Object().Name
				+ (member.IsThing ? $"(#{member.Object.Object().DBRef.Number})" : string.Empty)
				+ (member.Hidden, member.Gagging) switch
				{
					(true, true) => " (hidden,gagging)",
					(true, false) => " (hidden)",
					(false, true) => " (gagging)",
					_ => string.Empty
				}))
			.ToList();

		var memberOutput = MarkupText.Concat([
			MarkupText.Plain(string.Format(ErrorMessages.Notifications.ChatMembersOfChannelAre,
				channel.Name.ToPlainText())),
			MarkupText.NewLine,
			MessageFormatting.FormatMStringsWithOxfordComma(names)
		]);

		await notifyService.Notify(executor, memberOutput);

		return new CallState(string.Join(" ",
			listed.Select(x => x.Object.Object().DBRef.ToString())));
	}
}
