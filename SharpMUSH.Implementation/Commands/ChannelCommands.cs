using SharpMUSH.Implementation.Commands.ChannelCommand;
using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Notifications;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using CB = SharpMUSH.Library.Definitions.CommandBehavior;
using System.Collections.Immutable;
using System.Buffers;

namespace SharpMUSH.Implementation.Commands;

public partial class Commands
{
	[SharpCommand(Name = "@CEMIT", Switches = ["NOEVAL", "NOISY", "SILENT", "SPOOF"],
		Behavior = CB.Default | CB.EqSplit | CB.NoGagged, MinArgs = 0, MaxArgs = 0, ParameterNames = ["channel", "message"])]
	public async ValueTask<Option<CallState>> ChannelEmitCommand(IMUSHCodeParser parser, SharpCommandAttribute _2)
		=> await EmitOnChannel(parser, spoof: parser.CurrentState.Switches.Contains("SPOOF"));

	[SharpCommand(Name = "@NSCEMIT", Switches = ["NOEVAL", "NOISY", "SILENT"],
		Behavior = CB.Default | CB.EqSplit | CB.NoGagged, MinArgs = 0, MaxArgs = 0, ParameterNames = ["channel", "message"])]
	public async ValueTask<Option<CallState>> NoSpoofChannelEmitCommand(IMUSHCodeParser parser,
		SharpCommandAttribute _2)
		=> await EmitOnChannel(parser, spoof: true);

	/// <summary>
	/// The four spellings of PennMUSH's <c>do_cemit</c> differ only in whether they ask to spoof, so this
	/// reads the arguments and <see cref="ChannelEmit"/> does the rest. <c>cmd_cemit</c>
	/// (<c>src/extchat.c:3583</c>) sets <c>PEMIT_SPOOF</c> for <c>@NSCEMIT</c> and for <c>@CEMIT/SPOOF</c>;
	/// whether the caller MAY spoof is decided inside.
	/// </summary>
	private async ValueTask<Option<CallState>> EmitOnChannel(IMUSHCodeParser parser, bool spoof)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var arg0 = parser.CurrentState.Arguments.GetValueOrDefault("0")?.Message;
		var arg1 = parser.CurrentState.Arguments.GetValueOrDefault("1")?.Message;

		if (arg0 is null || arg1 is null)
		{
			await NotifyService.NotifyLocalized(executor,
				nameof(ErrorMessages.Notifications.DontYouHaveAnythingToSay), executor);
			return new CallState(ErrorMessages.Returns.NothingToDo);
		}

