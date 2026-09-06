using System.Buffers;
namespace MarkupString.Ansi;

/// <summary>
/// A growable <see cref="IBufferWriter{T}"/> over an <see cref="ArrayPool{T}"/> buffer, for the
/// scratch a set emitter builds its run in before handing it to the layers it does not own. The
/// buffer returns to the pool on <see cref="Dispose"/>.
/// </summary>
internal sealed class PooledCharWriter : IBufferWriter<char>, IDisposable
{
	private char[] _buffer;
	private int _written;

	public PooledCharWriter(int capacity) => _buffer = ArrayPool<char>.Shared.Rent(Math.Max(capacity, 16));

	public ReadOnlySpan<char> WrittenSpan => _buffer.AsSpan(0, _written);
	public int WrittenCount => _written;

	public void Clear() => _written = 0;

	public void Advance(int count)
	{
		ArgumentOutOfRangeException.ThrowIfNegative(count);
		ArgumentOutOfRangeException.ThrowIfGreaterThan(count, _buffer.Length - _written);
		_written += count;
	}

	public Memory<char> GetMemory(int sizeHint = 0)
	{
		Grow(sizeHint);
		return _buffer.AsMemory(_written);
	}

	public Span<char> GetSpan(int sizeHint = 0)
	{
		Grow(sizeHint);
		return _buffer.AsSpan(_written);
	}

	private void Grow(int sizeHint)
	{
		ArgumentOutOfRangeException.ThrowIfNegative(sizeHint);
		if (sizeHint == 0) sizeHint = 1;
		if (_buffer.Length - _written >= sizeHint) return;
		var next = ArrayPool<char>.Shared.Rent(Math.Max(_buffer.Length * 2, _written + sizeHint));
		_buffer.AsSpan(0, _written).CopyTo(next);
		ArrayPool<char>.Shared.Return(_buffer);
		_buffer = next;
	}

	public void Dispose()
	{
		var buffer = _buffer;
		_buffer = [];
		_written = 0;
		if (buffer.Length > 0) ArrayPool<char>.Shared.Return(buffer);
	}
}
