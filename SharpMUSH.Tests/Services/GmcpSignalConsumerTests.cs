using System.Collections.Concurrent;
using System.Text;
using Mediator;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.Utilities;
using SharpMUSH.Messaging.Messages;
using SharpMUSH.Server.Consumers;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// What the main process does with a GMCP package a client sends: softcode hears it as SOCKET`GMCP, and
/// <c>Core.Hello</c> says who the client is.
/// </summary>
public class GmcpSignalConsumerTests
{
	private const long Handle = 1000078;

	private static async Task<ConnectionService> RegisteredAsync()
	{
		var service = new ConnectionService(Substitute.For<IPublisher>());
		await service.Register(Handle, "127.0.0.1", "localhost", "telnet",
			_ => ValueTask.CompletedTask, _ => ValueTask.CompletedTask, () => Encoding.UTF8,
			new ConcurrentDictionary<string, string>());
		return service;
	}

	private static Task Receive(ConnectionService service, IEventService events, string package, string info) =>
		new GMCPSignalConsumer(NullLogger<GMCPSignalConsumer>.Instance, service, events)
			.HandleAsync(new GMCPSignalMessage(Handle, package, info));

	[Test]
	public async Task APackage_IsHandedToSoftcode()
	{
		var service = await RegisteredAsync();
		var events = Substitute.For<IEventService>();

		await Receive(service, events, "External.Discord.Hello", "{}");

		await events.Received(1).TriggerEventAsync("SOCKET`GMCP", null,
			Arg.Is<string[]>(args => args.SequenceEqual(new[] { Handle.ToString(), "External.Discord.Hello", "{}", "" })));
		await Assert.That(service.Get(Handle)!.Metadata["GMCP"]).IsEqualTo("1");
	}

	[Test]
	public async Task APackageFromALoggedInPlayer_NamesThePlayer()
	{
		var service = await RegisteredAsync();
		await service.Bind(Handle, new DBRef(4242, null));
		var events = Substitute.For<IEventService>();

		await Receive(service, events, "Char.Login", "{}");

		await events.Received(1).TriggerEventAsync("SOCKET`GMCP", Arg.Is<DBRef?>(r => r!.Value.Number == 4242),
			Arg.Is<string[]>(args => args[3] == "#4242"));
	}

	[Test]
	[Arguments("Core.Ping")]
	[Arguments("Core.KeepAlive")]
	public async Task PackagesTheConnectionServerAnswers_AreNotHandedToSoftcode(string package)
	{
		var service = await RegisteredAsync();
		var events = Substitute.For<IEventService>();

		await Receive(service, events, package, "");

		await events.DidNotReceiveWithAnyArgs().TriggerEventAsync(default!, default, default!);
	}

	[Test]
	public async Task CoreHello_RecordsTheClient()
	{
		var service = await RegisteredAsync();

		await Receive(service, Substitute.For<IEventService>(), "Core.Hello", """{"client":"Mudlet","version":"4.17.2"}""");

		var metadata = service.Get(Handle)!.Metadata;
		await Assert.That(metadata[ConnectionClient.NameKey]).IsEqualTo("Mudlet");
		await Assert.That(metadata[ConnectionClient.VersionKey]).IsEqualTo("4.17.2");
	}

	[Test]
	public async Task ClientName_IsKeptShortAndPrintable()
	{
		var service = await RegisteredAsync();

		ConnectionClient.Record(service, Handle, "Bad\u001b[2JName" + new string('x', 200), null);

		var name = service.Get(Handle)!.Metadata[ConnectionClient.NameKey];
		await Assert.That(name.Any(char.IsControl)).IsFalse();
		await Assert.That(name.Length).IsEqualTo(64);
	}
}
