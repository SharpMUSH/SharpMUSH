using Microsoft.Extensions.Logging;
using SharpMUSH.Implementation.Commands.ChannelCommand;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Notifications;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Functions;

public partial class Functions
{
	/// <summary>
	/// Helper method to determine which argument is the player and which is the channel name.
	/// Tries arg0 as player first, then arg1 if that fails.
	/// </summary>
	private static async ValueTask<(AnySharpObject? Player, SharpChannel? Channel, CallState? Error)>
		ResolvePlayerAndChannel(IMUSHCodeParser parser, AnySharpObject executor, string playerName, string channelName)
	{
		var maybePlayer =
			await LocateService!.LocateAndNotifyIfInvalidWithCallState(parser, executor, executor, playerName,
				LocateFlags.All);

		if (maybePlayer.IsError) return (null, null, maybePlayer.AsError);

		var maybeChannel = await ChannelHelper.GetChannelOrError(parser, LocateService!, PermissionService!, Mediator!,
			NotifyService!, MModule.single(channelName!), false);

		if (maybeChannel.IsError) return (maybePlayer.AsSharpObject, null, maybeChannel.AsError.Value);

		return (maybePlayer.AsSharpObject, maybeChannel.AsChannel, null);
	}

	[SharpFunction(Name = "cbufferadd", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular, ParameterNames = ["channel", "message"])]
	public static async ValueTask<CallState> ChannelBufferAdd(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		if (!Configuration!.CurrentValue.Function.FunctionSideEffects)
		{
			return new CallState(ErrorMessages.Returns.NoSideFx);
		}

		var channelName = parser.CurrentState.Arguments["0"].Message!;
		var message = parser.CurrentState.Arguments["1"].Message!;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);

		var maybeChannel = await ChannelHelper.GetChannelOrError(parser, LocateService!, PermissionService!, Mediator!,
			NotifyService!, channelName, false);

		if (maybeChannel.IsError)
		{
			return maybeChannel.AsError.Value;
		}

		var channel = maybeChannel.AsChannel;

		var maybeMemberStatus = await ChannelHelper.ChannelMemberStatus(executor, channel);
		if (maybeMemberStatus is null)
		{
			return new CallState(ErrorMessages.Returns.NotAMember);
		}

		using (Logger!.BeginScope("<{DbRef}> {Category}: {Channel}.",
						 executor.Object().ToString(),
						 "Channel",
						 channel.Name.ToPlainText()))
		{
			Logger!.LogInformation("{ChatMessage}", message);
		}

