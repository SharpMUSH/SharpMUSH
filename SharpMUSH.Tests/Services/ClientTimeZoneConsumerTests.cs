using System.Collections.Concurrent;
using System.Text;
using Mediator;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.Utilities;
using SharpMUSH.Messaging.Messages;
using SharpMUSH.Server.Consumers;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// What the main process does with the time zone a client reports (NEW-ENVIRON <c>TZ</c>, or the portal's browser zone):
/// a zone the server knows is recorded for <c>clientzone()</c> and raised as SOCKET`TIMEZONE; anything else is dropped.
/// </summary>
public class ClientTimeZoneConsumerTests
{
	private const long Handle = 1000079;

	private static async Task<ConnectionService> RegisteredAsync()
	{
		var service = new ConnectionService(Substitute.For<IPublisher>());
		await service.Register(Handle, "127.0.0.1", "localhost", "telnet",
			_ => ValueTask.CompletedTask, _ => ValueTask.CompletedTask, () => Encoding.UTF8,
			new ConcurrentDictionary<string, string>());
		return service;
	}

	private static Task Receive(ConnectionService service, IEventService events, string zone) =>
		new ClientTimeZoneConsumer(NullLogger<ClientTimeZoneConsumer>.Instance, service, events)
			.HandleAsync(new ClientTimeZoneMessage(Handle, zone));

	[Test]
	public async Task AZone_IsRecordedAndHandedToSoftcode()
	{
		var service = await RegisteredAsync();
		var events = Substitute.For<IEventService>();

		await Receive(service, events, "Europe/Paris");

		await Assert.That(service.Get(Handle)!.Metadata[ConnectionTimeZone.Key]).IsEqualTo("Europe/Paris");
		await events.Received(1).TriggerEventAsync("SOCKET`TIMEZONE", null,
			Arg.Is<string[]>(args => args.SequenceEqual(new[] { Handle.ToString(), "Europe/Paris", "" })));
	}

	[Test]
	public async Task AZoneFromALoggedInPlayer_NamesThePlayer()
	{
		var service = await RegisteredAsync();
		await service.Bind(Handle, new DBRef(4243, null));
		var events = Substitute.For<IEventService>();

		await Receive(service, events, "Asia/Tokyo");

		await events.Received(1).TriggerEventAsync("SOCKET`TIMEZONE", Arg.Is<DBRef?>(r => r!.Value.Number == 4243),
			Arg.Is<string[]>(args => args[2] == "#4243"));
	}

	[Test]
	public async Task TheSameZoneAgain_IsNotRaisedAgain()
	{
		var service = await RegisteredAsync();
		var events = Substitute.For<IEventService>();

		await Receive(service, events, "Europe/Paris");
		await Receive(service, events, "Europe/Paris");

		await events.Received(1).TriggerEventAsync("SOCKET`TIMEZONE", Arg.Any<DBRef?>(), Arg.Any<string[]>());
	}

	[Test]
	[Arguments("CET-1CEST")]
	[Arguments("Not/AZone")]
	[Arguments("europe/paris")]
	public async Task TextNamingNoZone_IsDropped(string zone)
	{
		var service = await RegisteredAsync();
		var events = Substitute.For<IEventService>();

		await Receive(service, events, zone);

		await Assert.That(service.Get(Handle)!.Metadata.ContainsKey(ConnectionTimeZone.Key)).IsFalse();
		await events.DidNotReceiveWithAnyArgs().TriggerEventAsync(default!, default, default(string[])!);
	}

	[Test]
	[Arguments("Europe/Paris", "Europe/Paris")]
	[Arguments(":America/New_York", "America/New_York")]
	[Arguments("  UTC ", "UTC")]
	public async Task Parse_TakesAnIanaZone(string reported, string expected)
	{
		await Assert.That(ConnectionTimeZone.Parse(reported) is string zone && zone == expected).IsTrue();
	}

	[Test]
	[Arguments("")]
	[Arguments("CET-1CEST")]
	[Arguments("Eastern Standard Time")]
	[Arguments("america/new_york")]
	[Arguments("Europe/\u001bParis")]
	public async Task Parse_RefusesWhatNamesNoZone(string reported)
	{
		// Reading the zone in its own case first puts it in the cache that ignores case.
		_ = ConnectionTimeZone.Parse("America/New_York");

		await Assert.That(ConnectionTimeZone.Parse(reported) is NotFound).IsTrue();
	}
}
