using Mediator;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
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

		return await ChannelHelper.GetVisibleChannelOrError(PermissionService, Mediator,
			NotifyService, executor, channelName, true) switch
		{
			SharpChannel channel => await ResizeAsync(PermissionService, Mediator, NotifyService, executor, channel, lines),
			Error<CallState> error => error.Value
		};
	}

	/// <summary>Sets how many lines the channel keeps for <c>@channel/recall</c>.</summary>
	private static async ValueTask<CallState> ResizeAsync(IPermissionService PermissionService, IMediator Mediator,
		INotifyService NotifyService, AnySharpObject executor, SharpChannel channel, MString lines)
	{
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