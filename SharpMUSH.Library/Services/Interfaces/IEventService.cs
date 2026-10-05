using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>
/// Service for triggering PennMUSH-compatible events.
/// <para>
/// Events allow administrators to designate an object as an event handler 
/// (using the "event_handler" config option) that receives notifications 
/// when specific system events occur. The event handler object should have
/// attributes matching event names (e.g., PLAYER`CONNECT, SOCKET`DISCONNECT).
/// </para>
/// <para>
/// To configure the event system:
/// <code>
/// @create Event Handler
/// @config/set event_handler=[num(Event Handler)]
/// &amp;PLAYER`CONNECT Event Handler=@pemit %#=Welcome, [name(%0)]!
/// </code>
/// </para>
/// </summary>
public interface IEventService
{
	/// <summary>
	/// Queues the event: the event handler's attribute named <paramref name="eventName"/> becomes a queue
	/// entry of its own, run by the handler (PennMUSH <c>queue_event</c>). The caller does not wait for it
	/// to run and does not share its time limit.
	/// <para>
	/// If no event handler is configured or the attribute doesn't exist, nothing is queued. Failures are
	/// logged; cancellation of the current execution lifetime propagates.
	/// </para>
	/// </summary>
	/// <param name="eventName">The name of the event using PennMUSH format (e.g., "PLAYER`CONNECT", "SOCKET`LOGINFAIL")</param>
	/// <param name="enactor">The object that caused the event, or null for system events (which use God, #1)</param>
	/// <param name="args">Arguments to pass to the event handler as %0, %1, %2, etc.</param>
	ValueTask TriggerEventAsync(string eventName, DBRef? enactor, params string[] args);
}
