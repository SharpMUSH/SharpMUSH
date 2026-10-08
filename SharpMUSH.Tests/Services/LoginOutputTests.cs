using System.Collections.Concurrent;
using System.Text;
using NSubstitute;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Messaging.Abstractions;
using SharpMUSH.Messaging.Messages;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// What a login tells its player (the last-connect lines, the look it ends with) reaches the connection that
/// logged in, not the player's other connections.
/// </summary>
public class LoginOutputTests
{
	private static readonly DBRef Player = new(42, null);
	private static readonly DBRef Other = new(43, null);

	private static IConnectionService.ConnectionData Connection(long handle, DBRef who) =>
		new(handle, who, IConnectionService.ConnectionState.LoggedIn, _ => ValueTask.CompletedTask,
			_ => ValueTask.CompletedTask, () => Encoding.UTF8, new ConcurrentDictionary<string, string>());

	private static (NotifyService Service, IMessageBus Bus) BuildService()
	{
		var connections = Substitute.For<IConnectionService>();
		var telnet = Connection(5, Player);
		var portal = Connection(6, Player);
		var other = Connection(7, Other);
		connections.Get(5L).Returns(telnet);
		connections.Get(6L).Returns(portal);
		connections.Get(7L).Returns(other);
		connections.Get(Player).Returns(_ => new[] { telnet, portal }.ToAsyncEnumerable());
		connections.Get(Other).Returns(_ => new[] { other }.ToAsyncEnumerable());
		var localization = Substitute.For<ILocalizationService>();
		localization.Format(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<object[]>()).Returns("localized line");
		var bus = Substitute.For<IMessageBus>();
		return (new NotifyService(bus, connections, localization, DisabledRealityPolicy.Instance, null, null, null), bus);
	}

	private static long[] Handles(IMessageBus bus) =>
		[.. bus.ReceivedCalls().SelectMany(call => call.GetArguments()).OfType<MarkupOutputMessage>().Select(m => m.Handle).Order()];

	[Test]
	public async Task DuringALogin_ThePlayerIsToldOnlyOnTheConnectionThatLoggedIn()
	{
		var (service, bus) = BuildService();

		using (LoginOutput.To(Player, 6))
		{
			await service.Notify(Player, "Room Zero", sender: null);
			await service.NotifyLocalized(Player, "LastConnectFormat", sender: null);
			await service.Notify(Other, "someone else", sender: null);
		}

		await Assert.That(Handles(bus)).IsEquivalentTo([6L, 6L, 7L]);
	}

	[Test]
	public async Task AfterTheLogin_ThePlayerIsToldEverywhere()
	{
		var (service, bus) = BuildService();

		using (LoginOutput.To(Player, 6)) { }
		await service.Notify(Player, "page", sender: null);

		await Assert.That(Handles(bus)).IsEquivalentTo([5L, 6L]);
	}
}
