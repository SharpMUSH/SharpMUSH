using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.Definitions;

namespace SharpMUSH.Implementation.Commands.ChannelCommand;

public static class ChannelChown
{
	public static async ValueTask<CallState> Handle(IMUSHCodeParser parser, ILocateService LocateService, IPermissionService PermissionService, IMediator Mediator, INotifyService NotifyService, MString channelName, MString newOwner)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		if (await executor.IsGuest())
		{
			await NotifyService.Notify(executor, ErrorMessages.Notifications.ChatGuestsCantModify, executor);
			return new CallState(ErrorMessages.Returns.GuestsCannotModifyChannels);
		}

		return await ChannelHelper.GetVisibleChannelOrError(PermissionService, Mediator,
			NotifyService, executor, channelName, true) switch
		{
			SharpChannel channel => await ChownAsync(parser, LocateService, PermissionService, Mediator, NotifyService,
				executor, channel, newOwner),
			Error<CallState> error => error.Value
		};
	}

	/// <summary>Hands a channel the executor may modify to the player <paramref name="newOwner"/> names.</summary>
	private static async ValueTask<CallState> ChownAsync(IMUSHCodeParser parser, ILocateService LocateService,
		IPermissionService PermissionService, IMediator Mediator, INotifyService NotifyService, AnySharpObject executor,
		SharpChannel channel, MString newOwner)
	{
		if (!await PermissionService.ChannelCanModifyAsync(executor, channel))
		{
			await NotifyService.Notify(executor, ErrorMessages.Returns.YouCannotModifyThisChannel, executor);
			return new CallState(ErrorMessages.Returns.YouCannotModifyThisChannel);
		}

		return await LocateService.LocatePlayerAndNotifyIfInvalid(parser, executor, executor, newOwner.ToPlainText()) switch
		{
			AnySharpObject and SharpPlayer newOwnerObject => await TransferAsync(Mediator, NotifyService, executor, channel,
				newOwnerObject),
			AnySharpObject => throw new InvalidOperationException("A player lookup found something that is not a player."),
			None => new CallState(ErrorMessages.Returns.PlayerNotFound),
			Error<string> error => new CallState(error.Value)
		};
	}

	private static async ValueTask<CallState> TransferAsync(IMediator Mediator, INotifyService NotifyService,
		AnySharpObject executor, SharpChannel channel, SharpPlayer newOwnerObject)
	{
		await Mediator.Send(new UpdateChannelOwnerCommand(channel, newOwnerObject));

		var output = MarkupText.Concat([MarkupText.Plain("CHAT: "), MarkupText.Plain(newOwnerObject.Object.Name), MarkupText.Plain(" is the new owner of "), channel.Name]);
		await NotifyService.Notify(executor, output, executor);
		return new CallState(string.Empty);
	}
}