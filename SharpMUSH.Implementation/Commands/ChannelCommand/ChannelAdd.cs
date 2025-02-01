using System.Threading.Channels;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Commands.ChannelCommand;

public static class ChannelAdd
{
	public static async ValueTask<CallState> Handle(IMUSHCodeParser parser, MString channelName, MString privileges, string[] switches)
	{
		var executor = (await parser.CurrentState.ExecutorObject(parser.Mediator)).Known();
		var channel = await parser.Mediator.Send(new GetChannelQuery(channelName.ToPlainText()));

		if (channel is not null)
		{
			await parser.NotifyService.Notify(executor, "Channel already exists.");
			return new CallState("Channel already exists.");
		}

		// TODO: Add a check for the executor's privileges to create a channel.
		// TODO: Add a check for the executor's 'cost' requirements to create a channel.
		// TODO: Add a check to confirm the privs are valid.
		var parsedPrivileges = privileges.ToPlainText().Split(" ");
		
		await parser.Mediator.Send(new CreateChannelCommand(channelName, parsedPrivileges, executor.Object().Owner.Value));

		await parser.NotifyService.Notify(executor, "Channel has been created.");
		return new CallState("Channel has been created.");
	}
}