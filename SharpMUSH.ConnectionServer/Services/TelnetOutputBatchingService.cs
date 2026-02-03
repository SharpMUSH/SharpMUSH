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
	private readonly IOutputTransformService _transformService;
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
		IOutputTransformService transformService,
		ILogger<TelnetOutputBatchingService> logger)
	{
		_connectionService = connectionService;
		_transformService = transformService;
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
			
			// Transform the data based on connection capabilities and preferences
			var connection = _connectionService.Get(handle);
			if (connection != null)
			{
				try
				{
					var transformedData = _transformService.Transform(
						data,
						connection.Capabilities,
						connection.Preferences);
					buffer.Messages.Add(transformedData);
				}
				catch (Exception ex)
				{
					_logger.LogError(ex, "Error transforming message for connection {Handle}, using original", handle);
					buffer.Messages.Add(data);
				}
			}
			else
			{
				// Connection not found, add original data (will be logged during flush)
				buffer.Messages.Add(data);
			}
			
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
		
		// Process each buffer independently. Use Task.Run to avoid blocking the timer thread
		// while still ensuring sequential processing per connection.
		foreach (var (handle, buffer) in _buffers)
		{
			// Check if we need to flush without holding the lock for too long
			bool shouldFlush = false;
			lock (buffer.Lock)
			{
				if (buffer.Messages.Count > 0)
				{
					var timeSinceFirst = (now - buffer.FirstMessageTime).TotalMilliseconds;
					shouldFlush = timeSinceFirst >= MaxBatchDelayMs;
				}
			}
			
			if (shouldFlush)
			{
				Interlocked.Increment(ref _flushesFromTimeout);
				// Fire and forget - don't wait for the flush to complete
				// Each connection's buffer has its own lock to ensure sequential processing
				_ = Task.Run(async () =>
				{
					try
					{
						await FlushBufferAsync(handle, buffer);
					}
					catch (Exception ex)
					{
						_logger.LogError(ex, "Unhandled error in timer-triggered flush for connection {Handle}", handle);
					}
				});
			}
		}
	}

	/// <summary>
	/// Flushes a specific buffer by combining all messages and sending via TCP.
	/// This async version properly handles the async OutputFunction call.
	/// </summary>
	private async Task FlushBufferAsync(long handle, MessageBuffer buffer)
	{
		List<byte[]>? messagesToFlush = null;
		int messageCount = 0;
		
		// Collect messages to flush while holding the lock
		lock (buffer.Lock)
		{
			if (buffer.Messages.Count == 0)
			{
				return;
			}
			
			messageCount = buffer.Messages.Count;
			messagesToFlush = new List<byte[]>(buffer.Messages);
			buffer.Messages.Clear();
		}
		
		// Perform the actual flush outside the lock to avoid blocking other operations
		Interlocked.Increment(ref _totalBatchesFlushed);
		Interlocked.Add(ref _totalMessagesInBatches, messageCount);

		var connection = _connectionService.Get(handle);
		if (connection == null)
		{
			_logger.LogWarning("Cannot flush {Count} messages for unknown connection handle: {Handle}",
				messageCount, handle);
			return;
		}

		try
		{
			// Combine all messages into a single buffer
			var totalLength = messagesToFlush.Sum(m => m.Length);
			var combined = new byte[totalLength];
			var offset = 0;

			foreach (var message in messagesToFlush)
			{
				Buffer.BlockCopy(message, 0, combined, offset, message.Length);
				offset += message.Length;
			}

			// Measure TCP write time
			var sw = Stopwatch.StartNew();
			await connection.OutputFunction(combined);
			sw.Stop();
			
			Interlocked.Add(ref _totalTcpWriteTimeMs, sw.ElapsedMilliseconds);

			_logger.LogInformation("Flushed {Count} messages ({Bytes} bytes) to connection {Handle} in {TcpWriteMs}ms",
				messageCount, totalLength, handle, sw.ElapsedMilliseconds);

			lock (buffer.Lock)
			{
				buffer.LastFlushTime = DateTime.UtcNow;
			}
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error flushing {Count} messages to connection {Handle}",
				messageCount, handle);
		}
	}
	
	/// <summary>
	/// Synchronous wrapper for FlushBufferAsync for compatibility with AddMessage.
	/// Used when immediate flush is needed due to batch size limit.
	/// </summary>
	private void FlushBuffer(long handle, MessageBuffer buffer)
	{
		// Fire and forget to avoid blocking AddMessage
		_ = Task.Run(async () =>
		{
			try
			{
				await FlushBufferAsync(handle, buffer);
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Unhandled error in size-triggered flush for connection {Handle}", handle);
			}
		});
	}

	/// <summary>
	/// Flushes all buffers. Called during shutdown.
	/// </summary>
	private void FlushAllBuffers()
	{
		var tasks = new List<Task>();
		
		foreach (var (handle, buffer) in _buffers)
		{
			// Launch async flush for each buffer
			tasks.Add(FlushBufferAsync(handle, buffer));
		}
		
		// Wait for all flushes to complete during shutdown with a timeout
		var completed = Task.WaitAll(tasks.ToArray(), TimeSpan.FromSeconds(5));
		
		if (!completed)
		{
			_logger.LogWarning("Not all buffers flushed within 5 seconds during shutdown. {Count} buffers pending.",
				tasks.Count(t => !t.IsCompleted));
		}
	}

	/// <summary>
	/// Removes the buffer for a disconnected connection.
	/// </summary>
	public void RemoveConnection(long handle)
	{
		if (_buffers.TryRemove(handle, out var buffer))
		{
			// Flush any remaining messages asynchronously
			_ = Task.Run(async () =>
			{
				try
				{
					await FlushBufferAsync(handle, buffer);
				}
				catch (Exception ex)
				{
					_logger.LogError(ex, "Error flushing buffer during connection removal for handle {Handle}", handle);
				}
			});
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
