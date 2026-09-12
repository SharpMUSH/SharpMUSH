using System.Collections.Concurrent;
using System.Text;
using Mediator;
using NSubstitute;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// <see cref="ConnectionService.ListenState"/> can be called at any time: a singleton resolved lazily
/// registers its listener whenever it is first needed, which may be while a connection is changing state.
/// </summary>
public class ConnectionServiceStateListenerTests
{
	private static async Task RegisterAsync(ConnectionService connections, long handle) =>
		await connections.Register(handle, "127.0.0.1", "localhost", "websocket",
			_ => ValueTask.CompletedTask,
			_ => ValueTask.CompletedTask,
			() => Encoding.UTF8,
			new ConcurrentDictionary<string, string>(new Dictionary<string, string>
			{
				["ConnectionStartTime"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(),
				["LastConnectionSignal"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString()
			}));

	/// <summary>
	/// A listener added while the listeners are being told of a change does not break that change, and
	/// hears the next one.
	/// </summary>
	[Test]
	public async Task AListenerAddedDuringANotificationHearsTheNextChange()
	{
		var connections = new ConnectionService(Substitute.For<IPublisher>());
		var late = new List<IConnectionService.ConnectionState>();
		var added = false;
		connections.ListenState(_ =>
		{
			if (added) return;
			added = true;
			connections.ListenState(change => late.Add(change.Item4));
		});
		await RegisterAsync(connections, 1);

		await connections.Bind(1, new DBRef(400, 0));
		await connections.Disconnect(1);

		await Assert.That(late).Contains(IConnectionService.ConnectionState.Disconnected);
		await Assert.That(late).DoesNotContain(IConnectionService.ConnectionState.Connected)
			.Because("the notification it was added during walks the listeners as they stood when it began");
	}

	/// <summary>
	/// Listeners registering from another thread while connections come and go.
	/// </summary>
	/// <remarks>
	/// A fixed number of registrations rather than as many as two seconds allows: each one copies the
	/// listener array and every later state change calls everything registered so far, so an open-ended
	/// loop spends the test allocating ever larger arrays instead of interleaving the two operations.
	/// </remarks>
	[Test]
	public async Task ListenersCanBeAddedWhileConnectionsChangeState()
	{
		const int registrations = 200;
		var connections = new ConnectionService(Substitute.For<IPublisher>());
		using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(30));

		var registering = Task.Run(async () =>
		{
			for (var i = 0; i < registrations; i++)
			{
				connections.ListenState(_ => { });
				await Task.Yield();
			}
		}, stop.Token);

		var changing = Task.Run(async () =>
		{
			for (var handle = 1L; !registering.IsCompleted; handle++)
			{
				stop.Token.ThrowIfCancellationRequested();
				await RegisterAsync(connections, handle);
				await connections.Bind(handle, new DBRef(400, 0));
				await connections.Disconnect(handle);
			}
		}, stop.Token);

		await Task.WhenAll(registering, changing);
	}
}
