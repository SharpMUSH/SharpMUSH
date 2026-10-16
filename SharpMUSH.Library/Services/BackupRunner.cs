using Microsoft.Extensions.Logging;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Services;

/// <inheritdoc />
public class BackupRunner(
	IWorldBackupService backups,
	IEventService events,
	IGameBroadcastService broadcast,
	IOptionsWrapper<SharpMUSHOptions> options,
	ILogger<BackupRunner> logger) : IBackupRunner
{
	/// <summary>PennMUSH's own text for a failed save (src/game.c:344), shown to wizards and royalty.</summary>
	public const string SaveFailed = "GAME: ERROR! Database save failed!";

	/// <summary>The dump events' forking argument: a backup never forks.</summary>
	private const string NoFork = "0";

	/// <inheritdoc />
	public async ValueTask WarnAsync(int minutes)
	{
		var dump = options.CurrentValue.Dump;

		// src/timer.c:209 dbsave_warn_event: the event, then the message to everyone.
		if (minutes == 5)
		{
			await events.TriggerEventAsync(SharpEvents.Dump5Min, null, dump.DumpWarning5Min ?? "", NoFork);
			await ShowAsync(dump.DumpWarning5Min);
		}
		else
		{
			await events.TriggerEventAsync(SharpEvents.Dump1Min, null, dump.DumpWarning1Min ?? "", NoFork);
			await ShowAsync(dump.DumpWarning1Min);
		}
	}

	/// <inheritdoc />
	public ValueTask<Result<WorldBackup>> RunAsync(DBRef? enactor, CancellationToken cancellationToken = default)
		=> RunAsync(enactor, new BackupProvenance(enactor is null ? "automatic safeguard" : "manual administrator request", enactor?.ToString()), cancellationToken);

	/// <inheritdoc />
	public async ValueTask<Result<WorldBackup>> RunAsync(DBRef? enactor, BackupProvenance provenance, CancellationToken cancellationToken = default)
	{
		var dump = options.CurrentValue.Dump;
		await ShowAsync(dump.DumpMessage);

		return await backups.CreateAsync(provenance, cancellationToken) switch
		{
			WorldBackup written => await WrittenAsync(enactor, dump.DumpComplete, written),
			Error<string> error => await FailedAsync(enactor, error)
		};
	}

	/// <summary>src/game.c:645-651: <c>DUMP`COMPLETE</c> (message, forking), then the message to everyone.</summary>
	private async ValueTask<Result<WorldBackup>> WrittenAsync(DBRef? enactor, string? message, WorldBackup written)
	{
		await events.TriggerEventAsync(SharpEvents.BackupComplete, enactor, written.Name, written.SizeBytes.ToString());
		await events.TriggerEventAsync(SharpEvents.DumpComplete, enactor, message ?? "", NoFork);
		await ShowAsync(message);
		return written;
	}

	/// <summary>src/game.c:342-346: <c>DUMP`ERROR</c> (message, forking, <c>PERROR</c> reason), then wizards and royalty are told.</summary>
	private async ValueTask<Result<WorldBackup>> FailedAsync(DBRef? enactor, Error<string> error)
	{
		logger.LogError("World backup failed: {Reason}", error.Value);
		await events.TriggerEventAsync(SharpEvents.BackupError, enactor, error.Value);
		await events.TriggerEventAsync(SharpEvents.DumpError, enactor, SaveFailed, NoFork, $"PERROR {error.Value}");
		await broadcast.BroadcastToFlagAsync(["WIZARD", "ROYALTY"], "", SaveFailed);
		return error;
	}

	private async ValueTask ShowAsync(string? message)
	{
		if (!string.IsNullOrEmpty(message))
		{
			await broadcast.BroadcastAsync(message);
		}
	}
}
