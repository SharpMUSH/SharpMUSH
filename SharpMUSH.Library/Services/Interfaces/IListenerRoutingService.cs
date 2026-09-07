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
	/// Runs the listener pass for the object a notification is addressed to: its ^-patterns, its
	/// LISTEN attribute, and its puppet relay. Called by NotifyService once per notification, before
	/// the message reaches that object's connections.
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
/// <param name="Target">The object the notification is addressed to, and the only one the pass weighs.</param>
/// <param name="Location">Where it happened. Stands in for the speaker when there is no sender.</param>
/// <param name="ExcludedObjects">Objects the caller has already decided are not to hear this.</param>
public record NotificationContext(
	DBRef Target,
	DBRef? Location,
	DBRef[] ExcludedObjects
);
