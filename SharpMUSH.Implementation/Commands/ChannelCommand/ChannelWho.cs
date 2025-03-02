using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Commands.ChannelCommand;

public static class ChannelWho
{
	public static async ValueTask<CallState> Handle(IMUSHCodeParser parser, MString channelName)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(parser.Mediator);
		var maybeChannel = await ChannelHelper.GetChannelOrError(parser, channelName, notify: true);
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

		await parser.NotifyService.Notify(executor, memberOutput);

		var memberList = memberArray.Select(x => x.Member.Object().DBRef).ToList();
		return new CallState(string.Join(", ", memberList));
	}
}