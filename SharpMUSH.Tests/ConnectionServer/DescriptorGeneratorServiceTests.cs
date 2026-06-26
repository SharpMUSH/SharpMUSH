using SharpMUSH.ConnectionServer.Configuration;
using SharpMUSH.ConnectionServer.Services;

namespace SharpMUSH.Tests.ConnectionServer;

/// <summary>
/// Tests for the descriptor generator service
/// </summary>
public class DescriptorGeneratorServiceTests
{
	[Test]
	public async Task TelnetDescriptorsAreUnique()
	{
		var options = new ConnectionServerOptions
		{
			TelnetDescriptorStart = 0,
			WebSocketDescriptorStart = 1000000
		};
		var service = new DescriptorGeneratorService(options);

		var descriptor1 = service.GetNextTelnetDescriptor();
		var descriptor2 = service.GetNextTelnetDescriptor();
		var descriptor3 = service.GetNextTelnetDescriptor();

		await Assert.That(descriptor1).IsEqualTo(1L);
		await Assert.That(descriptor2).IsEqualTo(2L);
		await Assert.That(descriptor3).IsEqualTo(3L);
	}

	[Test]
	public async Task WebSocketDescriptorsAreUnique()
	{
		var options = new ConnectionServerOptions
		{
			TelnetDescriptorStart = 0,
			WebSocketDescriptorStart = 1000000
		};
		var service = new DescriptorGeneratorService(options);

		var descriptor1 = service.GetNextWebSocketDescriptor();
		var descriptor2 = service.GetNextWebSocketDescriptor();
		var descriptor3 = service.GetNextWebSocketDescriptor();

		await Assert.That(descriptor1).IsEqualTo(1000001L);
		await Assert.That(descriptor2).IsEqualTo(1000002L);
		await Assert.That(descriptor3).IsEqualTo(1000003L);
	}

	[Test]
	public async Task TelnetAndWebSocketDescriptorsAreSeparate()
	{
		var options = new ConnectionServerOptions
		{
			TelnetDescriptorStart = 0,
			WebSocketDescriptorStart = 1000000
		};
		var service = new DescriptorGeneratorService(options);

		var telnet1 = service.GetNextTelnetDescriptor();
		var ws1 = service.GetNextWebSocketDescriptor();
		var telnet2 = service.GetNextTelnetDescriptor();
		var ws2 = service.GetNextWebSocketDescriptor();

		await Assert.That(telnet1).IsEqualTo(1L);
		await Assert.That(ws1).IsEqualTo(1000001L);
		await Assert.That(telnet2).IsEqualTo(2L);
		await Assert.That(ws2).IsEqualTo(1000002L);
	}

	[Test]
	public async Task DescriptorsAreThreadSafe()
	{
		var options = new ConnectionServerOptions
		{
			TelnetDescriptorStart = 0,
			WebSocketDescriptorStart = 1000000
		};
		var service = new DescriptorGeneratorService(options);

		var telnetDescriptors = new long[100];
		var wsDescriptors = new long[100];

		await Parallel.ForAsync(0, 100, async (i, _) =>
		{
			telnetDescriptors[i] = service.GetNextTelnetDescriptor();
			wsDescriptors[i] = service.GetNextWebSocketDescriptor();
			await ValueTask.CompletedTask;
		});

		var uniqueTelnet = telnetDescriptors.Distinct().Count();
		var uniqueWs = wsDescriptors.Distinct().Count();

		await Assert.That(uniqueTelnet).IsEqualTo(100);
		await Assert.That(uniqueWs).IsEqualTo(100);
	}

	[Test]
	public async Task CustomStartValuesAreRespected()
	{
		var options = new ConnectionServerOptions
		{
			TelnetDescriptorStart = 5000,
			WebSocketDescriptorStart = 2000000
		};
		var service = new DescriptorGeneratorService(options);

		var telnetDescriptor = service.GetNextTelnetDescriptor();
		var wsDescriptor = service.GetNextWebSocketDescriptor();

		await Assert.That(telnetDescriptor).IsEqualTo(5001L);
		await Assert.That(wsDescriptor).IsEqualTo(2000001L);
	}
}
