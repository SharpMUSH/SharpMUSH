using Mediator;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Messaging.Abstractions;
using SharpMUSH.Messaging.Messages;

namespace SharpMUSH.Server.Services;

/// <summary>
/// PennMUSH's timers whose only trace in a SharpMUSH game is the event they raise, run while
/// <c>compatibility_events</c> is on (src/timer.c): the save every <c>dump_interval</c>, with its warnings five
/// minutes and one minute before; <c>DB`DBCK</c> every <c>dbck_interval</c>; and the inactivity check every
/// minute, which disconnects players idle past <c>idle_timeout</c> and raises <c>PLAYER`INACTIVITY</c>.
/// Each timer starts when the option is turned on, stops when it is turned off, and starts again when its
/// interval is changed.
/// </summary>
public sealed class CompatibilityEventsService(
	IOptionsWrapper<SharpMUSHOptions> options,
	IDumpService dump,
	IEventService events,
	IConnectionService connections,
	INotifyService notify,
	IPermissionService permissions,
	IMediator mediator,
	IMessageBus messageBus,
	TimeProvider time,
	ILogger<CompatibilityEventsService> logger) : BackgroundService
{
	/// <summary>How often the inactivity check runs (src/timer.c:253, <c>sq_register_loop(60, idle_event, ...)</c>).</summary>
	public static readonly TimeSpan InactivityCheckInterval = TimeSpan.FromSeconds(60);

	private static readonly TimeSpan FiveMinutes = TimeSpan.FromMinutes(5);
	private static readonly TimeSpan OneMinute = TimeSpan.FromMinutes(1);

	private DateTimeOffset? _dumpCycleStart;
	private TimeSpan _dumpInterval;
	private bool _warnedFive;
	private bool _warnedOne;
	private DateTimeOffset? _nextDbck;
	private TimeSpan _dbckInterval;
	private DateTimeOffset? _nextInactivityCheck;

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1), time);
		try
		{
			while (await timer.WaitForNextTickAsync(stoppingToken))
			{
				try
				{
					await TickAsync(time.GetUtcNow());
				}
				catch (Exception ex) when (ex is not OperationCanceledException)
				{
					logger.LogError(ex, "Error running the compatibility event timers");
				}
			}
		}
		catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
		{
		}
	}

	/// <summary>Runs whichever timers are due at <paramref name="now"/>.</summary>
	public async ValueTask TickAsync(DateTimeOffset now)
	{
		var current = options.CurrentValue;
		if (!current.Compatibility.CompatibilityEvents)
		{
			_dumpCycleStart = null;
			_nextDbck = null;
			_nextInactivityCheck = null;
			return;
		}

		await DumpTimerAsync(now, ScheduledTaskManagementService.ParseTimeInterval(current.Dump.DumpInterval));
		await DbckTimerAsync(now, ScheduledTaskManagementService.ParseTimeInterval(current.Dump.DbckInterval));

		_nextInactivityCheck ??= now + InactivityCheckInterval;
		if (now >= _nextInactivityCheck)
		{
			_nextInactivityCheck = now + InactivityCheckInterval;
			await InactivityCheckAsync(current.Limit.IdleTimeout);
		}
	}

	/// <summary>
	/// src/timer.c:194-240: the warnings at <c>dump_interval</c> less five minutes and less one minute (each only
	/// when the interval is longer), then the save, and the next cycle starts.
	/// </summary>
	private async ValueTask DumpTimerAsync(DateTimeOffset now, TimeSpan interval)
	{
		if (interval <= TimeSpan.Zero)
		{
			_dumpCycleStart = null;
			return;
		}

		if (_dumpCycleStart is not { } start || interval != _dumpInterval)
		{
			_dumpInterval = interval;
			StartDumpCycle(now);
			return;
		}

		var due = start + interval;
		if (!_warnedFive && interval > FiveMinutes && now >= due - FiveMinutes)
		{
			_warnedFive = true;
			await dump.WarnAsync(5);
		}

		if (!_warnedOne && interval > OneMinute && now >= due - OneMinute)
		{
			_warnedOne = true;
			await dump.WarnAsync(1);
		}

		if (now >= due)
		{
			StartDumpCycle(now);
			await dump.DumpAsync(null);
		}
	}

	private void StartDumpCycle(DateTimeOffset now)
	{
		_dumpCycleStart = now;
		_warnedFive = false;
		_warnedOne = false;
	}

	/// <summary>
	/// src/timer.c:169-177: <c>DB`DBCK</c> after each scheduled check. SharpMUSH keeps its links consistent as it
	/// writes them, so the check itself finds nothing to repair.
	/// </summary>
	private async ValueTask DbckTimerAsync(DateTimeOffset now, TimeSpan interval)
	{
		if (interval <= TimeSpan.Zero)
		{
			_nextDbck = null;
			return;
		}

		if (_nextDbck is null || interval != _dbckInterval)
		{
			_dbckInterval = interval;
			_nextDbck = now + interval;
			return;
		}

		if (now >= _nextDbck)
		{
			_nextDbck = now + interval;
			await events.TriggerEventAsync(SharpEvents.DbDbck, null);
		}
	}

	/// <summary>
	/// PennMUSH's <c>inactivity_check</c> (src/bsd.c:7258-7312) for players: one idle past <c>idle_timeout</c> is
	/// disconnected unless they may idle (the Idle power, or wizard or royalty); one who may idle, is UNFINDABLE and
	/// may hide is hidden instead. <c>PLAYER`INACTIVITY</c> follows when anyone was disconnected or hidden. A
	/// portal tab is not a play connection and is never disconnected for idling.
	/// </summary>
	/// <param name="idleTimeout">Seconds; 0 disconnects nobody.</param>
	public async ValueTask InactivityCheckAsync(uint idleTimeout)
	{
		if (idleTimeout == 0)
		{
			return;
		}

		var limit = TimeSpan.FromSeconds(idleTimeout);
		var acted = false;
		await foreach (var connection in connections.GetAll())
		{
			if (connection is not { State: IConnectionService.ConnectionState.LoggedIn, Ref: { } playerRef }
				|| connection.PresenceClass != PresenceClasses.Play
				|| connection.Idle is not { } idle
				|| idle <= limit)
			{
				continue;
			}

			if (await mediator.Send(new GetObjectNodeQuery(playerRef)) is not AnySharpObject player)
			{
				continue;
			}

			acted |= await permissions.CanIdle(player)
				? await HideAsync(connection, player)
				: await DisconnectAsync(connection, player);
		}

		if (acted)
		{
			await events.TriggerEventAsync(SharpEvents.PlayerInactivity, null);
		}
	}

	private async ValueTask<bool> DisconnectAsync(IConnectionService.ConnectionData connection, AnySharpObject player)
	{
		logger.LogInformation("Disconnecting {Player} (descriptor {Handle}): idle past idle_timeout",
			player.Object().Name, connection.Handle);
		await notify.NotifyLocalized(connection.Handle, nameof(ErrorMessages.Notifications.InactivityTimeout), null);
		await connections.Disconnect(connection.Handle);
		await messageBus.Publish(new DisconnectConnectionMessage(connection.Handle, "idle"));
		return true;
	}

	private async ValueTask<bool> HideAsync(IConnectionService.ConnectionData connection, AnySharpObject player)
	{
		if (connection.IsHidden || !await player.HasFlag("UNFINDABLE") || !await permissions.CanHide(player))
		{
			return false;
		}

		await notify.NotifyLocalized(connection.Handle, nameof(ErrorMessages.Notifications.InactivityHidden), null);
		connections.Update(connection.Handle, "Hidden", "1");
		return true;
	}
}
