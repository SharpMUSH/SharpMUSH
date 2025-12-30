using System.Collections.Concurrent;
using System.Text;
using MarkupString;
using MassTransit;
using OneOf;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Messages;

namespace SharpMUSH.Library.Services;

/// <summary>
/// Notifies objects and sends telnet data with optional batching support.
/// Batching accumulates messages before publishing to reduce Kafka overhead for iteration commands.
/// </summary>
public class NotifyService(IBus publishEndpoint, IConnectionService connections, ITelemetryService? telemetryService = null) : INotifyService
{
	private readonly ConcurrentDictionary<long, BatchingState> _batchingStates = new();
	private static readonly AsyncLocal<BatchingContext?> _batchingContext = new();

	private class BatchingState
	{
		public int RefCount { get; set; }
		public List<byte[]> AccumulatedMessages { get; } = [];
		public object Lock { get; } = new();
	}

	private class BatchingContext
	{
		public int RefCount { get; set; }
		public ConcurrentDictionary<long, List<byte[]>> AccumulatedMessages { get; } = new();
		public object Lock { get; } = new();
	}

	private class BatchingScopeDisposable(NotifyService service) : IDisposable
	{
		private bool _disposed;

		public void Dispose()
		{
			if (!_disposed)
			{
				_disposed = true;
				service.EndBatchingContextInternal().AsTask().Wait();
			}
		}
	}
	public async ValueTask Notify(DBRef who, OneOf<MString, string> what, AnySharpObject? sender, INotifyService.NotificationType type = INotifyService.NotificationType.Announce)
	{
		if (what.Match(
			markupString => MModule.getLength(markupString) == 0,
			str => str.Length == 0
		))
		{
			return;
		}

		var text = what.Match(
			markupString => markupString.ToString(),
			str => str
		);

		var bytes = Encoding.UTF8.GetBytes(text);

		// Check if we're in a batching context (AsyncLocal-based batching)
		var context = _batchingContext.Value;
		if (context != null)
		{
			// Accumulate messages for all handles in the context
			await foreach (var handle in connections.Get(who).Select(x => x.Handle))
			{
				var messages = context.AccumulatedMessages.GetOrAdd(handle, _ => []);
				lock (messages)
				{
					messages.Add(bytes);
				}
			}
			return;
		}

		// Fall back to handle-based batching or immediate publish
		var nonBatchedCount = 0;
		long startTime = 0;
		
		await foreach (var handle in connections.Get(who).Select(x => x.Handle))
		{
			// Check if this handle has an active batching scope
			if (_batchingStates.TryGetValue(handle, out var state))
			{
				// Batching is active, accumulate the message
				lock (state.Lock)
				{
					state.AccumulatedMessages.Add(bytes);
				}
			}
			else
			{
				// Start timing on first non-batched message
				if (nonBatchedCount == 0)
				{
					startTime = System.Diagnostics.Stopwatch.GetTimestamp();
				}
				
				// No batching scope active, publish immediately
				await publishEndpoint.Publish(new TelnetOutputMessage(handle, bytes));
				nonBatchedCount++;
			}
		}
		
		// Track telemetry only for non-batched messages
		if (nonBatchedCount > 0)
		{
			var elapsedMs = System.Diagnostics.Stopwatch.GetElapsedTime(startTime).TotalMilliseconds;
			telemetryService?.RecordNotificationSpeed(type.ToString(), elapsedMs, nonBatchedCount);
		}
	}

	public ValueTask Notify(AnySharpObject who, OneOf<MString, string> what, AnySharpObject? sender, INotifyService.NotificationType type = INotifyService.NotificationType.Announce)
		=> Notify(who.Object().DBRef, what, sender, type);

	public async ValueTask Notify(long handle, OneOf<MString, string> what, AnySharpObject? sender, INotifyService.NotificationType type = INotifyService.NotificationType.Announce)
	{
		if (what.Match(
			markupString => MModule.getLength(markupString) == 0,
			str => str.Length == 0
		))
		{
			return;
		}

		var text = what.Match(
			markupString => markupString.ToString(),
			str => str
		);

		var bytes = Encoding.UTF8.GetBytes(text);

		// Always use batching - if no scope is active, messages are auto-flushed
		if (_batchingStates.TryGetValue(handle, out var state))
		{
			// For batched messages, we don't track individual notification times
			// as they are accumulated and sent in batch
			lock (state.Lock)
			{
				state.AccumulatedMessages.Add(bytes);
			}
		}
		else
		{
			// No batching scope active, publish immediately and track time
			var startTime = System.Diagnostics.Stopwatch.GetTimestamp();
			await publishEndpoint.Publish(new TelnetOutputMessage(handle, bytes));
			var elapsedMs = System.Diagnostics.Stopwatch.GetElapsedTime(startTime).TotalMilliseconds;
			telemetryService?.RecordNotificationSpeed(type.ToString(), elapsedMs, 1);
		}
	}

