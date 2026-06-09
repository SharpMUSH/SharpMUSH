using SharpMUSH.Library.Models.Portal;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Client.Services;

/// <summary>
/// In-memory notification store.  Thread-safe for single-circuit Blazor WASM
/// (all calls are on the browser's UI thread).
/// </summary>
public sealed class NotificationService : INotificationService
{
	private readonly List<PortalNotification> _items = [];

	public event Action? OnNotificationsChanged;

	/// <inheritdoc/>
	public void AddNotification(string title, string message, NotificationType type)
	{
		var notification = new PortalNotification(
			Id: Guid.NewGuid(),
			Title: title,
			Message: message,
			Type: type,
			Timestamp: DateTimeOffset.UtcNow,
			IsRead: false);

		_items.Add(notification);
		OnNotificationsChanged?.Invoke();
	}

	/// <inheritdoc/>
	public IReadOnlyList<PortalNotification> GetUnread()
		=> _items.Where(n => !n.IsRead).ToList();

	/// <inheritdoc/>
	public void MarkRead(Guid id)
	{
		var idx = _items.FindIndex(n => n.Id == id);
		if (idx < 0) return;

		_items[idx] = _items[idx] with { IsRead = true };
		OnNotificationsChanged?.Invoke();
	}

	/// <inheritdoc/>
	public void ClearAll()
	{
		_items.Clear();
		OnNotificationsChanged?.Invoke();
	}

	/// <inheritdoc/>
	public int GetUnreadCount() => _items.Count(n => !n.IsRead);
}
