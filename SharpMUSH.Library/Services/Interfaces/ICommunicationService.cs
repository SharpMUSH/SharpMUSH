using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>
/// Service for handling communication functions like pemit, emit, etc.
/// Centralizes the logic for sending messages to players, rooms, and ports.
/// </summary>
/// <summary>
/// Why a message could not be delivered to a target: the name did not resolve, or the target declined
/// to hear from the executor. Distinguishes "nobody received this" from a successful delivery without
/// resorting to a nullable service return.
/// </summary>
public readonly record struct DeliveryFailure(DeliveryFailure.Cause Reason)
{
	public enum Cause
	{
		TargetNotFound,
		TargetWillNotHear
	}
}

public interface ICommunicationService
{
	/// <summary>
	/// Delivers private/prompt, immediate, outermost, named-location, omission or zone output.
	/// The executor owns permissions and targeting; an authorized Spoof option selects the enactor
	/// as speaker for Speech locks and hearing. NoSpoof independently suppresses recipient tagging.
	/// Explicit Speech/Page refusals use FailLock; denied fanout locations are filtered.
	/// </summary>
	/// <returns>An empty successful command result after processing the recipients. Delivery refusals
	/// are notified through the normal lock/locate paths. Wrappers retain argument-evaluation errors.</returns>
	ValueTask<CallState> EmitAsync(IMUSHCodeParser parser, EmitRequest request);

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
		Func<AnySharpObject, SharpMessage> messageFunc,
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
	/// <param name="interact">
	/// Which interaction gate each candidate recipient must pass. PennMUSH's <c>notify_except2</c>
	/// takes this per message: movement leave/enter messages are <c>NA_INTER_PRESENCE</c>, the
	/// OX-prefixed and zone messages are <c>NA_INTER_SEE</c>, and speech is <c>NA_INTER_HEAR</c>.
	/// </param>
	/// <returns>Task representing the async operation</returns>
	ValueTask SendToRoomAsync(
		AnySharpObject executor,
		AnySharpContainer room,
		Func<AnySharpObject, SharpMessage> messageFunc,
		INotifyService.NotificationType notificationType,
		AnySharpObject? sender = null,
		IEnumerable<AnySharpObject>? excludeObjects = null,
		IPermissionService.InteractType interact = IPermissionService.InteractType.Hear);

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
	/// <returns>
	/// The object that received the message, or <see cref="DeliveryFailure"/> when it could not be
	/// delivered — the target could not be located, or it declined to hear from the executor.
	/// </returns>
	ValueTask<DeliveryResult> SendToObjectAsync(
		IMUSHCodeParser parser,
		AnySharpObject executor,
		AnySharpObject enactor,
		string targetName,
		Func<AnySharpObject, SharpMessage> messageFunc,
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
	/// <returns>The objects that actually received the message, in the order they were notified.</returns>
	ValueTask<IReadOnlyList<AnySharpObject>> SendToMultipleObjectsAsync(
		IMUSHCodeParser parser,
		AnySharpObject executor,
		AnySharpObject enactor,
		IAsyncEnumerable<DbRefOrName> targets,
		Func<AnySharpObject, SharpMessage> messageFunc,
		INotifyService.NotificationType notificationType,
		bool notifyOnPermissionFailure = true);
}
