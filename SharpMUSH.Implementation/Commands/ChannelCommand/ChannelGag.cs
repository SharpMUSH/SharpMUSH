using System.Collections.Immutable;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Commands.ChannelCommand;

public static class ChannelGag
{
	public static async ValueTask<CallState> Handle(IMUSHCodeParser parser, MString? channelName, MString? yesNo,
		string[] switches)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(parser.Mediator);
		ImmutableArray<SharpChannel> channels;

		if (await executor.IsGuest())
		{
			await parser.NotifyService.Notify(executor, "CHAT: Guests may not modify channels.");
			return new CallState("#-1 Guests may not modify channels.");
		}

		var yesNoString = yesNo?.ToPlainText();
		if (yesNoString is not null && !(yesNoString.Equals("yes", StringComparison.InvariantCultureIgnoreCase) ||
		                                 yesNoString.Equals("no", StringComparison.InvariantCultureIgnoreCase)))
		{
			await parser.NotifyService.Notify(executor, "CHAT: Yes or No are the only valid options.");
			return new CallState("#-1 INVALID OPTION");
		}

		if (channelName is null)
		{
			channels = [..await parser.Mediator.Send(new GetChannelListQuery())];
		}
		else
		{
			var maybeChannel = await ChannelHelper.GetChannelOrError(parser, channelName!, true);
			if (maybeChannel.IsError)
			{
				return maybeChannel.AsError.Value;
			}

			channels = [maybeChannel.AsChannel];
		}
		
		var gagOn = yesNoString?.Equals("yes", StringComparison.OrdinalIgnoreCase) ?? true;

		foreach (var channel in channels)
		{
			var maybeMemberStatus = await ChannelHelper.ChannelMemberStatus(executor, channel);

			if (maybeMemberStatus is null)
			{
				await parser.NotifyService.Notify(executor, $"CHAT: You are not a member of {channel.Name.ToPlainText()}.");
				return new CallState("#-1 YOU ARE NOT A MEMBER OF THAT CHANNEL");
			}

			var status = maybeMemberStatus.Value.Status;

			if ((status.Hide ?? false) == gagOn)
			{
			    await parser.NotifyService.Notify(executor, $"CHAT: You are already in that gag state on {channel.Name.ToPlainText()}.");
			    continue;
			}

			await parser.Mediator.Send(new UpdateChannelUserStatusCommand(
				channel, executor, new SharpChannelStatus(
					null,
					null,
					gagOn,
					null,
					null
				)));

			if (gagOn)
			{
				await parser.NotifyService.Notify(executor, $"CHAT: You have been gagged on {channel.Name.ToPlainText()}.");
			}
			else
			{
				await parser.NotifyService.Notify(executor, $"CHAT: You have been ungagged on {channel.Name.ToPlainText()}.");
			}
		}

		return new CallState(channels.Length);
	}
}