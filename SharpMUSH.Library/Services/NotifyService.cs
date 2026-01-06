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
/// Notifies objects and sends telnet data with automatic batching support.
/// All notifications are automatically batched with a 10ms timeout to reduce Kafka overhead.
/// Messages are accumulated and flushed after 10ms of inactivity, or immediately when explicitly flushed.
/// </summary>
public class NotifyService(IBus publishEndpoint, IConnectionService connections) : INotifyService
{
	private readonly ConcurrentDictionary<long, BatchingState> _batchingStates = new();
	private static readonly AsyncLocal<BatchingContext?> _batchingContext = new();

	private class BatchingState
	{
		public List<byte[]> AccumulatedMessages { get; } = [];
		public object Lock { get; } = new();
		public Timer? FlushTimer { get; set; }
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

		// Always use automatic batching with 10ms timeout
		await foreach (var handle in connections.Get(who).Select(x => x.Handle))
		{
			AddToBatch(handle, bytes);
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

		// Always use automatic batching with 10ms timeout
		AddToBatch(handle, bytes);
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

		// Always use automatic batching with 10ms timeout
		foreach (var handle in handles)
		{
			AddToBatch(handle, bytes);
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
		if (what.Match(
			markupString => MModule.getLength(markupString) == 0,
			str => str.Length == 0
		))
		{
			return;
		}

		// Get all handles for the target location/object
		var targetHandles = await connections.Get(who).Select(x => x.Handle).ToArrayAsync();
		
		// Get all handles to exclude
		var excludeHandles = new HashSet<long>();
		foreach (var exceptDbRef in except)
		{
			await foreach (var conn in connections.Get(exceptDbRef))
			{
				excludeHandles.Add(conn.Handle);
			}
		}
		
		// Filter out excluded handles and notify the rest
		var notifyHandles = targetHandles.Where(h => !excludeHandles.Contains(h)).ToArray();
		
		if (notifyHandles.Length > 0)
		{
			await Notify(notifyHandles, what, sender, type);
		}
	}

	public ValueTask NotifyExcept(AnySharpObject who, OneOf<MString, string> what, DBRef[] except, AnySharpObject? sender, INotifyService.NotificationType type = INotifyService.NotificationType.Announce)
		=> NotifyExcept(who.Object().DBRef, what, except, sender, type);

	public async ValueTask NotifyExcept(AnySharpObject who, OneOf<MString, string> what, AnySharpObject[] except, AnySharpObject? sender, INotifyService.NotificationType type = INotifyService.NotificationType.Announce)
		=> await NotifyExcept(who.Object().DBRef, what, except.Select(x => x.Object().DBRef).ToArray(), sender, type);

	public void BeginBatchingScope(long handle)
	{
		// No-op: Batching is now always active with 10ms timeout
		// This method is kept for backward compatibility
	}

	public async ValueTask EndBatchingScope(long handle)
	{
		// Flush immediately instead of waiting for timer
		// This maintains backward compatibility for code that expects immediate flushing
		await FlushHandle(handle);
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
	/// Adds a message to the batch for the specified handle and starts/resets the 10ms flush timer.
	/// </summary>
	private void AddToBatch(long handle, byte[] bytes)
	{
		var state = _batchingStates.GetOrAdd(handle, _ => new BatchingState());
		lock (state.Lock)
		{
			state.AccumulatedMessages.Add(bytes);

			// Start or reset the timer to 10ms
			if (state.FlushTimer == null)
			{
				state.FlushTimer = new Timer(
					_ =>
					{
						try
						{
							// Fire-and-forget pattern - don't block the timer thread
							_ = Task.Run(async () => await FlushHandle(handle));
						}
						catch (Exception)
						{
							// Suppress exceptions to prevent timer crashes
							// In production, this should log the exception
						}
					},
					null,
					10,
					Timeout.Infinite
				);
			}
			else
			{
				state.FlushTimer.Change(10, Timeout.Infinite);
			}
		}
	}

	/// <summary>
	/// Flushes accumulated messages for a specific handle.
	/// Called automatically by the 10ms timer or manually by EndBatchingScope.
	/// </summary>
	private async Task FlushHandle(long handle)
	{
		if (!_batchingStates.TryGetValue(handle, out var state))
		{
			return;
		}

		List<byte[]>? messagesToFlush = null;
		bool shouldRemoveState = false;
		lock (state.Lock)
		{
			if (state.AccumulatedMessages.Count == 0)
			{
				// No messages to flush. Clean up the state if this was a manual flush (EndBatchingScope)
				// indicated by a null timer. For timer-based flushes, keep the state for potential reuse.
				shouldRemoveState = state.FlushTimer == null;
			}
			else
			{
				messagesToFlush = [.. state.AccumulatedMessages];
				state.AccumulatedMessages.Clear();
				state.FlushTimer?.Dispose();
				state.FlushTimer = null;
				
				// Always remove state after flushing to prevent memory leak.
				// A new state will be created automatically when the next message arrives.
				shouldRemoveState = true;
			}
		}

		if (shouldRemoveState)
		{
			_batchingStates.TryRemove(handle, out _);
		}

		// Combine all accumulated messages with newlines and publish as one message
		if (messagesToFlush?.Count > 0)
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