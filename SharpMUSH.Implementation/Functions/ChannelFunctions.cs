using SharpMUSH.Implementation.Commands.ChannelCommand;
using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Notifications;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Functions;

public partial class Functions
{
	[SharpFunction(Name = "cbufferadd", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular)]
	public static async ValueTask<CallState> ChannelBufferAdd(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		if (!Configuration!.CurrentValue.Function.FunctionSideEffects)
		{
			return new CallState(Errors.ErrorNoSideFx);
		}

		var channelName = parser.CurrentState.Arguments["0"].Message!;
		var message = parser.CurrentState.Arguments["1"].Message!;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);

		var maybeChannel = await ChannelHelper.GetChannelOrError(parser, LocateService!, PermissionService!, Mediator!, NotifyService!, channelName, false);
		
		if (maybeChannel.IsError)
		{
			return maybeChannel.AsError.Value;
		}

		var channel = maybeChannel.AsChannel;
		
		// Check if player has permission to add to buffer
		var maybeMemberStatus = await ChannelHelper.ChannelMemberStatus(executor, channel);
		if (maybeMemberStatus is null)
		{
			return new CallState("#-1 You are not a member of that channel.");
		}
		
		// TODO: This should add a message to the channel buffer in the database
		// For now, return empty as a placeholder
		return CallState.Empty;
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
	public static async ValueTask<CallState> ChannelFlags(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var channelName = parser.CurrentState.Arguments["0"].Message!;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);

		var maybeChannel = await ChannelHelper.GetChannelOrError(parser, LocateService!, PermissionService!, Mediator!, NotifyService!, channelName, false);
		
		if (maybeChannel.IsError)
		{
			return maybeChannel.AsError.Value;
		}

		var channel = maybeChannel.AsChannel;
		
		// If no second argument, return channel flags
		if (!parser.CurrentState.Arguments.TryGetValue("1", out var arg1))
		{
			// Return channel privileges as flags
			var channelFlags = new List<string>();
			
			foreach (var priv in channel.Privs)
			{
				channelFlags.Add(priv.ToUpper());
			}
			
			return new CallState(string.Join(" ", channelFlags));
		}
		
		// If second argument, return player's status flags on the channel
		var playerArg = arg1.Message!.ToPlainText();
		var maybePlayer = await LocateService!.LocateAndNotifyIfInvalidWithCallState(parser, executor, executor, playerArg, LocateFlags.All);
		
		if (maybePlayer.IsError)
		{
			return maybePlayer.AsError;
		}
		
		var player = maybePlayer.AsSharpObject;
		
		// Get player's status on this channel
		var maybeMemberStatus = await ChannelHelper.ChannelMemberStatus(player, channel);
		
		if (maybeMemberStatus is null)
		{
			return CallState.Empty; // Player not on channel
		}
		
		var (_, status) = maybeMemberStatus.Value;
		
		// Build status flags
		var statusFlags = new List<string>();
		if (status.Combine == true) statusFlags.Add("COMBINE");
		if (status.Gagged == true) statusFlags.Add("GAG");
		if (status.Hide == true) statusFlags.Add("HIDE");
		if (status.Mute == true) statusFlags.Add("MUTE");
		
