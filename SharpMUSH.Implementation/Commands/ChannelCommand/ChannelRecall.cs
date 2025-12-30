using Mediator;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Commands.ChannelCommand;

public static class ChannelRecall
{
	public static async ValueTask<CallState> Handle(IMUSHCodeParser parser, ILocateService LocateService, IPermissionService PermissionService, IMediator Mediator, INotifyService NotifyService, MString channelName, MString lines, string[] switches)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var maybeChannel = await ChannelHelper.GetChannelOrError(parser, LocateService, PermissionService, Mediator, NotifyService, channelName, true);

		if (maybeChannel.IsError)
		{
			return maybeChannel.AsError.Value;
		}

		var channel = maybeChannel.AsChannel;

		var members = channel.Members.Value;
		var memberStatus = await members.FirstOrDefaultAsync(x => x.Member.Id() == executor.Id());
		if (memberStatus is null)
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

		// Retrieve messages from the recall buffer
		var messages = await Mediator.CreateStream(new GetChannelMessagesQuery(channel.Id ?? string.Empty, linesInt))
			.Select(x => x.Message)
			.ToListAsync();
			
		var message = MModule.multiple(messages);

		if (switches.Contains("QUIET"))
		{
			return message;
		}

		await NotifyService.Notify(executor, message, executor);
		return new CallState(string.Empty);
	}
}