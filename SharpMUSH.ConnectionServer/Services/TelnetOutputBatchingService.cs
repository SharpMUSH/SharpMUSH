using System.Collections.Concurrent;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace SharpMUSH.ConnectionServer.Services;

/// <summary>
/// Batches telnet output messages before sending them over TCP.
/// This solves the @dolist performance issue by combining multiple sequential messages
/// into a single TCP write operation, reducing overhead from 1000 writes to ~10 writes.
/// </summary>
public class TelnetOutputBatchingService : IHostedService, IDisposable
{
	private class MessageBuffer
	{
		public List<byte[]> Messages { get; } = new();
		public DateTime FirstMessageTime { get; set; }
		public DateTime LastFlushTime { get; set; }
		public readonly object Lock = new();
	}

	private readonly IConnectionServerService _connectionService;
	private readonly ILogger<TelnetOutputBatchingService> _logger;
	private readonly ConcurrentDictionary<long, MessageBuffer> _buffers = new();
	private Timer? _flushTimer;
	
	// Configuration
	private const int MaxBatchSize = 100; // Flush after 100 messages
	private const int MaxBatchDelayMs = 10; // Flush after 10ms
	private const int FlushTimerIntervalMs = 5; // Check for flushes every 5ms

	public TelnetOutputBatchingService(
		IConnectionServerService connectionService,
		ILogger<TelnetOutputBatchingService> logger)
	{
		_connectionService = connectionService;
		_logger = logger;
	}

	public Task StartAsync(CancellationToken cancellationToken)
	{
		_logger.LogInformation("Starting telnet output batching service (batch size: {MaxBatchSize}, delay: {MaxBatchDelayMs}ms)",
			MaxBatchSize, MaxBatchDelayMs);
		
		// Start timer to periodically flush buffers
		_flushTimer = new Timer(OnFlushTimer, null, TimeSpan.Zero, TimeSpan.FromMilliseconds(FlushTimerIntervalMs));
		
		return Task.CompletedTask;
	}

	public Task StopAsync(CancellationToken cancellationToken)
	{
		_logger.LogInformation("Stopping telnet output batching service");
		
		_flushTimer?.Change(Timeout.Infinite, 0);
		
		// Flush any remaining buffers
		FlushAllBuffers();
		
		return Task.CompletedTask;
	}

	/// <summary>
	/// Adds a message to the batch buffer for the specified connection.
	/// Messages are either flushed immediately (if batch is full) or on the next timer tick.
	/// </summary>
	public void AddMessage(long handle, byte[] data)
	{
		var buffer = _buffers.GetOrAdd(handle, _ => new MessageBuffer());
		
		lock (buffer.Lock)
		{
			if (buffer.Messages.Count == 0)
			{
				buffer.FirstMessageTime = DateTime.UtcNow;
			}
			
			buffer.Messages.Add(data);
			
			// Immediate flush if batch is full
			if (buffer.Messages.Count >= MaxBatchSize)
			{
				FlushBuffer(handle, buffer);
			}
		}
	}

	/// <summary>
	/// Timer callback that checks all buffers and flushes those that have exceeded the time limit.
	/// </summary>
	private void OnFlushTimer(object? state)
	{
		var now = DateTime.UtcNow;
		
		foreach (var (handle, buffer) in _buffers)
		{
			lock (buffer.Lock)
			{
				if (buffer.Messages.Count > 0)
				{
					var timeSinceFirst = (now - buffer.FirstMessageTime).TotalMilliseconds;
					
					if (timeSinceFirst >= MaxBatchDelayMs)
					{
						FlushBuffer(handle, buffer);
					}
				}
			}
		}
	}

	/// <summary>
	/// Flushes a specific buffer by combining all messages and sending via TCP.
	/// Must be called while holding buffer.Lock.
	/// </summary>
	private void FlushBuffer(long handle, MessageBuffer buffer)
	{
		if (buffer.Messages.Count == 0)
		{
			return;
		}

		var connection = _connectionService.Get(handle);
		if (connection == null)
		{
			_logger.LogWarning("Cannot flush {Count} messages for unknown connection handle: {Handle}",
				buffer.Messages.Count, handle);
			buffer.Messages.Clear();
			return;
		}

		try
		{
			// Combine all messages into a single buffer
			var totalLength = buffer.Messages.Sum(m => m.Length);
			var combined = new byte[totalLength];
			var offset = 0;

			foreach (var message in buffer.Messages)
			{
				Buffer.BlockCopy(message, 0, combined, offset, message.Length);
				offset += message.Length;
			}

			// Single TCP write for all batched messages
			connection.OutputFunction(combined).AsTask().Wait();

			_logger.LogDebug("Flushed {Count} messages ({Bytes} bytes) to connection {Handle}",
				buffer.Messages.Count, totalLength, handle);

			buffer.Messages.Clear();
			buffer.LastFlushTime = DateTime.UtcNow;
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error flushing {Count} messages to connection {Handle}",
				buffer.Messages.Count, handle);
			buffer.Messages.Clear();
		}
	}

	/// <summary>
	/// Flushes all buffers. Called during shutdown.
	/// </summary>
	private void FlushAllBuffers()
	{
		foreach (var (handle, buffer) in _buffers)
		{
			lock (buffer.Lock)
			{
				FlushBuffer(handle, buffer);
			}
		}
	}

	/// <summary>
	/// Removes the buffer for a disconnected connection.
	/// </summary>
	public void RemoveConnection(long handle)
	{
		if (_buffers.TryRemove(handle, out var buffer))
		{
			lock (buffer.Lock)
			{
				FlushBuffer(handle, buffer);
			}
		}
	}

	public void Dispose()
	{
		_flushTimer?.Dispose();
	}
}
