using System.Buffers.Binary;
using System.Text;

namespace SharpMUSH.Library.Plugins.Storage.Lightning;

/// <summary>
/// Key encoders. Every key is plain bytes compared lexicographically by LMDB, so fixed-width
/// big-endian integers sort numerically and a shorter key is a prefix of every key that extends it.
/// Variable-length parts are separated by a single 0x00, which no stored name contains.
/// <para>
/// Each key shape comes in two forms. The <see cref="byte[]"/> form is for a key that is kept — a
/// cursor prefix, a value written into another table. The <see cref="ReadOnlySpan{T}"/> form takes
/// the caller's buffer, <c>stackalloc byte[Keys.StackKeySize]</c>, and is for the key handed
/// straight to <see cref="ITx.TryGet"/>, <see cref="ITx.Put"/>, <see cref="ITx.Delete(TableDef, ReadOnlySpan{byte})"/>
/// or <see cref="ITx.CountDups"/>, which never keep it: no heap allocation on that path at all.
/// </para>
/// </summary>
public static class Keys
{
	public static readonly byte[] Sep = [0x00];

	/// <summary>
	/// A buffer this large holds every key made of a dbref, a separator and an ordinary name; a
	/// longer key spills to the heap on its own, so the size is a fast path, not a limit.
	/// </summary>
	public const int StackKeySize = 256;

	public static byte[] Dbref(long dbref) => Dbref(dbref, stackalloc byte[8]).ToArray();

	public static ReadOnlySpan<byte> Dbref(long dbref, Span<byte> buffer)
	{
		var key = new KeyBuilder(buffer);
		key.Dbref(dbref);
		return key.Key;
	}

	public static long ReadDbref(ReadOnlySpan<byte> bytes)
		=> unchecked((long)BinaryPrimitives.ReadUInt64BigEndian(bytes[..8]));

	public static byte[] Str(string value) => Encoding.UTF8.GetBytes(value);
	public static byte[] Lower(string value) => Lower(value, stackalloc byte[StackKeySize]).ToArray();
	public static byte[] Upper(string value) => Upper(value, stackalloc byte[StackKeySize]).ToArray();
	public static string ReadStr(ReadOnlySpan<byte> bytes) => Encoding.UTF8.GetString(bytes);

	public static ReadOnlySpan<byte> Str(string value, Span<byte> buffer)
	{
		var key = new KeyBuilder(buffer);
		key.Str(value);
		return key.Key;
	}

	public static ReadOnlySpan<byte> Lower(string value, Span<byte> buffer)
	{
		var key = new KeyBuilder(buffer);
		key.Lower(value);
		return key.Key;
	}

	public static ReadOnlySpan<byte> Upper(string value, Span<byte> buffer)
	{
		var key = new KeyBuilder(buffer);
		key.Upper(value);
		return key.Key;
	}

	public static byte[] Concat(params ReadOnlySpan<byte[]> parts)
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

	public static byte[] Attr(long dbref, string longName) => Attr(dbref, longName, stackalloc byte[StackKeySize]).ToArray();
	public static byte[] AttrPrefix(long dbref) => AttrPrefix(dbref, stackalloc byte[9]).ToArray();
	public static byte[] AttrPrefix(long dbref, string literalPrefix) => AttrPrefix(dbref, literalPrefix, stackalloc byte[StackKeySize]).ToArray();

	public static ReadOnlySpan<byte> Attr(long dbref, string longName, Span<byte> buffer)
	{
		var key = new KeyBuilder(buffer);
		key.Dbref(dbref);
		key.Sep();
		key.Upper(longName);
		return key.Key;
	}

	public static ReadOnlySpan<byte> AttrPrefix(long dbref, Span<byte> buffer)
	{
		var key = new KeyBuilder(buffer);
		key.Dbref(dbref);
		key.Sep();
		return key.Key;
	}

	public static ReadOnlySpan<byte> AttrPrefix(long dbref, string literalPrefix, Span<byte> buffer)
		=> Attr(dbref, literalPrefix, buffer);

