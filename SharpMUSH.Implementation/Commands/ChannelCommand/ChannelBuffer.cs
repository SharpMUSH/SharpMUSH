using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Commands.ChannelCommand;

public static class ChannelBuffer
{
	public static async ValueTask<CallState> Handle(IMUSHCodeParser parser, MString channelName, MString lines, string[] switches)
	{
		// TODO: How the heck are we going to handle Channel Buffers?
		// Channel Buffer can likely sit in a temporary file. Only lost on shutdown, not reboot?
		
		var executor = (await parser.CurrentState.ExecutorObject(parser.Mediator)).Known();
		var channel = await parser.Mediator.Send(new GetChannelQuery(channelName.ToPlainText()));

		if (channel is null)
		{
			return new CallState("Channel not found.");
		}

		var owner = await channel.Owner.WithCancellation(CancellationToken.None); 
		
		// TODO: This should be a controls check?
		if (!owner.Id!.Equals(executor.Object().Id))
		{
			return new CallState("You are not the owner of the channel.");
		}

		if (!int.TryParse(lines.ToPlainText(), out var linesInt))
		{
			return new CallState("Invalid number of lines.");
		}

		await parser.Mediator.Send(new UpdateChannelCommand(
			Channel: channel,
			null,
			null,
			null,
			null,
			null,
			null,
			null,
			null,
			Buffer: linesInt));

		return new CallState("Channel buffer has been updated.");
	}
}