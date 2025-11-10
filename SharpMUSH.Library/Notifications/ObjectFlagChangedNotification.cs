using Mediator;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Notifications;

/// <summary>
/// Notification published when a flag or power is set or cleared on an object.
/// </summary>
public record ObjectFlagChangedNotification(
	AnySharpObject Target,
	string FlagName,
	string Type, // "FLAG" or "POWER"
	bool IsSet, // true if being set, false if being cleared
	DBRef? Enactor) : INotification;
