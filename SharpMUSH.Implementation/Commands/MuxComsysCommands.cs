using SharpMUSH.Implementation.Commands.ChannelCommand;
using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using CB = SharpMUSH.Library.Definitions.CommandBehavior;

namespace SharpMUSH.Implementation.Commands;

/// <summary>
/// TinyMUX's channel administration commands, which PennMUSH's muxcomm set leaves out. Each one is the
/// <c>@channel</c> or <c>@clock</c> operation it stands for, run as whoever typed it, so those commands'
/// own permission checks decide what is allowed. Like <c>addcom</c>, they work only while
/// <c>use_muxcomm</c> is on.
/// </summary>
public partial class Commands
{
	/// <summary>The privileges a channel made with <c>@ccreate</c> starts with: players and objects may join.</summary>
	private static readonly MString MuxChannelPrivileges = MarkupText.Plain("player object");

	[SharpCommand(Name = "@CCREATE", Behavior = CB.Default | CB.NoGagged, MinArgs = 0, MaxArgs = 0,
		ParameterNames = ["channel"])]
	public async ValueTask<Option<CallState>> MuxChannelCreate(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		if (await MuxCommDisabledAsync(parser) is { } disabled) return disabled;

		return await MuxChannelArgument(parser, "@ccreate <channel>") switch
		{
			MString channel => await ChannelAdd.Handle(parser, LocateService, ChannelPermissions, Mediator, NotifyService,
				Configuration, channel, MuxChannelPrivileges),
			CallState usage => usage
		};
	}

	[SharpCommand(Name = "@CDESTROY", Behavior = CB.Default | CB.NoGagged, MinArgs = 0, MaxArgs = 0,
		ParameterNames = ["channel"])]
	public async ValueTask<Option<CallState>> MuxChannelDestroy(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		if (await MuxCommDisabledAsync(parser) is { } disabled) return disabled;

		return await MuxChannelArgument(parser, "@cdestroy <channel>") switch
		{
			MString channel => await ChannelDelete.Handle(parser, LocateService, ChannelPermissions, Mediator,
				NotifyService, channel, MarkupText.Empty),
			CallState usage => usage
		};
	}

	/// <summary><c>@channel/off</c> has no quiet form, so <c>/quiet</c> is accepted and changes nothing.</summary>
	[SharpCommand(Name = "@CBOOT", Switches = ["QUIET"], Behavior = CB.Default | CB.EqSplit | CB.NoGagged,
		MinArgs = 0, MaxArgs = 0, ParameterNames = ["channel", "object"])]
	public async ValueTask<Option<CallState>> MuxChannelBoot(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		if (await MuxCommDisabledAsync(parser) is { } disabled) return disabled;

		return await MuxChannelPair(parser, "@cboot[/quiet] <channel>=<object>") switch
		{
			MuxPair pair => await ChannelOff.Handle(parser, LocateService, PermissionService, ChannelPermissions,
				Mediator, NotifyService, pair.Channel, pair.Other),
			CallState usage => usage
		};
	}

	/// <summary><c>@channel/who</c> lists every member, so a trailing <c>/all</c> changes nothing.</summary>
	[SharpCommand(Name = "@CWHO", Behavior = CB.Default | CB.NoGagged, MinArgs = 0, MaxArgs = 0,
		ParameterNames = ["channel"])]
	public async ValueTask<Option<CallState>> MuxChannelWho(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		if (await MuxCommDisabledAsync(parser) is { } disabled) return disabled;

		return await MuxChannelArgument(parser, "@cwho <channel>[/all]") switch
		{
			MString channel => await ChannelWho.Handle(parser, LocateService, ChannelPermissions, Mediator,
				NotifyService, ConnectionService, WithoutAllSuffix(channel)),
			CallState usage => usage
		};
	}

	private static MString WithoutAllSuffix(MString channel)
		=> channel.ToPlainText().EndsWith("/all", StringComparison.OrdinalIgnoreCase)
			? MarkupText.Plain(channel.ToPlainText()[..^4].TrimEnd())
			: channel;

