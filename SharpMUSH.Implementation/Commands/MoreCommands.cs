using OneOf.Types;
using SharpMUSH.Implementation.Common;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.Queries.Database;
using CB = SharpMUSH.Library.Definitions.CommandBehavior;

namespace SharpMUSH.Implementation.Commands;

public partial class Commands
{
	[SharpCommand(Name = "@CLOCK", Switches = ["JOIN", "SPEAK", "MOD", "SEE", "HIDE"], Behavior = CB.Default | CB.EqSplit,
		MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> ChannelLock(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@LIST",
		Switches =
		[
			"LOWERCASE", "MOTD", "LOCKS", "FLAGS", "FUNCTIONS", "POWERS", "COMMANDS", "ATTRIBS", "ALLOCATIONS", "ALL",
			"BUILTIN", "LOCAL"
		], Behavior = CB.Default, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> List(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@LOGWIPE", Switches = ["CHECK", "CMD", "CONN", "ERR", "TRACE", "WIZ", "ROTATE", "TRIM", "WIPE"],
		Behavior = CB.Default | CB.NoGagged | CB.God, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> LogWipe(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@LSET", Switches = [], Behavior = CB.Default | CB.EqSplit | CB.NoGagged, MinArgs = 0,
		MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> LockSet(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@MALIAS",
		Switches =
		[
			"SET", "CREATE", "DESTROY", "DESCRIBE", "RENAME", "STATS", "CHOWN", "NUKE", "ADD", "REMOVE", "LIST", "ALL", "WHO",
			"MEMBERS", "USEFLAG", "SEEFLAG"
		], Behavior = CB.Default | CB.EqSplit | CB.NoGagged, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> MailAlias(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@SOCKSET", Switches = [], Behavior = CB.Default | CB.EqSplit | CB.NoGagged | CB.RSArgs,
		MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> SocketSet(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@SLAVE", Switches = ["RESTART"], Behavior = CB.Default, CommandLock = "FLAG^WIZARD",
		MinArgs = 0)]
	public static async ValueTask<Option<CallState>> Slave(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@UNRECYCLE", Switches = [], Behavior = CB.Default | CB.NoGagged, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> UnRecycle(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@WARNINGS", Switches = [], Behavior = CB.Default | CB.EqSplit, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> Warnings(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@WCHECK", Switches = ["ALL", "ME"], Behavior = CB.Default, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> WizardCheck(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "BUY", Switches = [], Behavior = CB.Default | CB.NoGagged, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> Buy(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "BRIEF", Switches = ["OPAQUE"], Behavior = CB.Default, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> Brief(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "DESERT", Switches = [], Behavior = CB.Player | CB.Thing, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> Desert(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "DISMISS", Switches = [], Behavior = CB.Player | CB.Thing, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> Dismiss(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "DROP", Switches = [], Behavior = CB.Player | CB.Thing, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> Drop(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "EMPTY", Switches = [], CommandLock = "(TYPE^PLAYER|TYPE^THING)&!FLAG^GAGGED",
		Behavior = CB.Player | CB.Thing | CB.NoGagged, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> Empty(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "ENTER", Switches = [], Behavior = CB.Default, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> Enter(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "FOLLOW", Switches = [], Behavior = CB.Player | CB.Thing | CB.NoGagged, MinArgs = 0,
		MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> Follow(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "GET", Switches = [], Behavior = CB.Player | CB.Thing | CB.NoGagged, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> Get(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "GIVE", Switches = ["SILENT"], Behavior = CB.Default | CB.EqSplit | CB.NoGagged, MinArgs = 0,
		MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> Give(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "HOME", Switches = [], Behavior = CB.Player | CB.Thing, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> Home(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "INVENTORY", Switches = [], Behavior = CB.Default, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> Inventory(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		// INVENTORY command - lists what you are carrying
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		
		// Default format
		var output = new System.Text.StringBuilder();
		output.AppendLine("You are carrying:");
		
		// Check if executor is a container (player or thing)
		if (executor.IsContainer)
		{
			var contents = await Mediator!.Send(new GetContentsQuery(executor.AsContainer));
			var itemsNames = new List<string>();
			
			if (contents != null)
			{
				await foreach (var item in contents)
				{
					itemsNames.Add(item.Object().Name);
				}
			}
			
			if (itemsNames.Count == 0)
			{
				output.AppendLine("  Nothing.");
			}
			else
			{
				foreach (var itemName in itemsNames)
				{
					output.AppendLine($"  {itemName}");
				}
			}
		}
		else
		{
			output.AppendLine("  Nothing.");
		}
		
		// TODO: Add money display once money system is implemented
		// output.AppendLine($"You have {pennies} {moneyName}.");
		
		await NotifyService!.Notify(executor, MModule.single(output.ToString().TrimEnd()));
		return CallState.Empty;
	}

	[SharpCommand(Name = "LEAVE", Switches = [], Behavior = CB.Player | CB.Thing, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> Leave(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "PAGE", Switches = ["LIST", "NOEVAL", "PORT", "OVERRIDE"],
		Behavior = CB.Default | CB.EqSplit | CB.NoGagged, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> Page(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var isNoEval = parser.CurrentState.Switches.Contains("NOEVAL");
		var isOverride = parser.CurrentState.Switches.Contains("OVERRIDE");
		
		// Get the raw arguments
		var recipientsArg = isNoEval 
			? ArgHelpers.NoParseDefaultNoParseArgument(args, 0, MModule.empty())
			: await ArgHelpers.NoParseDefaultEvaluatedArgument(parser, 0, MModule.empty());
		
		var messageArg = isNoEval
			? ArgHelpers.NoParseDefaultNoParseArgument(args, 1, MModule.empty())
			: await ArgHelpers.NoParseDefaultEvaluatedArgument(parser, 1, MModule.empty());
		
		// Parse message and recipients
		string recipientsText;
		
		// If no recipients provided, use last paged
		if (string.IsNullOrWhiteSpace(recipientsArg.ToPlainText()) && !string.IsNullOrWhiteSpace(messageArg.ToPlainText()))
		{
			// Get LASTPAGED attribute
			var lastPagedAttr = await AttributeService!.GetAttributeAsync(executor, executor, "LASTPAGED", IAttributeService.AttributeMode.Set, false);
			recipientsText = lastPagedAttr.Match(
				attr => attr.Last().Value.ToPlainText(),
				_ => string.Empty,
				_ => string.Empty
			);
			
			if (string.IsNullOrWhiteSpace(recipientsText))
			{
				await NotifyService!.Notify(executor, "Who do you want to page?");
				return CallState.Empty;
			}
		}
		else
		{
			recipientsText = recipientsArg.ToPlainText();
		}
		
		if (string.IsNullOrWhiteSpace(messageArg.ToPlainText()))
		{
			await NotifyService!.Notify(executor, "What do you want to page?");
			return CallState.Empty;
		}
		
		// Parse recipients list
		var recipientNames = recipientsText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
		var successfulRecipients = new List<AnySharpObject>();
		
		foreach (var recipientName in recipientNames)
		{
			// Locate the recipient
			var recipientResult = await LocateService!.LocateAndNotifyIfInvalidWithCallState(
				parser, executor, executor, recipientName, LocateFlags.All);
			
			if (!recipientResult.IsAnySharpObject)
			{
				continue;
			}
			
			var recipient = recipientResult.AsSharpObject;
			
			// Check HAVEN flag unless OVERRIDE switch is used
			if (!isOverride)
			{
				var recipientFlags = recipient.Object().Flags.Value;
				if (await recipientFlags.AnyAsync(f => f.Name.Equals("HAVEN", StringComparison.OrdinalIgnoreCase)))
				{
					await NotifyService!.Notify(executor, $"{recipient.Object().Name} is not accepting pages.");
					continue;
				}
			}
			
			// Check @lock/page unless OVERRIDE switch is used
			// TODO: Most of this nonsense is not evaluating those VERBs correctly.
			if (!isOverride)
			{
				var lockResult = await PermissionService!.CanInteract(executor, recipient, IPermissionService.InteractType.Hear | IPermissionService.InteractType.Page);
				if (!lockResult)
				{
					var failureAttr = await AttributeService!.GetAttributeAsync(executor, recipient, "PAGE_LOCK`FAILURE", IAttributeService.AttributeMode.Read);
					
					switch (failureAttr)
					{
						case { IsError: true }:
						case { IsNone: true }:
						{
							break;
						}
						case { IsAttribute: true, AsAttribute: var attr }:
						{
							await CommunicationService!.SendToRoomAsync(executor, await executor.Where(), _ => attr.Last().Value.ToPlainText(),
								INotifyService.NotificationType.Announce, recipient);
							break;
						}
					}
					
					var oFailureAttr = await AttributeService.GetAttributeAsync(executor, recipient, "PAGE_LOCK`OFAILURE", IAttributeService.AttributeMode.Read);
					
					switch (oFailureAttr)
					{
						case { IsError: true }:
						case { IsNone: true }:
						{
							break;
						}
						case { IsAttribute: true, AsAttribute: var attr }:
						{
							await CommunicationService!.SendToRoomAsync(executor, await executor.Where(), _ => attr.Last().Value.ToPlainText(),
								INotifyService.NotificationType.Announce, recipient);
							break;
						}
					}
					
					var aFailureAttr = await AttributeService.GetAttributeAsync(executor, recipient, "PAGE_LOCK`AFAILURE", IAttributeService.AttributeMode.Read);

					switch (aFailureAttr)
					{
						case { IsError: true }:
						case { IsNone: true }:
						{
							break;
						}
						case { IsAttribute: true }:
						{
							await CommunicationService!.SendToRoomAsync(executor, await executor.Where(), _ => messageArg,
								INotifyService.NotificationType.Announce, recipient);
							break;
						}
					}
					
					continue;
				}
			}
			
			// Send the page
			var pageMessage = $"From afar, {executor.Object().Name} pages: {messageArg}";
			await NotifyService!.Notify(recipient, pageMessage, executor, INotifyService.NotificationType.Say);
			
			successfulRecipients.Add(recipient);
		}
		
		// Notify executor
		if (successfulRecipients.Count > 0)
		{
			var recipientList = string.Join(", ", successfulRecipients.Select(r => r.Object().DBRef));
			await NotifyService!.Notify(executor, $"You paged {recipientList} with '{messageArg}'.");
			
			// Store LASTPAGED attribute
			var lastPagedText = string.Join(" ", successfulRecipients.Select(r => r.Object().DBRef));
			await AttributeService!.SetAttributeAsync(executor, executor, "LASTPAGED", MModule.single(lastPagedText));
		}
		else if (recipientNames.Length > 0)
		{
			await NotifyService!.Notify(executor, "No one to page.");
		}
		
		return CallState.Empty;
	}

	[SharpCommand(Name = "POSE", Switches = ["NOEVAL", "NOSPACE"], Behavior = CB.Default | CB.NoGagged, MinArgs = 0,
		MaxArgs = 1)]
	public static async ValueTask<Option<CallState>> Pose(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var executorLocation = await executor.Where();
		var contents = await executorLocation.Content(Mediator!);
		var isNoSpace = parser.CurrentState.Switches.Contains("NOSPACE");
		var isNoEvaluation = parser.CurrentState.Switches.Contains("NOEVAL");
		var message = isNoEvaluation
			? ArgHelpers.NoParseDefaultNoParseArgument(args, 1, MModule.empty())
			: await ArgHelpers.NoParseDefaultEvaluatedArgument(parser, 1, MModule.empty());

		var interactableContents = contents
			.Where(async (obj, _) =>
				await PermissionService!.CanInteract(obj, executor,
					IPermissionService.InteractType.Hear));

		await foreach (var obj in interactableContents)
		{
			await NotifyService!.Notify(
				obj.WithRoomOption(),
				isNoSpace
					? MModule.trim(message, MModule.single(" "), MModule.TrimType.TrimStart)
					: message,
				executor,
				INotifyService.NotificationType.Pose);
		}

		return new CallState(message);
	}

	[SharpCommand(Name = "SCORE", Switches = [], Behavior = CB.Default, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> Score(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		// SCORE command - displays how many pennies you have
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		
		// TODO: Implement money system - for now, just display a placeholder message
		// Once money system is implemented, retrieve from attribute or object property
		await NotifyService!.Notify(executor, "Score tracking not yet implemented.");
		
		// Future implementation:
		// var moneyAttr = await AttributeService!.GetAttributeAsync(executor, executor, "MONEY", IAttributeService.AttributeMode.Read);
		// var pennies = moneyAttr.Match(
		//     attr => int.TryParse(attr.LastOrDefault()?.Value.ToPlainText(), out var p) ? p : 0,
		//     _ => 0,
		//     _ => 0
		// );
		// var moneyName = pennies == 1 ? Configuration!.CurrentValue.Money.MoneySingular : Configuration!.CurrentValue.Money.MoneyPlural;
		// await NotifyService!.Notify(executor, $"You have {pennies} {moneyName}.");
		
		return CallState.Empty;
	}

	[SharpCommand(Name = "SAY", Switches = ["NOEVAL"], Behavior = CB.Default | CB.NoGagged, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> Say(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var executorLocation = await executor.Where();
		var contents = await executorLocation.Content(Mediator!);
		var isNoEvaluation = parser.CurrentState.Switches.Contains("NOEVAL");
		var message = isNoEvaluation
			? ArgHelpers.NoParseDefaultNoParseArgument(args, 1, MModule.empty())
			: await ArgHelpers.NoParseDefaultEvaluatedArgument(parser, 1, MModule.empty());

		var interactableContents = contents
			.Where(async (obj, _) =>
				await PermissionService!.CanInteract(obj.WithRoomOption(), executor,
					IPermissionService.InteractType.Hear));

		await foreach (var obj in interactableContents)
		{
			await NotifyService!.Notify(
				obj.WithRoomOption(),
				message,
				executor,
				INotifyService.NotificationType.Say);
		}

		return new CallState(message);
	}

	[SharpCommand(Name = "SEMIPOSE", Switches = ["NOEVAL"], Behavior = CB.Default | CB.NoGagged, MinArgs = 0,
		MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> SemiPose(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var executorLocation = await executor.Where();
		var contents = await executorLocation.Content(Mediator!);
		var isNoEvaluation = parser.CurrentState.Switches.Contains("NOEVAL");
		var message = isNoEvaluation
			? ArgHelpers.NoParseDefaultNoParseArgument(args, 1, MModule.empty())
			: await ArgHelpers.NoParseDefaultEvaluatedArgument(parser, 1, MModule.empty());

		var interactableContents = contents
			.Where(async (obj, _) =>
				await PermissionService!.CanInteract(obj.WithRoomOption(), executor, IPermissionService.InteractType.Hear));

		await foreach (var obj in interactableContents)
		{
			await NotifyService!.Notify(
				obj.WithRoomOption(),
				message,
				executor,
				INotifyService.NotificationType.SemiPose);
		}

		return new CallState(message);
	}

	[SharpCommand(Name = "TEACH", Switches = ["LIST"], Behavior = CB.Default | CB.NoParse, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> Teach(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "UNFOLLOW", Switches = [], Behavior = CB.Player | CB.Thing | CB.NoGagged, MinArgs = 0,
		MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> UnFollow(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "USE", Switches = [], Behavior = CB.Default | CB.NoGagged, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> Use(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "WHISPER", Switches = ["LIST", "NOISY", "SILENT", "NOEVAL"],
		Behavior = CB.Default | CB.EqSplit | CB.NoGagged, MinArgs = 2, MaxArgs = 2)]
	public static async ValueTask<Option<CallState>> Whisper(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		// WHISPER command - whispers a message to nearby players
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var args = parser.CurrentState.ArgumentsOrdered;
		var isNoEval = parser.CurrentState.Switches.Contains("NOEVAL");
		var isNoisy = parser.CurrentState.Switches.Contains("NOISY");
		var isSilent = parser.CurrentState.Switches.Contains("SILENT");
		var isList = parser.CurrentState.Switches.Contains("LIST");
		
		// Get target and message
		var targetArg = isNoEval
			? ArgHelpers.NoParseDefaultNoParseArgument(args, 0, MModule.empty())
			: await ArgHelpers.NoParseDefaultEvaluatedArgument(parser, 0, MModule.empty());
			
		var messageArg = isNoEval
			? ArgHelpers.NoParseDefaultNoParseArgument(args, 1, MModule.empty())
			: await ArgHelpers.NoParseDefaultEvaluatedArgument(parser, 1, MModule.empty());
		
		var targetText = targetArg.ToPlainText();
		var message = messageArg;
		
		if (string.IsNullOrWhiteSpace(targetText))
		{
			await NotifyService!.Notify(executor, "Whisper to whom?");
			return CallState.Empty;
		}
		
		if (string.IsNullOrWhiteSpace(message.ToPlainText()))
		{
			await NotifyService!.Notify(executor, "Whisper what?");
			return CallState.Empty;
		}
		
		// Get executor's location
		var executorLocation = await Mediator!.Send(new GetLocationQuery(executor.Object().DBRef));
		if (executorLocation.IsNone)
		{
			await NotifyService!.Notify(executor, "You have no location!");
			return CallState.Empty;
		}
		
		var location = executorLocation.Known();
		var targets = new List<AnySharpObject>();
		
		// Parse target list
		var targetNames = isList 
			? targetText.Split(' ', StringSplitOptions.RemoveEmptyEntries)
			: new[] { targetText };
		
		foreach (var targetName in targetNames)
		{
			// Locate target in same location
			var targetResult = await LocateService!.LocateAndNotifyIfInvalidWithCallState(
				parser, executor, executor, targetName, LocateFlags.PlayersPreference | LocateFlags.MatchObjectsInLookerLocation);
			
			if (!targetResult.IsAnySharpObject)
			{
				continue;
			}
			
			var target = targetResult.AsSharpObject;
			
			// Check if target is in same location
			var targetLocation = await Mediator!.Send(new GetLocationQuery(target.Object().DBRef));
			if (targetLocation.IsNone || !targetLocation.Known().Object().DBRef.Equals(location.Object().DBRef))
			{
				await NotifyService!.Notify(executor, $"{target.Object().Name} is not here.");
				continue;
			}
			
			targets.Add(target);
		}
		
		if (targets.Count == 0)
		{
			return CallState.Empty;
		}
		
		// Send whisper to targets
		foreach (var target in targets)
		{
			var targetDBRef = target.Object().DBRef;
			await NotifyService!.Notify(targetDBRef, MModule.concat(MModule.single($"{executor.Object().Name} whispers, \""), message, MModule.single("\"")));
		}
		
		// Notify executor
		var targetListText = string.Join(", ", targets.Select(t => t.Object().Name));
		await NotifyService!.Notify(executor, MModule.concat(MModule.single($"You whisper, \""), message, MModule.single($"\" to {targetListText}.")));
		
		// Handle noisy whispers (others in room may hear)
		if (isNoisy && !isSilent)
		{
			// Get all contents of the location
			var contents = await Mediator!.Send(new GetContentsQuery(location));
			if (contents != null)
			{
				await foreach (var occupant in contents)
				{
					var occupantDBRef = occupant.Object().DBRef;
					// Skip executor and whisper targets
					if (occupantDBRef.Equals(executor.Object().DBRef) || targets.Any(t => t.Object().DBRef.Equals(occupantDBRef)))
					{
						continue;
					}
					
					// Inform others that someone whispered (but not what)
					await NotifyService!.Notify(occupantDBRef, $"{executor.Object().Name} whispers something to {targetListText}.");
				}
			}
		}
		
		return new CallState(message);
	}

	[SharpCommand(Name = "WITH", Switches = ["NOEVAL", "ROOM"], Behavior = CB.Player | CB.Thing | CB.EqSplit, MinArgs = 0,
		MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> With(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "DOING", Switches = [], Behavior = CB.Default, MinArgs = 0, MaxArgs = 1)]
	public static async ValueTask<Option<CallState>> Doing(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		// DOING command - sets or displays the player's @doing message shown in WHO
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		
		// If no argument provided, display current @doing
		if (parser.CurrentState.Arguments.Count == 0 || string.IsNullOrWhiteSpace(parser.CurrentState.Arguments["0"].Message?.ToPlainText()))
		{
			var currentDoing = await AttributeService!.GetAttributeAsync(executor, executor, "DOING", IAttributeService.AttributeMode.Read);
			var doingText = currentDoing.Match(
				attr => attr.LastOrDefault()?.Value.ToPlainText() ?? "Nothing",
				_ => "Nothing",
				_ => "Nothing"
			);
			await NotifyService!.Notify(executor, $"Doing: {doingText}");
			return CallState.Empty;
		}
		
		// Set the @doing message
		var newDoing = parser.CurrentState.Arguments["0"].Message!;
		await AttributeService!.SetAttributeAsync(executor, executor, "DOING", newDoing);
		await NotifyService!.Notify(executor, "Doing set.");
		return new CallState(newDoing);
	}

	[SharpCommand(Name = "SESSION", Switches = [], Behavior = CB.Default, MinArgs = 0, MaxArgs = 1)]
	public static async ValueTask<Option<CallState>> Session(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		// SESSION command - displays connection information (admin version of WHO showing bytes)
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		
		// Check if executor has permission (wizard or admin)
		var isWizard = await executor.Object().Flags.Value.AnyAsync(f => f.Name.Equals("WIZARD", StringComparison.OrdinalIgnoreCase));
		if (!isWizard)
		{
			await NotifyService!.Notify(executor, "Permission denied.");
			return CallState.Empty;
		}
		
		// Get optional pattern filter
		var pattern = parser.CurrentState.Arguments.GetValueOrDefault("0")?.Message?.ToPlainText() ?? "";
		
		var output = new System.Text.StringBuilder();
		output.AppendLine("Player Name              On For Idle  Sent    Recv    Pend");
		output.AppendLine("------------------------ ------ ----- ------- ------- -------");
		
		var allConnections = ConnectionService!.GetAll();
		await foreach (var conn in allConnections)
		{
			string connPlayerName;
			
			// Filter by pattern if provided
			if (!string.IsNullOrWhiteSpace(pattern) && conn.Ref.HasValue)
			{
				var playerObj = await Mediator!.Send(new GetObjectNodeQuery(conn.Ref.Value));
				if (playerObj.IsNone)
				{
					continue;
				}
				var playerName = playerObj.AsSharpObject.Object().Name;
				if (!playerName.StartsWith(pattern, StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}
			}
			
			if (conn.Ref.HasValue)
			{
				var playerObj = await Mediator!.Send(new GetObjectNodeQuery(conn.Ref.Value));
				connPlayerName = playerObj.IsNone ? conn.Ref.Value.ToString() : playerObj.AsSharpObject.Object().Name;
			}
			else
			{
				connPlayerName = "<Connecting>";
			}
			
			var connectedTime = conn.Connected?.ToString(@"hh\:mm") ?? "00:00";
			var idleTime = conn.Idle?.TotalMinutes.ToString("F0") ?? "0";
			
			// TODO: Track bytes sent/received/pending once connection service tracks this
			var sent = conn.Metadata.GetValueOrDefault("BytesSent", "0");
			var recv = conn.Metadata.GetValueOrDefault("BytesReceived", "0");
			var pend = conn.Metadata.GetValueOrDefault("BytesPending", "0");
			
			output.AppendLine($"{connPlayerName,-24} {connectedTime,6} {idleTime,5} {sent,7} {recv,7} {pend,7}");
		}
		
		await NotifyService!.Notify(executor, MModule.single(output.ToString().TrimEnd()));
		return CallState.Empty;
	}

	[SharpCommand(Name = "WARN_ON_MISSING", Switches = [], Behavior = CB.Default | CB.NoParse | CB.Internal | CB.NoOp,
		MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> WarnOnMissing(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "UNIMPLEMENTED_COMMAND", Switches = [],
		Behavior = CB.Default | CB.NoParse | CB.Internal | CB.NoOp, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> UnimplementedCommand(IMUSHCodeParser parser,
		SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownEnactorObject(Mediator!);
		await NotifyService!.Notify(executor, "Huh?  (Type \"help\" for help.)");
		return new None();
	}
}