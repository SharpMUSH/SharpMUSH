using System.Diagnostics;
using Humanizer;
using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ExpandedObjectData;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using CB = SharpMUSH.Library.Definitions.CommandBehavior;

namespace SharpMUSH.Implementation.Commands;

public partial class Commands
{
	[SharpCommand(Name = "@ALLHALT", Switches = [], Behavior = CB.Default, CommandLock = "FLAG^WIZARD|POWER^HALT", MinArgs = 0)]
	public static async ValueTask<Option<CallState>> AllHalt(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@FLAG", Switches = ["ADD", "TYPE", "LETTER", "LIST", "RESTRICT", "DELETE", "ALIAS", "DISABLE", "ENABLE", "DEBUG", "DECOMPILE"], Behavior = CB.Default | CB.EqSplit | CB.RSArgs | CB.NoGagged, MinArgs = 0, MaxArgs = 2)]
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
			
			// TODO: Parse additional parameters like type restrictions, permissions
			var result = await Mediator!.Send(new CreateObjectFlagCommand(
				flagName.ToUpper(),
				null, // aliases
				symbol,
				false, // system
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
				await NotifyService!.Notify(executor, $"Failed to create flag '{flagName}'. Database operations not yet fully implemented.");
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
				await NotifyService!.Notify(executor, $"Failed to delete flag '{flagName}'. Database operations not yet fully implemented.");
				return CallState.Empty;
			}
		}
		
		// Default - show usage
		await NotifyService!.Notify(executor, "Usage: @flag/list, @flag/add <name>=<symbol>, @flag/delete <name>");
		return CallState.Empty;
	}

