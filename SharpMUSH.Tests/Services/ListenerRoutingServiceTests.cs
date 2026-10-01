using Mediator;
using NSubstitute;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Messaging.Abstractions;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// The guards at the top of <see cref="ListenerRoutingService.ProcessNotificationAsync"/>: a notification
/// that cannot have a listener is dropped before anything is looked up. Every collaborator is a substitute,
/// so "returns early" is checked as "touched none of them".
/// </summary>
public class ListenerRoutingServiceTests
{
	private readonly IMediator _mediator = Substitute.For<IMediator>();
	private readonly IListenPatternMatcher _patternMatcher = Substitute.For<IListenPatternMatcher>();
	private readonly IPermissionService _permissionService = Substitute.For<IPermissionService>();
	private readonly ILockService _lockService = Substitute.For<ILockService>();
	private readonly IConnectionService _connectionService = Substitute.For<IConnectionService>();
	private readonly IServiceProvider _serviceProvider = Substitute.For<IServiceProvider>();
	private readonly IMessageBus _messageBus = Substitute.For<IMessageBus>();

	private ListenerRoutingService CreateService() =>
		new(_mediator, _patternMatcher, _permissionService, _lockService, _connectionService, _serviceProvider, _messageBus);

	private async Task AssertNothingWasConsulted()
	{
		object[] collaborators =
			[_mediator, _patternMatcher, _permissionService, _lockService, _connectionService, _serviceProvider, _messageBus];

		foreach (var collaborator in collaborators)
		{
			await Assert.That(collaborator.ReceivedCalls()).IsEmpty()
				.Because($"{collaborator.GetType().Name} is behind the guard");
		}
	}

	[Test]
	public async ValueTask ProcessNotificationAsync_WithNullLocation_ReturnsEarly()
	{
		var context = new NotificationContext(
			Target: new DBRef(1, null),
			Location: null,
			ExcludedObjects: []
		);

		await CreateService().ProcessNotificationAsync(
			context,
			"Test message",
			null,
			INotifyService.NotificationType.Say);

		await AssertNothingWasConsulted();
	}

	[Test]
	public async ValueTask ProcessNotificationAsync_WithAnnounceType_ReturnsEarly()
	{
		var context = new NotificationContext(
			Target: new DBRef(1, null),
			Location: new DBRef(0, null),
			ExcludedObjects: []
		);

		await CreateService().ProcessNotificationAsync(
			context,
			"Private message",
			null,
			INotifyService.NotificationType.Announce);

		await AssertNothingWasConsulted();
	}

	/// <summary>The control for the two above: past the guards, the addressee is looked up.</summary>
	[Test]
	public async ValueTask ProcessNotificationAsync_PastTheGuards_LooksUpTheListener()
	{
		var context = new NotificationContext(
			Target: new DBRef(1, null),
			Location: new DBRef(0, null),
			ExcludedObjects: []
		);

		await CreateService().ProcessNotificationAsync(
			context,
			"Heard message",
			null,
			INotifyService.NotificationType.Say);

		await Assert.That(_mediator.ReceivedCalls()).IsNotEmpty();
	}
}
