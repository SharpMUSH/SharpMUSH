using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace SharpMUSH.Tools.WikiSemantics.Native;

/// <summary>Format-1 keys. All table IDs are positive unsigned big-endian values.</summary>
public static class NativeKeys
{
	private static readonly UTF8Encoding Utf8 = new(false, true);
	public static byte[] EncodeInteger(long value)
	{
		var bytes = new byte[8];
		BinaryPrimitives.WriteUInt64BigEndian(bytes, unchecked((ulong)value) ^ 0x8000000000000000UL);
		return bytes;
	}
	public static long DecodeInteger(ReadOnlySpan<byte> encoded)
	{
		Length(encoded);
		return unchecked((long)(BinaryPrimitives.ReadUInt64BigEndian(encoded) ^ 0x8000000000000000UL));
	}
	public static byte[] EncodeId(ulong value)
	{
		Positive(value);
		var bytes = new byte[8];
		BinaryPrimitives.WriteUInt64BigEndian(bytes, value);
		return bytes;
	}
	public static ulong DecodeId(ReadOnlySpan<byte> encoded)
	{
		Length(encoded);
		var value = BinaryPrimitives.ReadUInt64BigEndian(encoded);
		Positive(value);
		return value;
	}
	public static byte[] Subject(ulong subject) => EncodeId(subject);
	public static byte[] Forward(ulong subject, ulong property, NativeScalarTag type, ReadOnlySpan<byte> value)
	{
		Positive(subject);
		Positive(property);
		Scalar(type, value);
		var bytes = new byte[25];
		BinaryPrimitives.WriteUInt64BigEndian(bytes, subject);
		BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(8), property);
		bytes[16] = (byte)type;
		value.CopyTo(bytes.AsSpan(17));
		return bytes;
	}
	public static byte[] Posting(ulong collection, ulong classId, ulong property, NativeScalarTag type, ReadOnlySpan<byte> value, ulong subject)
	{
		Positive(collection);
		Positive(classId);
		Positive(property);
		Positive(subject);
		Scalar(type, value);
		var bytes = new byte[41];
		BinaryPrimitives.WriteUInt64BigEndian(bytes, collection);
		BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(8), classId);
		BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(16), property);
		bytes[24] = (byte)type;
		value.CopyTo(bytes.AsSpan(25));
		BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(33), subject);
		return bytes;
	}
	public static byte[] RegistryLookup(NativeRegistryKind kind, string identity)
	{
		Kind(kind);
		ArgumentException.ThrowIfNullOrWhiteSpace(identity);
		if (identity.Length > NativeFixtureLimits.MaxReadBytes || Utf8.GetByteCount(identity) > NativeFixtureLimits.MaxReadBytes)
			throw new ArgumentException("Registry identity exceeds the record bound.", nameof(identity));
		var bytes = new byte[33];
		bytes[0] = (byte)kind;
		SHA256.HashData(Utf8.GetBytes(identity), bytes.AsSpan(1));
		return bytes;
	}
	public static byte[] RegistryReverse(NativeRegistryKind kind, ulong id)
	{
		Kind(kind);
		Positive(id);
		var bytes = new byte[9];
		bytes[0] = (byte)kind;
		BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(1), id);
		return bytes;
	}
	private static void Scalar(NativeScalarTag type, ReadOnlySpan<byte> value)
	{
		Length(value);
		if (!Enum.IsDefined(type)) throw new ArgumentException("Unknown scalar tag.", nameof(type));
		if (type is NativeScalarTag.PageRef or NativeScalarTag.Keyword) Positive(BinaryPrimitives.ReadUInt64BigEndian(value));
		if (type == NativeScalarTag.Boolean && BinaryPrimitives.ReadUInt64BigEndian(value) > 1)
			throw new ArgumentException("Boolean scalar must be zero or one.", nameof(value));
	}
	private static void Kind(NativeRegistryKind kind)
	{
		if (!Enum.IsDefined(kind)) throw new ArgumentException("Unknown registry kind.", nameof(kind));
	}
	private static void Length(ReadOnlySpan<byte> encoded)
	{
		if (encoded.Length != 8) throw new ArgumentException("Exactly eight bytes are required.", nameof(encoded));
	}
	private static void Positive(ulong value)
	{
		if (value == 0) throw new ArgumentOutOfRangeException(nameof(value), "IDs must be positive.");
	}
}
