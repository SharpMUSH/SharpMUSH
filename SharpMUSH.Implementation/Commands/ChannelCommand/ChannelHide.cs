using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Commands.ChannelCommand;

public static class ChannelHide
{
	public static async ValueTask<CallState> Handle(IMUSHCodeParser parser, MString? channelName, MString? yesNo)
	{
		var executor = (await parser.CurrentState.ExecutorObject(parser.Mediator)).Known();
		IEnumerable<SharpChannel> channels;

		if (channelName != null)
		{
			channels = await parser.Mediator.Send(new GetChannelListQuery());
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

		foreach (var channel in channels)
		{
			var maybeMemberStatus = await ChannelHelper.ChannelMemberStatus(executor, channel);

			if (maybeMemberStatus is null)
			{
				return new CallState($"CHAT: You are not a member of {channel.Name.ToPlainText()}.");
			}

			var member = maybeMemberStatus.Value.Member;
			var status = maybeMemberStatus.Value.Status;

			if (status.Hide ?? false)
			{
				return new CallState($"CHAT: You are already hidden on {channel.Name.ToPlainText()}.");
			}

			await parser.Mediator.Send(new UpdateChannelUserStatusCommand(
				channel, member, status with { Hide = true }));

			return new CallState($"CHAT: You have been hidden on {channel.Name.ToPlainText()}.");
		}

		return new CallState("");
	}
}