	[SharpCommand(Name = "@CCHOWN", Behavior = CB.Default | CB.EqSplit | CB.NoGagged, MinArgs = 0, MaxArgs = 0,
		ParameterNames = ["channel", "player"])]
	public async ValueTask<Option<CallState>> MuxChannelChown(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		if (await MuxCommDisabledAsync(parser) is { } disabled) return disabled;

		return await MuxChannelPair(parser, "@cchown <channel>=<player>") switch
		{
			MuxPair pair => await ChannelChown.Handle(parser, LocateService, ChannelPermissions, Mediator,
				NotifyService, pair.Channel, pair.Other),
			CallState usage => usage
		};
	}

	/// <summary>
	/// <c>@cset/&lt;option&gt; &lt;channel&gt;[=&lt;value&gt;]</c>. <c>public</c> and <c>private</c> are the see
	/// lock, which decides who finds the channel in <c>@channel/list</c>; <c>loud</c>, <c>mute</c> and
	/// <c>quiet</c> the <c>quiet</c> privilege; <c>spoof</c>, <c>anon</c> and <c>nospoof</c> the
	/// <c>nonames</c> privilege; <c>log</c> the channel's buffer. The last three have no equivalent.
	/// </summary>
	[SharpCommand(Name = "@CSET",
		Switches = ["PUBLIC", "PRIVATE", "LOUD", "MUTE", "QUIET", "SPOOF", "ANON", "NOSPOOF", "LOG", "HEADER", "OBJECT",
			"TIMESTAMP_LOGS"],
		Behavior = CB.Default | CB.EqSplit | CB.NoGagged, MinArgs = 0, MaxArgs = 0, ParameterNames = ["channel", "value"])]
	public async ValueTask<Option<CallState>> MuxChannelSet(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		if (await MuxCommDisabledAsync(parser) is { } disabled) return disabled;

		const string usage = "@cset/<option> <channel>[=<value>]";
		var switches = parser.CurrentState.Switches.ToArray();
		if (switches.Length != 1) return await MuxUsageAsync(parser, usage);

		return await MuxChannelArgument(parser, usage) switch
		{
			MString channel => await MuxChannelOptionAsync(parser, switches[0], channel),
			CallState refused => refused
		};
	}

	private async ValueTask<CallState> MuxChannelOptionAsync(IMUSHCodeParser parser, string option, MString channel)
	{
		var value = parser.CurrentState.Arguments.GetValueOrDefault("1")?.Message ?? MarkupText.Empty;
		return option switch
		{
			"PUBLIC" => await MuxChannelLockAsync(parser, channel, "SEE", string.Empty),
			"PRIVATE" => await MuxChannelLockAsync(parser, channel, "SEE", "#FALSE"),
			"LOUD" => await MuxChannelPrivilegesAsync(parser, channel, "!quiet"),
			"MUTE" or "QUIET" => await MuxChannelPrivilegesAsync(parser, channel, "quiet"),
			"SPOOF" or "ANON" => await MuxChannelPrivilegesAsync(parser, channel, "nonames"),
			"NOSPOOF" => await MuxChannelPrivilegesAsync(parser, channel, "!nonames"),
			"LOG" when value.Length == 0 => await MuxUsageAsync(parser, "@cset/log <channel>=<number of messages>"),
			"LOG" => await ChannelBuffer.Handle(parser, LocateService, ChannelPermissions, Mediator, NotifyService,
				Configuration, channel, value),
			"HEADER" => await MuxNoEquivalentAsync(parser, nameof(ErrorMessages.Notifications.CsetNoHeader)),
			"OBJECT" => await MuxNoEquivalentAsync(parser, nameof(ErrorMessages.Notifications.CsetNoObject)),
			_ => await MuxNoEquivalentAsync(parser, nameof(ErrorMessages.Notifications.CsetNoTimestampLogs))
		};
	}

