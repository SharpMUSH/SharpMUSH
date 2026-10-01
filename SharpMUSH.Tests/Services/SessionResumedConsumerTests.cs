using System.Collections.Concurrent;
using System.Text;
using Mediator;
using NSubstitute;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Notifications;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Messaging.Messages;
using SharpMUSH.Server.Consumers;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// The connection server rebound a socket to a session (a reload, or a dropped connection coming back).
/// The engine re-sends the session's current state, as on connect, when the session is the one it
/// knows and is logged in as a player.
/// </summary>
public class SessionResumedConsumerTests
{
	private static readonly DBRef Player = new(7, 1234L);

	private static (SessionResumedConsumer Consumer, IPublisher Publisher) Build(
		IConnectionService.ConnectionState state, DBRef? player, string session = "session")
	{
		var connections = Substitute.For<IConnectionService>();
		connections.Get(42).Returns(new IConnectionService.ConnectionData(42, player, state,
			_ => ValueTask.CompletedTask, _ => ValueTask.CompletedTask, () => Encoding.UTF8,
			new ConcurrentDictionary<string, string> { ["SessionId"] = session }));
		var publisher = Substitute.For<IPublisher>();
		return (new SessionResumedConsumer(connections, publisher), publisher);
	}

	[Test]
	public async Task A_resumed_player_session_is_announced_to_the_engine()
	{
		var (consumer, publisher) = Build(IConnectionService.ConnectionState.LoggedIn, Player);

		await consumer.HandleAsync(new SessionResumedMessage(42, "session"));

		await publisher.Received(1).Publish(
			Arg.Is<ConnectionResumedNotification>(n => n.Handle == 42 && n.Player == Player), Arg.Any<CancellationToken>());
	}

	[Test]
	public async Task Another_incarnation_of_the_handle_is_not_announced()
	{
		var (consumer, publisher) = Build(IConnectionService.ConnectionState.LoggedIn, Player, session: "newer");

		await consumer.HandleAsync(new SessionResumedMessage(42, "session"));

		await publisher.DidNotReceive().Publish(Arg.Any<ConnectionResumedNotification>(), Arg.Any<CancellationToken>());
	}

	/// <summary>A session at the login prompt, or in account mode, has no character whose state to send.</summary>
	[Test]
	public async Task A_session_not_logged_in_as_a_player_is_not_announced()
	{
		var (consumer, publisher) = Build(IConnectionService.ConnectionState.AccountMode, null);

		await consumer.HandleAsync(new SessionResumedMessage(42, "session"));
		await consumer.HandleAsync(new SessionResumedMessage(43, "session"));

		await publisher.DidNotReceive().Publish(Arg.Any<ConnectionResumedNotification>(), Arg.Any<CancellationToken>());
	}
}
