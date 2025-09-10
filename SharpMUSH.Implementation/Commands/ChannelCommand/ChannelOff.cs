using Mediator;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Commands.ChannelCommand;

public static class ChannelOff
{
	public static async ValueTask<CallState> Handle(IMUSHCodeParser parser, ILocateService LocateService, IPermissionService PermissionService, IMediator Mediator, INotifyService NotifyService, MString channelName, MString? arg1)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var target = executor;

		if (arg1 is not null)
		{
			var targetName = arg1.ToPlainText();

			var maybeTarget =
				await LocateService!.LocatePlayerAndNotifyIfInvalid(parser, executor, executor, targetName);

			switch (maybeTarget)
			{
				case { IsError: true }:
					return new CallState(maybeTarget.AsError.Value);
				case { IsNone: true }:
					return new CallState("#-1 PLAYER NOT FOUND");
			}

			target = maybeTarget.AsAnyObject;
		}

		var maybeChannel = await ChannelHelper.GetChannelOrError(parser, LocateService, PermissionService, Mediator, NotifyService, channelName, true);
		if (maybeChannel.IsError)
		{
			return maybeChannel.AsError.Value;
		}

		var channel = maybeChannel.AsChannel;

		// TODO: Announce Channel Join
		await Mediator!.Send(new RemoveUserFromChannelCommand(channel, target));

		await NotifyService!.Notify(executor, $"CHAT: {target.Object().Name} has been added to {channelName}.");
		return new CallState($"{target.Object().Name} has been added to {channelName}.");
	}
}