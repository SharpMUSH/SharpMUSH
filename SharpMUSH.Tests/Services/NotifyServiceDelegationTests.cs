using NSubstitute;
using OneOf;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Services;

namespace SharpMUSH.Tests.Services;

[NotInParallel]
public class NotifyServiceDelegationTests
{
	[Test]
	public async Task NotifyService_Delegates_To_IMessageQueueNotifyService()
	{
		// Arrange
		var inner = Substitute.For<IMessageQueueNotifyService>();
		INotifyService sut = new NotifyService(inner);
		var who = new DBRef(1);
		OneOf<MString, string> what = "hello";

		// Act
		await sut.Notify(who, what, sender: null);

		// Assert
		await inner.Received(1).Notify(who, what, null, INotifyService.NotificationType.Announce);
	}
}
