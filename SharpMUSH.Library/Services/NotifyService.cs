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
/// Notifies objects and sends telnet data.
/// This is now a wrapper that delegates to an inner INotifyService implementation
/// (typically MessageQueueNotifyService in distributed architecture).
/// </summary>
/// <param name="innerService">The actual notify service implementation to delegate to</param>
public class NotifyService(IBus publishEndpoint, IConnectionService connections) : INotifyService
{
	/// <summary>
	/// Buffers for accumulating output per handle when buffering is enabled
	/// Key: handle, Value: list of byte arrays to send
	/// </summary>
	private readonly System.Collections.Concurrent.ConcurrentDictionary<long, List<byte[]>> _buffers = new();

	/// <summary>
	/// Tracks which handles have buffering enabled
	/// Key: handle, Value: unused (using as a set)
	/// </summary>
	private readonly System.Collections.Concurrent.ConcurrentDictionary<long, byte> _bufferingEnabled = new();

	/// <summary>
	/// Lock object for buffer operations to ensure thread safety
	/// </summary>
	private readonly object _bufferLock = new();
	public async ValueTask Notify(DBRef who, OneOf<MString, string> what, AnySharpObject? sender, INotifyService.NotificationType type = INotifyService.NotificationType.Announce)
	{
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

		await foreach (var handle in connections.Get(who).Select(x => x.Handle))
		{
			await publishEndpoint.Publish(new TelnetOutputMessage(handle, bytes));
		}
	}

	public ValueTask Notify(AnySharpObject who, OneOf<MString, string> what, AnySharpObject? sender, INotifyService.NotificationType type = INotifyService.NotificationType.Announce)
		=> Notify(who.Object().DBRef, what, sender, type);

	public async ValueTask Notify(long handle, OneOf<MString, string> what, AnySharpObject? sender, INotifyService.NotificationType type = INotifyService.NotificationType.Announce)
		=> await Notify([handle], what, sender, type);

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

		// Publish output message to each handle
		foreach (var handle in handles)
		{
			// Check if buffering is enabled for this handle
			if (_bufferingEnabled.ContainsKey(handle))
			{
				// Add to buffer instead of publishing immediately
				lock (_bufferLock)
				{
					if (!_buffers.TryGetValue(handle, out var buffer))
					{
						buffer = new List<byte[]>();
						_buffers[handle] = buffer;
					}
					buffer.Add(bytes);
				}
			}
			else
			{
				// Normal path - publish immediately
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

	public void EnableBuffering(long handle)
	{
		_bufferingEnabled.TryAdd(handle, 0);
		// Ensure buffer exists
		lock (_bufferLock)
		{
			if (!_buffers.ContainsKey(handle))
			{
				_buffers[handle] = new List<byte[]>();
			}
		}
	}

	public async ValueTask FlushBuffer(long handle)
	{
		List<byte[]>? buffer = null;

		lock (_bufferLock)
		{
			if (_buffers.TryGetValue(handle, out buffer) && buffer.Count > 0)
			{
				// Remove the buffer so new messages don't get added during flush
				_buffers.TryRemove(handle, out _);
			}
		}

		// If we have buffered data, combine and send it
		if (buffer != null && buffer.Count > 0)
		{
			// Combine all buffered messages with newlines between them
			var combinedSize = buffer.Sum(b => b.Length) + (buffer.Count - 1) * 2; // +2 for \r\n between messages
			var combined = new byte[combinedSize];
			var newline = Encoding.UTF8.GetBytes("\r\n");
			
			var offset = 0;
			for (var i = 0; i < buffer.Count; i++)
			{
				var bytes = buffer[i];
				Array.Copy(bytes, 0, combined, offset, bytes.Length);
				offset += bytes.Length;
				
				// Add newline between messages (but not after the last one)
				if (i < buffer.Count - 1)
				{
					Array.Copy(newline, 0, combined, offset, newline.Length);
					offset += newline.Length;
				}
			}

			// Publish the combined message
			await publishEndpoint.Publish(new TelnetOutputMessage(handle, combined));
		}
	}

	public void DisableBuffering(long handle)
	{
		_bufferingEnabled.TryRemove(handle, out _);
		lock (_bufferLock)
		{
			_buffers.TryRemove(handle, out _);
		}
	}
}