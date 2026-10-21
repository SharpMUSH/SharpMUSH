using System.Buffers;
using System.Buffers.Binary;
using System.Text;
using SharpMUSH.Library.Plugins.Storage.Lightning;

namespace SharpMUSH.Database.Lightning.Migration;

/// <summary>Category v1 bytes; readiness and visibility admission belong to the caller.</summary>
internal static class WikiCategoryValueCodec
{
	internal static byte[] LabelPrefix(string label)
	{
		var upper = Keys.Upper(label);
		var length = checked(upper.Length + 1);
		var prefix = new byte[length];
		upper.CopyTo(prefix, 0);
		return prefix;
	}

	internal static byte[] Key(ReadOnlySpan<byte> labelPrefix, string title, long pageKey)
	{
		ValidatePrefix(labelPrefix);
		ArgumentNullException.ThrowIfNull(title);
		var titleLength = Encoding.BigEndianUnicode.GetByteCount(title);
		var length = checked(labelPrefix.Length + titleLength + 10);
		var key = new byte[length];
		labelPrefix.CopyTo(key);
		Encoding.BigEndianUnicode.GetBytes(title.AsSpan(), key.AsSpan(labelPrefix.Length, titleLength));
		// The zero-initialized gap is the legacy two-byte title separator.
		BinaryPrimitives.WriteUInt64BigEndian(key.AsSpan(length - 8), unchecked((ulong)pageKey));
		return key;
	}

	internal static byte[] Value(ReadOnlySpan<byte> labelPrefix, bool? published, string author)
	{
		ValidatePrefix(labelPrefix);
		ArgumentNullException.ThrowIfNull(author);
		var labelLength = checked((uint)(labelPrefix.Length - 1));
		var authorLength = Encoding.UTF8.GetByteCount(author);
		var length = checked(authorLength + 9);
		var value = new byte[length];
		value[0] = (published ?? true) ? (byte)1 : (byte)0;
		Encoding.UTF8.GetBytes(author.AsSpan(), value.AsSpan(1, authorLength));
		BinaryPrimitives.WriteUInt32BigEndian(value.AsSpan(length - 8, 4), labelLength);
		// FF cannot occur in valid UTF-8 emitted by the legacy author encoder.
		BinaryPrimitives.WriteUInt32BigEndian(value.AsSpan(length - 4), 0xFF430001);
		return value;
	}

	internal static WikiCategoryDecodedValue Decode(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value,
		CancellationToken token = default)
	{
		token.ThrowIfCancellationRequested();
		if (value.Length < 9 || BinaryPrimitives.ReadUInt32BigEndian(value[^4..]) != 0xFF430001
			|| value[0] > 1)
			throw InvalidEntry();

		var encodedLabelLength = BinaryPrimitives.ReadUInt32BigEndian(value.Slice(value.Length - 8, 4));
		if (encodedLabelLength > int.MaxValue || key.Length < 11)
			throw InvalidEntry();
		var labelLength = (int)encodedLabelLength;
		// Subtract the fixed suffix first so malformed lengths cannot overflow an addition.
		if (labelLength > key.Length - 11)
			throw InvalidEntry();
		var titleLength = key.Length - 11 - labelLength;
		if ((titleLength & 1) != 0 || key[labelLength] != 0 || key[^10] != 0 || key[^9] != 0)
			throw InvalidEntry();

		var label = key[..labelLength];
		var visibility = value[..^8];
		ValidateUtf8(label, token);
		ValidateUtf8(visibility[1..], token);
		var pageKey = unchecked((long)BinaryPrimitives.ReadUInt64BigEndian(key[^8..]));
		token.ThrowIfCancellationRequested();
		return new WikiCategoryDecodedValue(label, visibility, pageKey);
	}

	private static void ValidatePrefix(ReadOnlySpan<byte> labelPrefix)
	{
		if (labelPrefix.IsEmpty || labelPrefix[^1] != 0)
			throw new ArgumentException("Category label prefix must end with a separator.", nameof(labelPrefix));
	}

	private static void ValidateUtf8(ReadOnlySpan<byte> bytes, CancellationToken token)
	{
		while (!bytes.IsEmpty)
		{
			token.ThrowIfCancellationRequested();
			if (Rune.DecodeFromUtf8(bytes, out _, out var consumed) != OperationStatus.Done)
				throw InvalidEntry();
			bytes = bytes[consumed..];
		}
	}

	private static InvalidDataException InvalidEntry() => new("Invalid category index entry.");
}

/// <summary>Validated label and visibility borrowed from their entry's owner.</summary>
internal readonly ref struct WikiCategoryDecodedValue(ReadOnlySpan<byte> labelBytes, ReadOnlySpan<byte> visibility, long pageKey)
{
	public ReadOnlySpan<byte> LabelBytes { get; } = labelBytes;
	public ReadOnlySpan<byte> Visibility { get; } = visibility;
	public int LabelByteLength => LabelBytes.Length;
	public long PageKey { get; } = pageKey;
}
