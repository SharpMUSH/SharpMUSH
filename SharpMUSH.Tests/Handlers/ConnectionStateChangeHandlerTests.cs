using System.Collections.Concurrent;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Library;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Notifications;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Messaging.Abstractions;
using SharpMUSH.Messaging.Messages;
using SharpMUSH.Server.Handlers;

namespace SharpMUSH.Tests.Handlers;

public class ConnectionStateChangeHandlerTests
{
	[Test]
	public async Task ReturningToConnectScreenClearsPlayerOutputPreferencesFirst()
	{
		const long handle = 42;
		var connectionService = Substitute.For<IConnectionService>();
		connectionService.Get(handle).Returns(new IConnectionService.ConnectionData(
			handle,
			null,
			IConnectionService.ConnectionState.Connected,
			_ => ValueTask.CompletedTask,
			_ => ValueTask.CompletedTask,
			() => Encoding.UTF8,
			new ConcurrentDictionary<string, string>()));
		var notifyService = Substitute.For<INotifyService>();
		var messageBus = Substitute.For<IMessageBus>();
		var handler = new ConnectionStateChangeHandler(
			NullLogger<ConnectionStateChangeHandler>.Instance,
			connectionService,
			notifyService,
			Substitute.For<IAttributeStore>(),
			Substitute.For<IObjectStore>(),
			messageBus);

		await handler.Handle(new ConnectionStateChangeNotification(
			handle,
			new DBRef(7),
			IConnectionService.ConnectionState.LoggedIn,
			IConnectionService.ConnectionState.Connected),
			CancellationToken.None);

		await messageBus.Received(1).Publish(
			Arg.Is<ClearPlayerOutputPreferencesMessage>(message => message.Handle == handle),
			Arg.Any<CancellationToken>());
	}

	/// <summary>
	/// A new connection gets the connect screen and nothing else: PennMUSH's welcome_user sends only
	/// connect.txt, and there is no "Connected!" line ahead of it.
	/// </summary>
	[Test]
	public async Task ANewConnectionIsNotToldItConnected()
	{
		const long handle = 43;
		var connectionService = Substitute.For<IConnectionService>();
		connectionService.Get(handle).Returns(new IConnectionService.ConnectionData(
			handle,
			null,
			IConnectionService.ConnectionState.Connected,
			_ => ValueTask.CompletedTask,
			_ => ValueTask.CompletedTask,
			() => Encoding.UTF8,
			new ConcurrentDictionary<string, string>()));
		var notifyService = Substitute.For<INotifyService>();
		var handler = new ConnectionStateChangeHandler(
			NullLogger<ConnectionStateChangeHandler>.Instance,
			connectionService,
			notifyService,
			Substitute.For<IAttributeStore>(),
			Substitute.For<IObjectStore>(),
			Substitute.For<IMessageBus>());

		await handler.Handle(new ConnectionStateChangeNotification(
			handle,
			null,
			IConnectionService.ConnectionState.None,
			IConnectionService.ConnectionState.Connected),
			CancellationToken.None);

		await Assert.That(notifyService.ReceivedCalls().Count()).IsEqualTo(0);
	}
}