	public async ValueTask Notify(long[] handles, OneOf<MString, string> what, AnySharpObject? sender, INotifyService.NotificationType type = INotifyService.NotificationType.Announce)
	{
		if (what.Match(
			markupString => MModule.getLength(markupString) == 0,
			str => str.Length == 0
		))
		{
			return;
		}

		var text = what.Match(
			markupString => markupString.ToString(),
			str => str
		);

		var bytes = Encoding.UTF8.GetBytes(text);

		// Always use batching - if no scope is active, messages are auto-flushed
		foreach (var handle in handles)
		{
			if (_batchingStates.TryGetValue(handle, out var state))
			{
				lock (state.Lock)
				{
					state.AccumulatedMessages.Add(bytes);
				}
			}
			else
			{
				// No batching scope active, publish immediately
				await publishEndpoint.Publish(new TelnetOutputMessage(handle, bytes));
			}
		}
	}

	public async ValueTask Prompt(DBRef who, OneOf<MString, string> what, AnySharpObject? sender, INotifyService.NotificationType type = INotifyService.NotificationType.Announce)
	{
		if (what.Match(
			markupString => MarkupStringModule.getLength(markupString) == 0,
			str => str.Length == 0
		))
		{
			return;
		}

		var text = what.Match(
			markupString => markupString.ToString(),
			str => str
		);

		var bytes = Encoding.UTF8.GetBytes(text);

		await foreach (var handle in connections.Get(who).Select(x => x.Handle))
		{
			await publishEndpoint.Publish(new TelnetPromptMessage(handle, bytes));
		}
	}

	public ValueTask Prompt(AnySharpObject who, OneOf<MString, string> what, AnySharpObject? sender, INotifyService.NotificationType type = INotifyService.NotificationType.Announce)
		=> Prompt(who.Object().DBRef, what, sender, type);

	public async ValueTask Prompt(long handle, OneOf<MString, string> what, AnySharpObject? sender, INotifyService.NotificationType type = INotifyService.NotificationType.Announce)
		=> await Prompt([handle], what, sender, type);

	public async ValueTask Prompt(long[] handles, OneOf<MString, string> what, AnySharpObject? sender, INotifyService.NotificationType type = INotifyService.NotificationType.Announce)
	{
		if (what.Match(
			markupString => MarkupStringModule.getLength(markupString) == 0,
			str => str.Length == 0
		))
		{
			return;
		}

		var text = what.Match(
			markupString => markupString.ToString(),
			str => str
		);

		var bytes = Encoding.UTF8.GetBytes(text);

		// Publish prompt message to each handle
		foreach (var handle in handles)
		{
			await publishEndpoint.Publish(new TelnetPromptMessage(handle, bytes));
		}
	}

	public async ValueTask NotifyExcept(DBRef who, OneOf<MString, string> what, DBRef[] except, AnySharpObject? sender, INotifyService.NotificationType type = INotifyService.NotificationType.Announce)
	{
		// TODO: Implement when we have DBRef to handle mapping
		await ValueTask.CompletedTask;
	}

	public ValueTask NotifyExcept(AnySharpObject who, OneOf<MString, string> what, DBRef[] except, AnySharpObject? sender, INotifyService.NotificationType type = INotifyService.NotificationType.Announce)
		=> NotifyExcept(who.Object().DBRef, what, except, sender, type);

	public async ValueTask NotifyExcept(AnySharpObject who, OneOf<MString, string> what, AnySharpObject[] except, AnySharpObject? sender, INotifyService.NotificationType type = INotifyService.NotificationType.Announce)
		=> await NotifyExcept(who.Object().DBRef, what, except.Select(x => x.Object().DBRef).ToArray(), sender, type);

	public void BeginBatchingScope(long handle)
	{
		var state = _batchingStates.GetOrAdd(handle, _ => new BatchingState());
		lock (state.Lock)
		{
			state.RefCount++;
		}
	}

