using NATS.Client.Core;
using NATS.Client.Serializers.Json;
using System.Buffers;
using System.IO.Compression;

namespace SharpMUSH.Messaging.NATS;

/// <summary>
/// Wraps another NATS serializer and gzips its output once the payload is large enough to be worth
/// it. Anything under the threshold is published as plain JSON.
/// </summary>
/// <remarks>
/// The encoding carries no flag. JSON always begins with <c>{</c> (0x7B) and gzip always begins with
/// 0x1F 0x8B, so the reader identifies the payload from its first two bytes. That matters because the
/// consuming side reads <c>ConsumeAsync&lt;JsonElement&gt;</c> and has to choose a decoder before it
/// can inspect per-message headers — and because it means a plain-JSON message from any other
/// producer still reads.
/// <para>
/// This sits on every message type, not just markup. Small messages fall under the threshold and pass
/// through untouched, so the cost is one extra copy on the publish path.
/// </para>
/// </remarks>
public sealed class CompressingNatsSerializer<T> : INatsSerializer<T>
{
	/// <summary>
	/// Below this, gzip's ~20-byte header and the CPU it costs outweigh what it saves. A markup
	/// payload only reaches this size when it carries a genuinely large amount of text — a wiki page,
	/// a long look description — which is exactly the case that used to exceed the NATS payload limit.
	/// </summary>
	public const int DefaultThresholdBytes = 4096;

	private static readonly byte[] GzipMagic = [0x1f, 0x8b];

	private readonly INatsSerializer<T> _inner;
	private readonly int _thresholdBytes;

	public CompressingNatsSerializer(INatsSerializer<T> inner, int thresholdBytes)
	{
		_inner = inner;
		_thresholdBytes = thresholdBytes;
	}

	public static CompressingNatsSerializer<T> Default { get; } =
			new(NatsJsonSerializer<T>.Default, DefaultThresholdBytes);

	public void Serialize(IBufferWriter<byte> bufferWriter, T value)
	{
		var staged = new ArrayBufferWriter<byte>();
		_inner.Serialize(staged, value);

		if (staged.WrittenCount <= _thresholdBytes)
		{
			bufferWriter.Write(staged.WrittenSpan);
			return;
		}

		using var compressed = new MemoryStream();
		using (var gzip = new GZipStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
		{
			gzip.Write(staged.WrittenSpan);
		}

		bufferWriter.Write(compressed.GetBuffer().AsSpan(0, (int)compressed.Length));
	}

	public T? Deserialize(in ReadOnlySequence<byte> buffer)
	{
		if (!IsGzip(buffer)) return _inner.Deserialize(buffer);

		using var source = new MemoryStream(buffer.ToArray(), writable: false);
		using var gzip = new GZipStream(source, CompressionMode.Decompress);
		using var expanded = new MemoryStream();
		gzip.CopyTo(expanded);

		return _inner.Deserialize(new ReadOnlySequence<byte>(expanded.GetBuffer(), 0, (int)expanded.Length));
	}

	public INatsSerializer<T> CombineWith(INatsSerializer<T> next) =>
			new CompressingNatsSerializer<T>(_inner.CombineWith(next), _thresholdBytes);

	/// <summary>
	/// Reads the first two bytes through a <see cref="SequenceReader{T}"/> rather than off
	/// <c>FirstSpan</c>: NATS may hand over a sequence whose first segment is shorter than the prefix.
	/// </summary>
	private static bool IsGzip(in ReadOnlySequence<byte> buffer)
	{
		if (buffer.Length < GzipMagic.Length) return false;

		var reader = new SequenceReader<byte>(buffer);
		return reader.TryRead(out var first)
				&& reader.TryRead(out var second)
				&& first == GzipMagic[0]
				&& second == GzipMagic[1];
	}
}