		return new CallState(string.Join(" ", statusFlags));
	}
	[SharpFunction(Name = "channels", MinArgs = 0, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static async ValueTask<CallState> Channels(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		
		// Get player argument (default to executor)
		AnySharpObject player = executor;
		if (parser.CurrentState.Arguments.TryGetValue("0", out var arg0) && !string.IsNullOrWhiteSpace(arg0.Message!.ToPlainText()))
		{
			var maybePlayer = await LocateService!.LocateAndNotifyIfInvalidWithCallState(parser, executor, executor, arg0.Message!.ToPlainText(), LocateFlags.All);
			if (maybePlayer.IsError)
			{
				return maybePlayer.AsError;
			}
			player = maybePlayer.AsSharpObject;
		}
		
		// Get type argument (default to "all")
		var type = "all";
		if (parser.CurrentState.Arguments.TryGetValue("1", out var arg1))
		{
			type = arg1.Message!.ToPlainText().ToLower();
		}
		
		// Get all channels
		var allChannels = await Mediator!.Send(new GetChannelListQuery());
		
		// Filter based on type
		var filteredChannels = type switch
		{
			"on" => allChannels.Where(async (c, ct) => await ChannelHelper.IsMemberOfChannel(player, c)),
			"off" => allChannels.Where(async (c, ct) => !await ChannelHelper.IsMemberOfChannel(player, c)),
			"quiet" => allChannels.Where(async (c, ct) => await PermissionService!.ChannelCanSeeAsync(player, c)),
			_ => allChannels.Where(async (c, ct) => await PermissionService!.ChannelCanSeeAsync(player, c))
		};
		
		var channelList = await filteredChannels.Select((c, ct) => c.Name.ToPlainText()).ToArrayAsync();
		
		return new CallState(string.Join(" ", channelList));
	}
	[SharpFunction(Name = "clflags", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static async ValueTask<CallState> ChannelListFlags(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var channelName = parser.CurrentState.Arguments["0"].Message!;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);

		var maybeChannel = await ChannelHelper.GetChannelOrError(parser, LocateService!, PermissionService!, Mediator!, NotifyService!, channelName, false);
		
		if (maybeChannel.IsError)
		{
			return maybeChannel.AsError.Value;
		}

		var channel = maybeChannel.AsChannel;
		
		// If no second argument, return channel lock flags
		if (!parser.CurrentState.Arguments.TryGetValue("1", out var arg1))
		{
			var lockFlags = new List<string>();
			
			if (!string.IsNullOrEmpty(channel.JoinLock)) lockFlags.Add("JOIN");
			if (!string.IsNullOrEmpty(channel.SpeakLock)) lockFlags.Add("SPEAK");
			if (!string.IsNullOrEmpty(channel.SeeLock)) lockFlags.Add("SEE");
			if (!string.IsNullOrEmpty(channel.HideLock)) lockFlags.Add("HIDE");
			if (!string.IsNullOrEmpty(channel.ModLock)) lockFlags.Add("MOD");
			
			return new CallState(string.Join(" ", lockFlags));
		}
		
		// If second argument provided, interpret as player and return their list status flags
		var playerArg = arg1.Message!.ToPlainText();
		var maybePlayer = await LocateService!.LocateAndNotifyIfInvalidWithCallState(parser, executor, executor, playerArg, LocateFlags.All);
		
		if (maybePlayer.IsError)
		{
			return maybePlayer.AsError;
		}
		
		var player = maybePlayer.AsSharpObject;
		var maybeMemberStatus = await ChannelHelper.ChannelMemberStatus(player, channel);
		
		if (maybeMemberStatus is null)
		{
			return CallState.Empty; // Player not on channel
		}
		
		// Return the same as cflags with a player argument
		var (_, status) = maybeMemberStatus.Value;
		var statusFlags = new List<string>();
		if (status.Combine == true) statusFlags.Add("COMBINE");
		if (status.Gagged == true) statusFlags.Add("GAG");
		if (status.Hide == true) statusFlags.Add("HIDE");
		if (status.Mute == true) statusFlags.Add("MUTE");
		
		return new CallState(string.Join(" ", statusFlags));
	}
	[SharpFunction(Name = "clock", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static async ValueTask<CallState> ChannelLock(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var channelName = parser.CurrentState.Arguments["0"].Message!;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);

		var maybeChannel = await ChannelHelper.GetChannelOrError(parser, LocateService!, PermissionService!, Mediator!, NotifyService!, channelName, false);
		
		if (maybeChannel.IsError)
		{
			return maybeChannel.AsError.Value;
		}

		var channel = maybeChannel.AsChannel;
		
		// Get lock type argument (default to "join")
		var lockType = "join";
		if (parser.CurrentState.Arguments.TryGetValue("1", out var arg1))
		{
			lockType = arg1.Message!.ToPlainText().ToLower();
		}
		
		// Return the appropriate lock
		var lockValue = lockType switch
		{
			"join" => channel.JoinLock,
			"speak" or "on" => channel.SpeakLock,
			"see" => channel.SeeLock,
			"hide" => channel.HideLock,
			"mod" => channel.ModLock,
			_ => channel.JoinLock
		};
		
		return new CallState(lockValue);
	}
	[SharpFunction(Name = "cmogrifier", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static async ValueTask<CallState> ChannelMogrifier(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var channelName = parser.CurrentState.Arguments["0"].Message!;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);

		var maybeChannel = await ChannelHelper.GetChannelOrError(parser, LocateService!, PermissionService!, Mediator!, NotifyService!, channelName, false);
		
		if (maybeChannel.IsError)
		{
			return maybeChannel.AsError.Value;
		}

		var channel = maybeChannel.AsChannel;
		
		return new CallState(channel.Mogrifier);
	}
	[SharpFunction(Name = "cowner", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static async ValueTask<CallState> ChannelOwner(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var channelName = parser.CurrentState.Arguments["0"].Message!;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);

		var maybeChannel = await ChannelHelper.GetChannelOrError(parser, LocateService!, PermissionService!, Mediator!, NotifyService!, channelName, false);
		
		if (maybeChannel.IsError)
		{
			return maybeChannel.AsError.Value;
		}

		var channel = maybeChannel.AsChannel;
		var owner = await channel.Owner.WithCancellation(CancellationToken.None);
		
		return new CallState(owner.Object.DBRef.ToString());
	}
	[SharpFunction(Name = "crecall", MinArgs = 1, MaxArgs = 5, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static async ValueTask<CallState> ChannelRecall(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var channelName = parser.CurrentState.Arguments["0"].Message!;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);

		var maybeChannel = await ChannelHelper.GetChannelOrError(parser, LocateService!, PermissionService!, Mediator!, NotifyService!, channelName, false);
		
		if (maybeChannel.IsError)
		{
			return maybeChannel.AsError.Value;
		}

		var channel = maybeChannel.AsChannel;
		
		// Check if player is on the channel
		var maybeMemberStatus = await ChannelHelper.ChannelMemberStatus(executor, channel);
		if (maybeMemberStatus is null)
		{
			return new CallState("#-1 You are not a member of that channel.");
		}
		
		// Get optional arguments
		var lines = 10;
		if (parser.CurrentState.Arguments.TryGetValue("1", out var arg1) && int.TryParse(arg1.Message!.ToPlainText(), out var parsedLines))
		{
			lines = parsedLines;
		}
		
		// TODO: This should query the actual channel message history from the database
		// For now, return empty as a placeholder
		return CallState.Empty;
	}
	[SharpFunction(Name = "cstatus", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static async ValueTask<CallState> ChannelStatus(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var arg0 = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var arg1 = parser.CurrentState.Arguments["1"].Message!.ToPlainText();
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		
		// Determine which arg is player and which is channel
		// Try arg0 as player first
		var maybePlayer = await LocateService!.LocateAndNotifyIfInvalidWithCallState(parser, executor, executor, arg0, LocateFlags.All);
		
		AnySharpObject? player = null;
		string? channelName = null;
		
		if (!maybePlayer.IsError)
		{
			player = maybePlayer.AsSharpObject;
			channelName = arg1;
		}
		else
		{
			// Try arg1 as player
			var maybePlayer2 = await LocateService!.LocateAndNotifyIfInvalidWithCallState(parser, executor, executor, arg1, LocateFlags.All);
			if (!maybePlayer2.IsError)
			{
				player = maybePlayer2.AsSharpObject;
				channelName = arg0;
			}
			else
			{
				return new CallState("#-1 Invalid player");
			}
		}
		
		var maybeChannel = await ChannelHelper.GetChannelOrError(parser, LocateService!, PermissionService!, Mediator!, NotifyService!, MModule.single(channelName), false);
		
		if (maybeChannel.IsError)
		{
			return maybeChannel.AsError.Value;
		}
		
		var channel = maybeChannel.AsChannel;
		
		// Get player's status on this channel
		var maybeMemberStatus = await ChannelHelper.ChannelMemberStatus(player, channel);
		
		if (maybeMemberStatus is null)
		{
			return new CallState("OFF");
		}
		
		var (_, status) = maybeMemberStatus.Value;
		
		// Build status flags - starting with ON
		var statusFlags = new List<string> { "ON" };
		if (status.Gagged == true) statusFlags.Add("GAG");
		if (status.Hide == true) statusFlags.Add("HIDE");
		if (status.Mute == true) statusFlags.Add("MUTE");
		if (status.Combine == true) statusFlags.Add("COMBINE");
		
		return new CallState(string.Join(" ", statusFlags));
	}
	[SharpFunction(Name = "ctitle", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static async ValueTask<CallState> ChannelTitle(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var arg0 = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var arg1 = parser.CurrentState.Arguments["1"].Message!.ToPlainText();
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		
		// Determine which arg is player and which is channel
		// Try arg0 as player first
		var maybePlayer = await LocateService!.LocateAndNotifyIfInvalidWithCallState(parser, executor, executor, arg0, LocateFlags.All);
		
		AnySharpObject? player = null;
		string? channelName = null;
		
		if (!maybePlayer.IsError)
		{
			player = maybePlayer.AsSharpObject;
			channelName = arg1;
		}
		else
		{
			// Try arg1 as player
			var maybePlayer2 = await LocateService!.LocateAndNotifyIfInvalidWithCallState(parser, executor, executor, arg1, LocateFlags.All);
			if (!maybePlayer2.IsError)
			{
				player = maybePlayer2.AsSharpObject;
				channelName = arg0;
			}
			else
			{
				return new CallState("#-1 Invalid player");
			}
		}
		
		var maybeChannel = await ChannelHelper.GetChannelOrError(parser, LocateService!, PermissionService!, Mediator!, NotifyService!, MModule.single(channelName), false);
		
		if (maybeChannel.IsError)
		{
			return maybeChannel.AsError.Value;
		}
		
		var channel = maybeChannel.AsChannel;
		
		// Get player's status on this channel
		var maybeMemberStatus = await ChannelHelper.ChannelMemberStatus(player, channel);
		
		if (maybeMemberStatus is null)
		{
			return CallState.Empty; // Player not on channel
		}
		
		var (_, status) = maybeMemberStatus.Value;
		
		return new CallState(status.Title ?? MModule.empty());
	}
	[SharpFunction(Name = "cwho", MinArgs = 1, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static async ValueTask<CallState> ChannelWho(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var channelName = parser.CurrentState.Arguments["0"].Message!;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);

		var maybeChannel = await ChannelHelper.GetChannelOrError(parser, LocateService!, PermissionService!, Mediator!, NotifyService!, channelName, false);
		
		if (maybeChannel.IsError)
		{
			return maybeChannel.AsError.Value;
		}

		var channel = maybeChannel.AsChannel;
		
		// Get all members
		var members = await channel.Members.Value.ToArrayAsync();
		
		// Get output separator (arg 1) or default to space
		var outputSep = parser.CurrentState.Arguments.TryGetValue("1", out var arg1) 
			? arg1.Message!.ToPlainText() 
			: " ";
		
		// Get list separator (arg 2) or default to outputSep
		var listSep = parser.CurrentState.Arguments.TryGetValue("2", out var arg2) 
			? arg2.Message!.ToPlainText() 
			: outputSep;
		
		// Build list of members
		var memberList = members.Select(x => x.Member.Object().DBRef.ToString()).ToList();
		
		return new CallState(string.Join(outputSep, memberList));
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

		var canNoSpoof = await PermissionService!.CanNoSpoof(executor);

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
	public static async ValueTask<CallState> ChannelBuffer(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var channelName = parser.CurrentState.Arguments["0"].Message!;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);

		var maybeChannel = await ChannelHelper.GetChannelOrError(parser, LocateService!, PermissionService!, Mediator!, NotifyService!, channelName, false);
		
		if (maybeChannel.IsError)
		{
			return maybeChannel.AsError.Value;
		}

		var channel = maybeChannel.AsChannel;
		
		return new CallState(channel.Buffer.ToString());
	}

	[SharpFunction(Name = "cdesc", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular)]
	public static async ValueTask<CallState> ChannelDescription(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var channelName = parser.CurrentState.Arguments["0"].Message!;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);

		var maybeChannel = await ChannelHelper.GetChannelOrError(parser, LocateService!, PermissionService!, Mediator!, NotifyService!, channelName, false);
		
		if (maybeChannel.IsError)
		{
			return maybeChannel.AsError.Value;
		}

		var channel = maybeChannel.AsChannel;
		
		return new CallState(channel.Description);
	}

	[SharpFunction(Name = "cmsgs", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static async ValueTask<CallState> ChannelMessages(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var channelName = parser.CurrentState.Arguments["0"].Message!;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);

		var maybeChannel = await ChannelHelper.GetChannelOrError(parser, LocateService!, PermissionService!, Mediator!, NotifyService!, channelName, false);
		
		if (maybeChannel.IsError)
		{
			return maybeChannel.AsError.Value;
		}

		var channel = maybeChannel.AsChannel;
		
		// TODO: This should query the actual message count from the database
		// For now, return 0 as a placeholder
		return new CallState("0");
	}

	[SharpFunction(Name = "cusers", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static async ValueTask<CallState> ChannelUsers(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var channelName = parser.CurrentState.Arguments["0"].Message!;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);

		var maybeChannel = await ChannelHelper.GetChannelOrError(parser, LocateService!, PermissionService!, Mediator!, NotifyService!, channelName, false);
		
		if (maybeChannel.IsError)
		{
			return maybeChannel.AsError.Value;
		}

		var channel = maybeChannel.AsChannel;
		
		// Count all members
		var memberCount = await channel.Members.Value.CountAsync();
		
		return new CallState(memberCount.ToString());
	}
}