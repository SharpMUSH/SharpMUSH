using SharpMUSH.Library.Authorization;
using Humanizer;
using Microsoft.Extensions.Logging;
using SharpMUSH.Implementation.Common;
using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ExpandedObjectData;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Messaging.Messages;
using System.Diagnostics;
using CB = SharpMUSH.Library.Definitions.CommandBehavior;
using ConfigGenerated = SharpMUSH.Configuration.Generated;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Configuration;
using SharpMUSH.Library.Requests;
using System.Collections.Immutable;
using System.Buffers;
using System.Runtime.InteropServices;
using MarkupString.Layout;
using SharpMUSH.Library.Markup;

namespace SharpMUSH.Implementation.Commands;

public partial class Commands
{
	[SharpCommand(Name = "@SHUTDOWN", Switches = ["PANIC", "REBOOT", "PARANOID"], Behavior = CB.Default,
		CommandLock = "PERM^server.operate", MinArgs = 0, ParameterNames = ["type"])]
	public async ValueTask<Option<CallState>> Shutdown(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var switches = parser.CurrentState.Switches;
		var executorName = executor.Object().Name;

		if (switches.Contains("PANIC"))
		{
			if (!executor.IsGod())
			{
				return await NotifyService.NotifyAndReturn(
					executor.Object().DBRef,
					errorReturn: ErrorMessages.Returns.PermissionDenied,
					notifyMessage: ErrorMessages.Notifications.ShutdownOnlyGodPanic,
					shouldNotify: true);
			}

			await GameBroadcastService.BroadcastShutdownAsync(executorName, isReboot: false);
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ShutdownPanicInitiated), executor);
			// In a web-based environment, panic shutdown should trigger immediate termination
			// This would typically be handled by orchestration (Kubernetes, Docker, etc.)
			Logger.LogCritical("PANIC SHUTDOWN initiated by {Executor}", executorName);
		}
		else if (switches.Contains("REBOOT"))
		{
			// PennMUSH's reboot keeps connections; here the connection server holds them while the engine restarts.
			if (!await ServerRestart.RestartAsync(executorName))
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ShutdownRebootPending), executor);
				return CallState.Empty;
			}

			await Audit.RecordAsync(executor, AuditActions.ServerRestart,
				new AuditTarget(AuditTargetKinds.Server, "engine", "Game engine"));
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ShutdownRebootInitiated), executor);
			return CallState.Empty;
		}
		else if (switches.Contains("PARANOID"))
		{
			await GameBroadcastService.BroadcastAsync(ErrorMessages.Notifications.GameSavingDatabase);
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ShutdownParanoidInitiated), executor);
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ShutdownParanoidDatabase), executor);
			Logger.LogWarning("PARANOID SHUTDOWN requested by {Executor}", executorName);
		}
		else
		{
			// Broadcast shutdown to all connected players (PennMUSH src/bsd.c).
			await GameBroadcastService.BroadcastShutdownAsync(executorName, isReboot: false);
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ShutdownInitiated), executor);
			Logger.LogWarning("SHUTDOWN requested by {Executor}", executorName);
		}

		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ShutdownNoteWebApp), executor);
		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ShutdownNoteOrchestration), executor);
		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ShutdownNoteNoSave), executor);

		return CallState.Empty;
	}

	[SharpCommand(Name = "@DUMP", Switches = ["PARANOID", "DEBUG", "NOFORK"], Behavior = CB.Default,
		CommandLock = "PERM^server.operate", MinArgs = 0, ParameterNames = ["type"])]
	public async ValueTask<Option<CallState>> Dump(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.DumpDoesNothing), executor);
		return new None();
	}

	/// <summary>
	/// Takes a hot copy of the world into the backup directory, so a snapshot tool has a consistent
	/// one to read while the game runs. This is what <c>@dump</c> would be if SharpMUSH kept the world
	/// in memory: it does not, so <c>@dump</c> has nothing to write out and this copies instead.
	///
	/// <para><c>/LIST</c> reports the copies already on disk, newest first. A provider that cannot copy
	/// its own world says why, in its own terms — a database server this game only talks to is not the
	/// same situation as one whose support is not written yet.</para>
	/// </summary>
	[SharpCommand(Name = "@BACKUP", Switches = ["LIST"], Behavior = CB.Default,
		CommandLock = "PERM^server.operate", MinArgs = 0, MaxArgs = 0, ParameterNames = [])]
	public async ValueTask<Option<CallState>> Backup(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);

		if (!WorldBackupService.IsSupported)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.BackupUnavailableFormat),
				executor, WorldBackupService.UnavailableReason);
			return new None();
		}

		if (parser.CurrentState.Switches.Contains("LIST"))
		{
			var existing = WorldBackupService.List();
			if (existing.Count == 0)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.BackupListEmpty), executor);
				return new None();
			}

			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.BackupListHeaderFormat),
				executor, WorldBackupService.Root);
			foreach (var backup in existing)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.BackupListRowFormat),
					executor, backup.Name, DescribeBytes(backup.SizeBytes));
			}

			return new None();
		}

		// Said before the copy starts, because a large world takes long enough that silence reads as a
		// wedged command.
		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.BackupStarted), executor);

		return await WorldBackupService.CreateAsync() switch
		{
			WorldBackup written => await BackupWrittenAsync(executor, written),
			Error<string> failure => await BackupFailedAsync(executor, failure.Value),
		};
	}

	/// <summary>Reports a finished <c>@backup</c> copy and returns its name.</summary>
	private async ValueTask<Option<CallState>> BackupWrittenAsync(AnySharpObject executor, WorldBackup written)
	{
		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.BackupCompleteFormat),
			executor, written.Name, DescribeBytes(written.SizeBytes), WorldBackupService.Keep);
		return new CallState(written.Name);
	}

	/// <summary>Reports why <c>@backup</c> wrote no copy.</summary>
	private async ValueTask<Option<CallState>> BackupFailedAsync(AnySharpObject executor, string reason)
	{
		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.BackupFailedFormat), executor,
			reason);
		return new None();
	}

	/// <summary>Byte count at a size a wizard reading it in a terminal can take in at a glance.</summary>
	private static string DescribeBytes(long bytes) => bytes switch
	{
		>= 1024L * 1024 * 1024 => $"{bytes / (double)(1024L * 1024 * 1024):F1} GB",
		>= 1024 * 1024 => $"{bytes / (double)(1024 * 1024):F1} MB",
		>= 1024 => $"{bytes / 1024.0:F1} KB",
		_ => $"{bytes} B"
	};

	[SharpCommand(Name = "@DBCK", Switches = [], Behavior = CB.Default, CommandLock = "PERM^server.operate", MinArgs = 0, ParameterNames = [])]
	public async ValueTask<Option<CallState>> DatabaseCheck(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.NotSupportedForSharpMUSH), executor);
		return CallState.Empty;
	}

	/// <remarks>
	/// PennMUSH <c>purge()</c> (<c>src/destroy.c</c>): one pass over the database, advancing objects
	/// marked <c>GOING</c> to <c>GOING_TWICE</c> and freeing the ones that already reached
	/// <c>GOING_TWICE</c>. Two passes, so an accidental <c>@destroy</c> stays recoverable via
	/// <c>@undestroy</c> for a whole purge interval. A special object that somehow got marked is
	/// spared rather than freed.
	/// </remarks>
	[SharpCommand(Name = "@PURGE", Switches = [], Behavior = CB.Default, CommandLock = "PERM^server.operate", MinArgs = 0, MaxArgs = 0, ParameterNames = [])]
	public async ValueTask<Option<CallState>> Purge(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);

		await ObjectDestructionService.PurgeAsync(parser);

		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PurgeComplete), executor);

		return CallState.Empty;
	}

	[SharpCommand(Name = "@READCACHE", Switches = [], Behavior = CB.Default, CommandLock = "PERM^server.operate", MinArgs = 0, ParameterNames = [])]
	public async ValueTask<Option<CallState>> ReadCache(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);

		if (TextFileService == null)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ReadCacheServiceNotAvailable), executor);
			return CallState.Empty;
		}

		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ReadCacheReindexing), executor);

		var startTime = DateTime.UtcNow;
		try
		{
			await TextFileService.ReindexAsync();
			var elapsed = DateTime.UtcNow - startTime;
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ReadCacheCompleteFormat), executor, elapsed.TotalMilliseconds.ToString("F0"));
		}
		catch (Exception ex)
		{
			var elapsed = DateTime.UtcNow - startTime;
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ReadCacheErrorFormat), executor, elapsed.TotalMilliseconds.ToString("F0"), ex.Message);
		}

		return CallState.Empty;
	}

	[SharpCommand(Name = "@KICK", Switches = [], Behavior = CB.Default, CommandLock = "PERM^server.operate", MinArgs = 0, ParameterNames = ["player"])]
	public async ValueTask<Option<CallState>> Kick(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var args = parser.CurrentState.Arguments;

		if (args.Count == 0)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.KickUsage), executor);
			return new CallState(ErrorMessages.Returns.InvalidArguments);
		}

		var playerArg = args["0"].Message.ToPlainText();
		return await LocateService.LocatePlayerAndNotifyIfInvalidWithCallState(parser, executor, executor, playerArg) switch
		{
			AnySharpObject and SharpPlayer playerObj => await KickAsync(executor, playerObj),
			AnySharpObject => throw new InvalidOperationException("A player lookup found something that is not a player."),
			Error<CallState> error => error.Value
		};
	}

	private async ValueTask<Option<CallState>> KickAsync(AnySharpObject executor, SharpPlayer playerObj)
	{
		var targetDbRef = playerObj.Object.DBRef;

		var any = false;
		await foreach (var cd in ConnectionService.Get(targetDbRef))
		{
			any = true;
			await NotifyService.NotifyLocalized(cd.Handle, nameof(ErrorMessages.Notifications.YouHaveBeenDisconnected));
			await ConnectionService.Disconnect(cd.Handle);
		}

		if (!any)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PlayerNotConnected), executor);
		}

		return CallState.Empty;
	}

	[SharpCommand(Name = "@BOOT", Switches = ["PORT", "ME", "SILENT"], Behavior = CB.Default, MinArgs = 0, MaxArgs = 0, ParameterNames = ["player"])]
	public async ValueTask<Option<CallState>> Boot(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var switches = parser.CurrentState.Switches;
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
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.BootPortUsage), executor);
				return new CallState(ErrorMessages.Returns.InvalidArguments);
			}
			var portText = args["0"].Message.ToPlainText();
			if (!long.TryParse(portText, out var handle))
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.BootDescriptorMustBeNumber), executor);
				return new CallState(ErrorMessages.Returns.InvalidArguments);
			}
			if (ConnectionService.Get(handle) is not null)
			{
				targetHandles.Add(handle);
			}
			else
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.BootNoSuchDescriptorFormat), executor, handle);
				return CallState.Empty;
			}
		}
		else
		{
			if (args.Count == 0)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.BootUsage), executor);
				return new CallState(ErrorMessages.Returns.InvalidArguments);
			}
			var playerArg = args["0"].Message.ToPlainText();
			switch (await LocateService.LocatePlayerAndNotifyIfInvalidWithCallState(parser, executor, executor, playerArg))
			{
				case AnySharpObject and SharpPlayer playerObj:
					// Boot only the last active connection to match PennMUSH behavior
					var lastConnection = await ConnectionService.Get(playerObj.Object.DBRef).LastOrDefaultAsync();
					if (lastConnection is not null)
					{
						targetHandles.Add(lastConnection.Handle);
					}

					break;
				case AnySharpObject:
					throw new InvalidOperationException("A player lookup found something that is not a player.");
				case Error<CallState> error:
					return error.Value;
			}

			if (targetHandles.Count == 0)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PlayerNotConnected), executor);
				return CallState.Empty;
			}
		}

		// PennMUSH's Can_Boot: anyone may boot their own connections; anyone else's takes the Boot power, or
		// players.moderate here, where the moderation half of WIZARD lives.
		var executorRef = executor.Object().DBRef;
		var targets = targetHandles
			.Select(handle => ConnectionService.Get(handle))
			.OfType<IConnectionService.ConnectionData>()
			.ToList();

		if (targets.Any(connection => connection.Ref is not { } owner || owner.Number != executorRef.Number)
			&& !await executor.Can(PortalPermission.PlayersModerate) && !await executor.HasPower("Boot"))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PermissionDenied), executor);
			return new CallState(ErrorMessages.Returns.PermissionDenied);
		}

		foreach (var handle in targetHandles)
		{
			if (ConnectionService.Get(handle)?.Ref is { } booted && booted.Number != executorRef.Number
				&& await Mediator.Send(new GetObjectNodeQuery(booted)) is AnySharpObject bootedPlayer)
			{
				await Audit.RecordAsync(executor, AuditActions.PlayerBoot, AuditTargets.Of(bootedPlayer),
					silent ? $"descriptor {handle}, silent" : $"descriptor {handle}");
			}

			if (!silent)
			{
				await NotifyService.NotifyLocalized(handle, nameof(ErrorMessages.Notifications.YouHaveBeenDisconnected));
			}
			await ConnectionService.Disconnect(handle);

			// Tell ConnectionServer to close the actual socket connection (mirrors the QUIT path in
			// SocketCommands.cs) — ConnectionService.Disconnect alone only updates server-side state
			// and does not close the socket.
			if (MessageBus != null)
			{
				await MessageBus.Publish(new DisconnectConnectionMessage(handle, "BOOT"));
			}
		}

		return CallState.Empty;
	}

	[SharpCommand(Name = "@UPTIME", Output = CommandOutput.Value, Switches = ["MORTAL"], Behavior = CB.Default, MinArgs = 0, MaxArgs = 0, ParameterNames = [])]
	public async ValueTask<Option<CallState>> Uptime(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var data = (await ObjectDataService.GetExpandedServerDataAsync<UptimeData>())!;
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

		await NotifyService.Notify(executor, details, executor);

		// The start time, as uptime() gives it; the report is only shown.
		var output = new CallState(data.StartTime.ToUnixTimeSeconds().ToString());
		if (!await executor.Can(PortalPermission.ServerOperate) || parser.CurrentState.Switches.Contains("MORTAL"))
		{
			return output;
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

		await NotifyService.Notify(executor, extra, executor);

		return output;
	}

	[SharpCommand(Name = "@LOG", Switches = ["CHECK", "CMD", "CONN", "ERR", "TRACE", "WIZ", "RECALL"],
		Behavior = CB.Default | CB.NoGagged, CommandLock = "PERM^server.operate", MinArgs = 0, ParameterNames = ["type", "message"])]
	public async ValueTask<Option<CallState>> Log(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var switches = parser.CurrentState.Switches;

		var category = switches.Contains("CHECK") ? "Check" :
									 switches.Contains("CMD") ? "Command" :
									 switches.Contains("CONN") ? "Connection" :
									 switches.Contains("ERR") ? "Error" :
									 switches.Contains("TRACE") ? "Trace" :
									 switches.Contains("WIZ") ? "Wizard" :
									 "Command"; // Default to Command log

		if (switches.Contains("RECALL"))
		{
			var countArg = parser.CurrentState.Arguments.TryGetValue("0", out var countCallState)
				? countCallState!.Message.ToPlainText()
				: "100";

			if (!int.TryParse(countArg, out var count))
			{
				count = 100;
			}

			count = Math.Max(1, Math.Min(count, 1000));

			var shown = 0;
			var lines = new System.Text.StringBuilder();
			await foreach (var log in Mediator.CreateStream(new GetConnectionLogsQuery(category, 0, count)))
			{
				shown++;
				var timestamp = log.Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
				var message = log.Message ?? log.MessageTemplate ?? "(no message)";
				lines.AppendLine($"[{timestamp}] {message}");
			}

			if (shown == 0)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.NoLogEntriesForCategoryFormat), executor, category);
				return CallState.Empty;
			}

			await NotifyService.Notify(executor,
				$"--- Log entries for {category} (showing {shown}) ---{Environment.NewLine}{lines.ToString().TrimEnd()}", executor);
			return CallState.Empty;
		}

		var logMessageArg = parser.CurrentState.Arguments.TryGetValue("0", out var logCallState);

		if (!logMessageArg || string.IsNullOrWhiteSpace(logCallState!.Message.ToPlainText()))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.LogUsage), executor);
			return new CallState(ErrorMessages.Returns.InvalidArguments);
		}

		var logMessage = logCallState!.Message;

		using (Logger.BeginScope(new Dictionary<string, string>
		{
			["Category"] = category,
			["ExecutorDBRef"] = executor.Object().DBRef.ToString(),
			["ExecutorName"] = executor.Object().Name
		}))
		{
			Logger.LogInformation("{LogMessage}", MarkupTextSerializer.Serialize(logMessage));
		}

		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.MessageLoggedToCategoryFormat), executor, category);
		return CallState.Empty;
	}

	[SharpCommand(Name = "@ENABLE", Switches = [], Behavior = CB.Default | CB.NoGagged, CommandLock = "PERM^config.admin",
		MinArgs = 1, MaxArgs = 1, ParameterNames = ["command"])]
	public async ValueTask<Option<CallState>> Enable(IMUSHCodeParser parser, SharpCommandAttribute _2)
		=> await ToggleConfigOptionAsync(parser, enable: true);

	[SharpCommand(Name = "@DISABLE", Switches = [], Behavior = CB.Default, CommandLock = "PERM^config.admin",
		MinArgs = 1, MaxArgs = 1, ParameterNames = ["command"])]
	public async ValueTask<Option<CallState>> Disable(IMUSHCodeParser parser, SharpCommandAttribute _2)
		=> await ToggleConfigOptionAsync(parser, enable: false);

	/// <summary>
	/// PennMUSH's <c>do_enable</c> (<c>src/conf.c:1780</c>): <c>@config/set &lt;option&gt;=yes|no</c> for
	/// an on/off option, answered "Enabled." or "Disabled.".
	/// </summary>
	private async ValueTask<Option<CallState>> ToggleConfigOptionAsync(IMUSHCodeParser parser, bool enable)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var optionName = parser.CurrentState.Arguments.GetValueOrDefault("0")?.Message.ToPlainText().Trim();
		if (string.IsNullOrEmpty(optionName))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.EnableDisableUsageSyntaxFormat), executor, enable ? "enable" : "disable");
			return new CallState(ErrorMessages.Returns.InvalidArguments);
		}

		if (ConfigWriter.PropertyFor(optionName) is not { } property || !CanViewConfigOption(executor, property))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.EnableDisableNoOptionFormat), executor, optionName);
			return new CallState(ErrorMessages.Returns.NotFound);
		}

		var name = ConfigGenerated.ConfigMetadata.PropertyMetadata[property].Name;
		if (!ConfigWriter.IsSettable(property))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ConfigOptionNotSettableFormat), executor, name);
			return new CallState(ErrorMessages.Returns.PermissionDenied);
		}

		if (ConfigGenerated.ConfigAccessor.GetPropertyType(property) != typeof(bool))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.EnableDisableNotBooleanFormat), executor, name);
			return new CallState(ErrorMessages.Returns.InvalidType);
		}

		return await ConfigWriter.SetAsync(property, enable) switch
		{
			SharpMUSHOptions => await ConfigToggledAsync(executor, name, enable),
			Error<string> error => await ConfigRefusedAsync(executor, name, enable ? "yes" : "no", error.Value)
		};
	}

	private async ValueTask<CallState> ConfigToggledAsync(AnySharpObject executor, string name, bool enable)
	{
		Logger.LogInformation("{Option} {State} by {Executor}", name, enable ? "ENABLED" : "DISABLED", executor.Object().Name);
		await NotifyService.NotifyLocalized(executor,
			enable ? nameof(ErrorMessages.Notifications.ConfigOptionEnabled) : nameof(ErrorMessages.Notifications.ConfigOptionDisabled), executor);
		return new CallState(enable ? "1" : "0");
	}

	/// <summary>
	/// <c>@config/set</c> and <c>@config/save</c>, after <c>cmd_config</c>'s permission checks: find the
	/// option, refuse the ones <c>config_set</c> keeps out of a command's reach, parse the value for the
	/// option's type, and store it.
	/// </summary>
	private async ValueTask<CallState> SetConfigOptionAsync(IMUSHCodeParser parser, AnySharpObject executor,
		string optionName, string? value, bool save)
	{
		// config_set's restrict_command pseudo-option (conf.c:866-896), which cmd_config reaches on its
		// second try (cmds.c:334-335).
		if (optionName.Equals("restrict_command", StringComparison.OrdinalIgnoreCase))
		{
			return await SetCommandRestrictionAsync(executor, value, save);
		}

		if (ConfigWriter.PropertyFor(optionName) is not { } property || value is null)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ConfigCouldntSet), executor);
			return new CallState(ErrorMessages.Returns.NoSuchConfigOption);
		}

		var name = ConfigGenerated.ConfigMetadata.PropertyMetadata[property].Name;
		if (!ConfigWriter.IsSettable(property))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ConfigOptionNotSettableFormat), executor, name);
			return new CallState(ErrorMessages.Returns.PermissionDenied);
		}

		if (!ConfigWriter.TryParse(property, value, out var parsed))
		{
			return await ConfigRefusedAsync(executor, name, value, reason: null);
		}

		return await ConfigWriter.SetAsync(property, parsed) switch
		{
			SharpMUSHOptions updated => await ConfigSetAsync(executor, name, AppliedText(updated, property, parsed, value),
				save),
			Error<string> error => await ConfigRefusedAsync(executor, name, value, error.Value)
		};
	}

	/// <summary>
	/// <c>@config/set restrict_command=&lt;command&gt; &lt;restriction&gt;</c>: the command's name up to the
	/// first space, and the restriction <c>restrict_command</c> reads (<c>src/conf.c:866-896</c>). A name
	/// that finds no command, a missing restriction, or one that is neither restriction words nor a lock
	/// is "Couldn't set that option.".
	/// <para>
	/// PennMUSH applies it to the live command table and, for <c>/save</c> only, appends the line to
	/// <c>mush.cnf</c>. Here it becomes the command's entry in <c>command_restrictions</c>, stored as every
	/// <c>@config/set</c> is, and the setting is applied before the command answers — so it replaces the
	/// command's restriction just as PennMUSH's does, and lasts across a restart.
	/// </para>
	/// </summary>
	private async ValueTask<CallState> SetCommandRestrictionAsync(AnySharpObject executor, string? value, bool save)
	{
		var text = value?.Trim() ?? "";
		var space = text.IndexOfAny([' ', '\t']);
		var name = space < 0 ? "" : text[..space];
		var restriction = space < 0 ? "" : text[(space + 1)..].Trim();
		if (restriction.Length == 0 || !await IsValidRestrictionAsync(executor, name, restriction))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ConfigCouldntSet), executor);
			return new CallState(ErrorMessages.Returns.InvalidArguments);
		}

		var updated = await ConfigWriter.UpdateAsync(current =>
		{
			var restrictions = current.Restriction.CommandRestrictions
				.Where(entry => !entry.Key.Equals(name, StringComparison.OrdinalIgnoreCase))
				.ToDictionary(entry => entry.Key, entry => entry.Value);
			restrictions[name] = [restriction];
			return current with { Restriction = current.Restriction with { CommandRestrictions = restrictions } };
		});
		// The reload reapplies the setting only when it changed. Applied here as well, because
		// restrict_command replaces the command's restriction even when the line is one already set,
		// and because the reload's own pass is not awaited.
		await ApplyConfiguredRestrictionsAsync(updated.Restriction.CommandRestrictions);

		return await ConfigSetAsync(executor, "restrict_command", text, save);
	}

	private async ValueTask<CallState> ConfigSetAsync(AnySharpObject executor, string name, string value, bool save)
	{
		Logger.LogInformation("Config option '{Option}' set to '{Value}'{Saved} by {Executor}",
			name, value, save ? " and saved" : "", executor.Object().Name);
		await Audit.RecordAsync(executor, AuditActions.ConfigSet, AuditTargets.Of(AuditTargetKinds.Setting, name), value);
		await NotifyService.NotifyLocalized(executor,
			save ? nameof(ErrorMessages.Notifications.ConfigOptionSetAndSaved) : nameof(ErrorMessages.Notifications.ConfigOptionSet), executor);
		return new CallState(value);
	}

	private async ValueTask<CallState> ConfigRefusedAsync(AnySharpObject executor, string name, string value, string? reason)
	{
		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ConfigInvalidValueFormat), executor, name, value);
		if (reason is not null)
		{
			await NotifyService.Notify(executor, reason, executor);
		}

		return new CallState(ErrorMessages.Returns.InvalidArguments);
	}

	/// <summary>
	/// The option's value as stored: <paramref name="given"/> as typed, unless the declared range moved it.
	/// </summary>
	private static string AppliedText(SharpMUSHOptions updated, string property, object? parsed, string given)
		=> ConfigGenerated.ConfigAccessor.GetValue(updated, property) is var applied && Equals(applied, parsed)
			? given
			: Convert.ToString(applied, System.Globalization.CultureInfo.InvariantCulture) ?? given;

	private static bool CanViewConfigOption(AnySharpObject viewer, string property)
		=> !ConfigOptionWriter.GodOnlyOptions.Contains(property) || viewer.IsGod();

	/// <remarks>
	/// PennMUSH <c>cmd_restart</c> (<c>src/cmds.c:1355-1361</c>): <c>/all</c> is <c>do_allrestart</c>
	/// (<c>src/cque.c:2366-2386</c>), anything else <c>do_restart_com</c> (<c>src/cque.c:2408-2449</c>).
	/// A restart is <c>do_halt</c> (<see cref="HaltQueuesAsync"/>) followed by <c>@STARTUP</c>.
	/// </remarks>
	[SharpCommand(Name = "@RESTART", Switches = ["ALL"], Behavior = CB.Default | CB.NoGagged, MinArgs = 0, MaxArgs = 1, ParameterNames = [])]
	public async ValueTask<Option<CallState>> Restart(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var args = parser.CurrentState.Arguments;
		var switches = parser.CurrentState.Switches;

		if (switches.Contains("ALL"))
		{
			if (!await HaltsAnything(executor))
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.RestartWorldPowerDenied), executor);
				return new CallState(ErrorMessages.Returns.PermissionDenied);
			}

			// do_allrestart is do_allhalt, announcement and all, and then the restart.
			await HaltWorldAsync(executor);

			// Penn queues each STARTUP and tells each player in the same pass, so every player hears
			// of the restart before any STARTUP has run. These run in place, so the notices go first.
			var executorName = executor.Object().Name;
			await foreach (var obj in Mediator.CreateStream(new GetAllTypedObjectsQuery()).Where(obj => obj.IsPlayer))
			{
				await NotifyService.NotifyLocalized(obj, nameof(ErrorMessages.Notifications.GloballyRestartedByFormat),
					executor, executorName);
			}

			// Then run @STARTUP on every object — the same pass used at boot, so global
			// @function registrations etc. re-establish identically. Errors are swallowed.
			await StartupAttributeRunner.RunAllAsync(parser, Mediator, AttributeService, executor);

			return CallState.Empty;
		}

		// do_restart_com (src/cque.c:2412-2414): no object restarts the enactor, without a word of its own.
		var targetName = args.GetValueOrDefault("0")?.Message.ToPlainText();
		if (string.IsNullOrEmpty(targetName))
		{
			await HaltQueuesAsync(executor);
			await RunStartupsAsync(parser, executor, executor);
			return CallState.Empty;
		}

		var maybeTarget = await LocateService.LocateAndNotifyIfInvalid(
			parser,
			executor,
			executor,
			targetName,
			LocateFlags.All);

		if (maybeTarget is not AnySharpObject target)
		{
			return new CallState(ErrorMessages.Returns.NotFound);
		}

		if (!await PermissionService.Controls(executor, target) && !await HaltsAnything(executor))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PermissionDenied), executor);
			return new CallState(ErrorMessages.Returns.PermissionDenied);
		}

		var targetObject = target.Object();
		var executorObject = executor.Object();
		var owner = (await targetObject.Owner.WithCancellation(CancellationToken.None)).Object;
		var dbref = $"#{targetObject.DBRef.Number}";

		// src/cque.c:2423-2445, which compares the owner with the enactor itself.
		if (owner.DBRef.Number != executorObject.DBRef.Number)
		{
			if (target.IsPlayer)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AllObjectsForPlayerRestartingFormat),
					executor, targetObject.Name);
				await NotifyService.NotifyLocalized(target, nameof(ErrorMessages.Notifications.AllYourObjectsRestartingByFormat),
					executor, executorObject.Name);
			}
			else
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.RestartingOthersObjectFormat),
					executor, owner.Name, targetObject.Name, dbref);
				await NotifyService.NotifyLocalized(owner.DBRef, nameof(ErrorMessages.Notifications.RestartingObjectByFormat),
					sender: executor, targetObject.Name, dbref, executorObject.Name);
			}
		}
		else if (targetObject.DBRef.Number == executorObject.DBRef.Number)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AllYourObjectsRestarting), executor);
		}
		else
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.RestartingObjectFormat),
				executor, targetObject.Name, dbref);
		}

		await HaltQueuesAsync(target);
		await RunStartupsAsync(parser, executor, target);
		return CallState.Empty;
	}

	/// <summary>
	/// PennMUSH <c>do_raw_restart</c> (<c>src/cque.c:2389-2403</c>): a player's restart runs the
	/// <c>@STARTUP</c> of everything they own, themselves included; anything else runs its own. Never
	/// inherited, and errors are non-fatal.
	/// </summary>
	private async ValueTask RunStartupsAsync(IMUSHCodeParser parser, AnySharpObject executor, AnySharpObject victim)
	{
		var victimRef = victim.Object().DBRef;
		if (!victim.IsPlayer)
		{
			await RunStartupAsync(parser, executor, victim);
			return;
		}

		// Seeded from the owner index rather than a world scan, in the same ascending dbref order. Read
		// before any @STARTUP runs, since one may change ownership; each object's owner is asked again
		// when its turn comes, as the scan asked it then.
		var owned = await Mediator.CreateStream(new GetFilteredObjectsQuery(new ObjectSearchFilter { Owner = victimRef }))
			.Select(obj => obj.DBRef)
			.ToArrayAsync();
		foreach (var dbref in owned)
		{
			if (await Mediator.Send(new GetObjectNodeQuery(dbref)) is not AnySharpObject obj) continue;
			var owner = await obj.Object().Owner.WithCancellation(CancellationToken.None);
			if (owner.Object.DBRef.Number == victimRef.Number)
			{
				await RunStartupAsync(parser, executor, obj);
			}
		}
	}

	private async ValueTask RunStartupAsync(IMUSHCodeParser parser, AnySharpObject executor, AnySharpObject obj)
	{
		// do_raw_restart skips a HALTed object (src/cque.c:2394, :2399).
		if (await obj.HasFlag("HALT")) return;

		try
		{
			await AttributeService.EvaluateAttributeFunctionAsync(
				parser, executor, obj, "STARTUP",
				new Dictionary<string, CallState>(),
				evalParent: false);
		}
		catch
		{
			// Ignore @STARTUP errors - they're non-fatal
		}
	}

	/// <summary>
	/// Line-for-line the shape of PennMUSH's <c>do_version</c> (src/version.c): the game's name, the
	/// address <em>only when one is configured</em>, the restart time, then the version banner — which is
	/// the very string <c>version()</c> returns, exactly as PennMUSH's <c>fun_version</c> and
	/// <c>do_version</c> both format from VERSION/PATCHLEVEL/PATCHDATE.
	/// </summary>
	[SharpCommand(Name = "@VERSION", Output = CommandOutput.Value, Switches = [], Behavior = CB.Default, MinArgs = 0, MaxArgs = 0, ParameterNames = [])]
	public async ValueTask<Option<CallState>> Version(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var uptimeData = await ObjectDataService.GetExpandedServerDataAsync<UptimeData>();
		var net = Configuration.CurrentValue.Net;

		var lines = new List<MString> { MarkupText.Plain($"You are connected to {net.MudName}") };

		// PennMUSH: `if (MUDURL && *MUDURL)`. An unset mud_url means the game has no published address,
		// which is not the same fact as "the address is Unknown" — so the line is omitted, not filled in.
		if (!string.IsNullOrWhiteSpace(net.MudUrl))
		{
			lines.Add(MarkupText.Plain($"Address: {net.MudUrl}"));
		}

		if (uptimeData != null)
		{
			lines.Add(MarkupText.Plain($"Last restarted: {uptimeData.LastRebootTime:ddd MMM dd HH:mm:ss yyyy}"));
		}

		lines.Add(MarkupText.Plain(Implementation.Generated.VersionInfo.Version));

		var result = MarkupText.Join(MarkupText.NewLine, lines);

		await NotifyService.Notify(executor, result, executor);

		// The version string, as version() gives it; the rest is only shown.
		return new CallState(Implementation.Generated.VersionInfo.Version);
	}

	/// <summary>
	/// PennMUSH's <c>cmd_stats</c> (<c>src/cmds.c:1472</c>), open to everyone (<c>CMD_T_ANY</c>).
	/// <c>/TABLES</c> and <c>/FLAGS</c> report the same tables Penn does, counted by the services that
	/// hold them here. The four chunk switches describe Penn's attribute-chunk allocator, which
	/// SharpMUSH does not have, and say so instead of printing a figure that measures something else.
	/// </summary>
	[SharpCommand(Name = "@STATS", Switches = ["CHUNKS", "FREESPACE", "PAGING", "REGIONS", "TABLES", "FLAGS"],
		Behavior = CB.Default, MinArgs = 0, MaxArgs = 1, ParameterNames = ["player"])]
	public async ValueTask<Option<CallState>> Stats(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var switches = parser.CurrentState.Switches;

		if (switches.Contains("TABLES"))
		{
			return await TableStatsAsync(executor);
		}

		if (switches.Contains("FLAGS"))
		{
			return await FlagStatsAsync(executor);
		}

		if (switches.FirstOrDefault(sw => sw is "CHUNKS" or "FREESPACE" or "PAGING" or "REGIONS") is { } chunkSwitch)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.StatsChunksUnsupportedFormat),
				executor, chunkSwitch.ToLowerInvariant());
			return new CallState(ErrorMessages.Returns.ErrorNotSupported);
		}

		var name = parser.CurrentState.Arguments.GetValueOrDefault("0")?.Message.ToPlainText().Trim() ?? "";
		if (name.Length == 0)
		{
			return await ObjectStatsAsync(executor, owner: null);
		}

		// do_stats resolves "me" to the executor itself before looking for a player.
		if (name.Equals("me", StringComparison.OrdinalIgnoreCase))
		{
			return await ObjectStatsAsync(executor, executor.Object().DBRef);
		}

		if (await LocateService.LocatePlayer(parser, executor, executor, name) is not AnySharpObject owner)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.StatsNoSuchPlayerFormat), executor, name);
			return new CallState(ErrorMessages.Returns.NoSuchPlayer);
		}

		// do_stats: without Search_All, only your own objects or the world's.
		if (owner.Object().DBRef != executor.Object().DBRef && !await ObjectStatsHelpers.CanSearchAll(executor))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.StatsNeedSearchWarrant), executor);
			return new CallState(ErrorMessages.Returns.PermissionDenied);
		}

		return await ObjectStatsAsync(executor, owner.Object().DBRef);
	}

	/// <summary>
	/// <c>do_stats</c>: the object count by type, over the world or over one owner's objects. The world's
	/// line ends with the garbage count, which is always <see cref="ObjectStatsHelpers.Garbage"/> here:
	/// SharpMUSH removes a destroyed object rather than keeping it as garbage, so there is also never a
	/// free dbref for Penn's "next object" line to name.
	/// </summary>
	private async ValueTask<Option<CallState>> ObjectStatsAsync(AnySharpObject executor, DBRef? owner)
	{
		var counts = await ObjectStatsHelpers.CountAsync(Mediator, owner);

		if (owner is null)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.StatsWorldCountsFormat), executor,
				counts.Total, counts.Rooms, counts.Exits, counts.Things, counts.Players, ObjectStatsHelpers.Garbage);
			return new CallState(counts.ForEveryone());
		}

		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.StatsObjectCountsFormat), executor,
			counts.Total, counts.Rooms, counts.Exits, counts.Things, counts.Players);
		return new CallState(counts.ForOnePlayer());
	}

	/// <summary>
	/// <c>flag_stats</c> (<c>src/flags.c:1136</c>): per flagspace, the size of its table and how the
	/// objects' flag sets are distributed. Penn's flagset byte width, slab and cache-bucket lines
	/// describe its in-memory layout and have nothing to count here.
	/// </summary>
	private async ValueTask<Option<CallState>> FlagStatsAsync(AnySharpObject executor)
	{
		var flagSets = new Dictionary<string, int>();
		var powerSets = new Dictionary<string, int>();

		static async ValueTask Tally(Dictionary<string, int> sets, IAsyncEnumerable<string> names)
		{
			var key = string.Join(' ', (await names.ToArrayAsync()).Order(StringComparer.Ordinal));
			CollectionsMarshal.GetValueRefOrAddDefault(sets, key, out _)++;
		}

		await foreach (var obj in Mediator.CreateStream(new GetAllObjectsQuery()))
		{
			await Tally(flagSets, (await obj.ReadFlagsAsync(ExecutionBudget.CurrentToken)).Flags.Select(f => f.Name).ToAsyncEnumerable());
			await Tally(powerSets, (await obj.ReadPowersAsync(ExecutionBudget.CurrentToken)).Select(p => p.Name).ToAsyncEnumerable());
		}

		await ReportFlagspaceAsync(executor, "FLAG", await Mediator.CreateStream(new GetAllObjectFlagsQuery()).CountAsync(), flagSets);
		await ReportFlagspaceAsync(executor, "POWER", await Mediator.CreateStream(new GetPowersQuery()).CountAsync(), powerSets);
		return CallState.Empty;
	}

	private async ValueTask ReportFlagspaceAsync(AnySharpObject executor, string flagspace, int entries, Dictionary<string, int> sets)
	{
		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.StatsFlagspaceHeaderFormat), executor, flagspace);
		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.StatsFlagspaceEntriesFormat), executor, entries);
		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.StatsFlagspaceFlagsetsFormat), executor,
			sets.Count, sets.GetValueOrDefault(""));
		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.StatsFlagspaceMostCommonFormat), executor,
			sets.Values.DefaultIfEmpty(0).Max());
		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.StatsFlagspaceUniqueFormat), executor,
			sets.Values.Count(count => count == 1));
	}

	/// <summary>
	/// <c>do_list_memstats</c> (<c>src/game.c:2663</c>): Penn's lookup tables by entry count. Each row
	/// here is the count held by the service that owns that table. Penn's bucket, lookup-depth and
	/// memory columns measure its own hash tables and are not reported.
	/// </summary>
	private async ValueTask<Option<CallState>> TableStatsAsync(AnySharpObject executor)
	{
		var builtinFunctions = FunctionLibrary.Values.Count(entry => entry.IsSystem);
		(string Table, int Entries)[] rows =
		[
			("Functions", builtinFunctions),
			("@Functions", FunctionLibrary.Count - builtinFunctions),
			("Commands", CommandLibrary.Count),
			("Flags", await Mediator.CreateStream(new GetAllObjectFlagsQuery()).CountAsync()),
			("Powers", await Mediator.CreateStream(new GetPowersQuery()).CountAsync()),
			("Attributes", await Mediator.CreateStream(new GetAllAttributeEntriesQuery()).CountAsync()),
			("ConfigOpts", ConfigGenerated.ConfigMetadata.PropertyToAttributeName.Count),
			("Connections", await ConnectionService.GetAll().CountAsync())
		];

		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.StatsTablesHeader), executor);
		foreach (var (table, entries) in rows)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.StatsTablesRowFormat), executor, table, entries);
		}

		return CallState.Empty;
	}

	[SharpCommand(Name = "@CONFIG", Output = CommandOutput.Value, Switches = ["SET", "SAVE", "LOWERCASE", "LIST"], Behavior = CB.Default | CB.EqSplit,
		MinArgs = 0, MaxArgs = 2, ParameterNames = ["option", "value"])]
	public async ValueTask<Option<CallState>> Config(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var args = parser.CurrentState.Arguments;
		var switches = parser.CurrentState.Switches;
		var useLowercase = switches.Contains("LOWERCASE");

		var allCategories = ConfigGenerated.ConfigAccessor.Categories.ToList();

		// PropertyNames is declaration order; do_config_list walks its conftable in a fixed order too.
		IEnumerable<(string Category, string PropertyName, SharpConfigAttribute ConfigAttr, object? Value)> getAllOptions() =>
			ConfigGenerated.ConfigMetadata.PropertyNames
				.Where(propName => CanViewConfigOption(executor, propName))
				.Select(propName => (
				Category: ConfigGenerated.ConfigAccessor.GetCategoryForProperty(propName) ?? "",
				PropertyName: propName,
				ConfigAttr: ConfigGenerated.ConfigMetadata.PropertyMetadata[propName],
				Value: ConfigGenerated.ConfigAccessor.GetValue(Configuration.CurrentValue, propName)));

		// cmd_config: /set needs a wizard, /save needs God, and both need an option.
		if (switches.Contains("SET") || switches.Contains("SAVE"))
		{
			var save = switches.Contains("SAVE");
			if (!await executor.Can(PortalPermission.ConfigAdmin) || (save && !executor.IsGod()))
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ConfigCantRemakeWorld), executor);
				return new CallState(ErrorMessages.Returns.PermissionDenied);
			}

			var optionName = args.GetValueOrDefault("0")?.Message.ToPlainText().Trim() ?? "";
			if (optionName.Length == 0)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ConfigWhatToSet), executor);
				return new CallState(ErrorMessages.Returns.InvalidArguments);
			}

			return await SetConfigOptionAsync(parser, executor, optionName, args.GetValueOrDefault("1")?.Message.ToPlainText(), save);
		}

		if (args.Count == 0)
		{
			var counts = getAllOptions().CountBy(opt => opt.Category).ToDictionary(StringComparer.OrdinalIgnoreCase);
			var categories = ServerLayout.KeyValues(allCategories.Select(cat =>
				(ServerLayout.CommandLink(cat, $"@config {cat}"), MarkupText.Plain(counts.GetValueOrDefault(cat) == 1 ? "1 option" : $"{counts.GetValueOrDefault(cat)} options")))) with
			{ Columns = 2 };
			await NotifyService.Notify(executor, ServerLayout.Build(ServerLayout.Panel(
				MarkupText.Plain(ErrorMessages.Notifications.ConfigCategoriesHeader),
				categories,
				new Rule(),
				new TextBlock(MarkupText.Plain($"{ErrorMessages.Notifications.ConfigUseCategoryHelp}\n{ErrorMessages.Notifications.ConfigUseOptionHelp}"))), 78), executor);
			return CallState.Empty;
		}

		var searchTerm = args.GetValueOrDefault("0")?.Message.ToPlainText() ?? "";

		var matchingCategory = allCategories.FirstOrDefault(c =>
			c.Equals(searchTerm, StringComparison.OrdinalIgnoreCase));

		if (matchingCategory != null)
		{
			var categoryOptions = getAllOptions()
				.Where(opt => opt.Category.Equals(matchingCategory, StringComparison.OrdinalIgnoreCase))
				.OrderBy(opt => opt.ConfigAttr.Name)
				.ToList();

			if (categoryOptions.Count == 0)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ConfigNoOptionsInCategoryFormat), executor, matchingCategory);
				return CallState.Empty;
			}

			var options = new Fields([.. categoryOptions.Select(opt =>
			{
				var name = useLowercase ? opt.ConfigAttr.Name.ToLower() : opt.ConfigAttr.Name;
				return new Field(ServerLayout.CommandLink(name, $"@config {name}"), ConfigValueBlock(opt.Value, opt.ConfigAttr));
			})]);
			await NotifyService.Notify(executor, ServerLayout.Build(ServerLayout.Panel(
				MarkupText.Plain(string.Format(ErrorMessages.Notifications.ConfigOptionsInCategoryFormat, matchingCategory)),
				options,
				new Rule(),
				new TextBlock(ServerLayout.CommandLink(ErrorMessages.Notifications.ConfigAllCategories, "@config"))), 78), executor);
			return CallState.Empty;
		}

		// do_config_list (src/conf.c:1584-1621): every option whose name starts with the word, or failing
		// that every option whose name contains it, one config_to_string line each.
		var allOptions = getAllOptions().ToList();
		var matchingOptions = allOptions
			.Where(opt => opt.ConfigAttr.Name.StartsWith(searchTerm, StringComparison.OrdinalIgnoreCase))
			.ToList();
		if (matchingOptions.Count == 0)
		{
			matchingOptions = allOptions
				.Where(opt => opt.ConfigAttr.Name.Contains(searchTerm, StringComparison.OrdinalIgnoreCase))
				.ToList();
		}

		if (matchingOptions.Count > 0)
		{
			string? lastValue = null;
			foreach (var opt in matchingOptions)
			{
				var name = useLowercase ? opt.ConfigAttr.Name.ToLower() : opt.ConfigAttr.Name;
				lastValue = ConfigValueDisplay.Format(opt.Value, opt.ConfigAttr);
				if (opt.Value is IEnumerable<KeyValuePair<string, string[]>> map)
				{
					// One entry per line, the name on the first and the rest kept to its value column.
					var label = name;
					foreach (var (key, values) in ConfigValueDisplay.Entries(map))
					{
						await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ConfigOptionValueFormat), executor, label, $"{key}: {values}");
						label = string.Empty;
					}

					if (label.Length > 0)
					{
						await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ConfigOptionValueFormat), executor, name, string.Empty);
					}

					continue;
				}

				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ConfigOptionValueFormat), executor, name, lastValue);
			}

			return new CallState(matchingOptions.Count == 1 ? lastValue! : string.Empty);
		}

		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ConfigNoCategoryOrOptionFormat), executor, searchTerm);
		return new CallState(ErrorMessages.Returns.NotFound);
	}

	/// <summary>
	/// An option's value in the <c>@config &lt;category&gt;</c> listing: a mapping option's entries as
	/// labelled values of their own, anything else as the text <c>config()</c> returns.
	/// </summary>
	private static Block ConfigValueBlock(object? value, SharpConfigAttribute metadata)
		=> value is IEnumerable<KeyValuePair<string, string[]>> map
			? ServerLayout.KeyValues(ConfigValueDisplay.Entries(map).Select(entry => (entry.Key, MarkupText.Plain(entry.Values))))
			: ServerLayout.Body(MarkupText.Plain(ConfigValueDisplay.Format(value, metadata)));

	[SharpCommand(Name = "@SLAVE", Switches = ["RESTART"], Behavior = CB.Default, CommandLock = "PERM^server.operate",
		MinArgs = 0, ParameterNames = ["object"])]
	public async ValueTask<Option<CallState>> Slave(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		await NotifyService.Notify(executor, "Slave command does nothing for SharpMUSH.", executor);
		return new None();
	}

	/// <summary>
	/// PennMUSH's <c>cmd_logwipe</c> (<c>src/cmds.c:971</c>) rotates, trims or wipes one of the game's
	/// log files. SharpMUSH owns no log file: its logs go to the logging sinks named in its
	/// configuration, and neither database provider stores them. There is nothing here that could
	/// honestly carry out any of the three policies, so each is refused by name.
	/// </summary>
	[SharpCommand(Name = "@LOGWIPE", Switches = ["CHECK", "CMD", "CONN", "ERR", "TRACE", "WIZ", "ROTATE", "TRIM", "WIPE"],
		Behavior = CB.Default | CB.NoGagged | CB.God, MinArgs = 0, MaxArgs = 1, ParameterNames = ["password"])]
	public async ValueTask<Option<CallState>> LogWipe(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var switches = parser.CurrentState.Switches;

		if (!executor.IsGod())
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PermissionDenied), executor);
			return new CallState(ErrorMessages.Returns.PermissionDenied);
		}

		// logtype_from_switch(sw, LT_ERR) and the policy switches, each with Penn's default.
		var log = switches.FirstOrDefault(sw => sw is "CHECK" or "CMD" or "CONN" or "ERR" or "TRACE" or "WIZ") ?? "ERR";
		var policy = switches.FirstOrDefault(sw => sw is "ROTATE" or "TRIM" or "WIPE") ?? "WIPE";

		Logger.LogWarning("@logwipe/{Log}/{Policy} refused for {Executor}: no log file is owned by the game",
			log, policy, executor.Object().Name);
		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.LogWipeUnsupportedFormat), executor,
			policy.ToLowerInvariant(), log.ToLowerInvariant());
		return new CallState(ErrorMessages.Returns.ErrorNotSupported);
	}
}
