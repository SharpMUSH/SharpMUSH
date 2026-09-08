using System.Runtime.CompilerServices;

namespace SharpMUSH.ConnectionServer.Services;

/// <summary>Orders lifecycle events from separate NATS subjects before notifying sockets.</summary>
public sealed class EngineLifecycleNoticeService(
	IConnectionServerService connections,
	ILogger<EngineLifecycleNoticeService> logger)
{
	private readonly SemaphoreSlim _transition = new(1, 1);
	private readonly ConditionalWeakTable<Func<byte[], ValueTask>, SemaphoreSlim> _writes = new();
	private DateTimeOffset _lastEvent = DateTimeOffset.MinValue;

	public async Task NotifyAsync(DateTimeOffset timestamp, bool ready, CancellationToken ct = default)
	{
		await _transition.WaitAsync(ct);
		try
		{
			if (timestamp <= _lastEvent) return;
			_lastEvent = timestamp;
			var text = ready
				? "\r\n[SharpMUSH] The game is ready. You may continue.\r\n"
				: "\r\n[SharpMUSH] The game is restarting. Your connection will remain open; please wait.\r\n";
			await Parallel.ForEachAsync(connections.GetAll(), new ParallelOptions
			{
				MaxDegreeOfParallelism = 16,
				CancellationToken = ct
			}, async (connection, token) =>
			{
				var gate = _writes.GetValue(connection.OutputFunction, _ => new SemaphoreSlim(1, 1));
				if (!await gate.WaitAsync(0, token)) return;
				async Task SendAsync()
				{
					try { await connection.OutputFunction(connection.EncodingFunction().GetBytes(text)); }
					finally { gate.Release(); }
				}
				try
				{
					await SendAsync().WaitAsync(TimeSpan.FromSeconds(2), token);
				}
				catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
				{
					logger.LogWarning(ex, "Could not send engine lifecycle notice to {Handle}", connection.Handle);
				}
			});
		}
		finally
		{
			_transition.Release();
		}
	}
}
