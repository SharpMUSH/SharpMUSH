using SharpMUSH.Implementation.Commands.ChannelCommand;
using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Notifications;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using CB = SharpMUSH.Library.Definitions.CommandBehavior;

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

		var (_, status) = maybeMemberStatus;

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

		var (_, status) = maybeMemberStatus;

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

		var (_, status) = maybeMemberStatus;

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
	
	[SharpCommand(Name = "ADDCOM", Switches = [], Behavior = CB.Default | CB.EqSplit | CB.NoGagged, MinArgs = 0,
		MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> AddCom(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var arg0Check = parser.CurrentState.Arguments.TryGetValue("0", out var arg0CallState);
		var arg1Check = parser.CurrentState.Arguments.TryGetValue("1", out var arg1CallState);
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);

		if (!arg0Check || !arg1Check)
		{
			await NotifyService!.Notify(executor, "Usage: addcom <alias>=<channel>");
			return new CallState("#-1 Usage: addcom <alias>=<channel>");
		}

		var alias = arg0CallState!.Message!.ToPlainText().Trim();
		var channelName = arg1CallState!.Message!;

		if (string.IsNullOrWhiteSpace(alias))
		{
			await NotifyService!.Notify(executor, "Alias name cannot be empty.");
			return new CallState("#-1 Alias name cannot be empty.");
		}

		// Get the channel
		var maybeChannel = await ChannelHelper.GetChannelOrError(parser, LocateService!, PermissionService!, Mediator!, NotifyService!, channelName, true);
		if (maybeChannel.IsError)
		{
			return maybeChannel.AsError.Value;
		}

		var channel = maybeChannel.AsChannel;

		// Check if already a member, if not join the channel
		var isMember = await ChannelHelper.IsMemberOfChannel(executor, channel);
		if (!isMember)
		{
			await Mediator!.Send(new AddUserToChannelCommand(channel, executor));
		}

		// Store the alias as an attribute
		var attributeName = $"CHANALIAS`{alias.ToUpper()}";
		var result = await AttributeService!.SetAttributeAsync(executor, executor, attributeName, channel.Name);

		if (result.IsT1)
		{
			await NotifyService!.Notify(executor, $"Error setting alias: {result.AsT1.Value}");
			return new CallState($"#-1 Error setting alias: {result.AsT1.Value}");
		}

		await NotifyService!.Notify(executor, $"Alias '{alias}' added for channel {channel.Name}.");
		return new CallState(string.Empty);
	}

	[SharpCommand(Name = "DELCOM", Switches = [], Behavior = CB.Default | CB.NoGagged, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> DeleteCom(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var arg0Check = parser.CurrentState.Arguments.TryGetValue("0", out var arg0CallState);
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);

		if (!arg0Check)
		{
			await NotifyService!.Notify(executor, "Usage: delcom <alias>");
			return new CallState("#-1 Usage: delcom <alias>");
		}

		var alias = arg0CallState!.Message!.ToPlainText().Trim();

		if (string.IsNullOrWhiteSpace(alias))
		{
			await NotifyService!.Notify(executor, "Alias name cannot be empty.");
			return new CallState("#-1 Alias name cannot be empty.");
		}

		// Get the alias attribute
		var attributeName = $"CHANALIAS`{alias.ToUpper()}";
		var maybeAttribute = await AttributeService!.GetAttributeAsync(executor, executor, attributeName, IAttributeService.AttributeMode.Read);

		if (maybeAttribute.IsNone)
		{
			await NotifyService!.Notify(executor, $"Alias '{alias}' not found.");
			return new CallState($"#-1 Alias '{alias}' not found.");
		}

		if (maybeAttribute.IsError)
		{
			await NotifyService!.Notify(executor, $"Error reading alias: {maybeAttribute.AsError.Value}");
			return new CallState($"#-1 Error reading alias: {maybeAttribute.AsError.Value}");
		}

		var channelName = maybeAttribute.AsAttribute.First().Value;

		// Delete the alias attribute
		var clearResult = await AttributeService!.ClearAttributeAsync(executor, executor, attributeName, IAttributeService.AttributePatternMode.Exact, IAttributeService.AttributeClearMode.Safe);

		if (clearResult.IsT1)
		{
			await NotifyService!.Notify(executor, $"Error deleting alias: {clearResult.AsT1.Value}");
			return new CallState($"#-1 Error deleting alias: {clearResult.AsT1.Value}");
		}

		// Check if this was the last alias for this channel
		var allAliases = await AttributeService!.GetAttributePatternAsync(executor, executor, "CHANALIAS`*", false, IAttributeService.AttributePatternMode.Wildcard);

		if (!allAliases.IsError)
		{
			var hasOtherAlias = allAliases.AsAttributes.Any(attr => attr.Value.ToPlainText().Equals(channelName.ToPlainText(), StringComparison.OrdinalIgnoreCase));

			if (!hasOtherAlias)
			{
				// Leave the channel if this was the last alias
				var maybeChannel = await ChannelHelper.GetChannelOrError(parser, LocateService!, PermissionService!, Mediator!, NotifyService!, channelName, false);
				if (!maybeChannel.IsError)
				{
					await Mediator!.Send(new RemoveUserFromChannelCommand(maybeChannel.AsChannel, executor));
				}
			}
		}

		await NotifyService!.Notify(executor, $"Alias '{alias}' deleted.");
		return new CallState(string.Empty);
	}

	[SharpCommand(Name = "@CLIST", Switches = ["FULL"], Behavior = CB.Default | CB.NoGagged, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> ChannelList(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		// @clist is an alias for @channel/list, with /full switch being ignored
		var switches = parser.CurrentState.Switches.Contains("FULL") 
			? new[] { "LIST" } 
			: new[] { "LIST" };
		
		return await ChannelCommand.ChannelList.Handle(
			parser, 
			LocateService!, 
			PermissionService!, 
			Mediator!, 
			NotifyService!, 
			MModule.empty(), 
			MModule.empty(), 
			switches);
	}

	[SharpCommand(Name = "COMTITLE", Switches = [], Behavior = CB.Default | CB.EqSplit | CB.NoGagged, MinArgs = 0,
		MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> ComTitle(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var arg0Check = parser.CurrentState.Arguments.TryGetValue("0", out var arg0CallState);
		var arg1Check = parser.CurrentState.Arguments.TryGetValue("1", out var arg1CallState);
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);

		if (!arg0Check || !arg1Check)
		{
			await NotifyService!.Notify(executor, "Usage: comtitle <alias>=<title>");
			return new CallState("#-1 Usage: comtitle <alias>=<title>");
		}

		var alias = arg0CallState!.Message!.ToPlainText().Trim();
		var title = arg1CallState!.Message!;

		if (string.IsNullOrWhiteSpace(alias))
		{
			await NotifyService!.Notify(executor, "Alias name cannot be empty.");
			return new CallState("#-1 Alias name cannot be empty.");
		}

		// Get the channel name from the alias
		var attributeName = $"CHANALIAS`{alias.ToUpper()}";
		var maybeAttribute = await AttributeService!.GetAttributeAsync(executor, executor, attributeName, IAttributeService.AttributeMode.Read);

		if (maybeAttribute.IsNone)
		{
			await NotifyService!.Notify(executor, $"Alias '{alias}' not found.");
			return new CallState($"#-1 Alias '{alias}' not found.");
		}

		if (maybeAttribute.IsError)
		{
			await NotifyService!.Notify(executor, $"Error reading alias: {maybeAttribute.AsError.Value}");
			return new CallState($"#-1 Error reading alias: {maybeAttribute.AsError.Value}");
		}

		var channelName = maybeAttribute.AsAttribute.First().Value;

		// Use the ChannelTitle handler
		return await ChannelTitle.Handle(parser, LocateService!, PermissionService!, Mediator!, NotifyService!, channelName, title);
	}

	[SharpCommand(Name = "COMLIST", Switches = [], Behavior = CB.Default | CB.NoGagged, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> ComList(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);

		// Get all CHANALIAS attributes
		var allAliases = await AttributeService!.GetAttributePatternAsync(executor, executor, "CHANALIAS`*", false, IAttributeService.AttributePatternMode.Wildcard);

		if (allAliases.IsError)
		{
			await NotifyService!.Notify(executor, $"Error reading aliases: {allAliases.AsError.Value}");
			return new CallState($"#-1 Error reading aliases: {allAliases.AsError.Value}");
		}

		var aliases = allAliases.AsAttributes.ToList();

		if (aliases.Count == 0)
		{
			await NotifyService!.Notify(executor, "You have no channel aliases.");
			return new CallState(string.Empty);
		}

		// Format output: <alias> : <channel>
		var outputLines = new List<MString>();
		foreach (var attr in aliases)
		{
			// Extract alias name from attribute name (CHANALIAS`ALIAS -> ALIAS)
			var attrName = attr.Name;
			var aliasName = attrName.StartsWith("CHANALIAS`") ? attrName.Substring(10) : attrName;
			var channelName = attr.Value;
			
			outputLines.Add(MModule.concat(
				MModule.single($"{aliasName.ToLower()} : "),
				channelName
			));
		}

		await NotifyService!.Notify(executor, MModule.multiple(outputLines.ToArray()));
		return new CallState(string.Empty);
	}
}