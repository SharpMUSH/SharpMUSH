using System.Buffers.Binary;
using System.Text;

namespace SharpMUSH.Library.Plugins.Storage.Lightning;

/// <summary>
/// Key encoders. Every key is plain bytes compared lexicographically by LMDB, so fixed-width
/// big-endian integers sort numerically and a shorter key is a prefix of every key that extends it.
/// Variable-length parts are separated by a single 0x00, which no stored name contains.
/// </summary>
public static class Keys
{
	public static readonly byte[] Sep = [0x00];

	public static byte[] Dbref(long dbref)
	{
		var buffer = new byte[8];
		BinaryPrimitives.WriteUInt64BigEndian(buffer, unchecked((ulong)dbref));
		return buffer;
	}

	public static long ReadDbref(ReadOnlySpan<byte> bytes)
		=> unchecked((long)BinaryPrimitives.ReadUInt64BigEndian(bytes[..8]));

	public static byte[] Str(string value) => Encoding.UTF8.GetBytes(value);
	public static byte[] Lower(string value) => Str(value.ToLowerInvariant());
	public static byte[] Upper(string value) => Str(value.ToUpperInvariant());
	public static string ReadStr(ReadOnlySpan<byte> bytes) => Encoding.UTF8.GetString(bytes);

	public static byte[] Concat(params byte[][] parts)
	{
		var total = 0;
		foreach (var part in parts) total += part.Length;
		var buffer = new byte[total];
		var offset = 0;
		foreach (var part in parts)
		{
			part.CopyTo(buffer, offset);
			offset += part.Length;
		}
		return buffer;
	}

	public static bool StartsWith(ReadOnlySpan<byte> key, ReadOnlySpan<byte> prefix)
		=> key.Length >= prefix.Length && key[..prefix.Length].SequenceEqual(prefix);

	public static byte[] Attr(long dbref, string longName) => Concat(Dbref(dbref), Sep, Upper(longName));
	public static byte[] AttrPrefix(long dbref) => Concat(Dbref(dbref), Sep);
	public static byte[] AttrPrefix(long dbref, string literalPrefix) => Concat(Dbref(dbref), Sep, Upper(literalPrefix));

	public static (long Dbref, string LongName) ParseAttr(ReadOnlySpan<byte> key)
		=> (ReadDbref(key), ReadStr(key[9..]));

	public static byte[] Composite(long dbref, string second) => Concat(Dbref(dbref), Sep, Str(second));
	public static byte[] Composite(string first, long dbref) => Concat(Str(first), Sep, Dbref(dbref));
	public static byte[] Composite(string first, string second) => Concat(Str(first), Sep, Str(second));

	public static byte[] Composite(string first, string second, uint number)
	{
		var tail = new byte[4];
		BinaryPrimitives.WriteUInt32BigEndian(tail, number);
		return Concat(Str(first), Sep, Str(second), Sep, tail);
	}
}
