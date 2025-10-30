using SharpMUSH.Implementation.Commands.ChannelCommand;
using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Notifications;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Functions;

public partial class Functions
{
	[SharpFunction(Name = "cbufferadd", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> ChannelBufferAdd(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}
	
	[SharpFunction(Name = "cemit", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular)]
	public static async ValueTask<CallState> ChannelEmit(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		if (!Configuration!.CurrentValue.Function.FunctionSideEffects)
		{
			return new CallState(Errors.ErrorNoSideFx);
		}

		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var channelName = parser.CurrentState.Arguments["0"].Message!;
		var message = parser.CurrentState.Arguments["1"].Message!;

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

		return CallState.Empty;
	}
	
	[SharpFunction(Name = "cflags", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> ChannelFlags(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}
	[SharpFunction(Name = "channels", MinArgs = 0, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> Channels(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}
	[SharpFunction(Name = "clflags", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> ChannelListFlags(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}
	[SharpFunction(Name = "clock", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> ChannelLock(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}
	[SharpFunction(Name = "cmogrifier", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> ChannelMogrifier(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}
	[SharpFunction(Name = "cowner", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> ChannelOwner(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}
	[SharpFunction(Name = "crecall", MinArgs = 1, MaxArgs = 5, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> ChannelRecall(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}
	[SharpFunction(Name = "cstatus", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> ChannelStatus(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}
	[SharpFunction(Name = "ctitle", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> ChannelTitle(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}
	[SharpFunction(Name = "cwho", MinArgs = 1, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> ChannelWho(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}
	[SharpFunction(Name = "nscemit", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular)]
	public static async ValueTask<CallState> NoSpoofChannelEmit(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		if (!Configuration!.CurrentValue.Function.FunctionSideEffects)
		{
			return new CallState(Errors.ErrorNoSideFx);
		}

		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var channelName = parser.CurrentState.Arguments["0"].Message!;
		var message = parser.CurrentState.Arguments["1"].Message!;

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

		return CallState.Empty;
	}

	[SharpFunction(Name = "cbuffer", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> ChannelBuffer(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "cdesc", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> ChannelDescription(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "cmsgs", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> ChannelMessages(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "cusers", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> ChannelUsers(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}
}