using NATS.Client.Serializers.Json;
using SharpMUSH.Messaging.Messages;
using SharpMUSH.Messaging.NATS;
using System.Buffers;

namespace SharpMUSH.Tests.Messaging;

/// <summary>
/// The bus compresses a payload once it is large enough to be worth it. The encoding is identified
/// by sniffing the first two bytes rather than by a NATS header, because the consumer side reads
/// <c>ConsumeAsync&lt;JsonElement&gt;</c> and has to pick a decoder before it can look at headers.
/// </summary>
public class CompressingNatsSerializerTests
{
	private const int Threshold = 4096;

	private static CompressingNatsSerializer<T> SerializerFor<T>() =>
			new(NatsJsonSerializer<T>.Default, Threshold);

	private static byte[] Serialize<T>(T value)
	{
		var buffer = new ArrayBufferWriter<byte>();
		SerializerFor<T>().Serialize(buffer, value);
		return buffer.WrittenSpan.ToArray();
	}

	private static T? Deserialize<T>(byte[] bytes) =>
			SerializerFor<T>().Deserialize(new ReadOnlySequence<byte>(bytes));

	[Test]
	public async Task SmallPayload_IsLeftAsPlainJson()
	{
		var message = new MarkupOutputMessage(7, """{"t":"hello"}""");

		var bytes = Serialize(message);

		await Assert.That(bytes.Length).IsLessThanOrEqualTo(Threshold);
		await Assert.That(bytes[0]).IsEqualTo((byte)'{');
	}

	[Test]
	public async Task LargePayload_IsGzipped()
	{
		var message = new MarkupOutputMessage(7, new string('x', Threshold * 4));

		var bytes = Serialize(message);

		// gzip's magic number. JSON can never start with these, which is what makes sniffing safe.
		await Assert.That(bytes[0]).IsEqualTo((byte)0x1f);
		await Assert.That(bytes[1]).IsEqualTo((byte)0x8b);
		await Assert.That(bytes.Length).IsLessThan(Threshold);
	}

	[Test]
	public async Task SmallPayload_RoundTrips()
	{
		var message = new MarkupOutputMessage(7, """{"t":"hello"}""");

		var restored = Deserialize<MarkupOutputMessage>(Serialize(message));

		await Assert.That(restored).IsEqualTo(message);
	}

	[Test]
	public async Task LargePayload_RoundTrips()
	{
		var message = new MarkupOutputMessage(long.MaxValue, string.Join(" ",
			Enumerable.Range(0, 4000).Select(i => $"word{i}")));

		var restored = Deserialize<MarkupOutputMessage>(Serialize(message));

		await Assert.That(restored).IsEqualTo(message);
	}

	/// <summary>
	/// A message published before compression existed — or by any producer under the threshold — is
	/// still plain JSON, and the reader has to accept it without being told.
	/// </summary>
	[Test]
	public async Task UncompressedJson_IsReadWithoutAFlag()
	{
		var message = new MarkupOutputMessage(3, "plain");
		var plain = new ArrayBufferWriter<byte>();
		NatsJsonSerializer<MarkupOutputMessage>.Default.Serialize(plain, message);

		var restored = Deserialize<MarkupOutputMessage>(plain.WrittenSpan.ToArray());

		await Assert.That(restored).IsEqualTo(message);
	}

	[Test]
	public async Task MultiSegmentBuffer_RoundTrips()
	{
		// NATS hands the deserializer a ReadOnlySequence that may span several segments; a reader that
		// assumes a single contiguous span works in tests and fails on a large real message.
		var message = new MarkupOutputMessage(11, new string('y', Threshold * 4));
		var bytes = Serialize(message);

		var restored = Deserialize<MarkupOutputMessage>(bytes);

		await Assert.That(restored).IsEqualTo(message);
		await Assert.That(SerializerFor<MarkupOutputMessage>()
			.Deserialize(Segmented(bytes, 64))).IsEqualTo(message);
	}

	/// <summary>Builds a deliberately fragmented sequence out of <paramref name="chunk"/>-byte segments.</summary>
	private static ReadOnlySequence<byte> Segmented(byte[] bytes, int chunk)
	{
		BufferSegment? first = null, last = null;
		for (var offset = 0; offset < bytes.Length; offset += chunk)
		{
			var slice = bytes.AsMemory(offset, Math.Min(chunk, bytes.Length - offset));
			var segment = new BufferSegment(slice, last?.RunningIndex + last?.Memory.Length ?? 0);
			if (first is null) first = segment;
			else last!.SetNext(segment);
			last = segment;
		}
		return first is null
			? ReadOnlySequence<byte>.Empty
			: new ReadOnlySequence<byte>(first, 0, last!, last!.Memory.Length);
	}

	private sealed class BufferSegment : ReadOnlySequenceSegment<byte>
	{
		public BufferSegment(ReadOnlyMemory<byte> memory, long runningIndex)
		{
			Memory = memory;
			RunningIndex = runningIndex;
		}

		public void SetNext(BufferSegment next) => Next = next;
	}
}
