using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Commands.ChannelCommand;

public static class ChannelRecall
{
	public static async ValueTask<CallState> Handle(IMUSHCodeParser parser, MString channelName, MString lines, string[] switches)
	{
		var executor = (await parser.CurrentState.ExecutorObject(parser.Mediator)).Known();
		var channel = await parser.Mediator.Send(new GetChannelQuery(channelName.ToPlainText()));

		if (channel is null)
		{
			return new CallState("Channel not found.");
		}

		var members = await channel.Members.WithCancellation(CancellationToken.None);
		var (member,status) = members.FirstOrDefault(x => x.Member.Id() == executor.Id());
		if (member is null)
		{
			return new CallState("Player is not a member of the channel.");
		}

		var linesInt = 10;
		if (lines.Length != 0)
		{
			if (!int.TryParse(lines.ToPlainText(), out linesInt))
			{
				return new CallState("Invalid number of lines.");
			}
		}

		/*
		var messages = await parser.Mediator.Send(new GetChannelMessagesQuery(channel.Id, linesInt));
		var messageList = messages.Select(x => x.Message).ToList();
		var message = MModule.multiple(messageList);

		if (switches.Contains("QUIET"))
		{
			return message;
		}
		*/

		// await parser.NotifyService.Notify(executor, message, executor);
		return new CallState(string.Empty);
	}
}