	public static (long Dbref, string LongName) ParseAttr(ReadOnlySpan<byte> key)
		=> (ReadDbref(key), ReadStr(key[9..]));

	public static byte[] Composite(long dbref, string second) => Composite(dbref, second, stackalloc byte[StackKeySize]).ToArray();
	public static byte[] Composite(string first, long dbref) => Composite(first, dbref, stackalloc byte[StackKeySize]).ToArray();
	public static byte[] Composite(string first, string second) => Composite(first, second, stackalloc byte[StackKeySize]).ToArray();
	public static byte[] Composite(string first, string second, string third) => Composite(first, second, third, stackalloc byte[StackKeySize]).ToArray();
	public static byte[] Composite(string first, string second, uint number) => Composite(first, second, number, stackalloc byte[StackKeySize]).ToArray();

	public static ReadOnlySpan<byte> Composite(long dbref, string second, Span<byte> buffer)
	{
		var key = new KeyBuilder(buffer);
		key.Dbref(dbref);
		key.Sep();
		key.Str(second);
		return key.Key;
	}

	public static ReadOnlySpan<byte> Composite(string first, long dbref, Span<byte> buffer)
	{
		var key = new KeyBuilder(buffer);
		key.Str(first);
		key.Sep();
		key.Dbref(dbref);
		return key.Key;
	}

	public static ReadOnlySpan<byte> Composite(string first, string second, Span<byte> buffer)
	{
		var key = new KeyBuilder(buffer);
		key.Str(first);
		key.Sep();
		key.Str(second);
		return key.Key;
	}

	public static ReadOnlySpan<byte> Composite(string first, string second, string third, Span<byte> buffer)
	{
		var key = new KeyBuilder(buffer);
		key.Str(first);
		key.Sep();
		key.Str(second);
		key.Sep();
		key.Str(third);
		return key.Key;
	}

	public static ReadOnlySpan<byte> Composite(string first, string second, uint number, Span<byte> buffer)
	{
		var key = new KeyBuilder(buffer);
		key.Str(first);
		key.Sep();
		key.Str(second);
		key.Sep();
		key.UInt32(number);
		return key.Key;
	}
}

/// <summary>
/// Appends the parts of one key to a caller-owned buffer, in order. The buffer is used as long as
/// the key fits; a key that outgrows it moves to the heap, so a caller can hand over a small
/// <c>stackalloc</c> without sizing it for the longest name a game will ever store. <see cref="Key"/>
/// is a slice of whichever the parts ended up in, valid for as long as the buffer is.
/// </summary>
public ref struct KeyBuilder(Span<byte> buffer)
{
	/// <summary>Longest name case-folded on the stack; a longer one goes through one heap array.</summary>
	private const int StackChars = 128;

	private Span<byte> _buffer = buffer;
	private int _length;

	public readonly ReadOnlySpan<byte> Key => _buffer[.._length];

	public void Dbref(long dbref) => BinaryPrimitives.WriteUInt64BigEndian(Reserve(8), unchecked((ulong)dbref));

	public void UInt32(uint number) => BinaryPrimitives.WriteUInt32BigEndian(Reserve(4), number);

	public void Sep() => Reserve(1)[0] = 0x00;

	public void Str(scoped ReadOnlySpan<char> value) => Encoding.UTF8.GetBytes(value, Reserve(Encoding.UTF8.GetByteCount(value)));

	public void Upper(string value)
	{
		Span<char> folded = value.Length <= StackChars ? stackalloc char[value.Length] : new char[value.Length];
		value.AsSpan().ToUpperInvariant(folded);
		Str(folded);
	}

	public void Lower(string value)
	{
		Span<char> folded = value.Length <= StackChars ? stackalloc char[value.Length] : new char[value.Length];
		value.AsSpan().ToLowerInvariant(folded);
		Str(folded);
	}

	private Span<byte> Reserve(int count)
	{
		if (_length + count > _buffer.Length)
		{
			var grown = new byte[Math.Max(_buffer.Length * 2, _length + count)];
			Key.CopyTo(grown);
			_buffer = grown;
		}

		var slice = _buffer.Slice(_length, count);
		_length += count;
		return slice;
	}
}
