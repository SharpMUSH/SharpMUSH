using System.Buffers;
namespace MarkupString;

/// <summary>Convenience writes for the <see cref="IBufferWriter{T}"/>s emitters are handed.</summary>
public static class BufferWriterExtensions
{
	/// <summary>Copies <paramref name="text"/> into <paramref name="writer"/> and advances it.</summary>
	public static void Write(this IBufferWriter<char> writer, ReadOnlySpan<char> text)
	{
		ArgumentNullException.ThrowIfNull(writer);
		if (text.IsEmpty) return;
		var span = writer.GetSpan(text.Length);
		text.CopyTo(span);
		writer.Advance(text.Length);
	}
}
