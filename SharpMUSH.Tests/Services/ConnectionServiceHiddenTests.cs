using NSubstitute;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services;
using Mediator;
using System.Collections.Concurrent;
using System.Text;

namespace SharpMUSH.Tests.Services;

public class ConnectionServiceHiddenTests
{
	private static ConcurrentDictionary<string, string> BuildMetadata() =>
		new(new Dictionary<string, string>
		{
			["ConnectionStartTime"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(),
			["LastConnectionSignal"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(),
			["InternetProtocolAddress"] = "127.0.0.1",
			["HostName"] = "localhost",
			["ConnectionType"] = "telnet"
		});

	[Test]
	public async Task IsPlayerHiddenAsync_NoConnectionsHidden_ReturnsFalse()
	{
		var publisher = Substitute.For<IPublisher>();
		var service = new ConnectionService(publisher);
		var playerRef = new DBRef(50, null);

		await service.Register(
			1,
			"127.0.0.1",
			"localhost",
			"telnet",
			_ => ValueTask.CompletedTask,
			_ => ValueTask.CompletedTask,
			() => Encoding.UTF8,
			BuildMetadata());
		await service.Bind(1, playerRef);

		var result = await service.IsPlayerHiddenAsync(playerRef);

		await Assert.That(result).IsFalse();
	}

	[Test]
	public async Task IsPlayerHiddenAsync_OneOfTwoConnectionsHidden_ReturnsTrue()
	{
		var publisher = Substitute.For<IPublisher>();
		var service = new ConnectionService(publisher);
		var playerRef = new DBRef(51, null);

		await service.Register(
			2,
			"127.0.0.1",
			"localhost",
			"telnet",
			_ => ValueTask.CompletedTask,
			_ => ValueTask.CompletedTask,
			() => Encoding.UTF8,
			BuildMetadata());
		await service.Bind(2, playerRef);

		await service.Register(
			3,
			"127.0.0.1",
			"localhost",
			"telnet",
			_ => ValueTask.CompletedTask,
			_ => ValueTask.CompletedTask,
			() => Encoding.UTF8,
			BuildMetadata());
		await service.Bind(3, playerRef);

		service.Update(3, "Hidden", "1");

		var result = await service.IsPlayerHiddenAsync(playerRef);

		await Assert.That(result).IsTrue();
	}
}
