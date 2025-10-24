using OneOf;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>
/// Service for handling communication functions like pemit, emit, etc.
/// Centralizes the logic for sending messages to players, rooms, and ports.
/// </summary>
public interface ICommunicationService
{
	/// <summary>
	/// Sends a private message to specified port recipients.
	/// Performs permission checks for ports with associated DBRef.
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
}
