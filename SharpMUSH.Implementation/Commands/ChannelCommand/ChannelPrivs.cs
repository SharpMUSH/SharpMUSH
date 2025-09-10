using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Commands.ChannelCommand;

public static class ChannelPrivs
{
	public static async ValueTask<CallState> Handle(IMUSHCodeParser parser, ILocateService LocateService, IPermissionService PermissionService, IMediator Mediator, INotifyService NotifyService, MString channelName, MString privs)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		if (await executor.IsGuest())
		{
			await NotifyService!.Notify(executor, "CHAT: Guests may not modify channels.");
			return new CallState("#-1 Guests may not modify channels.");
		}

		var maybeChannel = await ChannelHelper.GetChannelOrError(parser, LocateService, PermissionService, Mediator, NotifyService, channelName, true);

		if (maybeChannel.IsError)
		{
			return maybeChannel.AsError.Value;
		}

		var channel = maybeChannel.AsChannel;

		if (await PermissionService!.ChannelCanModifyAsync(executor, channel))
		{
			return new CallState("You are not the owner of the channel.");
		}

		var privilegeList = ChannelHelper.StringToChannelPrivileges(privs);
		if (privilegeList.IsError)
		{
			await NotifyService!.Notify(executor,
				$"CHAT: Invalid channel privileges(s):  {string.Join(",", privilegeList.AsError.Value)}");
		}
		
		await Mediator!.Send(new UpdateChannelCommand(channel,
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