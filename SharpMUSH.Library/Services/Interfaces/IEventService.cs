using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>
/// Service for triggering PennMUSH-compatible events.
/// Events allow administrators to designate an object as an event handler 
/// (using the "event_handler" config option) that receives notifications 
/// when specific system events occur.
/// </summary>
public interface IEventService
{
	/// <summary>
	/// Triggers an event by executing the corresponding attribute on the event handler object.
	/// </summary>
	/// <param name="parser">The parser context for executing the event code</param>
	/// <param name="eventName">The name of the event (e.g., "PLAYER`CONNECT", "SOCKET`LOGINFAIL")</param>
	/// <param name="enactor">The object that caused the event, or null/#-1 for system events</param>
	/// <param name="args">Arguments to pass to the event handler as %0, %1, %2, etc.</param>
	/// <returns>A task representing the asynchronous operation</returns>
	ValueTask TriggerEventAsync(IMUSHCodeParser parser, string eventName, DBRef? enactor, params string[] args);
}
