using OneOf;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using static SharpMUSH.Library.Services.Interfaces.INotifyService;

namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>
/// Service for routing notifications to listening objects.
/// Handles @listen attributes, ^-listen patterns, and puppet relaying.
/// </summary>
public interface IListenerRoutingService
{
	/// <summary>
	/// Process a notification to discover and trigger listening objects.
	/// Called by NotifyService before sending to player connections.
	/// </summary>
	ValueTask ProcessNotificationAsync(
		NotificationContext context,
		OneOf<MString, string> message,
		AnySharpObject? sender,
		NotificationType type);
}

/// <summary>
/// Context information for a notification being routed.
/// </summary>
public record NotificationContext(
	DBRef Target,
	DBRef? Location,
	bool IsRoomBroadcast,
	DBRef[] ExcludedObjects
);