	[SharpCommand(Name = "@LOG", Switches = ["CHECK", "CMD", "CONN", "ERR", "TRACE", "WIZ", "RECALL"], Behavior = CB.Default | CB.NoGagged, CommandLock = "FLAG^WIZARD", MinArgs = 0)]
	public static async ValueTask<Option<CallState>> Log(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@POOR", Switches = [], Behavior = CB.Default, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> Poor(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@SQUOTA", Switches = [], Behavior = CB.Default | CB.EqSplit, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> ShortQuota(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}
	
	
	[SharpCommand(Name = "@RWALL", Switches = ["NOEVAL", "EMIT"], Behavior = CB.Default,
		CommandLock = "FLAG^WIZARD|FLAG^ROYALTY", MinArgs = 0)]
	public static async ValueTask<Option<CallState>> RoyaltyWall(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{		
		// TODO: Pipe through SPEAK()
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

	[SharpCommand(Name = "@WIZWALL", Switches = ["NOEVAL", "EMIT"], Behavior = CB.Default, CommandLock = "FLAG^WIZARD", MinArgs = 1, MaxArgs = 1)]
	public static async ValueTask<Option<CallState>> WizardWall(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		// TODO: Pipe through SPEAK()
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

	[SharpCommand(Name = "@ALLQUOTA", Switches = ["QUIET"], Behavior = CB.Default, CommandLock = "FLAG^WIZARD|POWER^QUOTA", MinArgs = 0)]
	public static async ValueTask<Option<CallState>> AllQuota(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@DBCK", Switches = [], Behavior = CB.Default, CommandLock = "FLAG^WIZARD", MinArgs = 0)]
	public static async ValueTask<Option<CallState>> DatabaseCheck(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@HIDE", Switches = ["NO", "OFF", "YES", "ON"], Behavior = CB.Default, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> Hide(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@MOTD", Switches = ["CONNECT", "LIST", "WIZARD", "DOWN", "FULL", "CLEAR"], Behavior = CB.Default | CB.NoGagged, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> MessageOfTheDay(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		/*
			@motd[/<type>] <message>
			@motd/clear[/<type>]
			@motd/list
  */
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@POWER", Switches = ["ADD", "TYPE", "LETTER", "LIST", "RESTRICT", "DELETE", "ALIAS", "DISABLE", "ENABLE", "DECOMPILE"], Behavior = CB.Default | CB.EqSplit | CB.RSArgs, MinArgs = 0, MaxArgs = 0)]
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
			
			// TODO: Parse additional parameters like type restrictions, permissions
			var result = await Mediator!.Send(new CreatePowerCommand(
				powerName.ToUpper(),
				alias.ToUpper(),
				false, // system
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
				await NotifyService!.Notify(executor, $"Failed to create power '{powerName}'. Database operations not yet fully implemented.");
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
			
			// TODO: Check if power is a system power (would need GetPowerQuery)
			// For now, just attempt deletion
			
			var result = await Mediator!.Send(new DeletePowerCommand(powerName.ToUpper()));
			
			if (result)
			{
				await NotifyService!.Notify(executor, $"Power '{powerName}' deleted.");
				return new CallState(MModule.single(powerName));
			}
			else
			{
				await NotifyService!.Notify(executor, $"Failed to delete power '{powerName}'. Database operations not yet fully implemented.");
				return CallState.Empty;
			}
		}
		
		// Default - show usage
		await NotifyService!.Notify(executor, "Usage: @power/list, @power/add <name>=<alias>, @power/delete <name>");
		return CallState.Empty;
	}

	[SharpCommand(Name = "@REJECTMOTD", Switches = ["CLEAR"], Behavior = CB.Default, CommandLock = "FLAG^WIZARD", MinArgs = 0)]
	public static async ValueTask<Option<CallState>> RejectMessageOfTheDay(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@SUGGEST", Switches = ["ADD", "DELETE", "LIST"], Behavior = CB.Default | CB.EqSplit, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> Suggest(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@BOOT", Switches = ["PORT", "ME", "SILENT"], Behavior = CB.Default, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> Boot(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@DISABLE", Switches = [], Behavior = CB.Default, CommandLock = "FLAG^WIZARD", MinArgs = 0)]
	public static async ValueTask<Option<CallState>> Disable(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@HOOK", Switches = ["LIST", "AFTER", "BEFORE", "EXTEND", "IGSWITCH", "IGNORE", "OVERRIDE", "INPLACE", "INLINE", "LOCALIZE", "CLEARREGS", "NOBREAK"], Behavior = CB.Default | CB.EqSplit | CB.RSArgs, CommandLock = "FLAG^WIZARD|POWER^HOOK", MinArgs = 0)]
	public static async ValueTask<Option<CallState>> Hook(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@NEWPASSWORD", Switches = ["GENERATE"], Behavior = CB.Default | CB.EqSplit | CB.RSNoParse, CommandLock = "FLAG^WIZARD", MinArgs = 0)]
	public static async ValueTask<Option<CallState>> NewPassword(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		// @newpassword <player>=<password>
		// @newpassword / generate < player >

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

		var maybePlayer = await LocateService!.LocatePlayerAndNotifyIfInvalidWithCallState(parser, executor, executor, arg0);
		
		if(maybePlayer.IsError)
		{
			return maybePlayer.AsError;
		}

		var asPlayer = maybePlayer.AsSharpObject.AsPlayer;

		if (isGenerate)
		{
			var generatedPassword = PasswordService!.GenerateRandomPassword();

			await Mediator!.Send(
				new SetPlayerPasswordCommand(asPlayer, PasswordService.HashPassword(asPlayer.Object.DBRef.ToString(), generatedPassword)));

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

	[SharpCommand(Name = "@SHUTDOWN", Switches = ["PANIC", "REBOOT", "PARANOID"], Behavior = CB.Default, CommandLock = "FLAG^WIZARD", MinArgs = 0)]
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

	[SharpCommand(Name = "@CHOWNALL", Switches = ["PRESERVE", "THINGS", "ROOMS", "EXITS"], Behavior = CB.Default | CB.EqSplit, CommandLock = "FLAG^WIZARD", MinArgs = 0)]
	public static async ValueTask<Option<CallState>> Chown(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@DUMP", Switches = ["PARANOID", "DEBUG", "NOFORK"], Behavior = CB.Default, CommandLock = "FLAG^WIZARD", MinArgs = 0)]
	public static async ValueTask<Option<CallState>> Dump(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	/// <remarks>
	/// Creating on the DBRef is not implemented.
	/// </remarks>
	[SharpCommand(Name = "@PCREATE", Behavior = CB.Default, MinArgs = 2, MaxArgs = 3)]
	public static async ValueTask<Option<CallState>> PlayerCreate(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		// TODO: Validate Name and Passwords
		var args = parser.CurrentState.Arguments;
		var name = MModule.plainText(args["0"].Message!);
		var password = MModule.plainText(args["1"].Message!);

		var player = await Mediator!.Send(new CreatePlayerCommand(name, password, parser.CurrentState.Executor!.Value));

		return new CallState(player.ToString());
	}

	[SharpCommand(Name = "@QUOTA", Switches = ["ALL", "SET"], Behavior = CB.Default | CB.EqSplit, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> Quota(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@SITELOCK", Switches = ["BAN", "CHECK", "REGISTER", "REMOVE", "NAME", "PLAYER"], Behavior = CB.Default | CB.EqSplit | CB.RSArgs, CommandLock = "FLAG^WIZARD", MinArgs = 0)]
	public static async ValueTask<Option<CallState>> SiteLock(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@WALL", Switches = ["NOEVAL", "EMIT"], Behavior = CB.Default, CommandLock = "FLAG^WIZARD ROYALTY|POWER^ANNOUNCE", MinArgs = 0)]
	public static async ValueTask<Option<CallState>> Wall(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		// TODO: Pipe through SPEAK()
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

	[SharpCommand(Name = "@CHZONEALL", Switches = ["PRESERVE"], Behavior = CB.Default | CB.EqSplit, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> ChangeOwnerZoneAll(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@ENABLE", Switches = [], Behavior = CB.Default | CB.NoGagged, CommandLock = "FLAG^WIZARD", MinArgs = 0)]
	public static async ValueTask<Option<CallState>> Enable(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@KICK", Switches = [], Behavior = CB.Default, CommandLock = "FLAG^WIZARD", MinArgs = 0)]
	public static async ValueTask<Option<CallState>> Kick(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
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

	[SharpCommand(Name = "@WIZMOTD", Switches = ["CLEAR"], Behavior = CB.Default, CommandLock = "FLAG^WIZARD", MinArgs = 0)]
	public static async ValueTask<Option<CallState>> WizardMessageOfTheDay(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}
}
