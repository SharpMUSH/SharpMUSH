using Microsoft.Extensions.Logging;
using SharpMUSH.Library.Models.SchedulerModels;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Services;

/// <summary>
/// Holds the events and attribute hooks queued by a piece of engine bookkeeping until that bookkeeping
/// is done, so none of them can run while it is half-finished.
/// </summary>
/// <remarks>
/// PennMUSH is single-threaded: what <c>announce_disconnect</c> queues cannot run before it has written
/// <c>LASTLOGOUT</c> (<c>src/bsd.c:6020-6162</c>). Here a disconnect can be handled outside the queue's
/// consumer — a socket closing, not a <c>QUIT</c> — and the consumer would start an entry the moment it
/// is admitted. Inside a hold, <see cref="AdmitAsync"/> reserves each entry (counted, not runnable) and
/// the hold publishes them, in order, when it closes. Outside a hold it admits as usual. Holds do not
/// nest: an inner one joins the outer, which publishes everything.
/// </remarks>
public sealed class QueueHold : IAsyncDisposable
{
	private static readonly AsyncLocal<QueueHold?> Ambient = new();
	private readonly List<QueueCommandReservation> _held = [];
	private readonly ILogger? _logger;

	private QueueHold(ILogger? logger) => _logger = logger;

	/// <summary>Opens a hold, or joins the one already open.</summary>
	public static IAsyncDisposable Enter(ILogger? logger = null)
	{
		if (Ambient.Value is not null) return Joined.Instance;
		var hold = new QueueHold(logger);
		Ambient.Value = hold;
		return hold;
	}

	/// <summary>Queues <paramref name="command"/>: reserved until the open hold closes, admitted now when there is none.</summary>
	public static async ValueTask<QueueAdmissionResult> AdmitAsync(ITaskScheduler scheduler, MString command, ParserState state)
	{
		if (Ambient.Value is not { } hold) return await scheduler.AdmitCommandList(command, state);
		var reservation = await scheduler.ReserveCommandList(command, state);
		if (reservation.Admission.Accepted)
		{
			lock (hold._held) hold._held.Add(reservation);
		}
		return reservation.Admission;
	}

	/// <summary>Closes the hold and publishes what it held, in the order it was queued.</summary>
	public ValueTask DisposeAsync()
	{
		// Cleared before the first await, so the caller's context leaves the hold too.
		Ambient.Value = null;
		return PublishAsync();
	}

	private async ValueTask PublishAsync()
	{
		QueueCommandReservation[] held;
		lock (_held) held = [.. _held];
		foreach (var reservation in held)
		{
			// A reservation left unpublished is released when disposed, so a failure cannot strand it.
			using (reservation)
			{
				try
				{
					var published = await reservation.PublishAsync();
					if (!published.Accepted)
						_logger?.LogWarning("Held queue entry {Pid} was not published: {Reason}", reservation.Admission.Pid, published.Reason);
				}
				catch (Exception ex) when (ex is not OperationCanceledException)
				{
					// One entry that cannot be published does not keep the rest of the hold from running.
					_logger?.LogError(ex, "Could not publish held queue entry {Pid}", reservation.Admission.Pid);
				}
			}
		}
	}

	private sealed class Joined : IAsyncDisposable
	{
		public static readonly Joined Instance = new();
		public ValueTask DisposeAsync() => ValueTask.CompletedTask;
	}
}
