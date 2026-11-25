using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Mediator;
using NSubstitute;
using SharpMUSH.Implementation.Handlers;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Notifications;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Messaging.Abstractions;
using SharpMUSH.Messaging.Messages;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// The engine's <c>sharp.play</c> package tells a player's portal connections who they play and where, on login,
/// on resume and on every move; telnet connections are not sent it.
/// </summary>
public class PlayStateOobHandlerTests
{
	private const long Portal = 31;
	private const long Telnet = 32;

	private sealed record Setup(PlayStateOobHandler Handler, IMessageBus Bus, SharpPlayer Player, SharpRoom Room, SharpRoom Elsewhere);

	private static Setup Build()
	{
		var objects = new TestObjectFactory();
		var room = objects.CreateRoom(40, "Quay");
		var elsewhere = objects.CreateRoom(41, "Market");
		var player = objects.CreatePlayer(12, "Tamsin", room).Expect<SharpPlayer>();
		var connections = Substitute.For<IConnectionService>();
		var portal = Connection(Portal, player.Object.DBRef, "websocket");
		var telnet = Connection(Telnet, player.Object.DBRef, "telnet");
		connections.Get(Portal).Returns(portal);
		connections.Get(Telnet).Returns(telnet);
		connections.Get(player.Object.DBRef).Returns(_ => new[] { portal, telnet }.ToAsyncEnumerable());
		var mediator = Substitute.For<IMediator>();
		mediator.Send(Arg.Any<GetObjectNodeQuery>(), Arg.Any<CancellationToken>()).Returns(call =>
			ValueTask.FromResult<AnyOptionalSharpObject>(call.Arg<GetObjectNodeQuery>().DBRef == player.Object.DBRef
				? new AnySharpObject(player)
				: new None()));
		var bus = Substitute.For<IMessageBus>();
		return new(new PlayStateOobHandler(connections, mediator, bus), bus, player, room, elsewhere);
	}

	private static IConnectionService.ConnectionData Connection(long handle, DBRef player, string type) =>
		new(handle, player, IConnectionService.ConnectionState.LoggedIn,
			_ => ValueTask.CompletedTask, _ => ValueTask.CompletedTask, () => Encoding.UTF8,
			new ConcurrentDictionary<string, string>(new Dictionary<string, string> { ["ConnectionType"] = type }));

	private static WebSocketOutputMessage[] Sent(IMessageBus bus) =>
		[.. bus.ReceivedCalls().SelectMany(call => call.GetArguments()).OfType<WebSocketOutputMessage>()];

	/// <summary>The <c>data</c> of a <c>sharp.play</c> envelope.</summary>
	private static JsonElement Data(WebSocketOutputMessage message)
	{
		using var envelope = JsonDocument.Parse(message.Data);
		var root = envelope.RootElement;
		return root.GetProperty("package").GetString() == "sharp.play" && !root.TryGetProperty("from", out _)
			? root.GetProperty("data").Clone()
			: throw new InvalidOperationException($"Not a sharp.play envelope: {message.Data}");
	}

	[Test]
	public async Task ALoginOnThePortalIsToldTheCharacterAndRoom()
	{
		var setup = Build();

		await setup.Handler.Handle(new ConnectionStateChangeNotification(Portal, setup.Player.Object.DBRef,
			IConnectionService.ConnectionState.Connected, IConnectionService.ConnectionState.LoggedIn), CancellationToken.None);

		var sent = Sent(setup.Bus).Single();
		await Assert.That(sent.Handle).IsEqualTo(Portal);
		var data = Data(sent);
		await Assert.That(data.GetProperty("v").GetInt32()).IsEqualTo(1);
		await Assert.That(data.GetProperty("character").GetString()).IsEqualTo(setup.Player.Object.DBRef.ToString());
		await Assert.That(data.GetProperty("name").GetString()).IsEqualTo("Tamsin");
		await Assert.That(data.GetProperty("room").GetString()).IsEqualTo(setup.Room.Object.DBRef.ToString());
	}

	[Test]
	public async Task ATelnetLoginIsNotSentIt()
	{
		var setup = Build();

		await setup.Handler.Handle(new ConnectionStateChangeNotification(Telnet, setup.Player.Object.DBRef,
			IConnectionService.ConnectionState.Connected, IConnectionService.ConnectionState.LoggedIn), CancellationToken.None);

		await Assert.That(Sent(setup.Bus)).IsEmpty();
	}

	[Test]
	public async Task OtherTransitionsSendNothing()
	{
		var setup = Build();

		await setup.Handler.Handle(new ConnectionStateChangeNotification(Portal, setup.Player.Object.DBRef,
			IConnectionService.ConnectionState.LoggedIn, IConnectionService.ConnectionState.Disconnected), CancellationToken.None);

		await Assert.That(Sent(setup.Bus)).IsEmpty();
	}

	[Test]
	public async Task AResumedConnectionIsToldAgain()
	{
		var setup = Build();

		await setup.Handler.Handle(new ConnectionResumedNotification(Portal, setup.Player.Object.DBRef), CancellationToken.None);

		await Assert.That(Sent(setup.Bus).Single().Handle).IsEqualTo(Portal);
	}

	[Test]
	public async Task AMoveTellsEveryPortalConnectionTheNewRoom()
	{
		var setup = Build();

		await setup.Handler.Handle(new ObjectMovedNotification(new AnySharpContent(setup.Player),
			new AnySharpContainer(setup.Elsewhere), setup.Room.Object.DBRef, null, false, "move"), CancellationToken.None);

		var sent = Sent(setup.Bus).Single();
		await Assert.That(sent.Handle).IsEqualTo(Portal);
		await Assert.That(Data(sent).GetProperty("room").GetString()).IsEqualTo(setup.Elsewhere.Object.DBRef.ToString());
	}
}
