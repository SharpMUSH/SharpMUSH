using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Mediator;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Utilities;
using SharpMUSH.Messaging.Messages;
using SharpMUSH.Server.Consumers;

namespace SharpMUSH.Tests.ConnectionServer;

public class TerminalReportConsumerTests
{
	private const long Handle = 101;

	private static async Task<ConnectionService> RegisteredConnection()
	{
		var service = new ConnectionService(Substitute.For<IPublisher>());
		await service.Register(Handle, "127.0.0.1", "localhost", "telnet",
			_ => ValueTask.CompletedTask, _ => ValueTask.CompletedTask, () => Encoding.UTF8,
			new ConcurrentDictionary<string, string>());
		return service;
	}

	[Test]
	[Arguments(null, "")]
	[Arguments(false, "0")]
	[Arguments(true, "1")]
	public async Task SerializedSixelReplyPreservesAllThreeStatesInMetadata(bool? reply, string expected)
	{
		var service = await RegisteredConnection();
		var consumer = new TerminalReportConsumer(NullLogger<TerminalReportConsumer>.Instance, service);
		var sent = new TerminalReportMessage(Handle, new TerminalProbeResult(null, reply, null, 0, 0));
		var received = JsonSerializer.Deserialize<TerminalReportMessage>(JsonSerializer.Serialize(sent))!;

		await consumer.HandleAsync(received);

		var metadata = service.Get(Handle)!.Metadata;
		await Assert.That(metadata[TerminalFeatureReader.ProbeSixelKey]).IsEqualTo(expected);
		await Assert.That(TerminalFeatureReader.ProbeOf(metadata)!.SixelReply).IsEqualTo(reply);
	}

	[Test]
	public async Task UnansweredReportClearsAnEarlierPositiveReply()
	{
		var service = await RegisteredConnection();
		var consumer = new TerminalReportConsumer(NullLogger<TerminalReportConsumer>.Instance, service);
		await consumer.HandleAsync(new TerminalReportMessage(Handle, new TerminalProbeResult(true, true, "terminal", 8, 16)));
		await Assert.That(service.Get(Handle)!.Metadata[TerminalFeatureReader.ProbeSixelKey]).IsEqualTo("1");

		await consumer.HandleAsync(new TerminalReportMessage(Handle, new TerminalProbeResult(null, null, null, 0, 0)));

		var metadata = service.Get(Handle)!.Metadata;
		await Assert.That(metadata[TerminalFeatureReader.ProbeSixelKey]).IsEqualTo(string.Empty);
		await Assert.That(metadata[TerminalFeatureReader.ProbeKittyKey]).IsEqualTo(string.Empty);
		await Assert.That(metadata[TerminalFeatureReader.ProbeVersionKey]).IsEqualTo(string.Empty);
		await Assert.That(metadata[TerminalFeatureReader.ProbeCellSizeKey]).IsEqualTo(string.Empty);
		await Assert.That(TerminalFeatureReader.ProbeOf(metadata)!.SixelReply).IsNull();
	}
}
