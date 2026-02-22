using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;

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
	/// Triggers an event by executing the corresponding attribute on the event handler object.
	/// <para>
	/// If no event handler is configured or the attribute doesn't exist, the method returns
	/// silently. Exceptions during event execution are logged but not propagated.
	/// </para>
	/// </summary>
	/// <param name="parser">The parser context for executing the event code</param>
	/// <param name="eventName">The name of the event using PennMUSH format (e.g., "PLAYER`CONNECT", "SOCKET`LOGINFAIL")</param>
	/// <param name="enactor">The object that caused the event, or null for system events (which use #-1)</param>
	/// <param name="args">Arguments to pass to the event handler as %0, %1, %2, etc.</param>
	/// <returns>A task representing the asynchronous operation</returns>
	ValueTask TriggerEventAsync(IMUSHCodeParser parser, string eventName, DBRef? enactor, params string[] args);
}
