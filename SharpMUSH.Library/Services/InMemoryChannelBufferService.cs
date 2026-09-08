using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;
using System.Collections.Concurrent;

namespace SharpMUSH.Library.Services;

/// <summary>
/// In-memory implementation of channel message recall buffer service
/// Messages are stored in memory and lost on server restart
/// </summary>
public class InMemoryChannelBufferService : IChannelBufferService
{
	private readonly ConcurrentDictionary<string, CircularBuffer<SharpChannelMessage>> _buffers = new();
	private const int DefaultBufferSize = 100;

	public ValueTask AddMessageAsync(SharpChannelMessage message)
	{
		var buffer = _buffers.GetOrAdd(message.ChannelId, _ => new CircularBuffer<SharpChannelMessage>(DefaultBufferSize));
		buffer.Add(message);
		return ValueTask.CompletedTask;
	}

	public async IAsyncEnumerable<SharpChannelMessage> GetMessagesAsync(string channelId, int count)
	{
		if (!_buffers.TryGetValue(channelId, out var buffer))
		{
			yield break;
		}

		var messages = buffer.GetRecent(count);
		foreach (var message in messages)
		{
			yield return message;
		}

		await ValueTask.CompletedTask;
	}

	public ValueTask ClearBufferAsync(string channelId)
	{
		_buffers.TryRemove(channelId, out _);
		return ValueTask.CompletedTask;
	}

	/// <summary>
	/// Simple circular buffer implementation for storing a fixed number of recent messages
	/// </summary>
	private class CircularBuffer<T>
	{
		private readonly T[] _buffer;
		private readonly object _lock = new();
		private int _nextIndex;
		private int _count;

		public CircularBuffer(int size)
		{
			_buffer = new T[size];
			_nextIndex = 0;
			_count = 0;
		}

		public void Add(T item)
		{
			lock (_lock)
			{
				_buffer[_nextIndex] = item;
				_nextIndex = (_nextIndex + 1) % _buffer.Length;
				if (_count < _buffer.Length)
				{
					_count++;
				}
			}
		}

		/// <summary>
		/// The last <paramref name="count"/> items, oldest first: walk back from the write head to find
		/// where the window starts, then read forward from there. Reading backwards from the head is what
		/// made <c>@channel/recall</c> and <c>crecall()</c> replay conversations in reverse.
		/// </summary>
		public List<T> GetRecent(int count)
		{
			lock (_lock)
			{
				var take = Math.Min(count, _count);
				var result = new List<T>(take);

				var index = (_nextIndex - take + _buffer.Length) % _buffer.Length;

				for (var retrieved = 0; retrieved < take; retrieved++)
				{
					result.Add(_buffer[index]);
					index = (index + 1) % _buffer.Length;
				}

				return result;
			}
		}
	}
}
