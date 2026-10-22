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
/// PennMUSH's inactivity check (src/timer.c:253, <c>sq_register_loop(60, idle_event, ...)</c>): every minute, players
/// idle past <c>idle_timeout</c> are disconnected or hidden, sockets at the login screen idle past
/// <c>unconnected_idle_timeout</c> are dropped, and <c>PLAYER`INACTIVITY</c> is raised.
/// </summary>
public sealed class InactivityCheckService(
	IOptionsWrapper<SharpMUSHOptions> options,
	IEventService events,
	IConnectionService connections,
	INotifyService notify,
	IPermissionService permissions,
	IMediator mediator,
	IMessageBus messageBus,
	TimeProvider time,
	ILogger<InactivityCheckService> logger) : BackgroundService
{
	/// <summary>How often the check runs.</summary>
	public static readonly TimeSpan Interval = TimeSpan.FromSeconds(60);

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		using var timer = new PeriodicTimer(Interval, time);
		try
		{
			while (await timer.WaitForNextTickAsync(stoppingToken))
			{
				try
				{
					var limits = options.CurrentValue.Limit;
					await InactivityCheckAsync(limits.IdleTimeout, limits.UnconnectedIdleTimeout);
				}
				catch (Exception ex) when (ex is not OperationCanceledException)
				{
					logger.LogError(ex, "Error running the inactivity check");
				}
			}
		}
		catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
		{
			// Shutdown.
		}
	}

	/// <summary>
	/// PennMUSH's <c>inactivity_check</c> (src/bsd.c:7258-7312). A socket at the login screen idle past
	/// <c>unconnected_idle_timeout</c> is dropped without a message. A player idle past <c>idle_timeout</c> is
	/// disconnected unless they may idle (the Idle power, or wizard or royalty); one who may idle, is UNFINDABLE and
	/// may hide is hidden instead. <c>PLAYER`INACTIVITY</c> follows when anyone was dropped, disconnected or hidden.
	/// A portal tab is not a play connection and is never disconnected for idling.
	/// </summary>
	/// <param name="idleTimeout">Seconds; 0 disconnects no player.</param>
	/// <param name="unconnectedIdleTimeout">Seconds; 0 drops no socket at the login screen.</param>
	public async ValueTask InactivityCheckAsync(uint idleTimeout, uint unconnectedIdleTimeout)
	{
		if (idleTimeout == 0 && unconnectedIdleTimeout == 0)
		{
			return;
		}

		var acted = false;
		await foreach (var connection in connections.GetAll())
		{
			if (connection.PresenceClass != PresenceClasses.Play || connection.Idle is not { } idle)
			{
				continue;
			}

			acted |= connection switch
			{
				{ State: IConnectionService.ConnectionState.LoggedIn, Ref: { } playerRef }
					=> IsPast(idle, idleTimeout) && await PlayerIdleAsync(connection, playerRef),
				{ State: IConnectionService.ConnectionState.Connected or IConnectionService.ConnectionState.AccountMode }
					=> IsPast(idle, unconnectedIdleTimeout) && await DropAsync(connection),
				_ => false
			};
		}

		if (acted)
		{
			await events.TriggerEventAsync(SharpEvents.PlayerInactivity, null);
		}
	}

	private static bool IsPast(TimeSpan idle, uint limitSeconds)
		=> limitSeconds != 0 && idle > TimeSpan.FromSeconds(limitSeconds);

	private async ValueTask<bool> PlayerIdleAsync(IConnectionService.ConnectionData connection, DBRef playerRef)
		=> await mediator.Send(new GetObjectNodeQuery(playerRef)) is AnySharpObject player
			&& (await permissions.CanIdle(player)
				? await HideAsync(connection, player)
				: await DisconnectAsync(connection, player));

	/// <summary>Penn's <c>shutdownsock(d, "idle", ...)</c>: no message, the socket just closes.</summary>
	private async ValueTask<bool> DropAsync(IConnectionService.ConnectionData connection)
	{
		logger.LogInformation("Dropping descriptor {Handle}: idle at the login screen past unconnected_idle_timeout",
			connection.Handle);
		await connections.Disconnect(connection.Handle);
		await messageBus.Publish(new DisconnectConnectionMessage(connection.Handle, "idle"));
		return true;
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
