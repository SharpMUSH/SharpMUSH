using Mediator;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Notifications;

/// <summary>
/// Notification published when an object is moved to a new location.
/// </summary>
public record ObjectMovedNotification(
	AnySharpContent Target,
	AnySharpContainer NewLocation,
	DBRef OldLocation,
	DBRef? Enactor,
	bool IsSilent,
	string Cause) : INotification;
