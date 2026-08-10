using Mediator;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Commands.ChannelCommand;

public static class ChannelBuffer
{
	public static async ValueTask<CallState> Handle(IMUSHCodeParser parser, ILocateService LocateService, IPermissionService PermissionService, IMediator Mediator, INotifyService NotifyService, IOptionsWrapper<SharpMUSHOptions> Configuration, MString channelName, MString lines)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		if (await executor.IsGuest())
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ChatGuestsCantModify), executor);
			return new CallState(ErrorMessages.Returns.GuestsCannotModifyChannels);
		}

		var maybeChannel = await ChannelHelper.GetVisibleChannelOrError(parser, PermissionService, Mediator,
			NotifyService, executor, channelName, true);

		if (maybeChannel.IsError)
		{
			return maybeChannel.AsError.Value;
		}

		var channel = maybeChannel.AsChannel;

		// The sense of this check was inverted: whoever COULD modify the channel was refused,
		// and whoever could not fell through and made the change.
		if (!await PermissionService.ChannelCanModifyAsync(executor, channel))
		{
			await NotifyService.Notify(executor, "You cannot modify this channel.", executor);
			return new CallState("You cannot modify this channel.");
		}

		if (!int.TryParse(lines.ToPlainText(), out var linesInt))
		{
			return new CallState("Invalid number of lines.");
		}

		await Mediator.Send(new UpdateChannelCommand(
			Channel: channel,
			null,
			null,
			null,
			null,
			null,
			null,
			null,
			null,
			null,
			Buffer: linesInt));

		var bufferResult = string.Format(ErrorMessages.Notifications.ChatResizingBuffer, channel.Name.ToPlainText());
		await NotifyService.Notify(executor, bufferResult, executor);
		return new CallState(bufferResult);
	}
}