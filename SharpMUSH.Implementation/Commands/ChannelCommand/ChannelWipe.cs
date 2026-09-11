using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Implementation.Commands.ChannelCommand;

/// <summary>
/// <c>@channel/wipe &lt;channel&gt;</c> — PennMUSH <c>do_chan_wipe</c> / <c>channel_wipe</c>
/// (<c>src/extchat.c:2216-2255</c>): remove every member from the channel, telling each of them who did
/// it. Resizing the recall buffer is <c>@channel/buffer</c>'s job, not this one's.
/// </summary>
public static class ChannelWipe
{
	public static async ValueTask<CallState> Handle(IMUSHCodeParser parser, ILocateService LocateService,
		IPermissionService PermissionService, IMediator Mediator, INotifyService NotifyService, MString channelName,
		MString message)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		if (await executor.IsGuest())
		{
			await NotifyService.Notify(executor, ErrorMessages.Notifications.ChatGuestsCantModify, executor);
			return new CallState(ErrorMessages.Returns.GuestsCannotModifyChannels);
		}

		return await ChannelHelper.GetVisibleChannelOrError(PermissionService, Mediator,
			NotifyService, executor, channelName, true) switch
		{
			SharpChannel channel => await WipeAsync(PermissionService, Mediator, NotifyService, executor, channel),
			Error<CallState> error => error.Value
		};
	}

	private static async ValueTask<CallState> WipeAsync(IPermissionService PermissionService, IMediator Mediator,
		INotifyService NotifyService, AnySharpObject executor, SharpChannel channel)
	{
		if (!await PermissionService.ChannelCanModifyAsync(executor, channel))
		{
			await NotifyService.Notify(executor, ErrorMessages.Notifications.ChatWipeThatSillyGrin, executor);
			return new CallState(ErrorMessages.Returns.YouCannotModifyThisChannel);
		}

		// Materialised before the removals, which write to the very stream being read.
		var members = await channel.Members.Value.ToArrayAsync();
		var channelLabel = channel.Name.ToPlainText();

		foreach (var (member, _) in members)
		{
			await Mediator.Send(new RemoveUserFromChannelCommand(channel, member));
			await NotifyService.Notify(member,
				string.Format(ErrorMessages.Notifications.ChatRemovedAllUsers, executor.Object().Name, channelLabel),
				executor);
		}

		var wiped = string.Format(ErrorMessages.Notifications.ChatChannelWiped, channelLabel);
		await NotifyService.Notify(executor, wiped, executor);
		return new CallState(wiped);
	}
}
