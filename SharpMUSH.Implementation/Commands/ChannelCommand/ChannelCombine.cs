using System.Collections.Immutable;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Commands.ChannelCommand;

public static class ChannelCombine
{
	public static async ValueTask<CallState> Handle(IMUSHCodeParser parser, MString? channelName, MString? yesNo)
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
		
		if (channelName == null)
		{
			channels = [..await parser.Mediator.Send(new GetChannelListQuery())];
		}
		else
		{
			var maybeChannel = await ChannelHelper.GetChannelOrError(parser, channelName, true);
			if (maybeChannel.IsError)
			{
				return maybeChannel.AsError.Value;
			}

			channels = [maybeChannel.AsChannel];
		}
		
		var combineOn = yesNoString?.Equals("yes", StringComparison.OrdinalIgnoreCase) ?? true;

		foreach (var channel in channels)
		{
			var maybeMemberStatus = await ChannelHelper.ChannelMemberStatus(executor, channel);

			if (maybeMemberStatus is null)
			{
				await parser.NotifyService.Notify(executor, $"CHAT: You are not a member of {channel.Name.ToPlainText()}.");
				return new CallState("#-1 YOU ARE NOT A MEMBER OF THAT CHANNEL");
			}

			var status = maybeMemberStatus.Value.Status;

			if ((status.Combine ?? false) == combineOn)
			{
				return new CallState($"CHAT: You are already in that combination state on {channel.Name.ToPlainText()}.");
			}

			await parser.Mediator.Send(new UpdateChannelUserStatusCommand(channel, executor, new SharpChannelStatus(
				Combine: combineOn,
				null,
				null,
				null,
				null
			)));
			
			if (combineOn)
			{
				await parser.NotifyService.Notify(executor, $"CHAT: Combined channels turned on for {channel.Name.ToPlainText()}.");
			}
			else
			{
				await parser.NotifyService.Notify(executor, $"CHAT: Combined channels turned off for {channel.Name.ToPlainText()}.");
			}
		}

		return new CallState(channels.Length);
	}
}