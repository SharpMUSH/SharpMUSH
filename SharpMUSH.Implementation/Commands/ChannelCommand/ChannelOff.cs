using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Implementation.Commands.ChannelCommand;

public static class ChannelOff
{
	public static async ValueTask<CallState> Handle(IMUSHCodeParser parser, MString channelName, MString? arg1)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(parser.Mediator);
		var target = executor;

		if (arg1 is not null)
		{
			var targetName = arg1.ToPlainText();

			var maybeTarget =
				await parser.LocateService.LocatePlayerAndNotifyIfInvalid(parser, executor, executor, targetName);

			switch (maybeTarget)
			{
				case { IsError: true }:
					return new CallState(maybeTarget.AsError.Value);
				case { IsNone: true }:
					return new CallState("#-1 PLAYER NOT FOUND");
			}

			target = maybeTarget.AsAnyObject;
		}

		var maybeChannel = await ChannelHelper.GetChannelOrError(parser, channelName, true);
		if (maybeChannel.IsError)
		{
			return maybeChannel.AsError.Value;
		}

		var channel = maybeChannel.AsChannel;

		// TODO: Announce Channel Join
		await parser.Mediator.Send(new RemoveUserFromChannelCommand(channel, target));

		await parser.NotifyService.Notify(executor, $"CHAT: {target.Object().Name} has been added to {channelName}.");
		return new CallState($"{target.Object().Name} has been added to {channelName}.");
	}
}