	[SharpCommand(Name = "@COFLAGS", Behavior = CB.Default | CB.EqSplit | CB.NoGagged, MinArgs = 0, MaxArgs = 0,
		ParameterNames = ["channel", "flag"])]
	public async ValueTask<Option<CallState>> MuxChannelObjectFlags(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		if (await MuxCommDisabledAsync(parser) is { } disabled) return disabled;

		return await MuxChannelFlagsAsync(parser, "@coflags", "object");
	}

	[SharpCommand(Name = "@CPFLAGS", Behavior = CB.Default | CB.EqSplit | CB.NoGagged, MinArgs = 0, MaxArgs = 0,
		ParameterNames = ["channel", "flag"])]
	public async ValueTask<Option<CallState>> MuxChannelPlayerFlags(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		if (await MuxCommDisabledAsync(parser) is { } disabled) return disabled;

		return await MuxChannelFlagsAsync(parser, "@cpflags", "player");
	}

	/// <summary>
	/// <c>&lt;channel&gt;=[!]join</c>: whether objects (<c>@coflags</c>) or players (<c>@cpflags</c>) may join,
	/// the <c>object</c> and <c>player</c> privileges. TinyMUX's <c>transmit</c> and <c>receive</c> have no
	/// equivalent; <c>@clock/speak</c> chooses who may speak.
	/// </summary>
	private async ValueTask<CallState> MuxChannelFlagsAsync(IMUSHCodeParser parser, string command, string privilege)
		=> await MuxChannelPair(parser, $"{command} <channel>=[!]join") switch
		{
			MuxPair pair => await MuxChannelFlagAsync(parser, command, privilege, pair.Channel, pair.Other),
			CallState usage => usage
		};

	private async ValueTask<CallState> MuxChannelFlagAsync(IMUSHCodeParser parser, string command, string privilege,
		MString channel, MString typed)
	{
		var text = typed.ToPlainText().Trim().ToLowerInvariant();
		var negate = text.StartsWith('!');
		var flag = text.TrimStart('!');
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);

