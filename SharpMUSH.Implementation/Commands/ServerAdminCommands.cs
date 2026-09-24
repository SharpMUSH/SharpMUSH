using DotNext.Collections.Generic;
using Humanizer;
using Microsoft.Extensions.Logging;
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

namespace SharpMUSH.Implementation.Commands;

public partial class Commands
{
	[SharpCommand(Name = "@SHUTDOWN", Switches = ["PANIC", "REBOOT", "PARANOID"], Behavior = CB.Default,
		CommandLock = "FLAG^WIZARD", MinArgs = 0, ParameterNames = ["type"])]
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
			// Broadcast reboot to all connected players (PennMUSH src/bsd.c).
			await GameBroadcastService.BroadcastAsync(
				string.Format(ErrorMessages.Notifications.GameRebootBy, executorName));
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ShutdownRebootInitiated), executor);
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ShutdownRebootDocker), executor);
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ShutdownRebootStandalone), executor);
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ShutdownRebootRedis), executor);
			Logger.LogWarning("REBOOT requested by {Executor}", executorName);
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
		CommandLock = "FLAG^WIZARD", MinArgs = 0, ParameterNames = ["type"])]
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
		CommandLock = "FLAG^WIZARD", MinArgs = 0, MaxArgs = 0, ParameterNames = [])]
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

	[SharpCommand(Name = "@DBCK", Switches = [], Behavior = CB.Default, CommandLock = "FLAG^WIZARD", MinArgs = 0, ParameterNames = [])]
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
	[SharpCommand(Name = "@PURGE", Switches = [], Behavior = CB.Default, CommandLock = "FLAG^WIZARD", MinArgs = 0, MaxArgs = 0, ParameterNames = [])]
	public async ValueTask<Option<CallState>> Purge(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);

		await ObjectDestructionService.PurgeAsync(parser);

		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PurgeComplete), executor);

		return CallState.Empty;
	}

	[SharpCommand(Name = "@READCACHE", Switches = [], Behavior = CB.Default, CommandLock = "FLAG^WIZARD", MinArgs = 0, ParameterNames = [])]
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

	[SharpCommand(Name = "@KICK", Switches = [], Behavior = CB.Default, CommandLock = "FLAG^WIZARD", MinArgs = 0, ParameterNames = ["player"])]
	public async ValueTask<Option<CallState>> Kick(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var args = parser.CurrentState.Arguments;

		if (args.Count == 0)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.KickUsage), executor);
			return new CallState(ErrorMessages.Returns.InvalidArguments);
		}

		var playerArg = args["0"].Message!.ToPlainText();
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
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.BootPortUsage), executor);
				return new CallState(ErrorMessages.Returns.InvalidArguments);
			}
			var portText = args["0"].Message!.ToPlainText();
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
			var playerArg = args["0"].Message!.ToPlainText();
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

		foreach (var handle in targetHandles)
		{
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

	[SharpCommand(Name = "@UPTIME", Switches = ["MORTAL"], Behavior = CB.Default, MinArgs = 0, MaxArgs = 0, ParameterNames = [])]
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

		if (!await executor.IsWizard() || parser.CurrentState.Switches.Contains("MORTAL"))
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

		await NotifyService.Notify(executor, extra, executor);

		return new CallState(details);
	}

	[SharpCommand(Name = "@LOG", Switches = ["CHECK", "CMD", "CONN", "ERR", "TRACE", "WIZ", "RECALL"],
		Behavior = CB.Default | CB.NoGagged, CommandLock = "FLAG^WIZARD", MinArgs = 0, ParameterNames = ["type", "message"])]
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
				? countCallState!.Message!.ToPlainText()
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

		if (!logMessageArg || string.IsNullOrWhiteSpace(logCallState!.Message!.ToPlainText()))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.LogUsage), executor);
			return new CallState(ErrorMessages.Returns.InvalidArguments);
		}

		var logMessage = logCallState!.Message!;

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

	[SharpCommand(Name = "@ENABLE", Switches = [], Behavior = CB.Default | CB.NoGagged, CommandLock = "FLAG^WIZARD",
		MinArgs = 1, MaxArgs = 1, ParameterNames = ["command"])]
	public async ValueTask<Option<CallState>> Enable(IMUSHCodeParser parser, SharpCommandAttribute _2)
		=> await ToggleConfigOptionAsync(parser, enable: true);

	[SharpCommand(Name = "@DISABLE", Switches = [], Behavior = CB.Default, CommandLock = "FLAG^WIZARD",
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
		var optionName = parser.CurrentState.Arguments.GetValueOrDefault("0")?.Message?.ToPlainText().Trim();
		if (string.IsNullOrEmpty(optionName))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.EnableDisableUsageSyntaxFormat), executor, enable ? "enable" : "disable");
			return new CallState(ErrorMessages.Returns.InvalidArguments);
		}

		if (ConfigPropertyFor(optionName) is not { } property || !CanViewConfigOption(executor, property))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.EnableDisableNoOptionFormat), executor, optionName);
			return new CallState(ErrorMessages.Returns.NotFound);
		}

		var name = ConfigGenerated.ConfigMetadata.PropertyMetadata[property].Name;
		if (!IsConfigOptionSettable(property))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ConfigOptionNotSettableFormat), executor, name);
			return new CallState(ErrorMessages.Returns.PermissionDenied);
		}

		if (ConfigGenerated.ConfigAccessor.GetPropertyType(property) != typeof(bool))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.EnableDisableNotBooleanFormat), executor, name);
			return new CallState(ErrorMessages.Returns.InvalidType);
		}

		return await StoreConfigValueAsync(parser, property, enable) switch
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
		if (ConfigPropertyFor(optionName) is not { } property || value is null)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ConfigCouldntSet), executor);
			return new CallState(ErrorMessages.Returns.NoSuchConfigOption);
		}

		var name = ConfigGenerated.ConfigMetadata.PropertyMetadata[property].Name;
		if (!IsConfigOptionSettable(property))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ConfigOptionNotSettableFormat), executor, name);
			return new CallState(ErrorMessages.Returns.PermissionDenied);
		}

		if (!TryParseConfigValue(ConfigGenerated.ConfigAccessor.GetPropertyType(property)!, value, out var parsed))
		{
			return await ConfigRefusedAsync(executor, name, value, reason: null);
		}

		return await StoreConfigValueAsync(parser, property, parsed) switch
		{
			SharpMUSHOptions => await ConfigSetAsync(executor, name, value, save),
			Error<string> error => await ConfigRefusedAsync(executor, name, value, error.Value)
		};
	}

	private async ValueTask<CallState> ConfigSetAsync(AnySharpObject executor, string name, string value, bool save)
	{
		Logger.LogInformation("Config option '{Option}' set to '{Value}'{Saved} by {Executor}",
			name, value, save ? " and saved" : "", executor.Object().Name);
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
	/// Writes one option into the stored configuration — the document every service reads its options
	/// from, and the one the portal's configuration page edits — after the registered validators accept
	/// the whole result, then signals the reload that makes it live.
	/// </summary>
	private async ValueTask<Result<SharpMUSHOptions>> StoreConfigValueAsync(IMUSHCodeParser parser, string property, object? value)
	{
		var updated = ConfigGenerated.ConfigAccessor.WithValue(await CurrentPersistedOptionsAsync(), property, value);

		var failures = parser.ServiceProvider.GetServices<IValidateOptions<SharpMUSHOptions>>()
			.Select(validator => validator.Validate(Options.DefaultName, updated))
			.Where(result => result.Failed)
			.SelectMany(result => result.Failures ?? [])
			.ToArray();
		if (failures.Length > 0)
		{
			return new Error<string>(string.Join(" ", failures));
		}

		await ObjectDataService.SetExpandedServerDataAsync(updated);
		ConfigReloadService.SignalChange();
		return updated;
	}

	/// <summary>The option's property name, for an option named the way <c>@config</c> lists it.</summary>
	private static string? ConfigPropertyFor(string optionName)
		=> ConfigGenerated.ConfigMetadata.PropertyToAttributeName
			.FirstOrDefault(kvp => kvp.Value.Equals(optionName, StringComparison.OrdinalIgnoreCase)).Key;

	/// <summary>
	/// PennMUSH's CP_GODONLY options (<c>src/conf.c:153-158</c>): the SQL credentials, which
	/// <c>can_view_config_option</c> hides from everyone but God.
	/// </summary>
	private static readonly HashSet<string> GodOnlyConfigOptions =
		[nameof(NetOptions.SqlUsername), nameof(NetOptions.SqlPassword), nameof(NetOptions.SqlDatabase)];

	private static bool CanViewConfigOption(AnySharpObject viewer, string property)
		=> !GodOnlyConfigOptions.Contains(property) || viewer.IsGod();

	/// <summary>
	/// <c>config_set</c> lets a command reach every option except the <c>files</c> and <c>messages</c>
	/// groups — file paths, which could be pointed anywhere — and the CP_GODONLY ones. The list-valued
	/// options (banned names, sitelock rules, restrictions) have commands of their own.
	/// </summary>
	private static bool IsConfigOptionSettable(string property)
		=> !GodOnlyConfigOptions.Contains(property)
			 && ConfigGenerated.ConfigAccessor.GetCategoryForProperty(property) is not ("File" or "Message")
			 && ConfigGenerated.ConfigAccessor.GetPropertyType(property) is { } type
			 && (Nullable.GetUnderlyingType(type) ?? type) is var scalar
			 && (scalar.IsEnum || scalar == typeof(bool) || scalar == typeof(uint) || scalar == typeof(int)
					 || scalar == typeof(string) || scalar == typeof(char));

	/// <summary>
	/// A value as PennMUSH's handlers read it: <c>cf_bool</c> takes yes/true/1 and no/false/0 in any
	/// case; <c>cf_int</c> and <c>cf_dbref</c> take a number with an optional leading <c>#</c>, and a
	/// dbref option (<c>uint?</c> here) takes -1 for none; <c>cf_str</c> takes the text as it is.
	/// </summary>
	private static bool TryParseConfigValue(Type type, string text, out object? value)
	{
		value = null;
		var number = text.StartsWith('#') ? text[1..] : text;

		if (Nullable.GetUnderlyingType(type) is { } underlying)
		{
			if (number is "-1")
			{
				return true;
			}

			type = underlying;
		}

		switch (type)
		{
			case not null when type == typeof(bool):
				value = text.ToLowerInvariant() switch
				{
					"yes" or "true" or "1" => true,
					"no" or "false" or "0" => false,
					_ => null
				};
				return value is not null;
			case not null when type == typeof(uint):
				value = uint.TryParse(number, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var unsigned) ? unsigned : null;
				return value is not null;
			case not null when type == typeof(int):
				value = int.TryParse(number, System.Globalization.NumberStyles.AllowLeadingSign, System.Globalization.CultureInfo.InvariantCulture, out var signed) ? signed : null;
				return value is not null;
			case not null when type == typeof(char):
				value = text.Length == 1 ? text[0] : null;
				return value is not null;
			case not null when type == typeof(string):
				value = text;
				return true;
			case { IsEnum: true }:
				value = Enum.TryParse(type, text, ignoreCase: true, out var member) && Enum.IsDefined(type, member!) && !char.IsDigit(text[0]) ? member : null;
				return value is not null;
			default:
				return false;
		}
	}

	[SharpCommand(Name = "@RESTART", Switches = ["ALL"], Behavior = CB.Default | CB.NoGagged, MinArgs = 0, MaxArgs = 1, ParameterNames = [])]
	public async ValueTask<Option<CallState>> Restart(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var args = parser.CurrentState.Arguments;
		var switches = parser.CurrentState.Switches.ToArray();
		var scheduler = parser.ServiceProvider.GetRequiredService<ITaskScheduler>();

		if (switches.Contains("ALL"))
		{
			if (!await executor.IsWizard())
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PermissionDenied), executor);
				return new CallState(ErrorMessages.Returns.PermissionDenied);
			}

			await foreach (var obj in Mediator.CreateStream(new GetAllTypedObjectsQuery()))
			{
				await Mediator.Send(new HaltObjectQueueRequest(obj.Object().DBRef));
			}

			// Then run @STARTUP on every object — the same pass used at boot, so global
			// @function registrations etc. re-establish identically. Errors are swallowed.
			await StartupAttributeRunner.RunAllAsync(parser, Mediator, AttributeService, executor);

			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AllObjectsRestarted), executor);
			return CallState.Empty;
		}

		if (args.Count == 0)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.RestartMustSpecifyObject), executor);
			return new CallState(ErrorMessages.Returns.NoObjectSpecified);
		}

		var targetName = args["0"].Message!.ToPlainText();

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

		if (!await PermissionService.Controls(executor, target))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PermissionDenied), executor);
			return new CallState(ErrorMessages.Returns.PermissionDenied);
		}

		var targetObject = target.Object();

		await Mediator.Send(new HaltObjectQueueRequest(targetObject.DBRef));

		if (target.IsPlayer)
		{
			await foreach (var obj in Mediator.CreateStream(new GetAllTypedObjectsQuery()))
			{
				var owner = await obj.Object().Owner.WithCancellation(CancellationToken.None);
				if (owner.Object.DBRef == targetObject.DBRef)
				{
					await Mediator.Send(new HaltObjectQueueRequest(obj.Object().DBRef));

					// obj is already AnySharpObject — no secondary GetObjectNodeQuery needed
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
			}

			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.RestartedPlayerAndObjectsFormat), executor, targetObject.Name);
		}
		else
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.RestartedObjectFormat), executor, targetObject.Name);
		}

		// Trigger @STARTUP attribute if it exists (never inherited per PennMUSH spec)
		try
		{
			await AttributeService.EvaluateAttributeFunctionAsync(
				parser, executor, target, "STARTUP",
				new Dictionary<string, CallState>(),
				evalParent: false);
		}
		catch
		{
			// Ignore @STARTUP errors - they're non-fatal
		}

		return CallState.Empty;
	}

	/// <summary>
	/// Line-for-line the shape of PennMUSH's <c>do_version</c> (src/version.c): the game's name, the
	/// address <em>only when one is configured</em>, the restart time, then the version banner — which is
	/// the very string <c>version()</c> returns, exactly as PennMUSH's <c>fun_version</c> and
	/// <c>do_version</c> both format from VERSION/PATCHLEVEL/PATCHDATE.
	/// </summary>
	[SharpCommand(Name = "@VERSION", Switches = [], Behavior = CB.Default, MinArgs = 0, MaxArgs = 0, ParameterNames = [])]
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

		return new CallState(result);
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

		var name = parser.CurrentState.Arguments.GetValueOrDefault("0")?.Message?.ToPlainText().Trim() ?? "";
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
		if (owner.Object().DBRef != executor.Object().DBRef
				&& !await executor.IsPriv() && !await executor.HasPower("SEARCH"))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.StatsNeedSearchWarrant), executor);
			return new CallState(ErrorMessages.Returns.PermissionDenied);
		}

		return await ObjectStatsAsync(executor, owner.Object().DBRef);
	}

	/// <summary>
	/// <c>do_stats</c>: the object count by type, over the world or over one owner's objects. SharpMUSH
	/// removes a destroyed object rather than keeping it as garbage, so there is no garbage figure and
	/// no "next object" line.
	/// </summary>
	private async ValueTask<Option<CallState>> ObjectStatsAsync(AnySharpObject executor, DBRef? owner)
	{
		var countsByType = await Mediator.CreateStream(new GetFilteredObjectsQuery(new ObjectSearchFilter { Owner = owner }))
			.CountBy(o => o.Type)
			.ToDictionaryAsync(x => x.Key, x => x.Value);
		var rooms = countsByType.GetValueOrDefault("ROOM");
		var exits = countsByType.GetValueOrDefault("EXIT");
		var things = countsByType.GetValueOrDefault("THING");
		var players = countsByType.GetValueOrDefault("PLAYER");
		var total = rooms + exits + things + players;

		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.StatsObjectCountsFormat), executor,
			total, rooms, exits, things, players);
		return new CallState($"{total} {rooms} {exits} {things} {players}");
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
			await Tally(flagSets, obj.Flags.Value.Select(f => f.Name));
			await Tally(powerSets, obj.Powers.Value.Select(p => p.Name));
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

	[SharpCommand(Name = "@CONFIG", Switches = ["SET", "SAVE", "LOWERCASE", "LIST"], Behavior = CB.Default | CB.EqSplit,
		MinArgs = 0, MaxArgs = 2, ParameterNames = ["option", "value"])]
	public async ValueTask<Option<CallState>> Config(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var args = parser.CurrentState.Arguments;
		var switches = parser.CurrentState.Switches.ToArray();
		var useLowercase = switches.Contains("LOWERCASE");

		var allCategories = ConfigGenerated.ConfigAccessor.Categories.ToList();

		IEnumerable<(string Category, string PropertyName, SharpConfigAttribute ConfigAttr, object? Value)> getAllOptions() =>
			ConfigGenerated.ConfigMetadata.PropertyToAttributeName.Keys
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
			if (!await executor.IsWizard() || (save && !executor.IsGod()))
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ConfigCantRemakeWorld), executor);
				return new CallState(ErrorMessages.Returns.PermissionDenied);
			}

			var optionName = args.GetValueOrDefault("0")?.Message?.ToPlainText().Trim() ?? "";
			if (optionName.Length == 0)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ConfigWhatToSet), executor);
				return new CallState(ErrorMessages.Returns.InvalidArguments);
			}

			return await SetConfigOptionAsync(parser, executor, optionName, args.GetValueOrDefault("1")?.Message?.ToPlainText(), save);
		}

		if (args.Count == 0)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ConfigCategoriesHeader), executor);
			foreach (var cat in allCategories)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ConfigCategoryItemFormat), executor, cat);
			}
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ConfigUseCategoryHelp), executor);
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ConfigUseOptionHelp), executor);
			return CallState.Empty;
		}

		var searchTerm = args.GetValueOrDefault("0")?.Message?.ToPlainText() ?? "";

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

			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ConfigOptionsInCategoryFormat), executor, matchingCategory);
			foreach (var opt in categoryOptions)
			{
				var name = useLowercase ? opt.ConfigAttr.Name.ToLower() : opt.ConfigAttr.Name;
				var value = opt.Value?.ToString() ?? "null";
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ConfigOptionValueFormat), executor, name, value);
			}
			return CallState.Empty;
		}

		var allOptions = getAllOptions();
		var matchingOption = allOptions.FirstOrDefault(opt =>
			opt.ConfigAttr.Name.Equals(searchTerm, StringComparison.OrdinalIgnoreCase));

		if (matchingOption.PropertyName != null)
		{
			var name = useLowercase ? matchingOption.ConfigAttr.Name.ToLower() : matchingOption.ConfigAttr.Name;
			var value = matchingOption.Value?.ToString() ?? "null";
			var desc = matchingOption.ConfigAttr.Description;

			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ConfigOptionValueFormat), executor, name, value);
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ConfigOptionDescriptionFormat), executor, desc);
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ConfigOptionCategoryFormat), executor, matchingOption.Category);
			return new CallState(value);
		}

		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ConfigNoCategoryOrOptionFormat), executor, searchTerm);
		return new CallState(ErrorMessages.Returns.NotFound);
	}

	[SharpCommand(Name = "@SLAVE", Switches = ["RESTART"], Behavior = CB.Default, CommandLock = "FLAG^WIZARD",
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
