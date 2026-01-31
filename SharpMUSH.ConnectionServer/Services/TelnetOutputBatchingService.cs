using System.Collections.Concurrent;
using System.Diagnostics;
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
	private const int MaxBatchDelayMs = 1; // Flush after 1ms (near-instant for interactive use)
	private const int FlushTimerIntervalMs = 1; // Check for flushes every 1ms
	
	// Metrics for debugging
	private long _totalMessagesReceived = 0;
	private long _totalBatchesFlushed = 0;
	private long _totalMessagesInBatches = 0;
	private long _flushesFromSize = 0;
	private long _flushesFromTimeout = 0;
	private long _totalTcpWriteTimeMs = 0;

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
		Interlocked.Increment(ref _totalMessagesReceived);
		_logger.LogInformation("Adding message for handle {Handle} ({Bytes} bytes)", handle, data.Length);
		
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
				Interlocked.Increment(ref _flushesFromSize);
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
						Interlocked.Increment(ref _flushesFromTimeout);
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

		var messageCount = buffer.Messages.Count;
		Interlocked.Increment(ref _totalBatchesFlushed);
		Interlocked.Add(ref _totalMessagesInBatches, messageCount);

		var connection = _connectionService.Get(handle);
		if (connection == null)
		{
			_logger.LogWarning("Cannot flush {Count} messages for unknown connection handle: {Handle}",
				messageCount, handle);
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

			// Measure TCP write time
			var sw = Stopwatch.StartNew();
			connection.OutputFunction(combined).GetAwaiter().GetResult();
			sw.Stop();
			
			Interlocked.Add(ref _totalTcpWriteTimeMs, sw.ElapsedMilliseconds);

			_logger.LogInformation("Flushed {Count} messages ({Bytes} bytes) to connection {Handle} in {TcpWriteMs}ms",
				messageCount, totalLength, handle, sw.ElapsedMilliseconds);

			buffer.Messages.Clear();
			buffer.LastFlushTime = DateTime.UtcNow;
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error flushing {Count} messages to connection {Handle}",
				messageCount, handle);
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

	/// <summary>
	/// Gets current batching metrics for debugging/monitoring.
	/// </summary>
	public (long MessagesReceived, long BatchesFlushed, double AvgBatchSize, long FlushesFromSize, long FlushesFromTimeout, long TotalTcpWriteTimeMs) GetMetrics()
	{
		var batchesFlushed = Interlocked.Read(ref _totalBatchesFlushed);
		var messagesInBatches = Interlocked.Read(ref _totalMessagesInBatches);
		var avgBatchSize = batchesFlushed > 0 ? (double)messagesInBatches / batchesFlushed : 0;
		
		return (
			Interlocked.Read(ref _totalMessagesReceived),
			batchesFlushed,
			avgBatchSize,
			Interlocked.Read(ref _flushesFromSize),
			Interlocked.Read(ref _flushesFromTimeout),
			Interlocked.Read(ref _totalTcpWriteTimeMs)
		);
	}

	/// <summary>
	/// Logs current batching metrics.
	/// </summary>
	public void LogMetrics()
	{
		var metrics = GetMetrics();
		_logger.LogInformation(
			"Batching Metrics: Messages={Messages}, Batches={Batches}, AvgBatchSize={AvgBatchSize:F2}, " +
			"FlushSize={FlushSize}, FlushTimeout={FlushTimeout}, TcpWriteTime={TcpWriteMs}ms",
			metrics.MessagesReceived, metrics.BatchesFlushed, metrics.AvgBatchSize,
			metrics.FlushesFromSize, metrics.FlushesFromTimeout, metrics.TotalTcpWriteTimeMs);
	}

	public void Dispose()
	{
		_flushTimer?.Dispose();
	}
}
