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

	/// <summary>
	/// As <see cref="TriggerEventAsync(string, DBRef?, string[])"/>, with arguments that keep their markup: the
	/// SharpMUSH events whose catalogue entry gives an argument the markup kind pass it this way.
	/// </summary>
	ValueTask TriggerEventAsync(string eventName, DBRef? enactor, IReadOnlyList<MString> args);

	/// <summary>
	/// Whether the configured event handler has its own attribute named <paramref name="eventName"/>, or a
	/// package's child of it (<c>GALLERY`REPORT`JOBS</c>), so a trigger would queue something. A surface that only makes
	/// sense when the game answers an event (the portal's Report button and <c>GALLERY`REPORT</c>), or the server's
	/// own reaction to a signal nobody hears, asks first.
	/// </summary>
	ValueTask<bool> HasHandlerAsync(string eventName);

	/// <summary>
	/// Queues the event handler's attributes that <c>@attribute/watch</c> named for a change to a watched attribute: each
	/// of the handler's own attributes matching a wildcard in <paramref name="pattern"/> becomes a queue entry of its own,
	/// as a non-stoppable event's attribute does, run by the handler with its own permissions. It is not a catalogue
	/// event: no <c>EVENT`</c> attribute hears it, and <c>stopevent()</c> does nothing in it.
	/// <para>
	/// The handlers run one <see cref="AttributeChangeChain"/> level deeper than the code that made the change. Nothing
	/// is queued when no event handler is configured or no attribute matches. Failures are logged; cancellation of the
	/// current execution lifetime propagates.
	/// </para>
	/// </summary>
	/// <param name="pattern">One or more attribute wildcards, separated by spaces (<see cref="AttributeWatch"/>).</param>
	/// <param name="enactor">The object that made the change, or null for the server's own bookkeeping (God, #1).</param>
	/// <param name="args">%0-%4, also given by the names in <see cref="AttributeWatch.ArgumentNames"/>.</param>
	ValueTask QueueAttributeWatchHandlersAsync(string pattern, DBRef? enactor, IReadOnlyList<MString> args);
}
