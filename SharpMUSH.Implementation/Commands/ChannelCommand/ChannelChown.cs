using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Implementation.Commands.ChannelCommand;

public static class ChannelChown
{
	public static async ValueTask<CallState> Handle(IMUSHCodeParser parser, MString channelName, MString newOwner)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(parser.Mediator);
		if (await executor.IsGuest())
		{
			await parser.NotifyService.Notify(executor, "CHAT: Guests may not modify channels.");
			return new CallState("#-1 Guests may not modify channels.");
		}
		
		var maybeChannel = await ChannelHelper.GetChannelOrError(parser, channelName, true);

		if (maybeChannel.IsError)
		{
			return maybeChannel.AsError.Value;
		}

		var channel = maybeChannel.AsChannel;
		
		if (await parser.PermissionService.ChannelCanModifyAsync(executor, channel))
		{
			return new CallState("#-1 YOU CANNOT MODIFY THIS CHANNEL.");
		}

		var locate =
			await parser.LocateService.LocatePlayerAndNotifyIfInvalid(parser, executor, executor, newOwner.ToPlainText());

		switch (locate)
		{
			case { IsError: true }:
				return new CallState(locate.AsError.Value);
			case { IsNone: true }:
				return new CallState("#-1 PLAYER NOT FOUND");
		}

		var newOwnerObject = locate.AsPlayer;

		await parser.Mediator.Send(new UpdateChannelOwnerCommand(channel, newOwnerObject));

		var output = MModule.multiple([MModule.single("CHAT: "), MModule.single(newOwnerObject.Object().Name), MModule.single(" is the new owner of "), channel.Name]);
		await parser.NotifyService.Notify(executor, output);
		return new CallState(string.Empty);
	}
}