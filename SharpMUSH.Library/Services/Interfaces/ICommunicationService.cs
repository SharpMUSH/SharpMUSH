using OneOf;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>
/// Service for handling communication functions like pemit, emit, etc.
/// Centralizes the logic for sending messages to players, rooms, and ports.
/// </summary>
public interface ICommunicationService
{
	/// <summary>
	/// Sends a private message to specified port recipients.
	/// </summary>
	/// <param name="executor">The object executing the function</param>
	/// <param name="ports">Array of port numbers</param>
	/// <param name="message">The message to send</param>
	/// <param name="notificationType">The type of notification to send</param>
	/// <returns>Task representing the async operation</returns>
	ValueTask SendToPortsAsync(
		AnySharpObject executor,
		long[] ports,
		OneOf<MString, string> message,
		INotifyService.NotificationType notificationType);

	/// <summary>
	/// Sends a private message to a single object recipient.
	/// </summary>
	/// <param name="parser">The MUSH code parser</param>
	/// <param name="executor">The object executing the function</param>
	/// <param name="recipient">The recipient (DBRef or name)</param>
	/// <param name="message">The message to send</param>
	/// <param name="notificationType">The type of notification to send</param>
	/// <returns>Task representing the async operation</returns>
	ValueTask SendToRecipientAsync(
		IMUSHCodeParser parser,
		AnySharpObject executor,
		OneOf<DBRef, string> recipient,
		OneOf<MString, string> message,
		INotifyService.NotificationType notificationType);
}
