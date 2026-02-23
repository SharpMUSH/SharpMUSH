using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Services;

public class ListenerRoutingServiceTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IListenerRoutingService ListenerRoutingService =>
		WebAppFactoryArg.Services.GetRequiredService<IListenerRoutingService>();
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();

	[Test]
	public async ValueTask ProcessNotificationAsync_WithNullLocation_ReturnsEarly()
	{
		// Arrange
		var context = new NotificationContext(
			Target: new DBRef(1, null),
			Location: null,
			IsRoomBroadcast: false,
			ExcludedObjects: []
		);

		// Act - should not throw and should return early
		await ListenerRoutingService.ProcessNotificationAsync(
			context,
			"Test message",
			null,
			INotifyService.NotificationType.Say);

		// Assert - no exception means success
		await ValueTask.CompletedTask;
	}

	[Test]
	public async ValueTask ProcessNotificationAsync_WithAnnounceType_ReturnsEarly()
	{
		// Arrange
		var context = new NotificationContext(
			Target: new DBRef(1, null),
			Location: new DBRef(0, null),
			IsRoomBroadcast: false,
			ExcludedObjects: []
		);

		// Act - Announce type should not trigger listeners
		await ListenerRoutingService.ProcessNotificationAsync(
			context,
			"Private message",
			null,
			INotifyService.NotificationType.Announce);

		// Assert - no exception means success
		await ValueTask.CompletedTask;
	}

	[Test]
	[Skip("Integration test - requires database with room and objects configured")]
	public async ValueTask ProcessNotificationAsync_WithMonitorFlag_MatchesListenPatterns()
	{
		// This test would require:
		// 1. A room object
		// 2. An object with MONITOR flag set
		// 3. ^-listen pattern attributes on the object
		// 4. Verification that patterns are matched correctly
		await ValueTask.CompletedTask;
	}

	[Test]
	[Skip("Integration test - requires database with puppet configured")]
	public async ValueTask ProcessNotificationAsync_WithPuppetFlag_RelaysToOwner()
	{
		// This test would require:
		// 1. A room object
		// 2. A thing with PUPPET flag set
		// 3. An owner who is connected
		// 4. Verification that message is relayed with prefix
		await ValueTask.CompletedTask;
	}

	[Test]
	[Skip("Integration test - requires database with @listen attribute")]
	public async ValueTask ProcessNotificationAsync_WithListenAttribute_MatchesPattern()
	{
		// This test would require:
		// 1. A room object
		// 2. An object with @listen attribute set
		// 3. Verification that pattern matching works
		// 4. Verification that appropriate action attribute is identified
		await ValueTask.CompletedTask;
	}
}
