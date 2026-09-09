using Microsoft.Extensions.Logging;
using SharpMUSH.Library;
using SharpMUSH.Implementation.Commands.ChannelCommand;
using SharpMUSH.Implementation.Definitions;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Notifications;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Commands;
using SharpMUSH.Library.Queries;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.Utilities;

namespace SharpMUSH.Implementation.Functions;

public partial class Functions
{
	/// <summary>
	/// Helper method to determine which argument is the player and which is the channel name.
	/// Tries arg0 as player first, then arg1 if that fails.
	/// </summary>
	private async ValueTask<(AnySharpObject? Player, SharpChannel? Channel, CallState? Error)>
		ResolvePlayerAndChannel(IMUSHCodeParser parser, AnySharpObject executor, string playerName, string channelName)
	{
		var maybePlayer =
			await LocateService.LocateAndNotifyIfInvalidWithCallState(parser, executor, executor, playerName,
				LocateFlags.All);

		if (maybePlayer.IsError) return (null, null, maybePlayer.AsError);

		// extchat.c:2434 (fun_ctitle) / :2491 (fun_cstatus) — "You must pass the channel's see-lock".
		var maybeChannel = await ChannelHelper.GetVisibleChannelOrError(PermissionService, Mediator,
			NotifyService, executor, MarkupText.Plain(channelName!), false);

		if (maybeChannel.IsError) return (maybePlayer.AsSharpObject, null, maybeChannel.AsError.Value);

		return (maybePlayer.AsSharpObject, maybeChannel.AsChannel, null);
	}

	/// <summary>
	/// PennMUSH <c>fun_cbufferadd</c> (<c>src/extchat.c:2348-2400</c>):
	/// <c>cbufferadd(&lt;channel&gt;,&lt;message&gt;[,&lt;spoof?&gt;])</c> writes a line into the channel's
	/// recall buffer WITHOUT broadcasting it, for softcode that reconstructs history.
	/// </summary>
	[SharpFunction(Name = "cbufferadd", MinArgs = 2, MaxArgs = 3,
		Flags = FunctionFlags.Regular | FunctionFlags.HasSideFX, ParameterNames = ["channel", "message", "spoof"])]
	public async ValueTask<CallState> ChannelBufferAdd(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var channelName = parser.CurrentState.Arguments["0"].Message!;
		var message = parser.CurrentState.Arguments["1"].Message!;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);

		if (message.Length == 0)
		{
			return new CallState(ErrorMessages.Returns.NoTextGiven);
		}

		var maybeChannel = await ChannelHelper.GetVisibleChannelOrError(PermissionService, Mediator,
			NotifyService, executor, channelName, false);

		if (maybeChannel.IsError)
		{
			return maybeChannel.AsError.Value;
		}

		var channel = maybeChannel.AsChannel;

		// extchat.c:2393 — Chan_Can_Modify, the same gate @channel/buffer and @channel/wipe answer to.
		if (!await PermissionService.ChannelCanModifyAsync(executor, channel))
		{
			return new CallState(ErrorMessages.Returns.PermissionDenied);
		}

		// extchat.c:2380 — the third argument attributes the line to the enactor instead of the executor,
		// which is what makes it usable from a command object replaying somebody's speech.
		var speaker = executor;
		if (parser.CurrentState.Arguments.TryGetValue("2", out var arg2) && arg2.Message!.Truthy())
		{
			speaker = (await parser.CurrentState.EnactorObject(Mediator)).WithoutNone();
		}

		await Mediator.Send(new AddChannelMessageCommand(new SharpChannelMessage
		{
			ChannelId = channel.Id ?? string.Empty,
			Timestamp = DateTimeOffset.UtcNow,
			Sender = speaker.Object().DBRef,
			Message = message,
			MessageType = "Emit"
		}));

