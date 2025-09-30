using Mediator;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Commands.ChannelCommand;

public static class ChannelWho
{
	public static async ValueTask<CallState> Handle(IMUSHCodeParser parser, ILocateService LocateService, IPermissionService PermissionService, IMediator Mediator, INotifyService NotifyService, MString channelName)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var maybeChannel = await ChannelHelper.GetChannelOrError(parser, LocateService, PermissionService, Mediator, NotifyService, channelName, notify: true);
		if (maybeChannel.IsError)
		{
			return maybeChannel.AsError.Value;
		}

		var channel = maybeChannel.AsChannel;

		var members = await channel.Members.WithCancellation(CancellationToken.None);
		var memberArray = members.ToArray();


		var delimitedMembers = MModule.multipleWithDelimiter(MModule.single(", "),
			memberArray.Select(x => MModule.single(x.Member.Object().Name)));
		
		var memberOutput =
			MModule.multiple([
				MModule.single("Members of channel <"), channel.Name, MModule.single("> are:\n"),
				delimitedMembers
			]);

		await NotifyService!.Notify(executor, memberOutput);

		var memberList = memberArray.Select(x => x.Member.Object().DBRef).ToList();
		return new CallState(string.Join(", ", memberList));
	}
}