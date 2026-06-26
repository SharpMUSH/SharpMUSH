using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.Definitions;

namespace SharpMUSH.Implementation.Commands.ChannelCommand;

public static class ChannelDecompile
{
	public static async ValueTask<CallState> Handle(IMUSHCodeParser parser, ILocateService LocateService, IPermissionService PermissionService, IMediator Mediator, INotifyService NotifyService, MString channelName, MString brief, string[] switches)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		if (await executor.IsGuest())
		{
			await NotifyService.Notify(executor, ErrorMessages.Notifications.ChatGuestsCantModify, executor);
			return new CallState(ErrorMessages.Returns.GuestsCannotModifyChannels);
		}

		var maybeChannel = await ChannelHelper.GetChannelOrError(parser, LocateService, PermissionService, Mediator, NotifyService, channelName, true);

		if (maybeChannel.IsError)
		{
			return maybeChannel.AsError.Value;
		}

		var channel = maybeChannel.AsChannel;

		if (!await PermissionService.ChannelCanModifyAsync(executor, channel))
		{
			return new CallState("You cannot modify this channel.");
		}

		var channelOwner = await channel.Owner.WithCancellation(CancellationToken.None);
		var commands = new List<string>
		{
			$"@channel/add {channel.Name.ToPlainText()}",
		};

		var descText = channel.Description.ToPlainText();
		if (!string.IsNullOrEmpty(descText))
		{
			commands.Add($"@channel/desc {channel.Name.ToPlainText()}={descText}");
		}

		if (!string.IsNullOrEmpty(channel.Mogrifier))
		{
			commands.Add($"@channel/mogrifier {channel.Name.ToPlainText()}={channel.Mogrifier}");
		}

		// Note: This would require examining channel.Flags and outputting appropriate flag commands

		foreach (var command in commands)
		{
			await NotifyService.Notify(executor, command, executor);
		}

		return new CallState(string.Empty);
	}
}