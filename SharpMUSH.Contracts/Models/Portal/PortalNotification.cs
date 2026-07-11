namespace SharpMUSH.Library.Models.Portal;

/// <summary>
/// An in-portal notification that appears in the notification bell / tray.
/// Immutable; created via <see cref="Services.Interfaces.INotificationService.AddNotification"/>.
/// </summary>
public record PortalNotification(
	Guid Id,
	string Title,
	string Message,
	NotificationType Type,
	DateTimeOffset Timestamp,
	bool IsRead);
