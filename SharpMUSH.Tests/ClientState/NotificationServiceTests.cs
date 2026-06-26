using SharpMUSH.Client.Services;
using SharpMUSH.Library.Models.Portal;

namespace SharpMUSH.Tests.ClientState;

public class NotificationServiceTests
{
	[Test]
	public async Task InitialState_NoNotifications()
	{
		var svc = new NotificationService();
		await Assert.That(svc.GetUnread().Count).IsEqualTo(0);
		await Assert.That(svc.GetUnreadCount()).IsEqualTo(0);
	}

	[Test]
	public async Task AddNotification_IncreasesUnreadCount()
	{
		var svc = new NotificationService();
		svc.AddNotification("Hello", "World", NotificationType.Info);
		await Assert.That(svc.GetUnreadCount()).IsEqualTo(1);
	}

	[Test]
	public async Task AddNotification_FiresOnNotificationsChanged()
	{
		var svc = new NotificationService();
		var fired = false;
		svc.OnNotificationsChanged += () => fired = true;
		svc.AddNotification("Hello", "World", NotificationType.Info);
		await Assert.That(fired).IsTrue();
	}

	[Test]
	public async Task AddNotification_ReturnsInGetUnread()
	{
		var svc = new NotificationService();
		svc.AddNotification("Alert", "You have mail", NotificationType.Mail);
		var unread = svc.GetUnread();
		await Assert.That(unread.Count).IsEqualTo(1);
		await Assert.That(unread[0].Title).IsEqualTo("Alert");
		await Assert.That(unread[0].Message).IsEqualTo("You have mail");
		await Assert.That(unread[0].Type).IsEqualTo(NotificationType.Mail);
	}

	[Test]
	public async Task AddNotification_NotificationIsUnreadByDefault()
	{
		var svc = new NotificationService();
		svc.AddNotification("Hello", "World", NotificationType.Info);
		var unread = svc.GetUnread();
		await Assert.That(unread[0].IsRead).IsFalse();
	}

	[Test]
	public async Task AddNotification_MultipleTypes_AllAppear()
	{
		var svc = new NotificationService();
		svc.AddNotification("Info", "msg", NotificationType.Info);
		svc.AddNotification("Warn", "msg", NotificationType.Warning);
		svc.AddNotification("Mail", "msg", NotificationType.Mail);
		await Assert.That(svc.GetUnreadCount()).IsEqualTo(3);
	}

	[Test]
	public async Task MarkRead_ExistingNotification_DecrementsUnreadCount()
	{
		var svc = new NotificationService();
		svc.AddNotification("Hello", "World", NotificationType.Info);
		var id = svc.GetUnread()[0].Id;
		svc.MarkRead(id);
		await Assert.That(svc.GetUnreadCount()).IsEqualTo(0);
	}

	[Test]
	public async Task MarkRead_RemovesFromGetUnread()
	{
		var svc = new NotificationService();
		svc.AddNotification("Hello", "World", NotificationType.Info);
		var id = svc.GetUnread()[0].Id;
		svc.MarkRead(id);
		await Assert.That(svc.GetUnread().Count).IsEqualTo(0);
	}

	[Test]
	public async Task MarkRead_FiresOnNotificationsChanged()
	{
		var svc = new NotificationService();
		svc.AddNotification("Hello", "World", NotificationType.Info);
		var id = svc.GetUnread()[0].Id;
		var fired = false;
		svc.OnNotificationsChanged += () => fired = true;
		svc.MarkRead(id);
		await Assert.That(fired).IsTrue();
	}

	[Test]
	public async Task MarkRead_NonExistentId_DoesNotThrow()
	{
		var svc = new NotificationService();
		svc.MarkRead(Guid.NewGuid());
		await Assert.That(svc.GetUnreadCount()).IsEqualTo(0);
	}

	[Test]
	public async Task MarkRead_AlreadyRead_IsIdempotent()
	{
		var svc = new NotificationService();
		svc.AddNotification("Hello", "World", NotificationType.Info);
		var id = svc.GetUnread()[0].Id;
		svc.MarkRead(id);
		svc.MarkRead(id);
		await Assert.That(svc.GetUnreadCount()).IsEqualTo(0);
	}

	[Test]
	public async Task MarkRead_OnlyTargetedNotificationIsMarked()
	{
		var svc = new NotificationService();
		svc.AddNotification("First", "msg", NotificationType.Info);
		svc.AddNotification("Second", "msg", NotificationType.Warning);
		var firstId = svc.GetUnread()[0].Id;
		svc.MarkRead(firstId);
		await Assert.That(svc.GetUnreadCount()).IsEqualTo(1);
		await Assert.That(svc.GetUnread()[0].Title).IsEqualTo("Second");
	}

	[Test]
	public async Task ClearAll_RemovesAllNotifications()
	{
		var svc = new NotificationService();
		svc.AddNotification("A", "msg", NotificationType.Info);
		svc.AddNotification("B", "msg", NotificationType.Warning);
		svc.ClearAll();
		await Assert.That(svc.GetUnreadCount()).IsEqualTo(0);
		await Assert.That(svc.GetUnread().Count).IsEqualTo(0);
	}

	[Test]
	public async Task ClearAll_FiresOnNotificationsChanged()
	{
		var svc = new NotificationService();
		svc.AddNotification("A", "msg", NotificationType.Info);
		var fired = false;
		svc.OnNotificationsChanged += () => fired = true;
		svc.ClearAll();
		await Assert.That(fired).IsTrue();
	}

	[Test]
	public async Task ClearAll_WhenEmpty_DoesNotThrow()
	{
		var svc = new NotificationService();
		svc.ClearAll();
		await Assert.That(svc.GetUnreadCount()).IsEqualTo(0);
	}

	[Test]
	public async Task GetUnreadCount_CountsOnlyUnread()
	{
		var svc = new NotificationService();
		svc.AddNotification("A", "msg", NotificationType.Info);
		svc.AddNotification("B", "msg", NotificationType.Info);
		svc.AddNotification("C", "msg", NotificationType.Info);
		var firstId = svc.GetUnread()[0].Id;
		svc.MarkRead(firstId);
		await Assert.That(svc.GetUnreadCount()).IsEqualTo(2);
	}

	[Test]
	public async Task GetUnread_ReturnsNewListEachTime()
	{
		var svc = new NotificationService();
		svc.AddNotification("A", "msg", NotificationType.Info);
		var first = svc.GetUnread();
		svc.AddNotification("B", "msg", NotificationType.Info);
		var second = svc.GetUnread();
		await Assert.That(first.Count).IsEqualTo(1);
		await Assert.That(second.Count).IsEqualTo(2);
	}
}
