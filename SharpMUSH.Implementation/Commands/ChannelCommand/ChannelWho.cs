using Mediator;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Commands.ChannelCommand;

public static class ChannelWho
{
	public static async ValueTask<CallState> Handle(IMUSHCodeParser parser, ILocateService locateService, IPermissionService permissionService, IMediator mediator, INotifyService notifyService, MString channelName)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(mediator);
		var maybeChannel = await ChannelHelper.GetChannelOrError(parser, locateService, permissionService, mediator, notifyService, channelName, notify: true);
		if (maybeChannel.IsError)
		{
			return maybeChannel.AsError.Value;
		}

		var channel = maybeChannel.AsChannel;

		var members = channel.Members.Value;
		var memberArray = await members.ToArrayAsync();


		var delimitedMembers = MModule.multipleWithDelimiter(MModule.single(", "),
			memberArray.Select(x => MModule.single(x.Member.Object().Name)));

		var memberOutput =
			MModule.multiple([
				MModule.single("Members of channel <"), channel.Name, MModule.single("> are:\n"),
				delimitedMembers
			]);

		await notifyService.Notify(executor, memberOutput);

		var memberList = memberArray.Select(x => x.Member.Object().DBRef).ToList();
		return new CallState(string.Join(", ", memberList));
	}
}