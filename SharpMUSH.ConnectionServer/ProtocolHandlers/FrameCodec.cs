using System.Buffers.Binary;

namespace SharpMUSH.ConnectionServer.ProtocolHandlers;

/// <summary>
/// Length-prefixed framing for the WebTransport bidirectional stream: a 4-byte big-endian
/// unsigned length followed by that many payload bytes. Needed because a WebTransport stream
/// is a raw byte stream with no message boundaries (unlike WebSocket), and payloads may
/// themselves contain newlines, so a delimiter-based scheme is unsafe.
/// </summary>
public static class FrameCodec
{
	public static async Task WriteFrameAsync(Stream stream, ReadOnlyMemory<byte> payload, CancellationToken ct)
	{
		var header = new byte[4];
		BinaryPrimitives.WriteUInt32BigEndian(header, (uint)payload.Length);
		await stream.WriteAsync(header, ct);
		await stream.WriteAsync(payload, ct);
		await stream.FlushAsync(ct);
	}

	/// <summary>Reads one frame, or returns null on a clean end-of-stream at a frame boundary.</summary>
	public static async Task<byte[]?> ReadFrameAsync(Stream stream, CancellationToken ct)
	{
		var header = new byte[4];
		if (!await ReadExactlyOrEofAsync(stream, header, ct)) return null;

		var length = BinaryPrimitives.ReadUInt32BigEndian(header);
		if (length == 0) return [];

		var payload = new byte[length];
		if (!await ReadExactlyOrEofAsync(stream, payload, ct))
			throw new EndOfStreamException("Truncated WebTransport frame payload.");
		return payload;
	}

	private static async Task<bool> ReadExactlyOrEofAsync(Stream stream, Memory<byte> buffer, CancellationToken ct)
	{
		var read = 0;
		while (read < buffer.Length)
		{
			var n = await stream.ReadAsync(buffer[read..], ct);
			if (n == 0)
				return read != 0 ? throw new EndOfStreamException("Truncated frame header.") : false;
			read += n;
		}

		return true;
	}
}
