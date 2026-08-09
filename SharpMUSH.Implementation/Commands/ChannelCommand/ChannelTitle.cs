using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.Definitions;

namespace SharpMUSH.Implementation.Commands.ChannelCommand;

public static class ChannelTitle
{
	/// <summary>
	/// <c>@channel/title &lt;channel&gt;=&lt;title&gt;</c> — sharpchat.md:236: "sets your title on
	/// &lt;channel&gt;. Your title appears in front of your name when you speak on the channel...
	/// If &lt;title&gt; is not given, your title is cleared." It is the speaker's own title, so it
	/// requires channel membership rather than channel ownership.
	/// </summary>
	public static async ValueTask<CallState> Handle(IMUSHCodeParser parser, ILocateService LocateService, IPermissionService PermissionService, IMediator Mediator, INotifyService NotifyService, MString channelName, MString title)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		if (await executor.IsGuest())
		{
			await NotifyService.Notify(executor, ErrorMessages.Notifications.ChatGuestsCantModify, executor);
			return new CallState(ErrorMessages.Returns.GuestsCannotModifyChannels);
		}

		var maybeChannel = await ChannelHelper.GetVisibleChannelOrError(parser, PermissionService, Mediator,
			NotifyService, executor, channelName, true);

		if (maybeChannel.IsError)
		{
			return maybeChannel.AsError.Value;
		}

		var channel = maybeChannel.AsChannel;

		var memberStatus = await ChannelHelper.ChannelMemberStatus(executor, channel);
		if (memberStatus is null)
		{
			var notOn = string.Format(ErrorMessages.Notifications.ChatNotOnChannel, channel.Name.ToPlainText());
			await NotifyService.Notify(executor, notOn, executor);
			return new CallState(notOn);
		}

		var (_, status) = memberStatus;
		var cleared = MModule.getLength(title) == 0;

		await Mediator.Send(new UpdateChannelUserStatusCommand(channel, executor,
			status with { Title = cleared ? MModule.empty() : title }));

		var response = cleared
			? $"CHAT: Title cleared on {channel.Name.ToPlainText()}."
			: $"CHAT: Title set on {channel.Name.ToPlainText()}.";
		await NotifyService.Notify(executor, response, executor);
		return new CallState(response);
	}
}
