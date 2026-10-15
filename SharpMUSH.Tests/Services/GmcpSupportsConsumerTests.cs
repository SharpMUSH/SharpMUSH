using System.Collections.Concurrent;
using System.Text;
using Mediator;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Utilities;
using SharpMUSH.Messaging.Messages;
using SharpMUSH.Server.Consumers;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// The main process keeps a client's GMCP Core.Supports list as connection metadata, which
/// gmcpsupports() and <c>oob(..., supported)</c> read.
/// </summary>
public class GmcpSupportsConsumerTests
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

	private static Task Receive(ConnectionService service, params string[] modules) =>
		new GMCPSupportsConsumer(NullLogger<GMCPSupportsConsumer>.Instance, service)
			.HandleAsync(new GMCPSupportsMessage(Handle, modules));

	[Test]
	public async Task TheList_IsKeptAsSpaceSeparatedPairs()
	{
		var service = await RegisteredAsync();

		await Receive(service, "Room 1", "Client.Media 1");

		await Assert.That(service.Get(Handle)!.Metadata[GmcpSupports.MetadataKey]).IsEqualTo("Room 1 Client.Media 1");
	}

	[Test]
	public async Task ALaterList_ReplacesTheEarlierOne()
	{
		var service = await RegisteredAsync();

		await Receive(service, "Room 1", "Char 1");
		await Receive(service);

		await Assert.That(service.Get(Handle)!.Metadata[GmcpSupports.MetadataKey]).IsEqualTo(string.Empty)
			.Because("a client that withdraws GMCP is sent as an empty list, and lists nothing from then on");
	}

	[Test]
	[Arguments("Room 1 Char.Vitals 1", "Room.Info", true)]
	[Arguments("Room 1 Char.Vitals 1", "room", true)]
	[Arguments("Room 1 Char.Vitals 1", "Char.Vitals", true)]
	[Arguments("Room 1 Char.Vitals 1", "Char", false)]
	[Arguments("Room 1 Char.Vitals 1", "Char.Vitals.Extra", true)]
	[Arguments("Room 1 Char.Vitals 1", "Comm.Channel.Text", false)]
	[Arguments("Room 1 Char.Vitals 1", "Roomy", false)]
	[Arguments("", "Room", false)]
	public async Task Lists_FollowsTheParentModuleRule(string listed, string package, bool expected)
		=> await Assert.That(GmcpSupports.Lists(listed, package)).IsEqualTo(expected);

	[Test]
	[Arguments("", "Comm.Channel.Text", true)]
	[Arguments("Room 1", "Comm.Channel.Text", false)]
	[Arguments("Room 1", "Room.Info", true)]
	public async Task Accepts_SendsEverythingToAClientThatListedNothing(string listed, string package, bool expected)
		=> await Assert.That(GmcpSupports.Accepts(listed, package)).IsEqualTo(expected);
}
