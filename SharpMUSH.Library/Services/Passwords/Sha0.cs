using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace SharpMUSH.Library.Services.Passwords;

/// <summary>
/// SHA-0, the digest of the original FIPS 180 (1993), which SHA-1 superseded and .NET does not ship.
/// </summary>
/// <remarks>
/// SHA-1 differs only in rotating each expanded message-schedule word left by one bit; SHA-0 does
/// not. It is broken and is here solely to verify passwords old PennMUSH versions stored with it
/// (<c>mush_crypt_sha0</c>, which called OpenSSL's <c>SHA()</c>).
/// </remarks>
internal static class Sha0
{
	public static byte[] HashData(ReadOnlySpan<byte> data)
	{
		uint[] state = [0x67452301, 0xEFCDAB89, 0x98BADCFE, 0x10325476, 0xC3D2E1F0];
		Span<uint> schedule = stackalloc uint[80];

		var whole = data.Length - data.Length % 64;
		for (var offset = 0; offset < whole; offset += 64)
		{
			Compress(state, data.Slice(offset, 64), schedule);
		}

		// The tail, the 0x80 terminator and the big-endian bit length fill one block, or two when the
		// tail leaves fewer than nine bytes of room.
		Span<byte> tail = stackalloc byte[128];
		tail.Clear();
		var remainder = data[whole..];
		remainder.CopyTo(tail);
		tail[remainder.Length] = 0x80;
		var tailLength = remainder.Length < 56 ? 64 : 128;
		BinaryPrimitives.WriteUInt64BigEndian(tail[(tailLength - 8)..], (ulong)data.Length * 8);
		for (var offset = 0; offset < tailLength; offset += 64)
		{
			Compress(state, tail.Slice(offset, 64), schedule);
		}

		var digest = new byte[20];
		for (var i = 0; i < state.Length; i++)
		{
			BinaryPrimitives.WriteUInt32BigEndian(digest.AsSpan(i * 4), state[i]);
		}

		// Both held the password's bytes.
		CryptographicOperations.ZeroMemory(tail);
		CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(schedule));
		return digest;
	}

	private static void Compress(uint[] state, ReadOnlySpan<byte> block, Span<uint> w)
	{
		for (var t = 0; t < 16; t++)
		{
			w[t] = BinaryPrimitives.ReadUInt32BigEndian(block[(t * 4)..]);
		}

		for (var t = 16; t < 80; t++)
		{
			w[t] = w[t - 3] ^ w[t - 8] ^ w[t - 14] ^ w[t - 16];
		}

		var (a, b, c, d, e) = (state[0], state[1], state[2], state[3], state[4]);
		for (var t = 0; t < 80; t++)
		{
			var (f, k) = t switch
			{
				< 20 => ((b & c) | (~b & d), 0x5A827999u),
				< 40 => (b ^ c ^ d, 0x6ED9EBA1u),
				< 60 => ((b & c) | (b & d) | (c & d), 0x8F1BBCDCu),
				_ => (b ^ c ^ d, 0xCA62C1D6u)
			};
			var next = BitOperations.RotateLeft(a, 5) + f + e + k + w[t];
			(e, d, c, b, a) = (d, c, BitOperations.RotateLeft(b, 30), a, next);
		}

		state[0] += a;
		state[1] += b;
		state[2] += c;
		state[3] += d;
		state[4] += e;
	}
}
