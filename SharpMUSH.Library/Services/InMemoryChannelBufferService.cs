using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;
using System.Collections.Concurrent;

namespace SharpMUSH.Library.Services;

/// <summary>
/// In-memory implementation of channel message recall buffer service
/// Messages are stored in memory and lost on server restart
/// </summary>
/// <param name="ids">Gives a line that arrives without an id (<c>cbufferadd()</c> writes straight here) the
/// next one. A broadcast line already has the id its <c>CHANNEL`MESSAGE</c> event passed on.</param>
public class InMemoryChannelBufferService(IChannelMessageIdSource ids) : IChannelBufferService
{
	private readonly ConcurrentDictionary<string, CircularBuffer<SharpChannelMessage>> _buffers = new();
	private const int DefaultBufferSize = 100;

	/// <summary>Held by an add and by a move, so a line is never added to a buffer that a move is taking away.</summary>
	private readonly Lock _moving = new();

	public async ValueTask AddMessageAsync(SharpChannelMessage message)
	{
		if (message.Id == 0)
		{
			message.Id = await ids.NextAsync();
		}

		lock (_moving)
		{
			_buffers.GetOrAdd(message.ChannelId, _ => new CircularBuffer<SharpChannelMessage>(DefaultBufferSize)).Add(message);
		}
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

	public ValueTask<int> CountMessagesAsync(string channelId)
		=> ValueTask.FromResult(_buffers.TryGetValue(channelId, out var buffer) ? buffer.Count : 0);

	/// <remarks>
	/// A line can already be under the new id — broadcast on the renamed channel between the rename and
	/// this move — so the two buffers are merged in id order, keeping the newest
	/// <see cref="DefaultBufferSize"/>, rather than one replacing the other. A broadcast that resolved the
	/// channel before the rename and buffers after the move still files its line under the old id.
	/// </remarks>
	public ValueTask MoveBufferAsync(string fromChannelId, string toChannelId)
	{
		if (string.Equals(fromChannelId, toChannelId, StringComparison.Ordinal))
		{
			return ValueTask.CompletedTask;
		}

		lock (_moving)
		{
			if (!_buffers.TryRemove(fromChannelId, out var moved))
			{
				return ValueTask.CompletedTask;
			}

			var lines = moved.GetRecent(int.MaxValue);
			if (_buffers.TryGetValue(toChannelId, out var existing))
			{
				lines = [.. lines.Concat(existing.GetRecent(int.MaxValue)).OrderBy(line => line.Id)];
			}

			var merged = new CircularBuffer<SharpChannelMessage>(DefaultBufferSize);
			foreach (var line in lines)
			{
				line.ChannelId = toChannelId;
				merged.Add(line);
			}

			_buffers[toChannelId] = merged;
		}

		return ValueTask.CompletedTask;
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

		public int Count
		{
			get
			{
				lock (_lock)
				{
					return _count;
				}
			}
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
		/// where the window starts, then read forward from there.
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
