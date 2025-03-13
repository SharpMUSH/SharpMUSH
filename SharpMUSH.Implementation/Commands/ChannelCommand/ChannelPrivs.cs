using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Commands.ChannelCommand;

public static class ChannelPrivs
{
	public static async ValueTask<CallState> Handle(IMUSHCodeParser parser, MString channelName, MString privs)
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
			return new CallState("You are not the owner of the channel.");
		}

		var privilegeList = ChannelHelper.StringToChannelPrivileges(privs);
		if (privilegeList.IsError)
		{
			await parser.NotifyService.Notify(executor,
				$"CHAT: Invalid channel privileges(s):  {string.Join(",", privilegeList.AsError.Value)}");
		}
		
		await parser.Mediator.Send(new UpdateChannelCommand(channel,
			null, 
			null,
			Privs: privilegeList.AsPrivileges,
			null,
			null,
			null,
			null,
			null,
			null, 
			null));

		return new CallState("CHAT: Channel privileges have been updated.");
	}
}