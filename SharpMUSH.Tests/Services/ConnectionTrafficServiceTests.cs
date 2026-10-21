using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Implementation.Services;
using SharpMUSH.Messaging.Abstractions;
using SharpMUSH.Messaging.Messages;

namespace SharpMUSH.Tests.Services;

/// <summary><c>SESSION</c> asks the connection server for its byte counts and waits briefly for the answer.</summary>
public class ConnectionTrafficServiceTests
{
	[Test]
	public async Task AnswerCarriesEachConnectionsCounts()
	{
		var bus = Substitute.For<IMessageBus>();
		var service = new ConnectionTrafficService(bus, NullLogger<ConnectionTrafficService>.Instance);
		bus.Publish(Arg.Any<ConnectionTrafficRequestMessage>(), Arg.Any<CancellationToken>())
			.Returns(call =>
			{
				var request = call.Arg<ConnectionTrafficRequestMessage>();
				service.Complete(Guid.NewGuid(), [new ConnectionTraffic(7, 9, 9, 9)]);
				service.Complete(request.RequestId, [new ConnectionTraffic(1, 10, 20, 3), new ConnectionTraffic(2, 0, 5, 0)]);
				return Task.CompletedTask;
			});

		var counts = await service.GetAsync();

		await Assert.That(counts.Keys).IsEquivalentTo([1L, 2L]);
		await Assert.That(counts[1]).IsEqualTo(new ConnectionTraffic(1, 10, 20, 3));
	}

	[Test]
	public async Task NoAnswerLeavesTheCountsOut()
	{
		var service = new ConnectionTrafficService(Substitute.For<IMessageBus>(), NullLogger<ConnectionTrafficService>.Instance);

		var counts = await service.GetAsync();

		await Assert.That(counts).IsEmpty();
	}
}
