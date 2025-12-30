using System.Collections.Concurrent;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Services;

/// <summary>
/// In-memory implementation of channel message recall buffer service
/// Messages are stored in memory and lost on server restart
/// </summary>
public class InMemoryChannelBufferService : IChannelBufferService
{
	// Dictionary of channel ID to circular buffer of messages
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

		public List<T> GetRecent(int count)
		{
			lock (_lock)
			{
				var result = new List<T>(Math.Min(count, _count));
				
				// Start from the most recent and work backwards
				var index = (_nextIndex - 1 + _buffer.Length) % _buffer.Length;
				var retrieved = 0;
				
				while (retrieved < count && retrieved < _count)
				{
					result.Add(_buffer[index]);
					index = (index - 1 + _buffer.Length) % _buffer.Length;
					retrieved++;
				}
				
				return result;
			}
		}
	}
}
