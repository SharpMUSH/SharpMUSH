using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Commands.ChannelCommand;

public static class ChannelDescribe
{
	public static async ValueTask<CallState> Handle(IMUSHCodeParser parser, ILocateService LocateService, IPermissionService PermissionService, IMediator Mediator, INotifyService NotifyService, MString channelName, MString description)
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
			SharpChannel channel => await DescribeAsync(PermissionService, Mediator, NotifyService, executor, channel,
				description),
			Error<CallState> error => error.Value
		};
	}

	private static async ValueTask<CallState> DescribeAsync(IPermissionService PermissionService, IMediator Mediator,
		INotifyService NotifyService, AnySharpObject executor, SharpChannel channel, MString description)
	{
		if (!await PermissionService.ChannelCanModifyAsync(executor, channel))
		{
			await NotifyService.Notify(executor, "You cannot modify this channel.", executor);
			return new CallState("You cannot modify this channel.");
		}

		await Mediator.Send(new UpdateChannelCommand(channel,
			null,
			description,
			null,
			null,
			null,
								null,
			null,
			null,
			null,
			null));

		var describeResult = string.Format(ErrorMessages.Notifications.ChatChannelDescSet, channel.Name.ToPlainText());
		await NotifyService.Notify(executor, describeResult, executor);
		return new CallState(describeResult);
	}
}