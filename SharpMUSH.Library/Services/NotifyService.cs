using System.Collections.Concurrent;
using System.Text;
using MarkupString;
using SharpMUSH.Messaging.Abstractions;
using Mediator;
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
/// Messages are accumulated and flushed after 10ms of inactivity.
/// </summary>
public class NotifyService(
	IMessageBus publishEndpoint, 
	IConnectionService connections,
	IListenerRoutingService? listenerRoutingService = null,
	Mediator.IMediator? mediator = null) : INotifyService
{
	private readonly ConcurrentDictionary<long, BatchingState> _batchingStates = new();

	private class BatchingState
	{
		public List<byte[]> AccumulatedMessages { get; } = [];
		public object Lock { get; } = new();
		public Timer? FlushTimer { get; set; }
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

		// Route to listeners if service is available and we have location context
		if (listenerRoutingService != null && mediator != null && sender != null)
		{
			try
			{
				// Determine the location for listener routing
				var location = await sender.Match<ValueTask<DBRef>>(
					async player => (await player.Location.WithCancellation(CancellationToken.None)).Object().DBRef,
					room => ValueTask.FromResult(room.Object.DBRef),
					async exit => (await exit.Location.WithCancellation(CancellationToken.None)).Object().DBRef,
					async thing => (await thing.Location.WithCancellation(CancellationToken.None)).Object().DBRef
				);
				
				var notificationContext = new NotificationContext(
					Target: who,
					Location: location,
					IsRoomBroadcast: false,
					ExcludedObjects: []
				);
				
				// Fire and forget - don't await to avoid blocking notification
				_ = listenerRoutingService.ProcessNotificationAsync(notificationContext, what, sender, type);
			}
			catch
			{
				// Silently ignore errors in listener routing to not block notifications
			}
		}

		var text = what.Match(
			markupString => markupString.ToString(),
			str => str
		);

		var bytes = Encoding.UTF8.GetBytes(text);

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