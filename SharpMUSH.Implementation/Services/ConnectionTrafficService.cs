using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Messaging.Abstractions;
using SharpMUSH.Messaging.Messages;

namespace SharpMUSH.Implementation.Services;

/// <inheritdoc />
/// <remarks>
/// One <see cref="ConnectionTrafficRequestMessage"/> per call, answered by the connection server's
/// <see cref="ConnectionTrafficReportMessage"/>. The caller holds the command queue while it waits, so the
/// wait is short: a connection server that is down costs <see cref="Wait"/>, and the counts are left out.
/// </remarks>
public sealed class ConnectionTrafficService(IMessageBus bus, ILogger<ConnectionTrafficService> logger)
	: IConnectionTrafficService
{
	/// <summary>How long a request waits for the connection server.</summary>
	public static readonly TimeSpan Wait = TimeSpan.FromMilliseconds(500);

	private readonly ConcurrentDictionary<Guid, TaskCompletionSource<IReadOnlyList<ConnectionTraffic>>> _pending = new();

	public async ValueTask<IReadOnlyDictionary<long, ConnectionTraffic>> GetAsync(CancellationToken cancellationToken = default)
	{
		var requestId = Guid.NewGuid();
		var answer = new TaskCompletionSource<IReadOnlyList<ConnectionTraffic>>(TaskCreationOptions.RunContinuationsAsynchronously);
		_pending[requestId] = answer;
		try
		{
			await bus.Publish(new ConnectionTrafficRequestMessage(requestId), cancellationToken);
			var connections = await answer.Task.WaitAsync(Wait, cancellationToken);
			return connections.ToDictionary(connection => connection.Handle);
		}
		catch (TimeoutException)
		{
			logger.LogWarning("The connection server did not report connection traffic within {Wait}", Wait);
			return new Dictionary<long, ConnectionTraffic>();
		}
		finally
		{
			_pending.TryRemove(requestId, out _);
		}
	}

	public void Complete(Guid requestId, IReadOnlyList<ConnectionTraffic> connections)
	{
		if (_pending.TryGetValue(requestId, out var answer))
		{
			answer.TrySetResult(connections);
		}
	}
}
