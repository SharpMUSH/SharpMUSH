using SharpMUSH.Library.Models.Portal;

namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>
/// In-memory notification store for the web portal.
/// Manages the notification tray (bell icon) — add, read, clear.
/// In v1 there is no persistence; notifications are lost on page reload.
/// </summary>
public interface INotificationService
{
	/// <summary>Raised whenever the notification list changes (add, mark-read, clear).</summary>
	event Action? OnNotificationsChanged;

	/// <summary>Creates and stores a new notification.  Fires <see cref="OnNotificationsChanged"/>.</summary>
	void AddNotification(string title, string message, NotificationType type);

	/// <summary>Returns all stored notifications in insertion order.</summary>
	IReadOnlyList<PortalNotification> GetUnread();

	/// <summary>
	/// Marks a single notification as read by its <see cref="PortalNotification.Id"/>.
	/// Fires <see cref="OnNotificationsChanged"/> only if the id was found.
	/// </summary>
	void MarkRead(Guid id);

	/// <summary>Removes all notifications.  Fires <see cref="OnNotificationsChanged"/>.</summary>
	void ClearAll();

	/// <summary>Count of notifications where <see cref="PortalNotification.IsRead"/> is false.</summary>
	int GetUnreadCount();
}