		switch (flag)
		{
			case "join":
				return await MuxChannelPrivilegesAsync(parser, channel, negate ? $"!{privilege}" : privilege);
			case "transmit" or "receive":
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.CflagsNoEquivalentFormat),
					executor, command, flag);
				return new CallState(ErrorMessages.Returns.InvalidArgument);
			default:
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.CflagsUnknownFormat),
					executor, command, flag);
				return new CallState(ErrorMessages.Returns.InvalidArgument);
		}
	}

	/// <summary><c>allcom on|off|who</c>: <c>@channel/ungag</c>, <c>/gag</c> or <c>/who</c> on every channel your aliases name.</summary>
	[SharpCommand(Name = "ALLCOM", Behavior = CB.Default | CB.NoGagged, MinArgs = 0, MaxArgs = 0, ParameterNames = ["mode"])]
	public async ValueTask<Option<CallState>> AllCom(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		if (await MuxCommDisabledAsync(parser) is { } disabled) return disabled;

		var mode = parser.CurrentState.Arguments.GetValueOrDefault("0")?.Message.ToPlainText().Trim().ToLowerInvariant();
		if (mode is not ("on" or "off" or "who")) return await MuxUsageAsync(parser, "allcom on|off|who");

		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		if (await ChannelAliasesAsync(executor) is not { Length: > 0 } aliases)
			return await NoChannelAliasesAsync(executor);

		var channels = aliases.Select(alias => alias.Value.ToPlainText())
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.Select(MarkupText.Plain);
		foreach (var channel in channels)
		{
			await (mode switch
			{
				"on" => ChannelUserFlags.Handle(parser, ChannelPermissions, Mediator, NotifyService, channel, null,
					ChannelUserFlags.UserFlag.Gag, forceOff: true),
				"off" => ChannelUserFlags.Handle(parser, ChannelPermissions, Mediator, NotifyService, channel, null,
					ChannelUserFlags.UserFlag.Gag, forceOff: false),
				_ => ChannelWho.Handle(parser, LocateService, ChannelPermissions, Mediator, NotifyService,
					ConnectionService, channel)
			});
		}

		return CallState.Empty;
	}

	/// <summary><c>clearcom</c>: <c>delcom</c> for every alias.</summary>
	[SharpCommand(Name = "CLEARCOM", Behavior = CB.Default | CB.NoGagged, MinArgs = 0, MaxArgs = 0, ParameterNames = [])]
	public async ValueTask<Option<CallState>> ClearCom(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		if (await MuxCommDisabledAsync(parser) is { } disabled) return disabled;

		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		if (await ChannelAliasesAsync(executor) is not { Length: > 0 } aliases)
			return await NoChannelAliasesAsync(executor);

		foreach (var alias in aliases)
		{
			await DeleteChannelAliasAsync(executor, alias.LongName["CHANALIAS`".Length..].ToLowerInvariant(),
				alias.LongName, alias.Value);
		}

		return CallState.Empty;
	}

	private async ValueTask<SharpAttribute[]> ChannelAliasesAsync(AnySharpObject executor)
		=> await AttributeService.GetAttributePatternAsync(executor, executor, "CHANALIAS`*", false,
			IAttributeService.AttributePatternMode.Wildcard) is SharpAttribute[] aliases ? aliases : [];

	private async ValueTask<CallState> NoChannelAliasesAsync(AnySharpObject executor)
	{
		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.YouHaveNoChannelAliases), executor);
		return CallState.Empty;
	}

	private async ValueTask<CallState> MuxChannelPrivilegesAsync(IMUSHCodeParser parser, MString channel, string privileges)
		=> await ChannelPrivs.Handle(parser, LocateService, ChannelPermissions, Mediator, NotifyService, channel,
			MarkupText.Plain(privileges));

	private async ValueTask<CallState> MuxChannelLockAsync(IMUSHCodeParser parser, MString channel, string lockType,
		string lockKey)
		=> await ChannelLockAsync(await parser.CurrentState.KnownExecutorObject(Mediator), channel, lockType, lockKey);

	private async ValueTask<CallState> MuxNoEquivalentAsync(IMUSHCodeParser parser, string key)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		await NotifyService.NotifyLocalized(executor, key, executor);
		return new CallState(ErrorMessages.Returns.InvalidArgument);
	}

	/// <summary>The command's one argument, or the usage line when it is empty.</summary>
	private async ValueTask<MuxArgument> MuxChannelArgument(IMUSHCodeParser parser, string usage)
		=> parser.CurrentState.Arguments.GetValueOrDefault("0")?.Message is { Length: > 0 } channel
			// Cast, or the conditional's type is CallState, which MString converts to.
			? (MuxArgument)channel
			: await MuxUsageAsync(parser, usage);

	/// <summary>Both sides of <c>&lt;channel&gt;=&lt;other&gt;</c>, or the usage line when either is empty.</summary>
	private async ValueTask<MuxArguments> MuxChannelPair(IMUSHCodeParser parser, string usage)
		=> (parser.CurrentState.Arguments.GetValueOrDefault("0")?.Message,
				parser.CurrentState.Arguments.GetValueOrDefault("1")?.Message) switch
		{
			({ Length: > 0 } channel, { Length: > 0 } other) => new MuxPair(channel, other),
			_ => await MuxUsageAsync(parser, usage)
		};

	private async ValueTask<CallState> MuxUsageAsync(IMUSHCodeParser parser, string usage)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.UsageFormat), executor, usage);
		return new CallState(ErrorMessages.Returns.InvalidArgument);
	}

	/// <summary>A command's channel argument, or the usage line it answered instead.</summary>
	private union MuxArgument(MString, CallState);

	/// <summary>Both sides of a command's <c>=</c>, or the usage line it answered instead.</summary>
	private union MuxArguments(MuxPair, CallState);

	private readonly record struct MuxPair(MString Channel, MString Other);
}
