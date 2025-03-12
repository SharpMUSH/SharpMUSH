using System.Threading.Channels;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Commands.ChannelCommand;

public static class ChannelAdd
{
	public static async ValueTask<CallState> Handle(IMUSHCodeParser parser, MString channelName, MString privileges)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(parser.Mediator);
		var executorOwner = await executor.Object().Owner.WithCancellation(CancellationToken.None);
		if (await executor.IsGuest())
		{
			await parser.NotifyService.Notify(executor, "CHAT: Guests may not modify channels.");
			return new CallState("#-1 Guests may not modify channels.");
		}
		
		var maybeChannel = await ChannelHelper.GetChannelOrError(parser, channelName, true);
		if (!maybeChannel.IsError)
		{
			await parser.NotifyService.Notify(executor, "CHAT: Channel already exists.");
			return new CallState("#-1 Channel already exists.");
		}

		if (!ChannelHelper.IsValidChannelName(parser, channelName))
		{
			await parser.NotifyService.Notify(executor, "Invalid channel name.");
			return new CallState("#-1 Invalid channel name.");
		}

		var allChannels = await parser.Mediator.Send(new GetChannelListQuery());
		var ownedChannels = await allChannels
			.ToAsyncEnumerable()
			.WhereAwait(async x => 
				(await x.Owner.WithCancellation(CancellationToken.None)).Id == executorOwner.Id)
			.CountAsync();
		
		if (!await executor.IsPriv() && ownedChannels >= parser.Configuration.CurrentValue.Chat.MaxChannels)
		{
			await parser.NotifyService.Notify(executor, "#-1 You have too many channels.");
			return new CallState("#-1 You have too many channels.");
		}

		var parsedPrivileges = ChannelHelper.StringToChannelPrivileges(privileges);
		if (parsedPrivileges.IsError)
		{
			await parser.NotifyService.Notify(executor, $"Invalid privileges: {string.Join(", ", parsedPrivileges.AsError.Value)}.");
			return new CallState("#-1 Invalid privileges.");
		}
		
		await parser.Mediator.Send(new CreateChannelCommand(channelName, parsedPrivileges.AsPrivileges, executorOwner));

		await parser.NotifyService.Notify(executor, "Channel has been created.");
		return new CallState("Channel has been created.");
	}
}