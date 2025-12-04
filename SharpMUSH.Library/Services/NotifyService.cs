using System.Collections.Concurrent;
using System.Text;
using MarkupString;
using MassTransit;
using OneOf;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
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

	private class BatchingState
	{
		public int RefCount { get; set; }
		public List<byte[]> AccumulatedMessages { get; } = [];
		public object Lock { get; } = new();
	}
	public async ValueTask Notify(DBRef who, OneOf<MString, string> what, AnySharpObject? sender, INotifyService.NotificationType type = INotifyService.NotificationType.Announce)
	{
		var startTime = System.Diagnostics.Stopwatch.GetTimestamp();
		await ValueTask.CompletedTask;
		
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

		var recipientCount = 0;
		await foreach (var handle in connections.Get(who).Select(x => x.Handle))
		{
			await publishEndpoint.Publish(new TelnetOutputMessage(handle, bytes));
			recipientCount++;
		}
		
		var elapsedMs = System.Diagnostics.Stopwatch.GetElapsedTime(startTime).TotalMilliseconds;
		telemetryService?.RecordNotificationSpeed(type.ToString(), elapsedMs, recipientCount);
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

		var startTime = System.Diagnostics.Stopwatch.GetTimestamp();
		
		// Always use batching - if no scope is active, messages are auto-flushed
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
		
		var elapsedMs = System.Diagnostics.Stopwatch.GetElapsedTime(startTime).TotalMilliseconds;
		telemetryService?.RecordNotificationSpeed(type.ToString(), elapsedMs, 1);
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
}