using System.Globalization;
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

	[SharpCommand(Name = "@FLAG", Switches = ["ADD", "TYPE", "LETTER", "LIST", "RESTRICT", "DELETE", "ALIAS", "DISABLE", "ENABLE", "DEBUG", "DECOMPILE"], Behavior = CB.Default | CB.EqSplit | CB.RSArgs | CB.NoGagged, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> Flag(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
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
		var handles = parser.ConnectionService.GetAll().Select(x => x.Handle);

		if (!parser.CurrentState.Switches.Contains("EMIT"))
		{
			shout = MModule.concat(MModule.single(parser.Configuration.CurrentValue.Cosmetic.RoyaltyWallPrefix + " "), shout);
		}
		
		await foreach (var handle in handles.ToAsyncEnumerable())
		{
			await parser.NotifyService.Notify(handle, shout);
		}

		return new CallState(shout);
	}

	[SharpCommand(Name = "@WIZWALL", Switches = ["NOEVAL", "EMIT"], Behavior = CB.Default, CommandLock = "FLAG^WIZARD", MinArgs = 1, MaxArgs = 1)]
	public static async ValueTask<Option<CallState>> WizardWall(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		// TODO: Pipe through SPEAK()
		var shout = parser.CurrentState.Arguments["0"].Message!;
		var handles = parser.ConnectionService.GetAll().Select(x => x.Handle);

		if (!parser.CurrentState.Switches.Contains("EMIT"))
		{
			shout = MModule.concat(MModule.single(parser.Configuration.CurrentValue.Cosmetic.WizardWallPrefix + " "), shout);
		}
		
		await foreach (var handle in handles.ToAsyncEnumerable())
		{
			await parser.NotifyService.Notify(handle, shout);
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
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
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

		var executor = await parser.CurrentState.KnownExecutorObject(parser.Mediator);
		var args = parser.CurrentState.Arguments;
		var arg0 = args["0"].Message!.ToPlainText();
		var isGenerate = parser.CurrentState.Switches.Contains("GENERATE");

		if (isGenerate && parser.CurrentState.Arguments.Count > 1)
		{
			await parser.NotifyService.Notify(
				executor.Object().DBRef,
				"@NEWPASSWORD: /GENERATE switch cannot be used with other arguments.");
		}

		var maybePlayer = await parser.LocateService.LocatePlayerAndNotifyIfInvalidWithCallState(parser, executor, executor, arg0);
		
		if(maybePlayer.IsError)
		{
			return maybePlayer.AsError;
		}

		var asPlayer = maybePlayer.AsSharpObject.AsPlayer;

		if (isGenerate)
		{
			var generatedPassword = parser.PasswordService.GenerateRandomPassword();

			await parser.Mediator.Send(
				new SetPlayerPasswordCommand(asPlayer, parser.PasswordService.HashPassword(asPlayer.Object.DBRef.ToString(), generatedPassword)));

			await parser.NotifyService.Notify(
				executor.Object().DBRef,
				$"Generated password for {asPlayer.Object.Name}: {generatedPassword}");
			
			return new CallState(generatedPassword);
		}

		var arg1 = args["1"].Message!.ToPlainText();
		var newHashedPassword = parser.PasswordService.HashPassword(asPlayer.Object.DBRef.ToString(), arg1);

		await parser.Mediator.Send(new SetPlayerPasswordCommand(asPlayer, newHashedPassword));

		await parser.NotifyService.Notify(
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
		var executor = await parser.CurrentState.KnownExecutorObject(parser.Mediator);
		var data = (await parser.ObjectDataService.GetExpandedServerDataAsync<UptimeData>())!;
		var upSince = data.StartTime.ToString();
		var lastReboot = data.LastRebootTime.ToString();
		var reboots = data.Reboots.ToString();
		var now = DateTimeOffset.UtcNow.ToString(CultureInfo.InvariantCulture);
		var nextPurge = data.NextPurgeTime - DateTimeOffset.Now;
		var nextWarning = data.NextWarningTime - DateTimeOffset.Now;
		var uptime = DateTimeOffset.Now - data.StartTime;

		var details = $"""
		                          Up since: {upSince.Humanize()}
		                       Last Reboot: {lastReboot.Humanize()}
		                     Total Reboots: {reboots.Humanize()}
		                          Time now: {now.Humanize()}
		                        Next Purge: {nextPurge.Humanize()}
		                     Next Warnings: {nextWarning.Humanize()}
		                  SharpMUSH Uptime: {uptime.Humanize()}
		               """;

		await parser.NotifyService.Notify(executor, details);

		if(await executor.IsWizard() && !parser.CurrentState.Switches.Contains("MORTAL"))
		{
			// Process ID
			// Page Size
			// Memory Usage
			// Database Size
			
			var process = System.Diagnostics.Process.GetCurrentProcess();
			var pid = process.Id;
			var memoryUsage = process.WorkingSet64.Bytes().Humanize("0.00");
			var peakMemoryUsage = process.PeakWorkingSet64.Bytes().Humanize("0.00");
			var paged = process.PagedMemorySize64.Bytes().Humanize("0.00");
			var maxPaged = process.PagedSystemMemorySize64.Bytes().Humanize("0.00");
			var peakPaged = process.PeakPagedMemorySize64.Bytes().Humanize("0.00");
			
			var extra = $"""
			             
			                    Process ID: {pid}
			                  Memory Usage: {memoryUsage}
			             Peak Memory Usage: {peakMemoryUsage}
			                  Paged Memory: {paged}
			              Max Paged Memory: {maxPaged}
			             Peak Paged Memory: {peakPaged}
			             """;
			
			await parser.NotifyService.Notify(executor, extra);
		}
		
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

		var player = await parser.Mediator.Send(new CreatePlayerCommand(name, password, parser.CurrentState.Executor!.Value));

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
		var handles = parser.ConnectionService.GetAll().Select(x => x.Handle);

		if (!parser.CurrentState.Switches.Contains("EMIT"))
		{
			shout = MModule.concat(MModule.single(parser.Configuration.CurrentValue.Cosmetic.WallPrefix + " "), shout);
		}
		
		await foreach (var handle in handles.ToAsyncEnumerable())
		{
			await parser.NotifyService.Notify(handle, shout);
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
