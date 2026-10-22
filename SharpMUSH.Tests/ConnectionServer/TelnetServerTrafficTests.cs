using System.IO.Pipelines;
using System.Text;
using NSubstitute;
using SharpMUSH.Messaging.Messages;
using SharpMUSH.SocketServer.Models;
using SharpMUSH.SocketServer.Services;

namespace SharpMUSH.Tests.ConnectionServer;

/// <summary>The bytes a telnet connection has moved, which <c>SESSION</c> shows.</summary>
public partial class TelnetServerNegotiationTests
{
	[Test]
	public async Task TelnetConnectionCountsItsBytes()
	{
		var traffic = new ConnectionTrafficCounters();
		var output = new TaskCompletionSource<Func<byte[], ValueTask>>(TaskCreationOptions.RunContinuationsAsynchronously);
		var connectionService = Substitute.For<IConnectionServerService>();
		connectionService.RegisterAsync(
				Arg.Any<long>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
				Arg.Any<Func<byte[], ValueTask>>(), Arg.Any<Func<byte[], ValueTask>>(),
				Arg.Any<Func<Encoding>>(), Arg.Any<Action>(), Arg.Any<Func<string, string, ValueTask>>(),
				Arg.Any<ProtocolCapabilities?>(), Arg.Any<string>(), Arg.Any<bool>(),
				Arg.Any<string?>(), Arg.Any<CancellationToken>())
			.Returns(call =>
			{
				output.TrySetResult(call.ArgAt<Func<byte[], ValueTask>>(4));
				return Task.CompletedTask;
			});

		var (toServer, fromServer, handler, _, cts) = StartServer(connectionService: connectionService, traffic: traffic);
		try
		{
			var write = await output.Task.WaitAsync(Timeout);
			var typed = Encoding.ASCII.GetBytes("hello\r\n");
			await WriteAsync(toServer, typed);
			await write(Encoding.ASCII.GetBytes("You typed hello."));

			var drained = await DrainAsync(fromServer);
			var counted = await CountsAsync(traffic, expected => expected.Received == typed.Length && expected.Sent == drained);

			await Assert.That(counted.Received).IsEqualTo(typed.Length);
			await Assert.That(counted.Sent).IsEqualTo(drained);
			await Assert.That(counted.Sent).IsGreaterThan("You typed hello.".Length);
			await Assert.That(counted.Pending).IsEqualTo(0);
		}
		finally
		{
			await cts.CancelAsync();
			await toServer.CompleteAsync();
			await handler.WaitAsync(Timeout);
		}

		await Assert.That(traffic.Snapshot()).IsEmpty();
	}

	/// <summary>Reads what the server wrote until it has been quiet for a moment.</summary>
	private static async Task<long> DrainAsync(PipeReader reader)
	{
		var total = 0L;
		while (true)
		{
			using var quiet = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
			try
			{
				var result = await reader.ReadAsync(quiet.Token);
				total += result.Buffer.Length;
				reader.AdvanceTo(result.Buffer.End);
			}
			catch (OperationCanceledException)
			{
				return total;
			}
		}
	}

	private static async Task<ConnectionTraffic> CountsAsync(ConnectionTrafficCounters traffic, Func<ConnectionTraffic, bool> settled)
	{
		var deadline = DateTimeOffset.UtcNow + Timeout;
		var counted = traffic.Snapshot().Single(connection => connection.Handle == 42);
		while (!settled(counted) && DateTimeOffset.UtcNow < deadline)
		{
			await Task.Delay(20);
			counted = traffic.Snapshot().Single(connection => connection.Handle == 42);
		}

		return counted;
	}
}