		return await ChannelEmit.Handle(PermissionService, Mediator, NotifyService, executor, arg0, arg1, spoof);
	}

	[SharpCommand(Name = "@CHAT", Switches = [], Behavior = CB.Default | CB.EqSplit | CB.NoGagged, MinArgs = 0, MaxArgs = 0, ParameterNames = ["channel", "message"])]
	public async ValueTask<Option<CallState>> Chat(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var arg0Check = parser.CurrentState.Arguments.TryGetValue("0", out var arg0CallState);
		var arg1Check = parser.CurrentState.Arguments.TryGetValue("1", out var arg1CallState);
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);

		if (!arg0Check || !arg1Check)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.DontYouHaveAnythingToSay), executor);
			return new CallState(ErrorMessages.Returns.NothingToDo);
		}

		var channelName = arg0CallState!.Message!;
		var message = arg1CallState!.Message!;

		return await ChannelHelper.GetVisibleChannelOrError(PermissionService, Mediator,
			NotifyService, executor, channelName, true) switch
		{
			SharpChannel channel => await ChatAsync(executor, channel, message),
			Error<CallState> error => error.Value
		};
	}

	private async ValueTask<Option<CallState>> ChatAsync(AnySharpObject executor, SharpChannel channel, MString message)
	{
		// extchat.c:1533-1546 — the type gate, then Chan_Can_Speak, which LOUD bypasses.
		if (await ChannelHelper.SpeechRefusal(PermissionService, executor, channel) is { } refusal)
		{
			await NotifyService.Notify(executor, refusal, executor);
			return new CallState(ErrorMessages.Returns.ChannelPermissionDenied);
		}

		var maybeMemberStatus = await ChannelHelper.ChannelMemberStatus(executor, channel);

		// extchat.c:1553 — the same rule @cemit answers to, from the same helper.
		if (ChannelHelper.OpenChannelRefusal(channel, maybeMemberStatus) is { } refusalToSpeak)
		{
			await NotifyService.Notify(executor, refusalToSpeak, executor);
			return new CallState(refusalToSpeak);
		}

		var status = maybeMemberStatus?.Status ?? new SharpChannelStatus(null, null, null, null, null);

		// sharpchat.md:33 — "If <message> begins with a ':' or ';' it will be posed (or semiposed)
		// instead of spoken." Anything else is speech, which is what produces the documented
		// `<Public> Mike says, "Hello"` rendering.
		var (chatType, chatMessage) = ClassifyChannelSpeech(message);

		await Mediator.Publish(new ChannelMessageNotification(
			channel,
			executor.WithNoneOption(),
			chatType,
			chatMessage,
			status.Title ?? MarkupText.Empty,
			MarkupText.Plain(executor.Object().Name),
			MarkupText.Plain("says"),
			[]
		));

		return new CallState(string.Empty);
	}

	private (INotifyService.NotificationType Type, MString Message) ClassifyChannelSpeech(MString message)
	{
		var plain = message.ToPlainText();
		var rest = message.Substring(1, message.Length - 1);

		return plain switch
		{
			[':', ..] => (INotifyService.NotificationType.Pose, rest),
			[';', ..] => (INotifyService.NotificationType.SemiPose, rest),
			_ => (INotifyService.NotificationType.Say, message)
		};
	}

	[SharpCommand(Name = "ADDCOM", Switches = [], Behavior = CB.Default | CB.EqSplit | CB.NoGagged, MinArgs = 0,
		MaxArgs = 0, ParameterNames = ["channel", "alias"])]
	public async ValueTask<Option<CallState>> AddCom(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var arg0Check = parser.CurrentState.Arguments.TryGetValue("0", out var arg0CallState);
		var arg1Check = parser.CurrentState.Arguments.TryGetValue("1", out var arg1CallState);
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);

		if (!arg0Check || !arg1Check)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.UsageAddcom), executor);
			return new CallState(ErrorMessages.Returns.UsageAddcom);
		}

		var alias = arg0CallState!.Message!.ToPlainText().Trim();
		var channelName = arg1CallState!.Message!;

		if (string.IsNullOrWhiteSpace(alias))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AliasNameCannotBeEmpty), executor);
			return new CallState(ErrorMessages.Returns.AliasCannotBeEmpty);
		}

		return await ChannelHelper.GetVisibleChannelOrError(PermissionService, Mediator,
			NotifyService, executor, channelName, true) switch
		{
			SharpChannel channel => await AddComAsync(executor, alias, channel),
			Error<CallState> error => error.Value
		};
	}

	private async ValueTask<Option<CallState>> AddComAsync(AnySharpObject executor, string alias, SharpChannel channel)
	{
		var isMember = await ChannelHelper.IsMemberOfChannel(executor, channel);
		if (!isMember)
		{
			// addcom joins the channel, so it answers to the same join gate as @channel/on.
			if (await executor.IsGuest())
			{
				await NotifyService.Notify(executor, ErrorMessages.Notifications.ChatGuestsCantJoin, executor);
				return new CallState(ErrorMessages.Returns.PermissionDenied);
			}

			var joinCheck = await ChannelHelper.JoinRefusal(PermissionService, executor, executor, channel);
			if (joinCheck.Refused)
			{
				await NotifyService.Notify(executor, joinCheck.Refusal!, executor);
				return new CallState(ErrorMessages.Returns.ChannelPermissionDenied);
			}

			if (joinCheck.Warning is not null)
			{
				await NotifyService.Notify(executor, joinCheck.Warning, executor);
			}

			await Mediator.Send(new AddUserToChannelCommand(channel, executor));
		}

		var attributeName = $"CHANALIAS`{alias.ToUpper()}";
		var result = await AttributeService.SetAttributeAsync(executor, executor, attributeName, channel.Name);

		if (result is Error<string> error)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ErrorSettingAliasFormat), executor, error.Value);
			return new CallState($"#-1 Error setting alias: {error.Value}");
		}

		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AliasAddedForChannelFormat), executor, alias, channel.Name.ToPlainText());
		return new CallState(string.Empty);
	}

	[SharpCommand(Name = "DELCOM", Switches = [], Behavior = CB.Default | CB.NoGagged, MinArgs = 0, MaxArgs = 0, ParameterNames = ["alias"])]
	public async ValueTask<Option<CallState>> DeleteCom(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var arg0Check = parser.CurrentState.Arguments.TryGetValue("0", out var arg0CallState);
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);

		if (!arg0Check)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.UsageDelcom), executor);
			return new CallState(ErrorMessages.Returns.UsageDelcom);
		}

		var alias = arg0CallState!.Message!.ToPlainText().Trim();

		if (string.IsNullOrWhiteSpace(alias))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AliasNameCannotBeEmpty), executor);
			return new CallState(ErrorMessages.Returns.AliasCannotBeEmpty);
		}

		var attributeName = $"CHANALIAS`{alias.ToUpper()}";
		return await AttributeService.GetAttributeAsync(executor, executor, attributeName, IAttributeService.AttributeMode.Read) switch
		{
			SharpAttribute[] aliasAttribute => await DeleteChannelAliasAsync(executor, alias, attributeName,
				aliasAttribute.Last().Value),
			None => await ChannelAliasNotFoundAsync(executor, alias),
			Error<string> error => await ChannelAliasUnreadableAsync(executor, error.Value)
		};
	}

	/// <summary>Removes the alias, and takes the executor off its channel when no other alias names it.</summary>
	private async ValueTask<Option<CallState>> DeleteChannelAliasAsync(AnySharpObject executor, string alias,
		string attributeName, MString channelName)
	{
		var clearResult = await AttributeService.ClearAttributeAsync(executor, executor, attributeName, IAttributeService.AttributePatternMode.Exact);

		if (clearResult is Error<string> error)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ErrorDeletingAliasFormat), executor, error.Value);
			return new CallState($"#-1 Error deleting alias: {error.Value}");
		}

		var allAliases = await AttributeService.GetAttributePatternAsync(executor, executor, "CHANALIAS`*", false, IAttributeService.AttributePatternMode.Wildcard);

		if (allAliases is SharpAttribute[] remainingAliases)
		{
			var hasOtherAlias = remainingAliases.Any(attr => attr.Value.ToPlainText().Equals(channelName.ToPlainText(), StringComparison.OrdinalIgnoreCase));

			if (!hasOtherAlias)
			{
				// RAW lookup on purpose — do not "fix" this to GetVisibleChannelOrError.
				//
				// This resolves the channel only to take the executor off it, and nothing about the outcome
				// reaches the player: the lookup is silent, the result is used solely to decide whether to
				// send RemoveUserFromChannelCommand, and delcom answers "Alias deleted." either way. There is
				// no observable difference to leak. A visible lookup would instead strand a membership the
				// player can no longer reach — leaving them on a channel they just removed their alias for.
				if (await ChannelHelper.GetChannelOrError(Mediator, channelName) is SharpChannel channel)
				{
					await Mediator.Send(new RemoveUserFromChannelCommand(channel, executor));
				}
			}
		}

		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AliasDeletedFormat), executor, alias);
		return new CallState(string.Empty);
	}

	[SharpCommand(Name = "@CLIST", Switches = ["FULL"], Behavior = CB.Default | CB.NoGagged, MinArgs = 0, MaxArgs = 0, ParameterNames = ["channel"])]
	public async ValueTask<Option<CallState>> ChannelList(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		// @clist is an alias for @channel/list, with /full switch being ignored
		var switches = parser.CurrentState.Switches.Contains("FULL")
			? new[] { "LIST" }
			: new[] { "LIST" };

		return await ChannelCommand.ChannelList.Handle(
			parser,
			LocateService,
			PermissionService,
			Mediator,
			NotifyService,
			ConnectionService,
			MarkupText.Empty,
			MarkupText.Empty,
			switches);
	}

	[SharpCommand(Name = "COMTITLE", Switches = [], Behavior = CB.Default | CB.EqSplit | CB.NoGagged, MinArgs = 0,
		MaxArgs = 0, ParameterNames = ["alias", "title"])]
	public async ValueTask<Option<CallState>> ComTitle(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var arg0Check = parser.CurrentState.Arguments.TryGetValue("0", out var arg0CallState);
		var arg1Check = parser.CurrentState.Arguments.TryGetValue("1", out var arg1CallState);
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);

		if (!arg0Check || !arg1Check)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.UsageComtitle), executor);
			return new CallState(ErrorMessages.Returns.UsageComtitle);
		}

		var alias = arg0CallState!.Message!.ToPlainText().Trim();
		var title = arg1CallState!.Message!;

		if (string.IsNullOrWhiteSpace(alias))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AliasNameCannotBeEmpty), executor);
			return new CallState(ErrorMessages.Returns.AliasCannotBeEmpty);
		}

		var attributeName = $"CHANALIAS`{alias.ToUpper()}";
		return await AttributeService.GetAttributeAsync(executor, executor, attributeName, IAttributeService.AttributeMode.Read) switch
		{
			SharpAttribute[] aliasAttribute => await SetAliasTitleAsync(parser, executor, alias, aliasAttribute.Last().Value,
				title),
			None => await ChannelAliasNotFoundAsync(executor, alias),
			Error<string> error => await ChannelAliasUnreadableAsync(executor, error.Value)
		};
	}

	private async ValueTask<Option<CallState>> ChannelAliasNotFoundAsync(AnySharpObject executor, string alias)
	{
		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AliasNotFoundFormat), executor, alias);
		return new CallState($"#-1 Alias '{alias}' not found.");
	}

	private async ValueTask<Option<CallState>> ChannelAliasUnreadableAsync(AnySharpObject executor, string error)
	{
		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ErrorReadingAliasFormat), executor, error);
		return new CallState($"#-1 Error reading alias: {error}");
	}

	/// <summary>Sets the executor's title on the channel <paramref name="alias"/> names.</summary>
	private async ValueTask<Option<CallState>> SetAliasTitleAsync(IMUSHCodeParser parser, AnySharpObject executor,
		string alias, MString channelName, MString title)
		=> await ChannelHelper.GetVisibleChannelOrError(PermissionService, Mediator,
				NotifyService, executor, channelName, true) switch
		{
			SharpChannel channel => await SetTitleOnChannelAsync(parser, executor, alias, channelName, channel, title),
			Error<CallState> error => error.Value
		};

	private async ValueTask<Option<CallState>> SetTitleOnChannelAsync(IMUSHCodeParser parser, AnySharpObject executor,
		string alias, MString channelName, SharpChannel channel, MString title)
	{
		var result = await ChannelTitle.Handle(parser, LocateService, PermissionService, Mediator, NotifyService,
			Configuration, channelName, title);

		if (result.Message != null && !result.Message.ToPlainText().StartsWith("#-1"))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.TitleSetForAliasChannelFormat), executor, title.ToPlainText(), alias, channel.Name.ToPlainText());
		}

		return result;
	}

	[SharpCommand(Name = "COMLIST", Switches = [], Behavior = CB.Default | CB.NoGagged, MinArgs = 0, MaxArgs = 0, ParameterNames = [])]
	public async ValueTask<Option<CallState>> ComList(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);

		return await AttributeService.GetAttributePatternAsync(executor, executor, "CHANALIAS`*", false,
				IAttributeService.AttributePatternMode.Wildcard) switch
		{
			SharpAttribute[] aliases => await ListChannelAliasesAsync(executor, aliases),
			Error<string> error => await ChannelAliasesUnreadableAsync(executor, error.Value)
		};
	}

	private async ValueTask<Option<CallState>> ChannelAliasesUnreadableAsync(AnySharpObject executor, string error)
	{
		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ErrorReadingAliasesFormat), executor, error);
		return new CallState($"#-1 Error reading aliases: {error}");
	}

	private async ValueTask<Option<CallState>> ListChannelAliasesAsync(AnySharpObject executor, SharpAttribute[] aliases)
	{
		if (aliases.Length == 0)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.YouHaveNoChannelAliases), executor);
			return new CallState(string.Empty);
		}

		var outputLines = aliases.Select(attr =>
		{
			var aliasName = attr.Name.StartsWith("CHANALIAS`") ? attr.Name[10..] : attr.Name;
			return MarkupText.Concat(MarkupText.Plain($"{aliasName.ToLower()} : "), attr.Value);
		});

		await NotifyService.Notify(executor, MarkupText.Concat(outputLines), executor);
		return new CallState(string.Empty);
	}

	[SharpCommand(Name = "@CHANNEL",
		Switches =
		[
			"LIST", "ADD", "DELETE", "RENAME", "MOGRIFIER", "NAME", "PRIVS", "QUIET", "DECOMPILE", "DESCRIBE", "CHOWN",
			"WIPE", "MUTE", "UNMUTE", "GAG", "UNGAG", "HIDE", "UNHIDE", "WHAT", "TITLE", "BRIEF", "RECALL", "BUFFER",
			"COMBINE", "UNCOMBINE", "ON", "JOIN", "OFF", "LEAVE", "WHO"
		], Behavior = CB.Default | CB.EqSplit | CB.NoGagged | CB.RSArgs, MinArgs = 0, MaxArgs = 0, ParameterNames = ["channel", "options..."])]
	public async ValueTask<Option<CallState>> Channel(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var switches = parser.CurrentState.Switches.ToArray();
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);

		// /quiet only pairs with /list or /recall — it is invalid alongside anything else.
		if (switches.Contains("QUIET") && !switches.Contains("LIST") && !switches.Contains("RECALL"))
		{
			await NotifyService.Notify(executor, "CHAT: Incorrect combination of switches.", executor);
			return new CallState("CHAT: INCORRECT COMBINATION OF SWITCHES");
		}

		// sharpchat.md:179-181 documents `@channel/list[/on|/off][/quiet] [<prefix>]` and
		// `@channel/what [<prefix>]` — the argument is OPTIONAL there and required everywhere else.
		// Reading Arguments["0"] unconditionally turned every argument-less switched form into a
		// KeyNotFoundException, so read through the dictionary and let the arms state their own arity.
		var arg0 = args.GetValueOrDefault("0")?.Message;
		var arg1 = args.GetValueOrDefault("1")?.Message;
		var emptyIfMissing0 = arg0 ?? MarkupText.Empty;
		var emptyIfMissing1 = arg1 ?? MarkupText.Empty;

		// Note: Channel visibility checking is handled by PermissionService.ChannelCanSeeAsync in each handler
		return switches switch
		{
			// /list, /recall and /decompile combine with other switches (`@channel/list/on/quiet`), so they
			// match on membership rather than on a positional list pattern that only fires when they are last.
			_ when switches.Contains("LIST") => await ChannelCommand.ChannelList.Handle(parser, LocateService,
				PermissionService, Mediator, NotifyService, ConnectionService, emptyIfMissing0, emptyIfMissing1,
				switches),
			// CB.RSArgs comma-splits the right-hand side, so `@channel/recall <chan>=<lines>,<start>` arrives
			// as two arguments — PennMUSH reads the same pair out of its lineinfo array (src/extchat.c:4008).
			_ when switches.Contains("RECALL") && arg0 is not null => await ChannelRecall.Handle(parser, LocateService,
				PermissionService, Mediator, NotifyService, arg0, emptyIfMissing1,
				args.GetValueOrDefault("2")?.Message ?? MarkupText.Empty, switches),
			_ when switches.Contains("DECOMPILE") && arg0 is not null => await ChannelDecompile.Handle(parser,
				LocateService, PermissionService, Mediator, NotifyService, ConnectionService, arg0, emptyIfMissing1,
				switches),
			["WHAT"] => await ChannelWhat.Handle(parser, LocateService, PermissionService, Mediator, NotifyService,
				emptyIfMissing0),
			["WHO"] when arg0 is not null => await ChannelWho.Handle(parser, LocateService, PermissionService, Mediator,
				NotifyService, ConnectionService, arg0),
			(["ON"] or ["JOIN"]) when arg0 is not null => await ChannelOn.Handle(parser, LocateService, PermissionService,
				Mediator, NotifyService, arg0, arg1),
			(["OFF"] or ["LEAVE"]) when arg0 is not null => await ChannelOff.Handle(parser, LocateService,
				PermissionService, Mediator, NotifyService, arg0, arg1),
			// The eight per-member switches are one operation in PennMUSH (do_chan_user_flags,
			// src/extchat.c:1900), and the un-forms are it with "n" for an answer (cmd_channel, :3628-3640).
			// The channel is OPTIONAL in every one of them: omitted, they act on every channel you are on.
			["GAG"] => await ChannelUserFlags.Handle(parser, PermissionService, Mediator, NotifyService,
				arg0, arg1, ChannelUserFlags.UserFlag.Gag, forceOff: false),
			["UNGAG"] => await ChannelUserFlags.Handle(parser, PermissionService, Mediator, NotifyService,
				arg0, arg1, ChannelUserFlags.UserFlag.Gag, forceOff: true),
			["MUTE"] => await ChannelUserFlags.Handle(parser, PermissionService, Mediator, NotifyService,
				arg0, arg1, ChannelUserFlags.UserFlag.Quiet, forceOff: false),
			["UNMUTE"] => await ChannelUserFlags.Handle(parser, PermissionService, Mediator, NotifyService,
				arg0, arg1, ChannelUserFlags.UserFlag.Quiet, forceOff: true),
			["HIDE"] => await ChannelUserFlags.Handle(parser, PermissionService, Mediator, NotifyService,
				arg0, arg1, ChannelUserFlags.UserFlag.Hide, forceOff: false),
			["UNHIDE"] => await ChannelUserFlags.Handle(parser, PermissionService, Mediator, NotifyService,
				arg0, arg1, ChannelUserFlags.UserFlag.Hide, forceOff: true),
			["COMBINE"] => await ChannelUserFlags.Handle(parser, PermissionService, Mediator, NotifyService,
				arg0, arg1, ChannelUserFlags.UserFlag.Combine, forceOff: false),
			["UNCOMBINE"] => await ChannelUserFlags.Handle(parser, PermissionService, Mediator, NotifyService,
				arg0, arg1, ChannelUserFlags.UserFlag.Combine, forceOff: true),
			// arg1 is null when no `=` was typed at all, which is the QUERY form — distinct from an `=` with
			// nothing after it, which clears the title (do_chan_title's rhs_present, src/extchat.c:3145).
			["TITLE"] when arg0 is not null => await ChannelTitle.Handle(parser, LocateService, PermissionService,
				Mediator, NotifyService, Configuration, arg0, arg1),
			["ADD"] when arg0 is not null && arg1 is not null
				=> await ChannelAdd.Handle(parser, LocateService, PermissionService, Mediator, NotifyService,
					Configuration, arg0, arg1),
			["PRIVS"] when arg0 is not null && arg1 is not null
				=> await ChannelPrivs.Handle(parser, LocateService, PermissionService, Mediator, NotifyService,
					arg0, arg1),
			["DESCRIBE"] when arg0 is not null && arg1 is not null
				=> await ChannelDescribe.Handle(parser, LocateService, PermissionService, Mediator, NotifyService,
					arg0, arg1),
			["BUFFER"] when arg0 is not null && arg1 is not null
				=> await ChannelBuffer.Handle(parser, LocateService, PermissionService, Mediator, NotifyService,
					Configuration, arg0, arg1),
			["CHOWN"] when arg0 is not null => await ChannelChown.Handle(parser, LocateService, PermissionService,
				Mediator, NotifyService, arg0, emptyIfMissing1),
			// NAME and RENAME are the same operation, as in PennMUSH (src/extchat.c:3605-3608, where both
			// switches call do_chan_admin with CH_ADMIN_RENAME). NAME was declared in the switch list above
			// but had no arm, so `@channel/name` fell through to the "What do you want to do" usage line.
			(["RENAME"] or ["NAME"]) when arg0 is not null && arg1 is not null
				=> await ChannelRename.Handle(parser, LocateService, PermissionService, Mediator, NotifyService,
					Configuration, arg0, arg1),
			["WIPE"] when arg0 is not null => await ChannelWipe.Handle(parser, LocateService, PermissionService, Mediator,
				NotifyService, arg0, emptyIfMissing1),
			["DELETE"] when arg0 is not null => await ChannelDelete.Handle(parser, LocateService, PermissionService,
				Mediator, NotifyService, arg0, emptyIfMissing1),
			["MOGRIFIER"] when arg0 is not null => await ChannelMogrifier.Handle(parser, LocateService, PermissionService,
				Mediator, NotifyService, arg0, arg1),
			_ => await NotifyAndReturnChannelUsage(executor)
		};
	}

	private async ValueTask<CallState> NotifyAndReturnChannelUsage(AnySharpObject executor)
	{
		await NotifyService.Notify(executor, "What do you want to do with the channel?", executor);
		return new CallState("What do you want to do with the channel?");
	}

	[SharpCommand(Name = "@CLOCK", Switches = ["JOIN", "SPEAK", "MOD", "SEE", "HIDE"], Behavior = CB.Default | CB.EqSplit,
		MinArgs = 1, MaxArgs = 2, ParameterNames = [])]
	public async ValueTask<Option<CallState>> ChannelLock(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		if (await RejectIfTooFewArguments(parser, _2) is { } tooFewArguments) return tooFewArguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var args = parser.CurrentState.Arguments;
		var switches = parser.CurrentState.Switches;

		var channelName = args["0"].Message!;
		var lockKey = args.TryGetValue("1", out var arg1) ? arg1.Message!.ToPlainText() : string.Empty;

		var lockType = switches.FirstOrDefault() ?? "JOIN";
		lockType = lockType.ToUpper();

		// Setting a lock on a channel you cannot see must be refused the same way as setting one on a
		// channel that does not exist, or @clock reports which names are taken. notify: true because the
		// gate emits ONE refusal for both cases: suppressing it does not make the two cases more alike, it
		// only makes a mistyped channel name fail in silence.
		return await ChannelHelper.GetVisibleChannelOrError(PermissionService, Mediator,
			NotifyService, executor, channelName, notify: true) switch
		{
			SharpChannel channel => await SetChannelLockAsync(executor, channel, lockType, lockKey),
			Error<CallState> error => error.Value
		};
	}

	private async ValueTask<Option<CallState>> SetChannelLockAsync(AnySharpObject executor, SharpChannel channel,
		string lockType, string lockKey)
	{
		// An absent modify lock grants no additional rights beyond the owner and wizard gates.
		if (!await PermissionService.ChannelCanModifyAsync(executor, channel))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PermissionDenied), executor);
			return new CallState(ErrorMessages.Returns.PermissionDenied);
		}

		if (lockType is not ("JOIN" or "SPEAK" or "SEE" or "HIDE" or "MOD"))
		{
			await NotifyService.Notify(executor, $"Invalid lock type: {lockType}", executor);
			return new CallState(ErrorMessages.Returns.InvalidLockType);
		}

		if (!string.IsNullOrEmpty(lockKey))
		{
			if (await BooleanExpressionParser.BindAsync(lockKey, executor, ExecutionBudget.CurrentToken) is not string bound)
			{
				await NotifyService.Notify(executor, "CHAT: I don't understand that key.", executor);
				return new CallState(ErrorMessages.Returns.InvalidLock);
			}
			lockKey = bound;
		}

		UpdateChannelCommand updateCommand = lockType switch
		{
			"JOIN" => new UpdateChannelCommand(channel, null, null, null, lockKey, null, null, null, null, null, null),
			"SPEAK" => new UpdateChannelCommand(channel, null, null, null, null, lockKey, null, null, null, null, null),
			"SEE" => new UpdateChannelCommand(channel, null, null, null, null, null, lockKey, null, null, null, null),
			"HIDE" => new UpdateChannelCommand(channel, null, null, null, null, null, null, lockKey, null, null, null),
			"MOD" => new UpdateChannelCommand(channel, null, null, null, null, null, null, null, lockKey, null, null),
			_ => new UpdateChannelCommand(channel, null, null, null, null, null, null, null, null, null, null)
		};

		await Mediator.Send(updateCommand, ExecutionBudget.CurrentToken);

		if (string.IsNullOrEmpty(lockKey))
		{
			await NotifyService.Notify(executor, $"{lockType} lock removed from channel {channel.Name.ToPlainText()}.", executor);
		}
		else
		{
			await NotifyService.Notify(executor, $"{lockType} lock set on channel {channel.Name.ToPlainText()}.", executor);
		}

		return CallState.Empty;
	}
}