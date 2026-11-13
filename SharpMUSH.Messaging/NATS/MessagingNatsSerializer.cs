using System.Buffers;
using System.Text.Json;
using NATS.Client.Core;

namespace SharpMUSH.Messaging.NATS;

/// <summary>Writes and reads a bus payload as JSON with <see cref="MessagingJson"/>'s contract for <typeparamref name="T"/>.</summary>
public sealed class MessagingNatsSerializer<T> : INatsSerializer<T>
{
	public static MessagingNatsSerializer<T> Default { get; } = new();

	public void Serialize(IBufferWriter<byte> bufferWriter, T value)
	{
		using var writer = new Utf8JsonWriter(bufferWriter);
		JsonSerializer.Serialize(writer, value, MessagingJson.TypeInfo<T>());
	}

	public T? Deserialize(in ReadOnlySequence<byte> buffer)
	{
		if (buffer.Length == 0) return default;
		var reader = new Utf8JsonReader(buffer);
		return JsonSerializer.Deserialize(ref reader, MessagingJson.TypeInfo<T>());
	}

	public INatsSerializer<T> CombineWith(INatsSerializer<T> next) => throw new NotSupportedException();
}
