using System.Collections.Concurrent;
using System.Text;
using Mediator;
using NSubstitute;
using SharpMUSH.Implementation.Handlers;
using SharpMUSH.Library.API;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Notifications;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Messaging.Messages;

namespace SharpMUSH.Tests.Handlers;

/// <summary>
/// The connect screen tells a connection how to log in. The portal's terminal logs in the moment its socket
/// opens and says so on its first frame, so it is not shown the screen; after a LOGOUT it is at the login
/// prompt like any other connection and is.
/// </summary>
public class ConnectScreenSignInTests
{
	[Test]
	[Arguments(IConnectionService.ConnectionState.None, "1", false)]
	[Arguments(IConnectionService.ConnectionState.None, "0", true)]
	[Arguments(IConnectionService.ConnectionState.LoggedIn, "1", true)]
	public async Task TheConnectScreenIsLeftOutOnlyForAConnectionThatSignsInAsItOpens(
		IConnectionService.ConnectionState from, string signIn, bool shown)
	{
		const long handle = 44;
		var connections = Substitute.For<IConnectionService>();
		connections.Get(handle).Returns(new IConnectionService.ConnectionData(
			handle,
			null,
			IConnectionService.ConnectionState.Connected,
			_ => ValueTask.CompletedTask,
			_ => ValueTask.CompletedTask,
			() => Encoding.UTF8,
			new ConcurrentDictionary<string, string>(new Dictionary<string, string>
			{
				{ ConnectionEstablishedMessage.SignInMetadata, signIn }
			})));
		var messages = Substitute.For<IGameMessageService>();
		var handler = new ConnectionStateEventHandler(connections, Substitute.For<IEventService>(),
			Substitute.For<INotifyService>(), Substitute.For<IMediator>(), Substitute.For<IConnectionAnnounceService>(), messages);

		await handler.Handle(new ConnectionStateChangeNotification(handle, null, from,
			IConnectionService.ConnectionState.Connected), CancellationToken.None);

		await messages.Received(shown ? 1 : 0).RenderAsync(GameMessage.Connect, handle, Arg.Any<AnySharpObject?>());
	}
}
