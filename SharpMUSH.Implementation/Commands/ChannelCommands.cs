using SharpMUSH.Implementation.Commands.ChannelCommand;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Notifications;
using SharpMUSH.Library.Services.Interfaces;
using CB = SharpMUSH.Library.Definitions.CommandBehavior;
using SharpMUSH.Library;

namespace SharpMUSH.Implementation.Commands;

public partial class Commands
{
	[SharpCommand(Name = "@CEMIT", Switches = ["NOEVAL", "NOISY", "SILENT", "SPOOF"],
		Behavior = CB.Default | CB.EqSplit | CB.NoGagged, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> ChannelEmit(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var arg0Check = parser.CurrentState.Arguments.TryGetValue("0", out var arg0CallState);
		var arg1Check = parser.CurrentState.Arguments.TryGetValue("1", out var arg1CallState);
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);

		if (!arg0Check || !arg1Check)
		{
			await NotifyService!.Notify(parser.CurrentState.Executor!.Value, "Don't you have anything to say?");
			return new CallState("#-1 Don't you have anything to say?");
		}

		var channelName = arg0CallState!.Message!;
		var message = arg1CallState!.Message!;

		// TODO: Use standardized method.
		var maybeChannel = await ChannelHelper.GetChannelOrError(parser, LocateService!, PermissionService!, Mediator!, NotifyService!, channelName, true);

		if (maybeChannel.IsError)
		{
			return maybeChannel.AsError.Value;
		}

		var channel = maybeChannel.AsChannel;

		var maybeMemberStatus = await ChannelHelper.ChannelMemberStatus(executor, channel);

		if (maybeMemberStatus is null)
		{
			await NotifyService!.Notify(parser.CurrentState.Executor!.Value, "You are not a member of that channel.");
			return new CallState("#-1 You are not a member of that channel.");
		}

		var (_, status) = maybeMemberStatus.Value;

		await Mediator!.Send(new ChannelMessageNotification(
			channel,
			executor.WithNoneOption(),
			INotifyService.NotificationType.Emit,
			message,
			status.Title ?? MModule.empty(),
			MModule.single(executor.Object().Name),
			MModule.single("says"),
			[]
		));

		return new CallState(string.Empty);
	}

	[SharpCommand(Name = "@CHAT", Switches = [], Behavior = CB.Default | CB.EqSplit | CB.NoGagged, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> Chat(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var arg0Check = parser.CurrentState.Arguments.TryGetValue("0", out var arg0CallState);
		var arg1Check = parser.CurrentState.Arguments.TryGetValue("1", out var arg1CallState);
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);

		if (!arg0Check || !arg1Check)
		{
			await NotifyService!.Notify(parser.CurrentState.Executor!.Value, "Don't you have anything to say?");
			return new CallState("#-1 Don't you have anything to say?");
		}

		var channelName = arg0CallState!.Message!;
		var message = arg1CallState!.Message!;

		// TODO: Use standardized method.
		var maybeChannel = await ChannelHelper.GetChannelOrError(parser, LocateService!, PermissionService!, Mediator!, NotifyService!, channelName, true);

		if (maybeChannel.IsError)
		{
			return maybeChannel.AsError.Value;
		}

		var channel = maybeChannel.AsChannel;

		var maybeMemberStatus = await ChannelHelper.ChannelMemberStatus(executor, channel);

		if (maybeMemberStatus is null)
		{
			await NotifyService!.Notify(parser.CurrentState.Executor!.Value, "You are not a member of that channel.");
			return new CallState("#-1 You are not a member of that channel.");
		}

		var (_, status) = maybeMemberStatus.Value;

		// TODO: Change notification type based on the first character.
		await Mediator!.Send(new ChannelMessageNotification(
			channel,
			executor.WithNoneOption(),
			INotifyService.NotificationType.Emit,
			message,
			status.Title ?? MModule.empty(),
			MModule.single(executor.Object().Name),
			MModule.single("says"),
			[]
		));

		return new CallState(string.Empty);
	}

	[SharpCommand(Name = "@NSCEMIT", Switches = ["NOEVAL", "NOISY", "SILENT"],
		Behavior = CB.Default | CB.EqSplit | CB.NoGagged, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> NoSpoofChannelEmit(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var arg0Check = parser.CurrentState.Arguments.TryGetValue("0", out var arg0CallState);
		var arg1Check = parser.CurrentState.Arguments.TryGetValue("1", out var arg1CallState);
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);

		if (!arg0Check || !arg1Check)
		{
			await NotifyService!.Notify(parser.CurrentState.Executor!.Value, "Don't you have anything to say?");
			return new CallState("#-1 Don't you have anything to say?");
		}

		var channelName = arg0CallState!.Message!;
		var message = arg1CallState!.Message!;

		// TODO: Use standardized method.
		var maybeChannel = await ChannelHelper.GetChannelOrError(parser, LocateService!, PermissionService!, Mediator!, NotifyService!, channelName, true);

		if (maybeChannel.IsError)
		{
			return maybeChannel.AsError.Value;
		}

		var channel = maybeChannel.AsChannel;

		var maybeMemberStatus = await ChannelHelper.ChannelMemberStatus(executor, channel);

		if (maybeMemberStatus is null)
		{
			await NotifyService!.Notify(parser.CurrentState.Executor!.Value, "You are not a member of that channel.");
			return new CallState("#-1 You are not a member of that channel.");
		}

		var (_, status) = maybeMemberStatus.Value;

		var canNoSpoof = await executor.HasPower("CAN_SPOOF") || await executor.IsPriv();

		await Mediator!.Send(new ChannelMessageNotification(
			channel,
			executor.WithNoneOption(),
			canNoSpoof
				? INotifyService.NotificationType.NSEmit 
				: INotifyService.NotificationType.Emit,
			message,
			status.Title ?? MModule.empty(),
			MModule.single(executor.Object().Name),
			MModule.single("says"),
			[]
		));

		return new CallState(string.Empty);
	}
}