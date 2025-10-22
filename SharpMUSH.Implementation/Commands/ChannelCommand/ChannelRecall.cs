using Mediator;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.ParserInterfaces;
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
		var messages = await Mediator!.Send(new GetChannelMessagesQuery(channel.Id, linesInt));
		var messageList = messages.Select(x => x.Message).ToList();
		var message = MModule.multiple(messageList);

		if (switches.Contains("QUIET"))
		{
			return message;
		}
		*/

		// await NotifyService!.Notify(executor, message, executor);
		return new CallState(string.Empty);
	}
}