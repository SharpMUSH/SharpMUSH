using System.Diagnostics;
using System.Reflection;
using System.Linq;
using Humanizer;
using Microsoft.Extensions.Logging;
using OneOf.Types;
using SharpMUSH.Implementation.Common;
using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ExpandedObjectData;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using CB = SharpMUSH.Library.Definitions.CommandBehavior;

namespace SharpMUSH.Implementation.Commands;

public partial class Commands
{
	[SharpCommand(Name = "@ALLHALT", Switches = [], Behavior = CB.Default, CommandLock = "FLAG^WIZARD|POWER^HALT",
		MinArgs = 0)]
	public static async ValueTask<Option<CallState>> AllHalt(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@FLAG",
		Switches =
		[
			"ADD", "TYPE", "LETTER", "LIST", "RESTRICT", "DELETE", "ALIAS", "DISABLE", "ENABLE", "DEBUG", "DECOMPILE"
		], Behavior = CB.Default | CB.EqSplit | CB.RSArgs | CB.NoGagged, MinArgs = 0, MaxArgs = 2)]
	public static async ValueTask<Option<CallState>> Flag(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		// @FLAG command - manage object flags
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var switches = parser.CurrentState.Switches;
		
		// @flag/list - list all flags
		if (switches.Contains("LIST"))
		{
			var output = new System.Text.StringBuilder();
			output.AppendLine("Object Flags:");
			output.AppendLine("Name                 Symbol Type Restrictions");
			output.AppendLine("-------------------- ------ -------------------");
			
			var flags = Mediator!.CreateStream(new GetAllObjectFlagsQuery());
			await foreach (var flag in flags)
			{
				var types = string.Join(",", flag.TypeRestrictions);
				output.AppendLine($"{flag.Name,-20} {flag.Symbol,-6} {types}");
			}
			
			await NotifyService!.Notify(executor, output.ToString().TrimEnd());
			return CallState.Empty;
		}
		
		// @flag/add name=symbol - add a new flag
		if (switches.Contains("ADD"))
		{
			if (parser.CurrentState.Arguments.Count < 2)
			{
				await NotifyService!.Notify(executor, "@FLAG/ADD requires flag name and symbol.");
				return CallState.Empty;
			}
			
			var flagName = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
			var symbol = parser.CurrentState.Arguments["1"].Message!.ToPlainText();
			
			if (string.IsNullOrWhiteSpace(flagName) || string.IsNullOrWhiteSpace(symbol))
			{
				await NotifyService!.Notify(executor, "Flag name and symbol cannot be empty.");
				return CallState.Empty;
			}
			
			// Check if flag already exists
			var existingFlag = await Mediator!.Send(new GetObjectFlagQuery(flagName.ToUpper()));
			if (existingFlag != null)
			{
				await NotifyService!.Notify(executor, $"Flag '{flagName}' already exists.");
				return CallState.Empty;
			}
			
			// User-created flags are always non-system
			var result = await Mediator!.Send(new CreateObjectFlagCommand(
				flagName.ToUpper(),
				null, // aliases
				symbol,
				false, // system - user-created flags are NEVER system flags
				["FLAG^WIZARD"], // default set permissions
				["FLAG^WIZARD"], // default unset permissions
				["PLAYER", "THING", "ROOM", "EXIT"] // default type restrictions
			));
			
			if (result != null)
			{
				await NotifyService!.Notify(executor, $"Flag '{flagName}' created with symbol '{symbol}'.");
				return new CallState(MModule.single(flagName));
			}
			else
			{
				await NotifyService!.Notify(executor, $"Failed to create flag '{flagName}'.");
				return CallState.Empty;
			}
		}
		
		// @flag/delete name - delete a flag
		if (switches.Contains("DELETE"))
		{
			if (parser.CurrentState.Arguments.Count < 1)
			{
				await NotifyService!.Notify(executor, "@FLAG/DELETE requires a flag name.");
				return CallState.Empty;
			}
			
			var flagName = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
			
			if (string.IsNullOrWhiteSpace(flagName))
			{
				await NotifyService!.Notify(executor, "Flag name cannot be empty.");
				return CallState.Empty;
			}
			
			// Check if flag is a system flag
			var flag = await Mediator!.Send(new GetObjectFlagQuery(flagName.ToUpper()));
			if (flag == null)
			{
				await NotifyService!.Notify(executor, $"Flag '{flagName}' not found.");
				return CallState.Empty;
			}
			
			if (flag.System)
			{
				await NotifyService!.Notify(executor, $"Cannot delete system flag '{flagName}'.");
				return CallState.Empty;
			}
			
			var result = await Mediator!.Send(new DeleteObjectFlagCommand(flagName.ToUpper()));
			
			if (result)
			{
				await NotifyService!.Notify(executor, $"Flag '{flagName}' deleted.");
				return new CallState(MModule.single(flagName));
			}
			else
			{
				await NotifyService!.Notify(executor, $"Failed to delete flag '{flagName}'.");
				return CallState.Empty;
			}
		}
		
		// @flag/letter name=symbol - change flag symbol
		if (switches.Contains("LETTER"))
		{
			if (parser.CurrentState.Arguments.Count < 2)
			{
				await NotifyService!.Notify(executor, "@FLAG/LETTER requires flag name and new symbol.");
				return CallState.Empty;
			}
			
			var flagName = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
			var newSymbol = parser.CurrentState.Arguments["1"].Message!.ToPlainText();
			
			if (string.IsNullOrWhiteSpace(flagName) || string.IsNullOrWhiteSpace(newSymbol))
			{
				await NotifyService!.Notify(executor, "Flag name and symbol cannot be empty.");
				return CallState.Empty;
			}
			
			var flag = await Mediator!.Send(new GetObjectFlagQuery(flagName.ToUpper()));
			if (flag == null)
			{
				await NotifyService!.Notify(executor, $"Flag '{flagName}' not found.");
				return CallState.Empty;
			}
			
			if (flag.System)
			{
				await NotifyService!.Notify(executor, $"Cannot modify system flag '{flagName}'.");
				return CallState.Empty;
			}
			
			var result = await Mediator!.Send(new UpdateObjectFlagCommand(
				flagName.ToUpper(),
				flag.Aliases,
				newSymbol,
				flag.SetPermissions,
				flag.UnsetPermissions,
				flag.TypeRestrictions
			));
			
			if (result)
			{
				await NotifyService!.Notify(executor, $"Flag '{flagName}' symbol changed to '{newSymbol}'.");
				return new CallState(MModule.single(flagName));
			}
			else
			{
				await NotifyService!.Notify(executor, $"Failed to update flag '{flagName}'.");
				return CallState.Empty;
			}
		}
		
		// @flag/type name=types - change type restrictions
		if (switches.Contains("TYPE"))
		{
			if (parser.CurrentState.Arguments.Count < 2)
			{
				await NotifyService!.Notify(executor, "@FLAG/TYPE requires flag name and type restrictions.");
				return CallState.Empty;
			}
			
			var flagName = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
			var typesArg = parser.CurrentState.Arguments["1"].Message!.ToPlainText();
			
			if (string.IsNullOrWhiteSpace(flagName) || string.IsNullOrWhiteSpace(typesArg))
			{
				await NotifyService!.Notify(executor, "Flag name and types cannot be empty.");
				return CallState.Empty;
			}
			
			var flag = await Mediator!.Send(new GetObjectFlagQuery(flagName.ToUpper()));
			if (flag == null)
			{
				await NotifyService!.Notify(executor, $"Flag '{flagName}' not found.");
				return CallState.Empty;
			}
			
			if (flag.System)
			{
				await NotifyService!.Notify(executor, $"Cannot modify system flag '{flagName}'.");
				return CallState.Empty;
			}
			
			var types = typesArg.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries)
				.Select(t => t.ToUpper())
				.ToArray();
			
			var result = await Mediator!.Send(new UpdateObjectFlagCommand(
				flagName.ToUpper(),
				flag.Aliases,
				flag.Symbol,
				flag.SetPermissions,
				flag.UnsetPermissions,
				types
			));
			
			if (result)
			{
				await NotifyService!.Notify(executor, $"Flag '{flagName}' type restrictions updated to: {string.Join(", ", types)}.");
				return new CallState(MModule.single(flagName));
			}
			else
			{
				await NotifyService!.Notify(executor, $"Failed to update flag '{flagName}'.");
				return CallState.Empty;
			}
		}
		
		// @flag/alias name=aliases - set aliases for flag
		if (switches.Contains("ALIAS"))
		{
			if (parser.CurrentState.Arguments.Count < 2)
			{
				await NotifyService!.Notify(executor, "@FLAG/ALIAS requires flag name and aliases.");
				return CallState.Empty;
			}
			
			var flagName = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
			var aliasesArg = parser.CurrentState.Arguments["1"].Message!.ToPlainText();
			
			if (string.IsNullOrWhiteSpace(flagName))
			{
				await NotifyService!.Notify(executor, "Flag name cannot be empty.");
				return CallState.Empty;
			}
			
			var flag = await Mediator!.Send(new GetObjectFlagQuery(flagName.ToUpper()));
			if (flag == null)
			{
				await NotifyService!.Notify(executor, $"Flag '{flagName}' not found.");
				return CallState.Empty;
			}
			
			if (flag.System)
			{
				await NotifyService!.Notify(executor, $"Cannot modify system flag '{flagName}'.");
				return CallState.Empty;
			}
			
			string[]? aliases = null;
			if (!string.IsNullOrWhiteSpace(aliasesArg))
			{
				aliases = aliasesArg.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries)
					.Select(a => a.ToUpper())
					.ToArray();
			}
			
			var result = await Mediator!.Send(new UpdateObjectFlagCommand(
				flagName.ToUpper(),
				aliases,
				flag.Symbol,
				flag.SetPermissions,
				flag.UnsetPermissions,
				flag.TypeRestrictions
			));
			
			if (result)
			{
				var aliasStr = aliases != null && aliases.Length > 0 ? string.Join(", ", aliases) : "none";
				await NotifyService!.Notify(executor, $"Flag '{flagName}' aliases set to: {aliasStr}.");
				return new CallState(MModule.single(flagName));
			}
			else
			{
				await NotifyService!.Notify(executor, $"Failed to update flag '{flagName}'.");
				return CallState.Empty;
			}
		}
		
		// @flag/restrict name=permissions - set permissions
		if (switches.Contains("RESTRICT"))
		{
			if (parser.CurrentState.Arguments.Count < 2)
			{
				await NotifyService!.Notify(executor, "@FLAG/RESTRICT requires flag name and permissions.");
				return CallState.Empty;
			}
			
			var flagName = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
			var permsArg = parser.CurrentState.Arguments["1"].Message!.ToPlainText();
			
			if (string.IsNullOrWhiteSpace(flagName) || string.IsNullOrWhiteSpace(permsArg))
			{
				await NotifyService!.Notify(executor, "Flag name and permissions cannot be empty.");
				return CallState.Empty;
			}
			
			var flag = await Mediator!.Send(new GetObjectFlagQuery(flagName.ToUpper()));
			if (flag == null)
			{
				await NotifyService!.Notify(executor, $"Flag '{flagName}' not found.");
				return CallState.Empty;
			}
			
			if (flag.System)
			{
				await NotifyService!.Notify(executor, $"Cannot modify system flag '{flagName}'.");
				return CallState.Empty;
			}
			
			var perms = permsArg.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries);
			
			var result = await Mediator!.Send(new UpdateObjectFlagCommand(
				flagName.ToUpper(),
				flag.Aliases,
				flag.Symbol,
				perms,
				perms,
				flag.TypeRestrictions
			));
			
			if (result)
			{
				await NotifyService!.Notify(executor, $"Flag '{flagName}' permissions updated to: {string.Join(", ", perms)}.");
				return new CallState(MModule.single(flagName));
			}
			else
			{
				await NotifyService!.Notify(executor, $"Failed to update flag '{flagName}'.");
				return CallState.Empty;
			}
		}
		
		// @flag/decompile name - show flag definition
		if (switches.Contains("DECOMPILE"))
		{
			if (parser.CurrentState.Arguments.Count < 1)
			{
				await NotifyService!.Notify(executor, "@FLAG/DECOMPILE requires a flag name.");
				return CallState.Empty;
			}
			
			var flagName = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
			
			var flag = await Mediator!.Send(new GetObjectFlagQuery(flagName.ToUpper()));
			if (flag == null)
			{
				await NotifyService!.Notify(executor, $"Flag '{flagName}' not found.");
				return CallState.Empty;
			}
			
			var output = new System.Text.StringBuilder();
			output.AppendLine($"Flag: {flag.Name}");
			output.AppendLine($"Symbol: {flag.Symbol}");
			output.AppendLine($"System: {(flag.System ? "Yes" : "No")}");
			output.AppendLine($"Disabled: {(flag.Disabled ? "Yes" : "No")}");
			output.AppendLine($"Aliases: {(flag.Aliases != null && flag.Aliases.Length > 0 ? string.Join(", ", flag.Aliases) : "none")}");
			output.AppendLine($"Type Restrictions: {string.Join(", ", flag.TypeRestrictions)}");
			output.AppendLine($"Set Permissions: {string.Join(", ", flag.SetPermissions)}");
			output.AppendLine($"Unset Permissions: {string.Join(", ", flag.UnsetPermissions)}");
			
			await NotifyService!.Notify(executor, output.ToString().TrimEnd());
			return CallState.Empty;
		}
		
		// @flag/disable and @flag/enable - toggle flag disabled state
		if (switches.Contains("DISABLE") || switches.Contains("ENABLE"))
		{
			if (parser.CurrentState.Arguments.Count < 1)
			{
				await NotifyService!.Notify(executor, $"@FLAG/{(switches.Contains("DISABLE") ? "DISABLE" : "ENABLE")} requires a flag name.");
				return CallState.Empty;
			}
			
			var flagName = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
			
			if (string.IsNullOrWhiteSpace(flagName))
			{
				await NotifyService!.Notify(executor, "Flag name cannot be empty.");
				return CallState.Empty;
			}
			
			var flag = await Mediator!.Send(new GetObjectFlagQuery(flagName.ToUpper()));
			if (flag == null)
			{
				await NotifyService!.Notify(executor, $"Flag '{flagName}' not found.");
				return CallState.Empty;
			}
			
			if (flag.System)
			{
				await NotifyService!.Notify(executor, $"Cannot disable system flag '{flagName}'.");
				return CallState.Empty;
			}
			
			bool disable = switches.Contains("DISABLE");
			var result = await Mediator!.Send(new SetObjectFlagDisabledCommand(flagName.ToUpper(), disable));
			
			if (result)
			{
				await NotifyService!.Notify(executor, $"Flag '{flagName}' {(disable ? "disabled" : "enabled")}.");
				return new CallState(MModule.single(flagName));
			}
			else
			{
				await NotifyService!.Notify(executor, $"Failed to {(disable ? "disable" : "enable")} flag '{flagName}'.");
				return CallState.Empty;
			}
		}
		
		// @flag/debug - show debug information (currently same as decompile)
		if (switches.Contains("DEBUG"))
		{
			if (parser.CurrentState.Arguments.Count < 1)
			{
				await NotifyService!.Notify(executor, "@FLAG/DEBUG requires a flag name.");
				return CallState.Empty;
			}
			
			var flagName = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
			
			var flag = await Mediator!.Send(new GetObjectFlagQuery(flagName.ToUpper()));
			if (flag == null)
			{
				await NotifyService!.Notify(executor, $"Flag '{flagName}' not found.");
				return CallState.Empty;
			}
			
			var output = new System.Text.StringBuilder();
			output.AppendLine($"DEBUG - Flag: {flag.Name}");
			output.AppendLine($"ID: {flag.Id ?? "N/A"}");
			output.AppendLine($"Symbol: {flag.Symbol}");
			output.AppendLine($"System: {(flag.System ? "Yes" : "No")}");
			output.AppendLine($"Disabled: {(flag.Disabled ? "Yes" : "No")}");
			output.AppendLine($"Aliases: {(flag.Aliases != null && flag.Aliases.Length > 0 ? string.Join(", ", flag.Aliases) : "none")}");
			output.AppendLine($"Type Restrictions: {string.Join(", ", flag.TypeRestrictions)}");
			output.AppendLine($"Set Permissions: {string.Join(", ", flag.SetPermissions)}");
			output.AppendLine($"Unset Permissions: {string.Join(", ", flag.UnsetPermissions)}");
			
			await NotifyService!.Notify(executor, output.ToString().TrimEnd());
			return CallState.Empty;
		}
		
		// Default - show usage
		await NotifyService!.Notify(executor, "Usage: @flag/list, @flag/add <name>=<symbol>, @flag/delete <name>, @flag/letter <name>=<symbol>, @flag/type <name>=<types>, @flag/alias <name>=<aliases>, @flag/restrict <name>=<permissions>, @flag/decompile <name>");
		return CallState.Empty;
	}

	[SharpCommand(Name = "@LOG", Switches = ["CHECK", "CMD", "CONN", "ERR", "TRACE", "WIZ", "RECALL"],
		Behavior = CB.Default | CB.NoGagged, CommandLock = "FLAG^WIZARD", MinArgs = 0)]
	public static async ValueTask<Option<CallState>> Log(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var switches = parser.CurrentState.Switches;
		
		// Determine the log category based on switches
		var category = switches.Contains("CHECK") ? "Check" :
		               switches.Contains("CMD") ? "Command" :
		               switches.Contains("CONN") ? "Connection" :
		               switches.Contains("ERR") ? "Error" :
		               switches.Contains("TRACE") ? "Trace" :
		               switches.Contains("WIZ") ? "Wizard" :
		               "Command"; // Default to Command log
		
		// Handle /recall switch - display log entries
		if (switches.Contains("RECALL"))
		{
			// Get optional number argument for how many lines to display
			var countArg = parser.CurrentState.Arguments.TryGetValue("0", out var countCallState) 
				? countCallState!.Message!.ToPlainText() 
				: "100";
			
			if (!int.TryParse(countArg, out var count))
			{
				count = 100;
			}
			
			// Clamp count to reasonable values
			count = Math.Max(1, Math.Min(count, 1000));
			
			// Retrieve logs from the database via Mediator
			var logs = Mediator!.CreateStream(new GetConnectionLogsQuery(category, 0, count));
			var logList = new List<LogEventEntity>();
			
			await foreach (var log in logs)
			{
				logList.Add(log);
			}
			
			if (logList.Count == 0)
			{
				await NotifyService!.Notify(executor, $"No log entries found for category '{category}'.");
				return CallState.Empty;
			}
			
			// Display logs in reverse chronological order
			var output = new System.Text.StringBuilder();
			output.AppendLine($"--- Log entries for {category} (showing {logList.Count}) ---");
			
			foreach (var log in logList)
			{
				var timestamp = log.Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
				var message = log.Message ?? log.MessageTemplate ?? "(no message)";
				output.AppendLine($"[{timestamp}] {message}");
			}
			
			await NotifyService!.Notify(executor, output.ToString().TrimEnd());
			return CallState.Empty;
		}
		
		// Handle writing a log entry
		var logMessageArg = parser.CurrentState.Arguments.TryGetValue("0", out var logCallState);
		
		if (!logMessageArg || string.IsNullOrWhiteSpace(logCallState!.Message!.ToPlainText()))
		{
			await NotifyService!.Notify(executor, "Usage: @log[/<switch>] <message> or @log/recall[/<switch>] [<number>]");
			return new CallState("#-1 INVALID ARGUMENTS");
		}
		
		var logMessage = logCallState!.Message!;
		
		// Log the message with the appropriate category
		using (Logger!.BeginScope(new Dictionary<string, string>
		{
			["Category"] = category,
			["ExecutorDBRef"] = executor.Object().DBRef.ToString(),
			["ExecutorName"] = executor.Object().Name
		}))
		{
			Logger.LogInformation("{LogMessage}", MModule.serialize(logMessage));
		}
		
		await NotifyService!.Notify(executor, $"Message logged to {category} log.");
		return CallState.Empty;
	}

	[SharpCommand(Name = "@POOR", Switches = [], Behavior = CB.Default, MinArgs = 1, MaxArgs = 1)]
	public static async ValueTask<Option<CallState>> Poor(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		// @poor <player> - Set a player's quota to 0 (prevent building)
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		
		// Check permission - wizard only
		if (!await executor.IsWizard())
		{
			return await NotifyService!.NotifyAndReturn(
				executor.Object().DBRef,
				errorReturn: ErrorMessages.Returns.PermissionDenied,
				notifyMessage: ErrorMessages.Notifications.PermissionDenied,
				shouldNotify: true);
		}
		
		if (parser.CurrentState.Arguments.Count < 1)
		{
			await NotifyService!.Notify(executor, "Usage: @poor <player>");
			return new CallState("#-1 INVALID ARGUMENTS");
		}
		
		// Check if quota system is enabled
		if (!Configuration!.CurrentValue.Limit.UseQuota)
		{
			await NotifyService!.Notify(executor, "The quota system is disabled on this server.");
			return CallState.Empty;
		}
		
		var playerArg = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var maybePlayer = await LocateService!.LocatePlayerAndNotifyIfInvalidWithCallState(parser, executor, executor, playerArg);
		
		if (maybePlayer.IsError)
		{
			return maybePlayer.AsError;
		}
		
		var player = maybePlayer.AsSharpObject.AsPlayer;
		
		// Set player's quota to 0 (poor status)
		await Mediator!.Send(new SetPlayerQuotaCommand(player, 0));
		
		await NotifyService!.Notify(executor, $"{player.Object.Name} has been set to poor status (quota: 0).");
		await NotifyService.Notify(player.Object.DBRef, $"Your building quota has been set to 0 by {executor.Object().Name}.");
		
		return CallState.Empty;
	}

	[SharpCommand(Name = "@SQUOTA", Switches = [], Behavior = CB.Default | CB.EqSplit, MinArgs = 0, MaxArgs = 1)]
	public static async ValueTask<Option<CallState>> ShortQuota(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		// @squota [<player>] - Short form quota display
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var args = parser.CurrentState.Arguments;
		
		// Check if quota system is enabled
		if (!Configuration!.CurrentValue.Limit.UseQuota)
		{
			await NotifyService!.Notify(executor, "Quota system disabled.");
			return CallState.Empty;
		}
		
		AnySharpObject targetPlayer = executor;
		if (args.Count > 0)
		{
			var playerArg = args["0"].Message!.ToPlainText();
			var maybePlayer = await LocateService!.LocatePlayerAndNotifyIfInvalidWithCallState(parser, executor, executor, playerArg);
			if (maybePlayer.IsError)
			{
				return maybePlayer.AsError;
			}
			targetPlayer = maybePlayer.AsSharpObject;
		}
		
		var targetPlayerObj = targetPlayer.AsPlayer;
		var quota = targetPlayerObj.Quota;
		
		// Count objects owned by the player
		var objectsOwned = await Mediator!.Send(new GetOwnedObjectCountQuery(targetPlayerObj));
		
		await NotifyService!.Notify(executor, $"Quota: {objectsOwned}/{quota}");
		
		return CallState.Empty;
	}


	[SharpCommand(Name = "@RWALL", Switches = ["NOEVAL", "EMIT"], Behavior = CB.Default,
		CommandLock = "FLAG^WIZARD|FLAG^ROYALTY", MinArgs = 0)]
	public static async ValueTask<Option<CallState>> RoyaltyWall(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		// Future enhancement: Could pipe message through SPEAK() function for text processing
		var shout = parser.CurrentState.Arguments["0"].Message!;
		var handles = ConnectionService!.GetAll().Select(x => x.Handle);

		if (!parser.CurrentState.Switches.Contains("EMIT"))
		{
			shout = MModule.concat(MModule.single(Configuration!.CurrentValue.Cosmetic.RoyaltyWallPrefix + " "), shout);
		}

		await foreach (var handle in handles)
		{
			await NotifyService!.Notify(handle, shout);
		}

		return new CallState(shout);
	}

	[SharpCommand(Name = "@WIZWALL", Switches = ["NOEVAL", "EMIT"], Behavior = CB.Default, CommandLock = "FLAG^WIZARD",
		MinArgs = 1, MaxArgs = 1)]
	public static async ValueTask<Option<CallState>> WizardWall(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		// Future enhancement: Could pipe message through SPEAK() function for text processing
		var shout = parser.CurrentState.Arguments["0"].Message!;
		var handles = ConnectionService!.GetAll().Select(x => x.Handle);

		if (!parser.CurrentState.Switches.Contains("EMIT"))
		{
			shout = MModule.concat(MModule.single(Configuration!.CurrentValue.Cosmetic.WizardWallPrefix + " "), shout);
		}

		await foreach (var handle in handles)
		{
			await NotifyService!.Notify(handle, shout);
		}

		return new CallState(shout);
	}

	[SharpCommand(Name = "@ALLQUOTA", Switches = ["QUIET"], Behavior = CB.Default,
		CommandLock = "FLAG^WIZARD|POWER^QUOTA", MinArgs = 1, MaxArgs = 1)]
	public static async ValueTask<Option<CallState>> AllQuota(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		// @allquota <amount> - Set quota for all players
		// @allquota/quiet <amount> - Set quota without notification
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var switches = parser.CurrentState.Switches;
		var isQuiet = switches.Contains("QUIET");
		
		if (parser.CurrentState.Arguments.Count < 1)
		{
			await NotifyService!.Notify(executor, "Usage: @allquota <amount>");
			return new CallState("#-1 INVALID ARGUMENTS");
		}
		
		var amountArg = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		if (!int.TryParse(amountArg, out var amount))
		{
			await NotifyService!.Notify(executor, "Quota amount must be a number.");
			return new CallState("#-1 INVALID ARGUMENTS");
		}
		
		// Check if quota system is enabled
		if (!Configuration!.CurrentValue.Limit.UseQuota)
		{
			await NotifyService!.Notify(executor, "The quota system is disabled on this server.");
			return CallState.Empty;
		}
		
		// Query all players and set their quota
		var players = Mediator!.CreateStream(new GetAllPlayersQuery());
		var count = 0;
		
		await foreach (var player in players)
		{
			await Mediator.Send(new SetPlayerQuotaCommand(player, amount));
			count++;
			
			if (!isQuiet)
			{
				await NotifyService!.Notify(player.Object.DBRef, 
					$"Your building quota has been set to {amount} by {executor.Object().Name}.");
			}
		}
		
		await NotifyService!.Notify(executor, $"Set quota to {amount} for {count} players.");
		
		return CallState.Empty;
	}

	[SharpCommand(Name = "@DBCK", Switches = [], Behavior = CB.Default, CommandLock = "FLAG^WIZARD", MinArgs = 0)]
	public static async ValueTask<Option<CallState>> DatabaseCheck(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		await NotifyService!.Notify(executor, "Not Supported for SharpMUSH.");
		return CallState.Empty;
	}

	[SharpCommand(Name = "@HIDE", Switches = ["NO", "OFF", "YES", "ON"], Behavior = CB.Default, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> Hide(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		// @HIDE command - sets/unsets the DARK flag on the executor to hide from WHO lists
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var switches = parser.CurrentState.Switches;
		
		// Get the DARK flag
		var darkFlag = await Mediator!.Send(new GetObjectFlagQuery("DARK"));
		if (darkFlag == null)
		{
			await NotifyService!.Notify(executor, "Error: DARK flag not found in database.");
			return CallState.Empty;
		}
		
		// Check current DARK flag state
		var isDark = await executor.HasFlag("DARK");
		
		// Determine desired state
		bool shouldBeDark;
		if (switches.Contains("YES") || switches.Contains("ON"))
		{
			shouldBeDark = true;
		}
		else if (switches.Contains("NO") || switches.Contains("OFF"))
		{
			shouldBeDark = false;
		}
		else
		{
			// No switch = toggle
			shouldBeDark = !isDark;
		}
		
		// Apply the change if needed
		if (shouldBeDark && !isDark)
		{
			// Set DARK flag
			await Mediator!.Send(new SetObjectFlagCommand(executor, darkFlag));
			await NotifyService!.Notify(executor, "You are now hidden from the WHO list.");
		}
		else if (!shouldBeDark && isDark)
		{
			// Unset DARK flag
			await Mediator!.Send(new UnsetObjectFlagCommand(executor, darkFlag));
			await NotifyService!.Notify(executor, "You are no longer hidden from the WHO list.");
		}
		else
		{
			// No change needed
			if (isDark)
			{
				await NotifyService!.Notify(executor, "You are already hidden from the WHO list.");
			}
			else
			{
				await NotifyService!.Notify(executor, "You are already visible on the WHO list.");
			}
		}
		
		return CallState.Empty;
	}

	[SharpCommand(Name = "@MOTD", Switches = ["CONNECT", "LIST", "WIZARD", "DOWN", "FULL", "CLEAR"],
		Behavior = CB.Default | CB.NoGagged, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> MessageOfTheDay(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		/*
			@motd[/<type>] <message>
			@motd/clear[/<type>]
			@motd/list
  */
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var switches = parser.CurrentState.Switches;
		var args = parser.CurrentState.ArgumentsOrdered;
		var argText = ArgHelpers.NoParseDefaultNoParseArgument(args, 1, MModule.empty()).ToString();
		
		// Get current MOTD data
		var motdData = await ObjectDataService!.GetExpandedServerDataAsync<MotdData>() ?? new MotdData();
		
		// @motd/list - list all MOTDs
		if (switches.Contains("LIST"))
		{
			// Permission check - must be wizard/royalty
			if (!executor.IsGod() && !await executor.IsWizard())
			{
				await NotifyService!.Notify(executor, "Permission denied.");
				return CallState.Empty;
			}
			
			var output = new System.Text.StringBuilder();
			output.AppendLine("Message of the Day settings:");
			output.AppendLine($"Connect MOTD: {(string.IsNullOrEmpty(motdData.ConnectMotd) ? "(not set)" : motdData.ConnectMotd)}");
			output.AppendLine($"Wizard MOTD:  {(string.IsNullOrEmpty(motdData.WizardMotd) ? "(not set)" : motdData.WizardMotd)}");
			output.AppendLine($"Down MOTD:    {(string.IsNullOrEmpty(motdData.DownMotd) ? "(not set)" : motdData.DownMotd)}");
			output.AppendLine($"Full MOTD:    {(string.IsNullOrEmpty(motdData.FullMotd) ? "(not set)" : motdData.FullMotd)}");
			
			await NotifyService!.Notify(executor, output.ToString().TrimEnd());
			return CallState.Empty;
		}
		
		// Determine which type of MOTD we're working with
		string motdType;
		bool isConnect = !switches.Any() || switches.Contains("CONNECT");
		bool isWizard = switches.Contains("WIZARD");
		bool isDown = switches.Contains("DOWN");
		bool isFull = switches.Contains("FULL");
		
		if (isWizard)
			motdType = "wizard";
		else if (isDown)
			motdType = "down";
		else if (isFull)
			motdType = "full";
		else
			motdType = "connect";
		
		// Permission checks
		if (motdType == "connect")
		{
			// Need Announce power for connect MOTD
			if (!executor.IsGod() && !await executor.HasPower("ANNOUNCE"))
			{
				await NotifyService!.Notify(executor, "Permission denied. You need the Announce power.");
				return CallState.Empty;
			}
		}
		else
		{
			// Need wizard/royalty for other MOTDs
			if (!executor.IsGod() && !await executor.IsWizard())
			{
				await NotifyService!.Notify(executor, "Permission denied.");
				return CallState.Empty;
			}
		}
		
		// @motd/clear - clear the MOTD
		if (switches.Contains("CLEAR"))
		{
			var newMotdData = motdType switch
			{
				"wizard" => motdData with { WizardMotd = null },
				"down" => motdData with { DownMotd = null },
				"full" => motdData with { FullMotd = null },
				_ => motdData with { ConnectMotd = null }
			};
			
			await ObjectDataService!.SetExpandedServerDataAsync(newMotdData, ignoreNull: true);
			await NotifyService!.Notify(executor, $"{motdType.Humanize(LetterCasing.Title)} MOTD cleared.");
			return CallState.Empty;
		}
		
		// Set the MOTD
		if (string.IsNullOrEmpty(argText))
		{
			await NotifyService!.Notify(executor, "Usage: @motd[/<type>] <message>");
			return CallState.Empty;
		}
		
		var newMotdDataSet = motdType switch
		{
			"wizard" => motdData with { WizardMotd = argText },
			"down" => motdData with { DownMotd = argText },
			"full" => motdData with { FullMotd = argText },
			_ => motdData with { ConnectMotd = argText }
		};
		
		await ObjectDataService!.SetExpandedServerDataAsync(newMotdDataSet, ignoreNull: true);
		await NotifyService!.Notify(executor, $"{motdType.Humanize(LetterCasing.Title)} MOTD set.");
		return CallState.Empty;
	}

	[SharpCommand(Name = "@POWER",
		Switches = ["ADD", "TYPE", "LETTER", "LIST", "RESTRICT", "DELETE", "ALIAS", "DISABLE", "ENABLE", "DECOMPILE"],
		Behavior = CB.Default | CB.EqSplit | CB.RSArgs, MinArgs = 0, MaxArgs = 2)]
	public static async ValueTask<Option<CallState>> Power(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		// @POWER command - manage object powers
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var switches = parser.CurrentState.Switches;
		
		// @power/list - list all powers
		if (switches.Contains("LIST"))
		{
			var output = new System.Text.StringBuilder();
			output.AppendLine("Object Powers:");
			output.AppendLine("Name                 Alias              Type Restrictions");
			output.AppendLine("-------------------- ------------------ -------------------");
			
			var powers = Mediator!.CreateStream(new GetPowersQuery());
			await foreach (var power in powers)
			{
				var types = string.Join(",", power.TypeRestrictions);
				output.AppendLine($"{power.Name,-20} {power.Alias,-18} {types}");
			}
			
			await NotifyService!.Notify(executor, output.ToString().TrimEnd());
			return CallState.Empty;
		}
		
		// @power/add name=alias - add a new power
		if (switches.Contains("ADD"))
		{
			if (parser.CurrentState.Arguments.Count < 2)
			{
				await NotifyService!.Notify(executor, "@POWER/ADD requires power name and alias.");
				return CallState.Empty;
			}
			
			var powerName = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
			var alias = parser.CurrentState.Arguments["1"].Message!.ToPlainText();
			
			if (string.IsNullOrWhiteSpace(powerName) || string.IsNullOrWhiteSpace(alias))
			{
				await NotifyService!.Notify(executor, "Power name and alias cannot be empty.");
				return CallState.Empty;
			}
			
			// User-created powers are always non-system
			var result = await Mediator!.Send(new CreatePowerCommand(
				powerName.ToUpper(),
				alias.ToUpper(),
				false, // system - user-created powers are NEVER system powers
				["FLAG^WIZARD"], // default set permissions
				["FLAG^WIZARD"], // default unset permissions
				["PLAYER"] // default type restrictions (powers typically only on players)
			));
			
			if (result != null)
			{
				await NotifyService!.Notify(executor, $"Power '{powerName}' created with alias '{alias}'.");
				return new CallState(MModule.single(powerName));
			}
			else
			{
				await NotifyService!.Notify(executor, $"Failed to create power '{powerName}'.");
				return CallState.Empty;
			}
		}
		
		// @power/delete name - delete a power
		if (switches.Contains("DELETE"))
		{
			if (parser.CurrentState.Arguments.Count < 1)
			{
				await NotifyService!.Notify(executor, "@POWER/DELETE requires a power name.");
				return CallState.Empty;
			}
			
			var powerName = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
			
			if (string.IsNullOrWhiteSpace(powerName))
			{
				await NotifyService!.Notify(executor, "Power name cannot be empty.");
				return CallState.Empty;
			}
			
			// Check if power is a system power
			var power = await Mediator!.Send(new GetPowerQuery(powerName.ToUpper()));
			if (power == null)
			{
				await NotifyService!.Notify(executor, $"Power '{powerName}' not found.");
				return CallState.Empty;
			}
			
			if (power.System)
			{
				await NotifyService!.Notify(executor, $"Cannot delete system power '{powerName}'.");
				return CallState.Empty;
			}
			
			var result = await Mediator!.Send(new DeletePowerCommand(powerName.ToUpper()));
			
			if (result)
			{
				await NotifyService!.Notify(executor, $"Power '{powerName}' deleted.");
				return new CallState(MModule.single(powerName));
			}
			else
			{
				await NotifyService!.Notify(executor, $"Failed to delete power '{powerName}'.");
				return CallState.Empty;
			}
		}
		
		// @power/alias name=alias - change power alias
		if (switches.Contains("ALIAS"))
		{
			if (parser.CurrentState.Arguments.Count < 2)
			{
				await NotifyService!.Notify(executor, "@POWER/ALIAS requires power name and new alias.");
				return CallState.Empty;
			}
			
			var powerName = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
			var newAlias = parser.CurrentState.Arguments["1"].Message!.ToPlainText();
			
			if (string.IsNullOrWhiteSpace(powerName) || string.IsNullOrWhiteSpace(newAlias))
			{
				await NotifyService!.Notify(executor, "Power name and alias cannot be empty.");
				return CallState.Empty;
			}
			
			var power = await Mediator!.Send(new GetPowerQuery(powerName.ToUpper()));
			if (power == null)
			{
				await NotifyService!.Notify(executor, $"Power '{powerName}' not found.");
				return CallState.Empty;
			}
			
			if (power.System)
			{
				await NotifyService!.Notify(executor, $"Cannot modify system power '{powerName}'.");
				return CallState.Empty;
			}
			
			var result = await Mediator!.Send(new UpdatePowerCommand(
				powerName.ToUpper(),
				newAlias.ToUpper(),
				power.SetPermissions,
				power.UnsetPermissions,
				power.TypeRestrictions
			));
			
			if (result)
			{
				await NotifyService!.Notify(executor, $"Power '{powerName}' alias changed to '{newAlias}'.");
				return new CallState(MModule.single(powerName));
			}
			else
			{
				await NotifyService!.Notify(executor, $"Failed to update power '{powerName}'.");
				return CallState.Empty;
			}
		}
		
		// @power/type name=types - change type restrictions
		if (switches.Contains("TYPE"))
		{
			if (parser.CurrentState.Arguments.Count < 2)
			{
				await NotifyService!.Notify(executor, "@POWER/TYPE requires power name and type restrictions.");
				return CallState.Empty;
			}
			
			var powerName = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
			var typesArg = parser.CurrentState.Arguments["1"].Message!.ToPlainText();
			
			if (string.IsNullOrWhiteSpace(powerName) || string.IsNullOrWhiteSpace(typesArg))
			{
				await NotifyService!.Notify(executor, "Power name and types cannot be empty.");
				return CallState.Empty;
			}
			
			var power = await Mediator!.Send(new GetPowerQuery(powerName.ToUpper()));
			if (power == null)
			{
				await NotifyService!.Notify(executor, $"Power '{powerName}' not found.");
				return CallState.Empty;
			}
			
			if (power.System)
			{
				await NotifyService!.Notify(executor, $"Cannot modify system power '{powerName}'.");
				return CallState.Empty;
			}
			
			var types = typesArg.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries)
				.Select(t => t.ToUpper())
				.ToArray();
			
			var result = await Mediator!.Send(new UpdatePowerCommand(
				powerName.ToUpper(),
				power.Alias,
				power.SetPermissions,
				power.UnsetPermissions,
				types
			));
			
			if (result)
			{
				await NotifyService!.Notify(executor, $"Power '{powerName}' type restrictions updated to: {string.Join(", ", types)}.");
				return new CallState(MModule.single(powerName));
			}
			else
			{
				await NotifyService!.Notify(executor, $"Failed to update power '{powerName}'.");
				return CallState.Empty;
			}
		}
		
		// @power/restrict name=permissions - set permissions
		if (switches.Contains("RESTRICT"))
		{
			if (parser.CurrentState.Arguments.Count < 2)
			{
				await NotifyService!.Notify(executor, "@POWER/RESTRICT requires power name and permissions.");
				return CallState.Empty;
			}
			
			var powerName = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
			var permsArg = parser.CurrentState.Arguments["1"].Message!.ToPlainText();
			
			if (string.IsNullOrWhiteSpace(powerName) || string.IsNullOrWhiteSpace(permsArg))
			{
				await NotifyService!.Notify(executor, "Power name and permissions cannot be empty.");
				return CallState.Empty;
			}
			
			var power = await Mediator!.Send(new GetPowerQuery(powerName.ToUpper()));
			if (power == null)
			{
				await NotifyService!.Notify(executor, $"Power '{powerName}' not found.");
				return CallState.Empty;
			}
			
			if (power.System)
			{
				await NotifyService!.Notify(executor, $"Cannot modify system power '{powerName}'.");
				return CallState.Empty;
			}
			
			var perms = permsArg.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries);
			
			var result = await Mediator!.Send(new UpdatePowerCommand(
				powerName.ToUpper(),
				power.Alias,
				perms,
				perms,
				power.TypeRestrictions
			));
			
			if (result)
			{
				await NotifyService!.Notify(executor, $"Power '{powerName}' permissions updated to: {string.Join(", ", perms)}.");
				return new CallState(MModule.single(powerName));
			}
			else
			{
				await NotifyService!.Notify(executor, $"Failed to update power '{powerName}'.");
				return CallState.Empty;
			}
		}
		
		// @power/decompile name - show power definition
		if (switches.Contains("DECOMPILE"))
		{
			if (parser.CurrentState.Arguments.Count < 1)
			{
				await NotifyService!.Notify(executor, "@POWER/DECOMPILE requires a power name.");
				return CallState.Empty;
			}
			
			var powerName = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
			
			var power = await Mediator!.Send(new GetPowerQuery(powerName.ToUpper()));
			if (power == null)
			{
				await NotifyService!.Notify(executor, $"Power '{powerName}' not found.");
				return CallState.Empty;
			}
			
			var output = new System.Text.StringBuilder();
			output.AppendLine($"Power: {power.Name}");
			output.AppendLine($"Alias: {power.Alias}");
			output.AppendLine($"System: {(power.System ? "Yes" : "No")}");
			output.AppendLine($"Disabled: {(power.Disabled ? "Yes" : "No")}");
			output.AppendLine($"Type Restrictions: {string.Join(", ", power.TypeRestrictions)}");
			output.AppendLine($"Set Permissions: {string.Join(", ", power.SetPermissions)}");
			output.AppendLine($"Unset Permissions: {string.Join(", ", power.UnsetPermissions)}");
			
			await NotifyService!.Notify(executor, output.ToString().TrimEnd());
			return CallState.Empty;
		}
		
		// @power/disable and @power/enable - toggle power disabled state
		if (switches.Contains("DISABLE") || switches.Contains("ENABLE"))
		{
			if (parser.CurrentState.Arguments.Count < 1)
			{
				await NotifyService!.Notify(executor, $"@POWER/{(switches.Contains("DISABLE") ? "DISABLE" : "ENABLE")} requires a power name.");
				return CallState.Empty;
			}
			
			var powerName = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
			
			if (string.IsNullOrWhiteSpace(powerName))
			{
				await NotifyService!.Notify(executor, "Power name cannot be empty.");
				return CallState.Empty;
			}
			
			var power = await Mediator!.Send(new GetPowerQuery(powerName.ToUpper()));
			if (power == null)
			{
				await NotifyService!.Notify(executor, $"Power '{powerName}' not found.");
				return CallState.Empty;
			}
			
			if (power.System)
			{
				await NotifyService!.Notify(executor, $"Cannot disable system power '{powerName}'.");
				return CallState.Empty;
			}
			
			bool disable = switches.Contains("DISABLE");
			var result = await Mediator!.Send(new SetPowerDisabledCommand(powerName.ToUpper(), disable));
			
			if (result)
			{
				await NotifyService!.Notify(executor, $"Power '{powerName}' {(disable ? "disabled" : "enabled")}.");
				return new CallState(MModule.single(powerName));
			}
			else
			{
				await NotifyService!.Notify(executor, $"Failed to {(disable ? "disable" : "enable")} power '{powerName}'.");
				return CallState.Empty;
			}
		}
		
		// Default - show usage
		await NotifyService!.Notify(executor, "Usage: @power/list, @power/add <name>=<alias>, @power/delete <name>, @power/alias <name>=<alias>, @power/type <name>=<types>, @power/restrict <name>=<permissions>, @power/decompile <name>");
		return CallState.Empty;
	}

	[SharpCommand(Name = "@REJECTMOTD", Switches = ["CLEAR"], Behavior = CB.Default, CommandLock = "FLAG^WIZARD",
		MinArgs = 0)]
	public static async ValueTask<Option<CallState>> RejectMessageOfTheDay(IMUSHCodeParser parser,
		SharpCommandAttribute _2)
	{
		// Alias for @motd/full
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var switches = parser.CurrentState.Switches;
		var args = parser.CurrentState.ArgumentsOrdered;
		var argText = ArgHelpers.NoParseDefaultNoParseArgument(args, 1, MModule.empty()).ToString();
		
		var motdData = await ObjectDataService!.GetExpandedServerDataAsync<MotdData>() ?? new MotdData();
		
		if (switches.Contains("CLEAR"))
		{
			var newMotdData = motdData with { FullMotd = null };
			await ObjectDataService!.SetExpandedServerDataAsync(newMotdData, ignoreNull: true);
			await NotifyService!.Notify(executor, "Full MOTD cleared.");
		}
		else if (string.IsNullOrEmpty(argText))
		{
			await NotifyService!.Notify(executor, "Usage: @rejectmotd <message>");
		}
		else
		{
			var newMotdData = motdData with { FullMotd = argText };
			await ObjectDataService!.SetExpandedServerDataAsync(newMotdData, ignoreNull: true);
			await NotifyService!.Notify(executor, "Full MOTD set.");
		}
		
		return CallState.Empty;
	}

	[SharpCommand(Name = "@SUGGEST", Switches = ["ADD", "DELETE", "LIST"], Behavior = CB.Default | CB.EqSplit,
		MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> Suggest(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@BOOT", Switches = ["PORT", "ME", "SILENT"], Behavior = CB.Default, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> Boot(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var switches = parser.CurrentState.Switches.ToHashSet();
		var args = parser.CurrentState.Arguments;
		var silent = switches.Contains("SILENT");

		List<long> targetHandles = [];

		if (switches.Contains("ME"))
		{
			if (parser.CurrentState.Handle is { } h)
				targetHandles.Add(h);
		}
		else if (switches.Contains("PORT"))
		{
			if (args.Count == 0)
			{
				await NotifyService!.Notify(executor, "Usage: @boot/port <descriptor number>");
				return new CallState("#-1 INVALID ARGUMENTS");
			}
			var portText = args["0"].Message!.ToPlainText();
			if (!long.TryParse(portText, out var handle))
			{
				await NotifyService!.Notify(executor, "Descriptor number must be a number.");
				return new CallState("#-1 INVALID ARGUMENTS");
			}
			if (ConnectionService!.Get(handle) is not null)
			{
				targetHandles.Add(handle);
			}
			else
			{
				await NotifyService!.Notify(executor, $"No such descriptor: {handle}.");
				return CallState.Empty;
			}
		}
		else
		{
			if (args.Count == 0)
			{
				await NotifyService!.Notify(executor, "Usage: @boot <player> | @boot/me | @boot/port <descriptor>");
				return new CallState("#-1 INVALID ARGUMENTS");
			}
			var playerArg = args["0"].Message!.ToPlainText();
			var maybePlayer = await LocateService!.LocatePlayerAndNotifyIfInvalidWithCallState(parser, executor, executor, playerArg);
			if (maybePlayer.IsError)
			{
				return maybePlayer.AsError;
			}
			var playerObj = maybePlayer.AsSharpObject.AsPlayer;
			var targetDbRef = playerObj.Object.DBRef;
			// TODO: This should only boot the last active connection, to match Penn behavior.
			await foreach (var cd in ConnectionService!.Get(targetDbRef))
			{
				targetHandles.Add(cd.Handle);
			}
			if (targetHandles.Count == 0)
			{
				await NotifyService!.Notify(executor, "That player is not connected.");
				return CallState.Empty;
			}
		}

		foreach (var handle in targetHandles)
		{
			if (!silent)
			{
				await NotifyService!.Notify(handle, "You have been disconnected.", type: INotifyService.NotificationType.Announce);
			}
			await ConnectionService!.Disconnect(handle);
		}

		return CallState.Empty;
	}

	[SharpCommand(Name = "@DISABLE", Switches = [], Behavior = CB.Default, CommandLock = "FLAG^WIZARD",
		MinArgs = 1, MaxArgs = 1)]
	public static async ValueTask<Option<CallState>> Disable(IMUSHCodeParser parser, SharpCommandAttribute _2) 
		=> await ConfigSetHelper(parser, isEnable: false);

	[SharpCommand(Name = "@HOOK",
		Switches =
		[
			"LIST", "AFTER", "BEFORE", "EXTEND", "IGSWITCH", "IGNORE", "OVERRIDE", "INPLACE", "INLINE", "LOCALIZE",
			"CLEARREGS", "NOBREAK"
		], Behavior = CB.Default | CB.EqSplit | CB.RSArgs, CommandLock = "FLAG^WIZARD|POWER^HOOK", MinArgs = 0)]
	public static async ValueTask<Option<CallState>> Hook(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var args = parser.CurrentState.Arguments;
		var switches = parser.CurrentState.Switches.ToArray();
		
		if (!await executor.IsWizard())
		{
			return await NotifyService!.NotifyAndReturn(
				executor.Object().DBRef,
				errorReturn: ErrorMessages.Returns.PermissionDenied,
				notifyMessage: ErrorMessages.Notifications.PermissionDenied,
				shouldNotify: true);
		}
		
		if (args.Count == 0)
		{
			await NotifyService!.Notify(executor, "You must specify a command name.");
			return new CallState("#-1 NO COMMAND SPECIFIED");
		}
		
		var commandName = args["0"].Message?.ToPlainText()?.ToUpper();
		if (string.IsNullOrEmpty(commandName))
		{
			await NotifyService!.Notify(executor, "You must specify a command name.");
			return new CallState("#-1 NO COMMAND SPECIFIED");
		}
		
		if (switches.Contains("LIST"))
		{
			var hooks = await HookService!.GetAllHooksAsync(commandName);
			if (hooks.Count == 0)
			{
				await NotifyService!.Notify(executor, $"No hooks set for command '{commandName}'.");
				return CallState.Empty;
			}
			
			await NotifyService!.Notify(executor, $"Hooks for command '{commandName}':");
			foreach (var (hookType, hook) in hooks)
			{
				var flags = new List<string>();
				if (hook.Inline) flags.Add("inline");
				if (hook.NoBreak) flags.Add("nobreak");
				if (hook.Localize) flags.Add("localize");
				if (hook.ClearRegs) flags.Add("clearregs");
				
				var flagStr = flags.Count > 0 ? $" ({string.Join(", ", flags)})" : "";
				await NotifyService.Notify(executor, $"  {hookType}: {hook.TargetObject}/{hook.AttributeName}{flagStr}");
			}
			return CallState.Empty;
		}
		
		var hookTypes = new[] { "IGNORE", "OVERRIDE", "BEFORE", "AFTER", "EXTEND", "IGSWITCH" };
		var selectedHookType = hookTypes.FirstOrDefault(switches.Contains);
		
		if (selectedHookType == "IGSWITCH")
		{
			selectedHookType = "EXTEND";
		}
		
		if (selectedHookType == null)
		{
			await NotifyService!.Notify(executor, "You must specify a hook type: /ignore, /override, /before, /after, or /extend");
			return new CallState("#-1 NO HOOK TYPE");
		}
		
		if (args.Count < 2 || string.IsNullOrWhiteSpace(args["1"].Message?.ToPlainText()))
		{
			var cleared = await HookService!.ClearHookAsync(commandName, selectedHookType);
			if (cleared)
			{
				await NotifyService!.Notify(executor, $"Hook '{selectedHookType}' cleared for command '{commandName}'.");
				return CallState.Empty;
			}
		
			await NotifyService!.Notify(executor, $"No '{selectedHookType}' hook set for command '{commandName}'.");
			return new CallState("#-1 NO HOOK");
		}
		
		var objectAndAttribute = args["1"].Message!.ToPlainText();
		var parts = objectAndAttribute.Split(',', 2);
		
		if (parts.Length < 1 || string.IsNullOrWhiteSpace(parts[0]))
		{
			await NotifyService!.Notify(executor, "You must specify an object.");
			return new CallState("#-1 NO OBJECT");
		}
		
		var objectRef = parts[0].Trim();
		var maybeObject = await LocateService!.LocateAndNotifyIfInvalid(parser, executor, executor, 
			objectRef, LocateFlags.All);
		
		if (!maybeObject.IsValid())
		{
			return CallState.Empty;
		}
		
		var targetObject = maybeObject.WithoutError().Known();
		var dbref = targetObject.Object().DBRef;
		
		var attributeName = parts.Length > 1 && !string.IsNullOrWhiteSpace(parts[1])
			? parts[1].Trim()
			: $"cmd.{selectedHookType.ToLower()}";
		
		var attrResult = await AttributeService!.GetAttributeAsync(executor, targetObject, 
			attributeName, IAttributeService.AttributeMode.Read);
		
		if (attrResult.IsError)
		{
			await NotifyService!.Notify(executor, $"Attribute '{attributeName}' not found on object {dbref}.");
			return new CallState("#-1 NO ATTRIBUTE");
		}
		
		var inline = switches.Contains("INLINE");
		var inplace = switches.Contains("INPLACE");
		var nobreak = switches.Contains("NOBREAK") || inplace;
		var localize = switches.Contains("LOCALIZE") || inplace;
		var clearregs = switches.Contains("CLEARREGS") || inplace;
		
		await HookService!.SetHookAsync(commandName, selectedHookType, dbref, attributeName, 
			inline || inplace, nobreak, localize, clearregs);
		
		var flagDesc = inline || inplace ? " (inline)" : "";
		await NotifyService!.Notify(executor, $"Hook '{selectedHookType}' set for command '{commandName}'{flagDesc}.");
		
		return CallState.Empty;
	}

	[SharpCommand(Name = "@NEWPASSWORD", Switches = ["GENERATE"], Behavior = CB.Default | CB.EqSplit | CB.RSNoParse,
		CommandLock = "FLAG^WIZARD", MinArgs = 0)]
	public static async ValueTask<Option<CallState>> NewPassword(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var args = parser.CurrentState.Arguments;
		var arg0 = args["0"].Message!.ToPlainText();
		var isGenerate = parser.CurrentState.Switches.Contains("GENERATE");

		if (isGenerate && parser.CurrentState.Arguments.Count > 1)
		{
			await NotifyService!.Notify(
				executor.Object().DBRef,
				"@NEWPASSWORD: /GENERATE switch cannot be used with other arguments.");
		}

		var maybePlayer =
			await LocateService!.LocatePlayerAndNotifyIfInvalidWithCallState(parser, executor, executor, arg0);

		if (maybePlayer.IsError)
		{
			return maybePlayer.AsError;
		}

		var asPlayer = maybePlayer.AsSharpObject.AsPlayer;

		if (isGenerate)
		{
			var generatedPassword = PasswordService!.GenerateRandomPassword();

			await Mediator!.Send(
				new SetPlayerPasswordCommand(asPlayer,
					PasswordService.HashPassword(asPlayer.Object.DBRef.ToString(), generatedPassword)));

			await NotifyService!.Notify(
				executor.Object().DBRef,
				$"Generated password for {asPlayer.Object.Name}: {generatedPassword}");

			return new CallState(generatedPassword);
		}

		var arg1 = args["1"].Message!.ToPlainText();
		var newHashedPassword = PasswordService!.HashPassword(asPlayer.Object.DBRef.ToString(), arg1);

		await Mediator!.Send(new SetPlayerPasswordCommand(asPlayer, newHashedPassword));

		await NotifyService!.Notify(
			executor.Object().DBRef,
			$"Set new password for {asPlayer.Object.Name}: {arg1}");

		return new CallState(arg1);
	}

	[SharpCommand(Name = "@PURGE", Switches = [], Behavior = CB.Default, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> Purge(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@SHUTDOWN", Switches = ["PANIC", "REBOOT", "PARANOID"], Behavior = CB.Default,
		CommandLock = "FLAG^WIZARD", MinArgs = 0)]
	public static async ValueTask<Option<CallState>> Shutdown(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@UPTIME", Switches = ["MORTAL"], Behavior = CB.Default, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> Uptime(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var data = (await ObjectDataService!.GetExpandedServerDataAsync<UptimeData>())!;
		var upSince = data.StartTime;
		var lastReboot = data.LastRebootTime.Humanize();
		var reboots = data.Reboots.ToString();
		var now = DateTimeOffset.UtcNow;
		var nextPurge = (data.NextPurgeTime - DateTimeOffset.Now).Humanize();
		var nextWarning = (data.NextWarningTime - DateTimeOffset.Now).Humanize();
		var uptime = (DateTimeOffset.Now - data.StartTime).Humanize();

		var details = $"""
		                          Up since: {upSince}
		                       Last Reboot: {lastReboot}
		                     Total Reboots: {reboots}
		                          Time now: {now}
		                        Next Purge: {nextPurge}
		                     Next Warnings: {nextWarning}
		                  SharpMUSH Uptime: {uptime}
		               """;

		await NotifyService!.Notify(executor, details);

		if ((!await executor.IsWizard() && !executor.IsGod()) || parser.CurrentState.Switches.Contains("MORTAL"))
		{
			return new CallState(details);
		}

		var process = Process.GetCurrentProcess();
		var pid = process.Id;
		var memoryUsage = process.WorkingSet64.Bytes().Humanize("0.00");
		var peakMemoryUsage = process.PeakWorkingSet64.Bytes().Humanize("0.00");
		var paged = process.PagedMemorySize64.Bytes().Humanize("0.00");
		var peakPaged = process.PeakPagedMemorySize64.Bytes().Humanize("0.00");

		var extra = $"""

		                    Process ID: {pid}
		                  Memory Usage: {memoryUsage}
		             Peak Memory Usage: {peakMemoryUsage}
		                  Paged Memory: {paged}
		             Peak Paged Memory: {peakPaged}
		             """;

		await NotifyService.Notify(executor, extra);

		return new CallState(details);
	}

	[SharpCommand(Name = "@CHOWNALL", Switches = ["PRESERVE", "THINGS", "ROOMS", "EXITS"],
		Behavior = CB.Default | CB.EqSplit, CommandLock = "FLAG^WIZARD", MinArgs = 0)]
	public static async ValueTask<Option<CallState>> Chown(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@DUMP", Switches = ["PARANOID", "DEBUG", "NOFORK"], Behavior = CB.Default,
		CommandLock = "FLAG^WIZARD", MinArgs = 0)]
	public static async ValueTask<Option<CallState>> Dump(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		await NotifyService!.Notify(executor, "Dump command does nothing for SharpMUSH. Consider using @backup.");
		return new None();
	}

	/// <remarks>
	/// Creating on the DBRef is not implemented.
	/// </remarks>
	[SharpCommand(Name = "@PCREATE", Behavior = CB.Default, MinArgs = 2, MaxArgs = 3)]
	public static async ValueTask<Option<CallState>> PlayerCreate(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		// TODO: Validate Name and Passwords
		var defaultHome = Configuration!.CurrentValue.Database.DefaultHome;
		var defaultHomeDbref = new DBRef((int)defaultHome);
		var startingQuota = (int)Configuration!.CurrentValue.Limit.StartingQuota;
		var args = parser.CurrentState.Arguments;
		var name = MModule.plainText(args["0"].Message!);
		var password = MModule.plainText(args["1"].Message!);
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);

		var player = await Mediator!.Send(new CreatePlayerCommand(name, password, defaultHomeDbref, defaultHomeDbref, startingQuota));

		// Trigger PLAYER`CREATE event
		// PennMUSH spec: player`create (objid, name, how, descriptor, email)
		await EventService!.TriggerEventAsync(
			parser,
			"PLAYER`CREATE",
			executor.Object().DBRef, // Enactor is the wizard who did @pcreate
			player.ToString(),
			name,
			"pcreate",
			"", // descriptor (not applicable for @pcreate)
			""); // email (not applicable for @pcreate)

		return new CallState(player.ToString());
	}

	[SharpCommand(Name = "@QUOTA", Switches = ["ALL", "SET"], Behavior = CB.Default | CB.EqSplit, MinArgs = 0,
		MaxArgs = 2)]
	public static async ValueTask<Option<CallState>> Quota(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		// @quota [<player>] - Display quota for player
		// @quota/set <player>=<amount> - Set quota for player (wizard only)
		// @quota/all - Display quota summary for all players (wizard only)
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var switches = parser.CurrentState.Switches;
		var args = parser.CurrentState.Arguments;
		
		// Check if quota system is enabled
		if (!Configuration!.CurrentValue.Limit.UseQuota)
		{
			await NotifyService!.Notify(executor, "The quota system is disabled on this server.");
			return CallState.Empty;
		}
		
		// @quota/set <player>=<amount> - set quota (wizard only)
		if (switches.Contains("SET"))
		{
			if (!await executor.IsWizard())
			{
				return await NotifyService!.NotifyAndReturn(
					executor.Object().DBRef,
					errorReturn: ErrorMessages.Returns.PermissionDenied,
					notifyMessage: ErrorMessages.Notifications.PermissionDenied,
					shouldNotify: true);
			}
			
			if (args.Count < 2)
			{
				await NotifyService!.Notify(executor, "Usage: @quota/set <player>=<amount>");
				return new CallState("#-1 INVALID ARGUMENTS");
			}
			
			var playerArg = args["0"].Message!.ToPlainText();
			var amountArg = args["1"].Message!.ToPlainText();
			
			if (!int.TryParse(amountArg, out var amount))
			{
				await NotifyService!.Notify(executor, "Quota amount must be a number.");
				return new CallState("#-1 INVALID ARGUMENTS");
			}
			
			var maybePlayer = await LocateService!.LocatePlayerAndNotifyIfInvalidWithCallState(parser, executor, executor, playerArg);
			if (maybePlayer.IsError)
			{
				return maybePlayer.AsError;
			}
			
			var player = maybePlayer.AsSharpObject.AsPlayer;
			
			// Update the player's quota
			await Mediator!.Send(new SetPlayerQuotaCommand(player, amount));
			
			await NotifyService!.Notify(executor, $"Quota for {player.Object.Name} set to {amount}.");
			await NotifyService.Notify(player.Object.DBRef, $"Your quota has been set to {amount} by {executor.Object().Name}.");
			
			return CallState.Empty;
		}
		
		// @quota/all - show all player quotas (wizard only)
		if (switches.Contains("ALL"))
		{
			if (!await executor.IsWizard())
			{
				return await NotifyService!.NotifyAndReturn(
					executor.Object().DBRef,
					errorReturn: ErrorMessages.Returns.PermissionDenied,
					notifyMessage: ErrorMessages.Notifications.PermissionDenied,
					shouldNotify: true);
			}
			
			await NotifyService!.Notify(executor, "Quota listing for all players:");
			await NotifyService.Notify(executor, "Player                      Used/Quota");
			await NotifyService.Notify(executor, "=========================================");
			
			// Iterate through all players and show their quota
			var players = Mediator!.CreateStream(new GetAllPlayersQuery());
			await foreach (var player in players)
			{
				var objectCount = await Mediator.Send(new GetOwnedObjectCountQuery(player));
				var playerName = player.Object.Name.PadRight(27);
				await NotifyService.Notify(executor, $"{playerName} {objectCount,4}/{player.Quota,-4}");
			}
			
			return CallState.Empty;
		}
		
		// @quota [<player>] - display quota
		AnySharpObject targetPlayer = executor;
		if (args.Count > 0)
		{
			var playerArg = args["0"].Message!.ToPlainText();
			var maybePlayer = await LocateService!.LocatePlayerAndNotifyIfInvalidWithCallState(parser, executor, executor, playerArg);
			if (maybePlayer.IsError)
			{
				return maybePlayer.AsError;
			}
			targetPlayer = maybePlayer.AsSharpObject;
		}
		
		var targetPlayerObj = targetPlayer.AsPlayer;
		var quota = targetPlayerObj.Quota;
		
		// Count objects owned by the player
		var objectsOwned = await Mediator!.Send(new GetOwnedObjectCountQuery(targetPlayerObj));
		
		await NotifyService!.Notify(executor, 
			$"{targetPlayerObj.Object.Name}'s quota: {objectsOwned}/{quota} objects used.");
		
		return CallState.Empty;
	}

	/// <summary>
	/// Manages sitelock rules that control which hosts can connect, create players, or use guests.
	/// @sitelock - Lists all rules and banned names
	/// @sitelock/check &lt;host&gt; - Checks which rule matches a host
	/// @sitelock/name &lt;name&gt; - Manages banned player names (not yet implemented)
	/// @sitelock/ban &lt;pattern&gt; - Bans a host pattern (not yet implemented)
	/// @sitelock/register &lt;pattern&gt; - Sets registration requirement (not yet implemented)
	/// @sitelock/remove &lt;pattern&gt; - Removes a rule (not yet implemented)
	/// </summary>
	[SharpCommand(Name = "@SITELOCK", Switches = ["BAN", "CHECK", "REGISTER", "REMOVE", "NAME", "PLAYER", "LIST"],
		Behavior = CB.Default | CB.EqSplit | CB.RSArgs, CommandLock = "FLAG^WIZARD", MinArgs = 0)]
	public static async ValueTask<Option<CallState>> SiteLock(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var args = parser.CurrentState.Arguments;
		var switches = parser.CurrentState.Switches.ToArray();

		// Get current sitelock configuration
		var sitelockRules = Configuration!.CurrentValue.SitelockRules;
		var bannedNames = Configuration!.CurrentValue.BannedNames;

		// @sitelock with no arguments or @sitelock/list - list all rules
		if (args.Count == 0 || switches.Contains("LIST"))
		{
			var output = new System.Text.StringBuilder();
			output.AppendLine($"Sitelock Rules ({sitelockRules.Rules.Count} total):");
			output.AppendLine("Pattern                      Options");
			output.AppendLine("---------------------------- ------------------------------");

			if (sitelockRules.Rules.Count == 0)
			{
				output.AppendLine("  (No rules defined - all connections allowed by default)");
			}
			else
			{
				foreach (var rule in sitelockRules.Rules)
				{
					var pattern = rule.Key;
					var options = string.Join(", ", rule.Value);
					output.AppendLine($"{pattern,-28} {options}");
				}
			}

			output.AppendLine();
			output.AppendLine($"Banned Player Names ({bannedNames.BannedNames.Length} total):");
			if (bannedNames.BannedNames.Length == 0)
			{
				output.AppendLine("  (No banned names defined)");
			}
			else
			{
				output.AppendLine("  " + string.Join(", ", bannedNames.BannedNames));
			}

			await NotifyService!.Notify(executor, output.ToString().TrimEnd());
			return CallState.Empty;
		}

		// @sitelock/check <host> - check which rule matches a host
		if (switches.Contains("CHECK"))
		{
			if (args.Count == 0)
			{
				await NotifyService!.Notify(executor, "@SITELOCK/CHECK requires a hostname or IP address.");
				return new CallState("#-1 INVALID ARGUMENTS");
			}

			var hostToCheck = args["0"].Message!.ToPlainText();
			
			// Find matching rule (simple wildcard matching for now)
			KeyValuePair<string, string[]>? matchingRule = sitelockRules.Rules
				.FirstOrDefault(rule => WildcardMatch(hostToCheck, rule.Key));

			if (matchingRule.HasValue)
			{
				var options = string.Join(", ", matchingRule.Value.Value);
				await NotifyService!.Notify(executor, $"Host '{hostToCheck}' matches pattern '{matchingRule.Value.Key}' with options: {options}");
			}
			else
			{
				await NotifyService!.Notify(executor, $"Host '{hostToCheck}' does not match any sitelock rules (default access allowed).");
			}

			return CallState.Empty;
		}

		// @sitelock/name <name> - add/remove banned name
		if (switches.Contains("NAME"))
		{
			if (args.Count == 0)
			{
				await NotifyService!.Notify(executor, "@SITELOCK/NAME requires a player name.");
				return new CallState("#-1 INVALID ARGUMENTS");
			}

			// Note: Actual modification of configuration is not yet implemented
			// This would require saving to the database
			await NotifyService!.Notify(executor, "@SITELOCK/NAME modification is not yet implemented. Use the admin UI to modify banned names.");
			return new CallState("#-1 NOT IMPLEMENTED");
		}

		// @sitelock/ban <pattern> - shorthand for !connect !create !guest
		if (switches.Contains("BAN"))
		{
			if (args.Count == 0)
			{
				await NotifyService!.Notify(executor, "@SITELOCK/BAN requires a host pattern.");
				return new CallState("#-1 INVALID ARGUMENTS");
			}

			// Note: Actual modification of configuration is not yet implemented
			await NotifyService!.Notify(executor, "@SITELOCK/BAN modification is not yet implemented. Use the admin UI to add sitelock rules.");
			return new CallState("#-1 NOT IMPLEMENTED");
		}

		// @sitelock/register <pattern> - shorthand for !create register
		if (switches.Contains("REGISTER"))
		{
			if (args.Count == 0)
			{
				await NotifyService!.Notify(executor, "@SITELOCK/REGISTER requires a host pattern.");
				return new CallState("#-1 INVALID ARGUMENTS");
			}

			// Note: Actual modification of configuration is not yet implemented
			await NotifyService!.Notify(executor, "@SITELOCK/REGISTER modification is not yet implemented. Use the admin UI to add sitelock rules.");
			return new CallState("#-1 NOT IMPLEMENTED");
		}

		// @sitelock/remove <pattern> - remove a sitelock rule
		if (switches.Contains("REMOVE"))
		{
			if (args.Count == 0)
			{
				await NotifyService!.Notify(executor, "@SITELOCK/REMOVE requires a host pattern.");
				return new CallState("#-1 INVALID ARGUMENTS");
			}

			// Note: Actual modification of configuration is not yet implemented
			await NotifyService!.Notify(executor, "@SITELOCK/REMOVE modification is not yet implemented. Use the admin UI to remove sitelock rules.");
			return new CallState("#-1 NOT IMPLEMENTED");
		}

		// @sitelock <pattern>=<options> - add/modify a rule
		if (args.Count == 2)
		{
			// Note: Actual modification of configuration is not yet implemented
			await NotifyService!.Notify(executor, "@SITELOCK rule modification is not yet implemented. Use the admin UI to modify sitelock rules.");
			return new CallState("#-1 NOT IMPLEMENTED");
		}

		await NotifyService!.Notify(executor, "Invalid @SITELOCK syntax. Use '@help @sitelock' for usage information.");
		return new CallState("#-1 INVALID ARGUMENTS");
	}

	/// <summary>
	/// Simple wildcard matching for sitelock patterns (* and ? wildcards)
	/// </summary>
	private static bool WildcardMatch(string text, string pattern)
	{
		// Convert wildcard pattern to regex
		var regexPattern = "^" + System.Text.RegularExpressions.Regex.Escape(pattern)
			.Replace("\\*", ".*")
			.Replace("\\?", ".") + "$";
		
		return System.Text.RegularExpressions.Regex.IsMatch(text, regexPattern, 
			System.Text.RegularExpressions.RegexOptions.IgnoreCase);
	}

	[SharpCommand(Name = "@WALL", Switches = ["NOEVAL", "EMIT"], Behavior = CB.Default,
		CommandLock = "FLAG^WIZARD ROYALTY|POWER^ANNOUNCE", MinArgs = 0)]
	public static async ValueTask<Option<CallState>> Wall(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		// Future enhancement: Could pipe message through SPEAK() function for text processing
		var shout = parser.CurrentState.Arguments["0"].Message!;
		var handles = ConnectionService!.GetAll().Select(x => x.Handle);

		if (!parser.CurrentState.Switches.Contains("EMIT"))
		{
			shout = MModule.concat(MModule.single(Configuration!.CurrentValue.Cosmetic.WallPrefix + " "), shout);
		}

		await foreach (var handle in handles)
		{
			await NotifyService!.Notify(handle, shout);
		}

		return new CallState(shout);
	}

	[SharpCommand(Name = "@CHZONEALL", Switches = ["PRESERVE"], Behavior = CB.Default | CB.EqSplit, CommandLock = "FLAG^WIZARD",
		MinArgs = 2, MaxArgs = 2)]
	public static async ValueTask<Option<CallState>> ChangeZoneAll(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var args = parser.CurrentState.Arguments;
		var playerName = args["0"].Message!.ToPlainText();
		var zoneName = args["1"].Message!.ToPlainText();
		var preserve = parser.CurrentState.Switches.Contains("PRESERVE");

		// Locate the player whose objects we're changing zones for
		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor, executor, playerName, LocateFlags.All,
			async player =>
			{
				if (!player.IsPlayer)
				{
					return await NotifyService!.NotifyAndReturn(
						executor.Object().DBRef,
						errorReturn: ErrorMessages.Returns.InvalidPlayer,
						notifyMessage: ErrorMessages.Notifications.MustBePlayer,
						shouldNotify: true);
				}

				// Handle clearing zones with "none"
				if (zoneName.Equals("none", StringComparison.InvariantCultureIgnoreCase))
				{
					// Get all objects owned by this player
					var allObjects = Mediator!.CreateStream(new GetAllObjectsQuery())!;
					var count = 0;

					await foreach (var obj in allObjects)
					{
						var objOwner = await obj.Owner.WithCancellation(CancellationToken.None);
						var ownerAsAny = new AnySharpObject(objOwner);
						if (ownerAsAny.Object().DBRef.Number == player.Object().DBRef.Number)
						{
							// Get the full object node to use with commands
							var fullObj = await Mediator!.Send(new GetObjectNodeQuery(obj.DBRef));
							if (!fullObj.IsNone)
							{
								// Clear the zone
								await Mediator!.Send(new UnsetObjectZoneCommand(fullObj.Known));
								count++;
							}
						}
					}

					await NotifyService!.Notify(executor, $"Zones cleared for {count} object(s) owned by {player.Object().Name}.");
					return CallState.Empty;
				}

				// Locate the zone object
				return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
					executor, executor, zoneName, LocateFlags.All,
					async zoneObj =>
					{
						// Get all objects owned by this player
						var allObjects = Mediator!.CreateStream(new GetAllObjectsQuery())!;
						var count = 0;

						await foreach (var obj in allObjects)
						{
							var objOwner = await obj.Owner.WithCancellation(CancellationToken.None);
							var ownerAsAny = new AnySharpObject(objOwner);
							if (ownerAsAny.Object().DBRef.Number == player.Object().DBRef.Number)
							{
								// Get the full object node to use with commands
								var fullObj = await Mediator!.Send(new GetObjectNodeQuery(obj.DBRef));
								if (!fullObj.IsNone)
								{
									var anyObj = fullObj.Known;
									
									// Set the zone
									await Mediator!.Send(new SetObjectZoneCommand(anyObj, zoneObj));

									// Strip flags and powers unless /preserve is used
									if (!preserve && !anyObj.IsPlayer)
									{
										if (await anyObj.HasFlag("WIZARD"))
										{
											await ManipulateSharpObjectService!.SetOrUnsetFlag(executor, anyObj, "!WIZARD", false);
										}
										if (await anyObj.HasFlag("ROYALTY"))
										{
											await ManipulateSharpObjectService!.SetOrUnsetFlag(executor, anyObj, "!ROYALTY", false);
										}
										if (await anyObj.HasFlag("TRUST"))
										{
											await ManipulateSharpObjectService!.SetOrUnsetFlag(executor, anyObj, "!TRUST", false);
										}
										// TODO: Strip powers
									}

									count++;
								}
							}
						}

						await NotifyService!.Notify(executor, $"Zone set to {zoneObj.Object().Name} for {count} object(s) owned by {player.Object().Name}.");
						return CallState.Empty;
					}
				);
			}
		);
	}

	[SharpCommand(Name = "@ENABLE", Switches = [], Behavior = CB.Default | CB.NoGagged, CommandLock = "FLAG^WIZARD",
		MinArgs = 1, MaxArgs = 1)]
	public static async ValueTask<Option<CallState>> Enable(IMUSHCodeParser parser, SharpCommandAttribute _2) 
		=> await ConfigSetHelper(parser, isEnable: true);

	[SharpCommand(Name = "@KICK", Switches = [], Behavior = CB.Default, CommandLock = "FLAG^WIZARD", MinArgs = 0)]
	public static async ValueTask<Option<CallState>> Kick(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var args = parser.CurrentState.Arguments;

		if (args.Count == 0)
		{
			await NotifyService!.Notify(executor, "Usage: @kick <player>");
			return new CallState("#-1 INVALID ARGUMENTS");
		}

		var playerArg = args["0"].Message!.ToPlainText();
		var maybePlayer = await LocateService!.LocatePlayerAndNotifyIfInvalidWithCallState(parser, executor, executor, playerArg);
		if (maybePlayer.IsError)
		{
			return maybePlayer.AsError;
		}

		var playerObj = maybePlayer.AsSharpObject.AsPlayer;
		var targetDbRef = playerObj.Object.DBRef;

		var any = false;
		await foreach (var cd in ConnectionService!.Get(targetDbRef))
		{
			any = true;
			await NotifyService!.Notify(cd.Handle, "You have been disconnected.", type: INotifyService.NotificationType.Announce);
			await ConnectionService.Disconnect(cd.Handle);
		}

		if (!any)
		{
			await NotifyService!.Notify(executor, "That player is not connected.");
		}

		return CallState.Empty;
	}

	[SharpCommand(Name = "@POLL", Switches = ["CLEAR"], Behavior = CB.Default, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> Poll(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@READCACHE", Switches = [], Behavior = CB.Default, CommandLock = "FLAG^WIZARD", MinArgs = 0)]
	public static async ValueTask<Option<CallState>> ReadCache(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@WIZMOTD", Switches = ["CLEAR"], Behavior = CB.Default, CommandLock = "FLAG^WIZARD",
		MinArgs = 0)]
	public static async ValueTask<Option<CallState>> WizardMessageOfTheDay(IMUSHCodeParser parser,
		SharpCommandAttribute _2)
	{
		// Alias for @motd/wizard
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var switches = parser.CurrentState.Switches;
		var args = parser.CurrentState.ArgumentsOrdered;
		var argText = ArgHelpers.NoParseDefaultNoParseArgument(args, 1, MModule.empty()).ToString();
		
		var motdData = await ObjectDataService!.GetExpandedServerDataAsync<MotdData>() ?? new MotdData();
		
		if (switches.Contains("CLEAR"))
		{
			var newMotdData = motdData with { WizardMotd = null };
			await ObjectDataService!.SetExpandedServerDataAsync(newMotdData, ignoreNull: true);
			await NotifyService!.Notify(executor, "Wizard MOTD cleared.");
		}
		else if (string.IsNullOrEmpty(argText))
		{
			await NotifyService!.Notify(executor, "Usage: @wizmotd <message>");
		}
		else
		{
			var newMotdData = motdData with { WizardMotd = argText };
			await ObjectDataService!.SetExpandedServerDataAsync(newMotdData, ignoreNull: true);
			await NotifyService!.Notify(executor, "Wizard MOTD set.");
		}
		
		return CallState.Empty;
	}

	/// <summary>
	/// Helper method for @ENABLE and @DISABLE commands.
	/// Mimics @config/set behavior for boolean options.
	/// </summary>
	private static async ValueTask<Option<CallState>> ConfigSetHelper(IMUSHCodeParser parser, bool isEnable)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var args = parser.CurrentState.Arguments;

		// Get the option name from arguments
		var optionName = args.GetValueOrDefault("0")?.Message?.ToPlainText();
		if (string.IsNullOrWhiteSpace(optionName))
		{
			await NotifyService!.Notify(executor, $"Usage: @{(isEnable ? "enable" : "disable")} <option>");
			return new CallState("#-1 INVALID ARGUMENTS");
		}

		// Use reflection to find the configuration option (same as @config)
		var optionsType = typeof(SharpMUSH.Configuration.Options.SharpMUSHOptions);
		var categoryProperties = optionsType.GetProperties();
		
		// Search for the option across all categories
		var allOptions = categoryProperties
			.SelectMany(category =>
			{
				var categoryType = category.PropertyType;
				var props = categoryType.GetProperties();
				return props.Select(prop =>
				{
					var attr = prop.GetCustomAttribute<SharpMUSH.Configuration.SharpConfigAttribute>();
					if (attr == null) return null;
					var categoryValue = category.GetValue(Configuration!.CurrentValue);
					var value = prop.GetValue(categoryValue);
					return new
					{
						Category = category.Name,
						CategoryInstance = categoryValue,
						PropertyInfo = prop,
						PropertyName = prop.Name,
						ConfigAttr = attr,
						Value = value
					};
				}).Where(x => x != null);
			})
			.Select(x => x!)
			.ToList();

		// Find the matching option (case-insensitive)
		var matchingOption = allOptions.FirstOrDefault(opt =>
			opt.ConfigAttr.Name.Equals(optionName, StringComparison.OrdinalIgnoreCase));

		if (matchingOption == null)
		{
			await NotifyService!.Notify(executor, $"No configuration option named '{optionName}'.");
			return new CallState("#-1 NOT FOUND");
		}

		// Check if the option is a boolean
		if (matchingOption.PropertyInfo.PropertyType != typeof(bool))
		{
			await NotifyService!.Notify(executor, 
				$"Option '{matchingOption.ConfigAttr.Name}' is not a boolean option. Use @config/set instead.");
			return new CallState("#-1 INVALID TYPE");
		}

		// Note: Runtime configuration modification is not yet fully implemented
		// This would require writing to a configuration file or database and reloading
		await NotifyService!.Notify(executor, 
			$"@{(isEnable ? "enable" : "disable")} is equivalent to @config/set {matchingOption.ConfigAttr.Name}={(isEnable ? "yes" : "no")}");
		await NotifyService.Notify(executor, 
			"Runtime configuration modification is not yet implemented. Changes require server restart.");
		await NotifyService.Notify(executor, 
			$"Current value: {matchingOption.ConfigAttr.Name}={(matchingOption.Value?.ToString() ?? "null")}");
		
		return new CallState("#-1 NOT IMPLEMENTED");
	}

}