		return CallState.Empty;
	}

	/// <summary>
	/// PennMUSH <c>fun_cemit</c> (<c>src/extchat.c:3445</c>) calls <c>do_cemit</c> rather than
	/// reimplementing it, and so does this: <see cref="ChannelEmit"/> is the one implementation behind
	/// all four spellings, so softcode cannot be held to a different gate than the command.
	/// </summary>
	[SharpFunction(Name = "cemit", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.HasSideFX,
		ParameterNames = ["channel", "message", "noisy"])]
	public async ValueTask<CallState> ChannelEmitFunction(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> await EmitOnChannel(parser, spoof: false);

	/// <summary>PennMUSH <c>fun_cemit</c> called as <c>NSCEMIT</c> (<c>src/extchat.c:3447</c>).</summary>
	[SharpFunction(Name = "nscemit", MinArgs = 2, MaxArgs = 3,
		Flags = FunctionFlags.Regular | FunctionFlags.HasSideFX, ParameterNames = ["channel", "message", "noisy"])]
	public async ValueTask<CallState> NoSpoofChannelEmitFunction(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> await EmitOnChannel(parser, spoof: true);

	private async ValueTask<CallState> EmitOnChannel(IMUSHCodeParser parser, bool spoof)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);

		return await ChannelEmit.Handle(PermissionService, Mediator, NotifyService, executor,
			parser.CurrentState.Arguments["0"].Message!,
			parser.CurrentState.Arguments["1"].Message!,
			spoof);
	}

	[SharpFunction(Name = "cflags", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		ParameterNames = ["channel", "object"])]
	public async ValueTask<CallState> ChannelFlags(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> await ChannelFlagList(parser, verbose: false);

	/// <summary>PennMUSH <c>fun_cflags</c> called as <c>CLFLAGS</c> (<c>src/extchat.c:2286</c>).</summary>
	[SharpFunction(Name = "clflags", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		ParameterNames = ["channel", "object"])]
	public async ValueTask<CallState> ChannelListFlags(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> await ChannelFlagList(parser, verbose: true);

	private async ValueTask<CallState> ChannelFlagList(IMUSHCodeParser parser, bool verbose)
	{
		var channelName = parser.CurrentState.Arguments["0"].Message!;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);

		var maybeChannel = await ChannelHelper.GetVisibleChannelOrError(PermissionService, Mediator,
			NotifyService, executor, channelName, false);

		if (maybeChannel.IsError)
		{
			return maybeChannel.AsError.Value;
		}

		var channel = maybeChannel.AsChannel;

		if (!parser.CurrentState.Arguments.TryGetValue("1", out var arg1) || arg1.Message!.Length == 0)
		{
			return new CallState(verbose
				? ChannelHelper.PrivilegeNames(channel.Privs)
				: ChannelHelper.PrivilegeLetters(channel.Privs));
		}

		var maybePlayer = await LocateService.LocateAndNotifyIfInvalidWithCallState(parser, executor, executor,
			arg1.Message!.ToPlainText(), LocateFlags.All);

		if (maybePlayer.IsError)
		{
			return maybePlayer.AsError;
		}

		var player = maybePlayer.AsSharpObject;

		// extchat.c:2295 — a member's own channel flags are examine-gated, so this cannot be used to read
		// who is hiding or gagging on a channel you share with them.
		if (!await PermissionService.CanExamine(executor, player))
		{
			return new CallState(ErrorMessages.Returns.PermissionDenied);
		}

		var maybeMemberStatus = await ChannelHelper.ChannelMemberStatus(player, channel);

		if (maybeMemberStatus is null)
		{
			return new CallState(ErrorMessages.Returns.NotOnChannel);
		}

		return new CallState(ChannelHelper.MemberFlags(maybeMemberStatus.Status, verbose));
	}

	[SharpFunction(Name = "channels", MinArgs = 0, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public async ValueTask<CallState> Channels(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);

		var player = executor;
		if (parser.CurrentState.Arguments.TryGetValue("0", out var arg0) &&
				!string.IsNullOrWhiteSpace(arg0.Message!.ToPlainText()))
		{
			var maybePlayer = await LocateService.LocateAndNotifyIfInvalidWithCallState(parser, executor, executor,
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

		// PennMUSH fun_channels (src/extchat.c:3313-3375): "You can see an object's channels if you can
		// examine it. Otherwise you can see only channels that you share with it where it's not hidden."
		//
		// Visibility is judged against the EXECUTOR, never against the object being asked about. Judging it
		// against the object let any mortal read back a wizard's wizard-only channels by naming the wizard;
		// and the "on"/"off" arms applied no visibility rule at all, so `channels(me,off)` listed every
		// channel in the game by name.
		var askingAboutSomeoneElse = player.Id() != executor.Id();
		var canExamineTarget = !askingAboutSomeoneElse || await PermissionService.CanExamine(executor, player);
		var privWho = await executor.IsPriv() || await executor.HasPower("Who");

		// Materialised before the loop: the per-channel checks below open their own streams, and
		var channelArray = await Mediator.CreateStream(new GetChannelListQuery()).ToArrayAsync();

		var filteredChannels = new List<string>();
		foreach (var channel in channelArray)
		{
			var isMember = await ChannelHelper.IsMemberOfChannel(player, channel);

			var matchesType = type switch
			{
				"on" => isMember,
				"off" => !isMember,
				"quiet" or _ => true
			};

			if (!matchesType)
			{
				continue;
			}

			if (!canExamineTarget)
			{
				// Not examinable: the executor may only learn about channels they can see themselves, that
				// the object is actually on, and on which the object is not hidden from them.
				var status = await ChannelHelper.ChannelMemberStatus(player, channel);
				if (status is null
						|| (!privWho && (status.Status.Hide ?? false))
						|| !await ChannelHelper.CanSeeChannel(PermissionService, executor, channel))
				{
					continue;
				}
			}
			else if (!await ChannelHelper.CanSeeChannel(PermissionService, executor, channel))
			{
				continue;
			}

			filteredChannels.Add(channel.Name.ToPlainText());
		}

		return new CallState(string.Join(" ", filteredChannels));
	}

	/// <summary>
	/// PennMUSH <c>fun_clock</c> (<c>src/extchat.c:3377-3443</c>):
	/// <c>clock(&lt;channel&gt;[/&lt;locktype&gt;])</c> returns that lock's key.
	/// </summary>
	[SharpFunction(Name = "clock", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		ParameterNames = ["channel/locktype", "lock"])]
	public async ValueTask<CallState> ChannelLock(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var argument = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);

		var separator = argument.IndexOf('/');
		var channelName = separator < 0 ? argument : argument[..separator];
		var lockType = separator < 0 ? "JOIN" : argument[(separator + 1)..];

		// Chan_Can_Decomp below refuses with #-1 PERMISSION DENIED, which a raw lookup's
		// #-1 NO SUCH CHANNEL is distinguishable from — so softcode could tell an invisible channel from a
		// nonexistent one even though the lock itself stayed secret.
		var maybeChannel = await ChannelHelper.GetVisibleChannelOrError(PermissionService, Mediator,
			NotifyService, executor, MarkupText.Plain(channelName), false);

		if (maybeChannel.IsError)
		{
			return maybeChannel.AsError.Value;
		}

		var channel = maybeChannel.AsChannel;

		// extchat.c:3400-3419 — five lock types, and anything else is an error rather than a silent JOIN.
		// The type is resolved to a reader before it is read, so "unrecognised type" and "recognised type
		// holding nothing" cannot answer the same way.
		Func<SharpChannel, string>? readLock = lockType.ToUpperInvariant() switch
		{
			"JOIN" => x => x.JoinLock,
			"SPEAK" => x => x.SpeakLock,
			"MOD" => x => x.ModLock,
			"SEE" => x => x.SeeLock,
			"HIDE" => x => x.HideLock,
			_ => null
		};

		if (readLock is null)
		{
			return new CallState(ErrorMessages.Returns.NoSuchLockType);
		}

		// extchat.c:3437 — reading a channel's lock needs Chan_Can_Decomp. This handed every channel's
		// join/speak/see/hide/mod lock key to any mortal who asked for it.
		if (!await PermissionService.ChannelCanDecomposeAsync(executor, channel))
		{
			return new CallState(ErrorMessages.Returns.PermissionDenied);
		}

		return new CallState(readLock(channel) ?? string.Empty);
	}

	[SharpFunction(Name = "cmogrifier", MinArgs = 1, MaxArgs = 1,
		Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["channel"])]
	public async ValueTask<CallState> ChannelMogrifier(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var channelName = parser.CurrentState.Arguments["0"].Message!;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);

		var maybeChannel = await ChannelHelper.GetVisibleChannelOrError(PermissionService, Mediator,
			NotifyService, executor, channelName, false);

		if (maybeChannel.IsError)
		{
			return maybeChannel.AsError.Value;
		}

		var channel = maybeChannel.AsChannel;

		return new CallState(channel.Mogrifier);
	}

	[SharpFunction(Name = "cowner", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["channel"])]
	public async ValueTask<CallState> ChannelOwner(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var channelName = parser.CurrentState.Arguments["0"].Message!;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);

		var maybeChannel = await ChannelHelper.GetVisibleChannelOrError(PermissionService, Mediator,
			NotifyService, executor, channelName, false);

		if (maybeChannel.IsError)
		{
			return maybeChannel.AsError.Value;
		}

		var channel = maybeChannel.AsChannel;
		var owner = await channel.Owner.WithCancellation(CancellationToken.None);

		return new CallState(owner.Object.DBRef.ToString());
	}

	/// <summary>
	/// PennMUSH <c>fun_crecall</c> (<c>src/extchat.c:3461-3576</c>):
	/// <c>crecall(&lt;channel&gt;[,&lt;lines&gt;[,&lt;start&gt;[,&lt;osep&gt;[,&lt;timestamps?&gt;]]]])</c>.
	/// The window comes from <see cref="ChannelRecall.SelectAsync"/>, shared with
	/// <c>@channel/recall</c>; only the rendering differs.
	/// </summary>
	[SharpFunction(Name = "crecall", MinArgs = 1, MaxArgs = 5, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		ParameterNames = ["channel", "lines", "start", "osep", "timestamps"])]
	public async ValueTask<CallState> ChannelRecallFunction(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var arguments = parser.CurrentState.Arguments;

		MString Argument(string key)
			=> arguments.TryGetValue(key, out var value) ? value.Message! : MarkupText.Empty;

		var selection = await ChannelRecall.SelectAsync(PermissionService, Mediator, NotifyService, executor,
			arguments["0"].Message!, Argument("1"), Argument("2"), notify: false);

		if (selection.IsT1)
		{
			return selection.AsT1;
		}

		var separator = arguments.TryGetValue("3", out var osep) ? osep.Message! : MarkupText.Space;
		var showStamp = arguments.TryGetValue("4", out var stamp) && stamp.Message!.Truthy();

		var messages = selection.AsT0.Lines
			.Select(x => showStamp ? ChannelRecall.Stamped(x) : x.Message)
			.ToList();

		return new CallState(MarkupText.Join(separator, messages));
	}

	[SharpFunction(Name = "cstatus", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object", "channel"])]
	public async ValueTask<CallState> ChannelStatus(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var channelArg = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var playerArg = parser.CurrentState.Arguments["1"].Message!.ToPlainText();
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);

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
	public async ValueTask<CallState> ChannelTitle(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var arg0 = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var arg1 = parser.CurrentState.Arguments["1"].Message!.ToPlainText();
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);

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

		return new CallState(status.Title ?? MarkupText.Empty);
	}

	/// <summary>
	/// PennMUSH <c>fun_cwho</c> (<c>src/extchat.c:3004-3079</c>):
	/// <c>cwho(&lt;channel&gt;[,&lt;on|off|all&gt;[,&lt;skip gagged?&gt;]])</c>, returning space-separated
	/// dbrefs.
	/// </summary>
	[SharpFunction(Name = "cwho", MinArgs = 1, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		ParameterNames = ["channel", "type", "skipgagged"])]
	public async ValueTask<CallState> ChannelWho(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var channelName = parser.CurrentState.Arguments["0"].Message!;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);

		var maybeChannel = await ChannelHelper.GetVisibleChannelOrError(PermissionService, Mediator,
			NotifyService, executor, channelName, false);

		if (maybeChannel.IsError)
		{
			return maybeChannel.AsError.Value;
		}

		var channel = maybeChannel.AsChannel;

		var matchCondition = "on";
		if (parser.CurrentState.Arguments.TryGetValue("1", out var arg1) && arg1.Message!.Length != 0)
		{
			matchCondition = arg1.Message!.ToPlainText().ToLowerInvariant();
			if (matchCondition is not ("on" or "off" or "all"))
			{
				return new CallState(ErrorMessages.Returns.InvalidArgument);
			}
		}

		var skipGagged = parser.CurrentState.Arguments.TryGetValue("2", out var arg2)
										 && arg2.Message!.Truthy();

		var privilegedWho = await ChannelHelper.PrivilegedWho(executor);

		var listed = (await ChannelHelper.ChannelMembers(ConnectionService, channel))
			.Where(x => matchCondition switch
			{
				"off" => x.ListedAsOff(privilegedWho),
				"all" => true,
				_ => x.ListedAsOn(privilegedWho)
			})
			.Where(x => !skipGagged || !x.Gagging)
			.Select(x => x.Object.Object().DBRef.ToString());

		return new CallState(string.Join(" ", listed));
	}

	[SharpFunction(Name = "cbuffer", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular, ParameterNames = ["channel"])]
	public async ValueTask<CallState> ChannelBuffer(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var channelName = parser.CurrentState.Arguments["0"].Message!;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);

		var maybeChannel = await ChannelHelper.GetVisibleChannelOrError(PermissionService, Mediator,
			NotifyService, executor, channelName, false);

		if (maybeChannel.IsError)
		{
			return maybeChannel.AsError.Value;
		}

		var channel = maybeChannel.AsChannel;

		return new CallState(channel.Buffer.ToString());
	}

	[SharpFunction(Name = "cdesc", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular, ParameterNames = ["channel"])]
	public async ValueTask<CallState> ChannelDescription(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var channelName = parser.CurrentState.Arguments["0"].Message!;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);

		var maybeChannel = await ChannelHelper.GetVisibleChannelOrError(PermissionService, Mediator,
			NotifyService, executor, channelName, false);

		if (maybeChannel.IsError)
		{
			return maybeChannel.AsError.Value;
		}

		var channel = maybeChannel.AsChannel;

		return new CallState(channel.Description);
	}

	[SharpFunction(Name = "cmsgs", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["channel"])]
	public async ValueTask<CallState> ChannelMessages(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var channelName = parser.CurrentState.Arguments["0"].Message!;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);

		var maybeChannel = await ChannelHelper.GetVisibleChannelOrError(PermissionService, Mediator,
			NotifyService, executor, channelName, false);

		if (maybeChannel.IsError)
		{
			return maybeChannel.AsError.Value;
		}

		var channel = maybeChannel.AsChannel;

		var count = await Mediator.CreateStream(new GetChannelMessagesQuery(channel.Id ?? string.Empty, int.MaxValue))
			.CountAsync();

		return new CallState(count.ToString());
	}

	[SharpFunction(Name = "cusers", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["channel"])]
	public async ValueTask<CallState> ChannelUsers(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var channelName = parser.CurrentState.Arguments["0"].Message!;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);

		var maybeChannel = await ChannelHelper.GetVisibleChannelOrError(PermissionService, Mediator,
			NotifyService, executor, channelName, false);

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
	public async ValueTask<CallState> CInfo(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var channelName = parser.CurrentState.Arguments["0"].Message!;
		var infoType = parser.CurrentState.Arguments.TryGetValue("1", out var typeArg)
			? typeArg.Message!.ToPlainText().ToLowerInvariant()
			: "name";

		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var maybeChannel = await ChannelHelper.GetVisibleChannelOrError(PermissionService, Mediator,
			NotifyService, executor, channelName, false);

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