		return CallState.Empty;
	}

	[SharpFunction(Name = "cemit", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular, ParameterNames = ["channel", "message"])]
	public static async ValueTask<CallState> ChannelEmit(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		if (!Configuration!.CurrentValue.Function.FunctionSideEffects)
		{
			return new CallState(ErrorMessages.Returns.NoSideFx);
		}

		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var channelName = parser.CurrentState.Arguments["0"].Message!;
		var message = parser.CurrentState.Arguments["1"].Message!;

		var maybeChannel = await ChannelHelper.GetChannelOrError(parser, LocateService!, PermissionService!, Mediator!,
			NotifyService!, channelName, true);

		if (maybeChannel.IsError)
		{
			return maybeChannel.AsError.Value;
		}

		var channel = maybeChannel.AsChannel;

		var maybeMemberStatus = await ChannelHelper.ChannelMemberStatus(executor, channel);

		if (maybeMemberStatus is null)
		{
			return new CallState(ErrorMessages.Returns.NotAMember);
		}

		var (_, status) = maybeMemberStatus;

		await Mediator!.Publish(new ChannelMessageNotification(
			channel,
			executor.WithNoneOption(),
			INotifyService.NotificationType.Emit,
			message,
			status.Title ?? MModule.empty(),
			MModule.single(executor.Object().Name),
			MModule.single("says"),
			[]
		));


		using (Logger!.BeginScope("<{DbRef} {Category}: {Channel}.",
						 executor.Object().DBRef.ToString(),
						 "Channel",
						 channel.Name.ToPlainText()))
		{
			Logger!.LogInformation("{ChatMessage}", message);
		}

		return CallState.Empty;
	}

	[SharpFunction(Name = "cflags", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["channel"])]
	public static async ValueTask<CallState> ChannelFlags(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var channelName = parser.CurrentState.Arguments["0"].Message!;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);

		var maybeChannel = await ChannelHelper.GetChannelOrError(parser, LocateService!, PermissionService!, Mediator!,
			NotifyService!, channelName, false);

		if (maybeChannel.IsError)
		{
			return maybeChannel.AsError.Value;
		}

		var channel = maybeChannel.AsChannel;

		if (!parser.CurrentState.Arguments.TryGetValue("1", out var arg1))
		{
			var channelFlags = new List<string>();

			foreach (var priv in channel.Privs)
			{
				channelFlags.Add(priv.ToUpper());
			}

			return new CallState(string.Join(" ", channelFlags));
		}

		var playerArg = arg1.Message!.ToPlainText();
		var maybePlayer =
			await LocateService!.LocateAndNotifyIfInvalidWithCallState(parser, executor, executor, playerArg,
				LocateFlags.All);

		if (maybePlayer.IsError)
		{
			return maybePlayer.AsError;
		}

		var player = maybePlayer.AsSharpObject;

		var maybeMemberStatus = await ChannelHelper.ChannelMemberStatus(player, channel);

		if (maybeMemberStatus is null)
		{
			return CallState.Empty;
		}

		var (_, status) = maybeMemberStatus;

		var statusFlags = new List<string>();
		if (status.Combine is true) statusFlags.Add("COMBINE");
		if (status.Gagged is true) statusFlags.Add("GAG");
		if (status.Hide is true) statusFlags.Add("HIDE");
		if (status.Mute is true) statusFlags.Add("MUTE");

		return new CallState(string.Join(" ", statusFlags));
	}

	[SharpFunction(Name = "channels", MinArgs = 0, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public static async ValueTask<CallState> Channels(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);

		var player = executor;
		if (parser.CurrentState.Arguments.TryGetValue("0", out var arg0) &&
				!string.IsNullOrWhiteSpace(arg0.Message!.ToPlainText()))
		{
			var maybePlayer = await LocateService!.LocateAndNotifyIfInvalidWithCallState(parser, executor, executor,
				arg0.Message!.ToPlainText(), LocateFlags.All);
			if (maybePlayer.IsError)
			{
				return maybePlayer.AsError;
			}

			player = maybePlayer.AsSharpObject;
		}

		var type = "all";
		if (parser.CurrentState.Arguments.TryGetValue("1", out var arg1))
		{
			type = arg1.Message!.ToPlainText().ToLower();
		}

		var allChannels = Mediator!.CreateStream(new GetChannelListQuery());
		var channelArray = await allChannels.ToArrayAsync();

		var filteredChannels = new List<string>();
		foreach (var channel in channelArray)
		{
			var shouldInclude = type switch
			{
				"on" => await ChannelHelper.IsMemberOfChannel(player, channel),
				"off" => !await ChannelHelper.IsMemberOfChannel(player, channel),
				"quiet" or _ => await PermissionService!.ChannelCanSeeAsync(player, channel)
			};

			if (shouldInclude)
			{
				filteredChannels.Add(channel.Name.ToPlainText());
			}
		}

		return new CallState(string.Join(" ", filteredChannels));
	}

	[SharpFunction(Name = "clflags", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["channel"])]
	public static async ValueTask<CallState> ChannelListFlags(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var channelName = parser.CurrentState.Arguments["0"].Message!;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);

		var maybeChannel = await ChannelHelper.GetChannelOrError(parser, LocateService!, PermissionService!, Mediator!,
			NotifyService!, channelName, false);

		if (maybeChannel.IsError)
		{
			return maybeChannel.AsError.Value;
		}

		var channel = maybeChannel.AsChannel;

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

		var playerArg = arg1.Message!.ToPlainText();
		var maybePlayer =
			await LocateService!.LocateAndNotifyIfInvalidWithCallState(parser, executor, executor, playerArg,
				LocateFlags.All);

		if (maybePlayer.IsError)
		{
			return maybePlayer.AsError;
		}

		var player = maybePlayer.AsSharpObject;
		var maybeMemberStatus = await ChannelHelper.ChannelMemberStatus(player, channel);

		if (maybeMemberStatus is null)
		{
			return CallState.Empty;
		}

		var (_, status) = maybeMemberStatus;
		var statusFlags = new List<string>();
		if (status.Combine is true) statusFlags.Add("COMBINE");
		if (status.Gagged is true) statusFlags.Add("GAG");
		if (status.Hide is true) statusFlags.Add("HIDE");
		if (status.Mute is true) statusFlags.Add("MUTE");

		return new CallState(string.Join(" ", statusFlags));
	}

	[SharpFunction(Name = "clock", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["channel"])]
	public static async ValueTask<CallState> ChannelLock(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var channelName = parser.CurrentState.Arguments["0"].Message!;
		await parser.CurrentState.KnownExecutorObject(Mediator!);

		var maybeChannel = await ChannelHelper.GetChannelOrError(parser, LocateService!, PermissionService!, Mediator!,
			NotifyService!, channelName, false);

		if (maybeChannel.IsError)
		{
			return maybeChannel.AsError.Value;
		}

		var channel = maybeChannel.AsChannel;

		var lockType = "join";
		if (parser.CurrentState.Arguments.TryGetValue("1", out var arg1))
		{
			lockType = arg1.Message!.ToPlainText().ToLower();
		}

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

	[SharpFunction(Name = "cmogrifier", MinArgs = 1, MaxArgs = 1,
		Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["channel"])]
	public static async ValueTask<CallState> ChannelMogrifier(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var channelName = parser.CurrentState.Arguments["0"].Message!;
		await parser.CurrentState.KnownExecutorObject(Mediator!);

		var maybeChannel = await ChannelHelper.GetChannelOrError(parser, LocateService!, PermissionService!, Mediator!,
			NotifyService!, channelName, false);

		if (maybeChannel.IsError)
		{
			return maybeChannel.AsError.Value;
		}

		var channel = maybeChannel.AsChannel;

		return new CallState(channel.Mogrifier);
	}

	[SharpFunction(Name = "cowner", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["channel"])]
	public static async ValueTask<CallState> ChannelOwner(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var channelName = parser.CurrentState.Arguments["0"].Message!;
		await parser.CurrentState.KnownExecutorObject(Mediator!);

		var maybeChannel = await ChannelHelper.GetChannelOrError(parser, LocateService!, PermissionService!, Mediator!,
			NotifyService!, channelName, false);

		if (maybeChannel.IsError)
		{
			return maybeChannel.AsError.Value;
		}

		var channel = maybeChannel.AsChannel;
		var owner = await channel.Owner.WithCancellation(CancellationToken.None);

		return new CallState(owner.Object.DBRef.ToString());
	}

	[SharpFunction(Name = "crecall", MinArgs = 1, MaxArgs = 5, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["channel", "lines", "start"])]
	public static async ValueTask<CallState> ChannelRecall(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var channelName = parser.CurrentState.Arguments["0"].Message!;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);

		var maybeChannel = await ChannelHelper.GetChannelOrError(parser, LocateService!, PermissionService!, Mediator!,
			NotifyService!, channelName, false);

		if (maybeChannel.IsError)
		{
			return maybeChannel.AsError.Value;
		}

		var channel = maybeChannel.AsChannel;

		var maybeMemberStatus = await ChannelHelper.ChannelMemberStatus(executor, channel);
		if (maybeMemberStatus is null)
		{
			return new CallState(ErrorMessages.Returns.NotAMember);
		}

		var lines = 10;
		if (parser.CurrentState.Arguments.TryGetValue("1", out var arg1) &&
				int.TryParse(arg1.Message!.ToPlainText(), out var parsedLines))
		{
			lines = parsedLines;
		}

		var messages = await Mediator!.CreateStream(new GetChannelMessagesQuery(channel.Id ?? string.Empty, lines))
			.Select(x => x.Message)
			.ToListAsync();

		return new CallState(MModule.multiple(messages));
	}

	[SharpFunction(Name = "cstatus", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object", "channel"])]
	public static async ValueTask<CallState> ChannelStatus(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var channelArg = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var playerArg = parser.CurrentState.Arguments["1"].Message!.ToPlainText();
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);

		var (player, channel, error) = await ResolvePlayerAndChannel(parser, executor, channelArg, playerArg);
		if (error != null)
		{
			return error;
		}

		var maybeMemberStatus = await ChannelHelper.ChannelMemberStatus(player!, channel!);

		if (maybeMemberStatus is null)
		{
			return new CallState("OFF");
		}

		var (_, status) = maybeMemberStatus;

		var statusFlags = new List<string> { "ON" };
		if (status.Gagged is true) statusFlags.Add("GAG");
		if (status.Hide is true) statusFlags.Add("HIDE");
		if (status.Mute is true) statusFlags.Add("MUTE");
		if (status.Combine is true) statusFlags.Add("COMBINE");

		return new CallState(string.Join(" ", statusFlags));
	}

	[SharpFunction(Name = "ctitle", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object", "channel"])]
	public static async ValueTask<CallState> ChannelTitle(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var arg0 = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var arg1 = parser.CurrentState.Arguments["1"].Message!.ToPlainText();
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);

		var (player, channel, error) = await ResolvePlayerAndChannel(parser, executor, arg0, arg1);
		if (error != null)
		{
			return error;
		}

		var maybeMemberStatus = await ChannelHelper.ChannelMemberStatus(player!, channel!);

		if (maybeMemberStatus is null)
		{
			return CallState.Empty;
		}

		var (_, status) = maybeMemberStatus;

		return new CallState(status.Title ?? MModule.empty());
	}

	[SharpFunction(Name = "cwho", MinArgs = 1, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["channel"])]
	public static async ValueTask<CallState> ChannelWho(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var channelName = parser.CurrentState.Arguments["0"].Message!;
		await parser.CurrentState.KnownExecutorObject(Mediator!);

		var maybeChannel = await ChannelHelper.GetChannelOrError(parser, LocateService!, PermissionService!, Mediator!,
			NotifyService!, channelName, false);

		if (maybeChannel.IsError)
		{
			return maybeChannel.AsError.Value;
		}

		var channel = maybeChannel.AsChannel;

		var members = await channel.Members.Value.ToArrayAsync();

		var outputSep = parser.CurrentState.Arguments.TryGetValue("1", out var arg1)
			? arg1.Message!.ToPlainText()
			: " ";

		_ = parser.CurrentState.Arguments.TryGetValue("2", out var arg2)
			? arg2.Message!.ToPlainText()
			: outputSep;

		var memberList = members.Select(x => x.Member.Object().DBRef.ToString()).ToList();

		return new CallState(string.Join(outputSep, memberList));
	}

	[SharpFunction(Name = "nscemit", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular, ParameterNames = ["channel", "message"])]
	public static async ValueTask<CallState> NoSpoofChannelEmit(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		if (!Configuration!.CurrentValue.Function.FunctionSideEffects)
		{
			return new CallState(ErrorMessages.Returns.NoSideFx);
		}

		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var channelName = parser.CurrentState.Arguments["0"].Message!;
		var message = parser.CurrentState.Arguments["1"].Message!;

		var maybeChannel = await ChannelHelper.GetChannelOrError(parser, LocateService!, PermissionService!, Mediator!,
			NotifyService!, channelName, true);

		if (maybeChannel.IsError)
		{
			return maybeChannel.AsError.Value;
		}

		var channel = maybeChannel.AsChannel;

		var maybeMemberStatus = await ChannelHelper.ChannelMemberStatus(executor, channel);

		if (maybeMemberStatus is null)
		{
			return new CallState(ErrorMessages.Returns.NotAMember);
		}

		var (_, status) = maybeMemberStatus;

		var canNoSpoof = await PermissionService!.CanNoSpoof(executor);

		await Mediator!.Publish(new ChannelMessageNotification(
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

	[SharpFunction(Name = "cbuffer", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular, ParameterNames = ["channel"])]
	public static async ValueTask<CallState> ChannelBuffer(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var channelName = parser.CurrentState.Arguments["0"].Message!;

		var maybeChannel = await ChannelHelper.GetChannelOrError(parser, LocateService!, PermissionService!, Mediator!,
			NotifyService!, channelName, false);

		if (maybeChannel.IsError)
		{
			return maybeChannel.AsError.Value;
		}

		var channel = maybeChannel.AsChannel;

		return new CallState(channel.Buffer.ToString());
	}

	[SharpFunction(Name = "cdesc", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular, ParameterNames = ["channel"])]
	public static async ValueTask<CallState> ChannelDescription(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var channelName = parser.CurrentState.Arguments["0"].Message!;


		var maybeChannel = await ChannelHelper.GetChannelOrError(parser, LocateService!, PermissionService!, Mediator!,
			NotifyService!, channelName, false);

		if (maybeChannel.IsError)
		{
			return maybeChannel.AsError.Value;
		}

		var channel = maybeChannel.AsChannel;

		return new CallState(channel.Description);
	}

	[SharpFunction(Name = "cmsgs", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["channel"])]
	public static async ValueTask<CallState> ChannelMessages(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var channelName = parser.CurrentState.Arguments["0"].Message!;


		var maybeChannel = await ChannelHelper.GetChannelOrError(parser, LocateService!, PermissionService!, Mediator!,
			NotifyService!, channelName, false);

		if (maybeChannel.IsError)
		{
			return maybeChannel.AsError.Value;
		}

		var channel = maybeChannel.AsChannel;

		var count = await Mediator!.CreateStream(new GetChannelMessagesQuery(channel.Id ?? string.Empty, int.MaxValue))
			.CountAsync();

		return new CallState(count.ToString());
	}

	[SharpFunction(Name = "cusers", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["channel"])]
	public static async ValueTask<CallState> ChannelUsers(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var channelName = parser.CurrentState.Arguments["0"].Message!;
		await parser.CurrentState.KnownExecutorObject(Mediator!);

		var maybeChannel = await ChannelHelper.GetChannelOrError(parser, LocateService!, PermissionService!, Mediator!,
			NotifyService!, channelName, false);

		if (maybeChannel.IsError)
		{
			return maybeChannel.AsError.Value;
		}

		var channel = maybeChannel.AsChannel;

		var memberCount = await channel.Members.Value.CountAsync();

		return new CallState(memberCount.ToString());
	}

	[SharpFunction(Name = "CINFO", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		ParameterNames = ["channel", "info-type"])]
	public static async ValueTask<CallState> CInfo(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var channelName = parser.CurrentState.Arguments["0"].Message!;
		var infoType = parser.CurrentState.Arguments.TryGetValue("1", out var typeArg)
			? typeArg.Message!.ToPlainText().ToLowerInvariant()
			: "name";

		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var maybeChannel = await ChannelHelper.GetChannelOrError(parser, LocateService!, PermissionService!, Mediator!,
			NotifyService!, channelName, false);

		if (maybeChannel.IsError)
		{
			return maybeChannel.AsError.Value;
		}

		var channel = maybeChannel.AsChannel;

		var owner = await channel.Owner.WithCancellation(CancellationToken.None);
		return infoType switch
		{
			"name" => new CallState(channel.Name),
			"owner" => new CallState($"#{owner.Object.DBRef.Number}"),
			"members" => new CallState((await channel.Members.Value.CountAsync()).ToString()),
			"buffer" => new CallState("50"), // Default buffer size
			_ => new CallState(ErrorMessages.Returns.InvalidInfoType)
		};
	}
}