	public async ValueTask EndBatchingScope(long handle)
	{
		if (!_batchingStates.TryGetValue(handle, out var state))
		{
			return;
		}

		List<byte[]> messagesToFlush;
		bool shouldRemove;

		lock (state.Lock)
		{
			state.RefCount--;
			shouldRemove = state.RefCount <= 0;
			messagesToFlush = shouldRemove ? [.. state.AccumulatedMessages] : [];
		}

		if (shouldRemove)
		{
			_batchingStates.TryRemove(handle, out _);

			// Combine all accumulated messages with newlines and publish as one message
			if (messagesToFlush.Count > 0)
			{
				var totalSize = messagesToFlush.Sum(m => m.Length) + (messagesToFlush.Count - 1) * 2; // +2 for \r\n
				var combined = new byte[totalSize];
				var offset = 0;

				for (var i = 0; i < messagesToFlush.Count; i++)
				{
					var message = messagesToFlush[i];
					Array.Copy(message, 0, combined, offset, message.Length);
					offset += message.Length;

					// Add \r\n between messages (not after the last one)
					if (i < messagesToFlush.Count - 1)
					{
						combined[offset++] = (byte)'\r';
						combined[offset++] = (byte)'\n';
					}
				}

				await publishEndpoint.Publish(new TelnetOutputMessage(handle, combined));
			}
		}
	}

	/// <summary>
	/// Begin a context-based batching scope that batches notifications to ANY target.
	/// Returns an IDisposable that should be disposed to end the scope and flush messages.
	/// Supports ref-counting for nested scopes.
	/// </summary>
	public IDisposable BeginBatchingContext()
	{
		var context = _batchingContext.Value;
		if (context != null)
		{
			// Already in a batching context, increment ref count
			lock (context.Lock)
			{
				context.RefCount++;
			}
		}
		else
		{
			// Create new batching context
			_batchingContext.Value = new BatchingContext { RefCount = 1 };
		}

		return new BatchingScopeDisposable(this);
	}

	private async ValueTask EndBatchingContextInternal()
	{
		var context = _batchingContext.Value;
		if (context == null)
		{
			return;
		}

		Dictionary<long, List<byte[]>>? messagesToFlush = null;

		lock (context.Lock)
		{
			context.RefCount--;
			if (context.RefCount <= 0)
			{
				// This is the outermost scope, collect all messages to flush
				messagesToFlush = new Dictionary<long, List<byte[]>>(context.AccumulatedMessages);
				_batchingContext.Value = null;
			}
		}

		// Flush messages outside the lock
		if (messagesToFlush != null)
		{
			foreach (var (handle, messages) in messagesToFlush)
			{
				if (messages.Count > 0)
				{
					var totalSize = messages.Sum(m => m.Length) + (messages.Count - 1) * 2; // +2 for \r\n
					var combined = new byte[totalSize];
					var offset = 0;

					for (var i = 0; i < messages.Count; i++)
					{
						var message = messages[i];
						Array.Copy(message, 0, combined, offset, message.Length);
						offset += message.Length;

						// Add \r\n between messages (not after the last one)
						if (i < messages.Count - 1)
						{
							combined[offset++] = (byte)'\r';
							combined[offset++] = (byte)'\n';
						}
					}

					await publishEndpoint.Publish(new TelnetOutputMessage(handle, combined));
				}
			}
		}
	}

	/// <summary>
	/// Unified error handling: optionally notify user, then return error.
	/// The notify message and error return are SEPARATE and can be different strings.
	/// Callers choose which error and notification to use via ErrorMessages constants.
	/// </summary>
	/// <param name="target">Object to notify (DBRef)</param>
	/// <param name="errorReturn">Error string for return value (e.g., "#-1 PERMISSION DENIED")</param>
	/// <param name="notifyMessage">Message to show user (e.g., "You don't have permission to do that.")</param>
	/// <param name="shouldNotify">Whether to send notification to user (required parameter)</param>
	/// <returns>CallState with error return string</returns>
	public async ValueTask<CallState> NotifyAndReturn(
		DBRef target,
		string errorReturn,
		string notifyMessage,
		bool shouldNotify)
	{
		if (shouldNotify)
		{
			await Notify(target, notifyMessage, sender: null);
		}
		
		return new CallState(errorReturn);
	}
}