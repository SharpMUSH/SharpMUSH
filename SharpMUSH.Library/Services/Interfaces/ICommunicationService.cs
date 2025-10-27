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
	/// Performs permission checks for ports with associated DBRef.
	/// </summary>
	/// <param name="executor">The object executing the function</param>
	/// <param name="ports">Array of port numbers</param>
	/// <param name="messageFunc">Function that takes the target and base message, returns the final message to send</param>
	/// <param name="notificationType">The type of notification to send</param>
	/// <returns>Task representing the async operation</returns>
	ValueTask SendToPortsAsync(
		AnySharpObject executor,
		long[] ports,
		Func<AnySharpObject, OneOf<MString, string>> messageFunc,
		INotifyService.NotificationType notificationType);

	/// <summary>
	/// Sends a message to all contents of a room.
	/// Filters recipients by CanInteract permission.
	/// </summary>
	/// <param name="executor">The object executing the function</param>
	/// <param name="room">The room whose contents will receive the message</param>
	/// <param name="messageFunc">Function that takes the target and base message, returns the final message to send</param>
	/// <param name="notificationType">The type of notification to send</param>
	/// <param name="sender">The object shown as the sender (for spoof support)</param>
	/// <param name="excludeObjects">Optional list of objects to exclude from receiving the message</param>
	/// <returns>Task representing the async operation</returns>
	ValueTask SendToRoomAsync(
		AnySharpObject executor,
		AnySharpContainer room,
		Func<AnySharpObject, OneOf<MString, string>> messageFunc,
		INotifyService.NotificationType notificationType,
		AnySharpObject? sender = null,
		IEnumerable<AnySharpObject>? excludeObjects = null);

	/// <summary>
	/// Sends a message to a single object after locating it and checking permissions.
	/// </summary>
	/// <param name="parser">The parser for locate and notify operations</param>
	/// <param name="executor">The object executing the function</param>
	/// <param name="enactor">The enactor for locate operations</param>
	/// <param name="targetName">The name or DBRef of the target</param>
	/// <param name="messageFunc">Function that takes the target and base message, returns the final message to send</param>
	/// <param name="notificationType">The type of notification to send</param>
	/// <param name="notifyOnPermissionFailure">Whether to notify executor if permission check fails</param>
	/// <returns>True if message was sent successfully, false otherwise</returns>
	ValueTask<bool> SendToObjectAsync(
		IMUSHCodeParser parser,
		AnySharpObject executor,
		AnySharpObject enactor,
		string targetName,
		Func<AnySharpObject, OneOf<MString, string>> messageFunc,
		INotifyService.NotificationType notificationType,
		bool notifyOnPermissionFailure = true);

	/// <summary>
	/// Sends a message to multiple objects specified by names or DBRefs.
	/// </summary>
	/// <param name="parser">The parser for locate and notify operations</param>
	/// <param name="executor">The object executing the function</param>
	/// <param name="enactor">The enactor for locate operations</param>
	/// <param name="targets">List of target names or DBRefs</param>
	/// <param name="messageFunc">Function that takes the target and base message, returns the final message to send</param>
	/// <param name="notificationType">The type of notification to send</param>
	/// <param name="notifyOnPermissionFailure">Whether to notify executor if permission check fails</param>
	/// <returns>Task representing the async operation</returns>
	ValueTask SendToMultipleObjectsAsync(
		IMUSHCodeParser parser,
		AnySharpObject executor,
		AnySharpObject enactor,
		IEnumerable<OneOf<DBRef, string>> targets,
		Func<AnySharpObject, OneOf<MString, string>> messageFunc,
		INotifyService.NotificationType notificationType,
		bool notifyOnPermissionFailure = true);
}
