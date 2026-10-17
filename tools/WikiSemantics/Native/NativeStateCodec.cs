using System.Collections.Immutable;
using System.Text;

namespace SharpMUSH.Tools.WikiSemantics.Native;

internal sealed record NativeSummaryState(NativePageSummary Summary, ulong CollectionId, ulong ClassId, long CommitSequence);
internal sealed record NativeRegistryEntry(ulong Id, string Identity);
internal sealed record NativeCatalogEntry(long References, byte[] Descriptor);

/// <summary>Private version-1 durable envelopes. Scalar fact encoding remains owned by NativeCodec.</summary>
internal static class NativeStateCodec
{
	private static readonly UTF8Encoding Utf8 = new(false, true);
	internal static byte[] Encode(Action<BinaryWriter> write)
	{
		using var counter = new Counter();
		using (var writer = new BinaryWriter(counter, Utf8, true)) { writer.Write((byte)1); write(writer); }
		if (counter.Length > NativeFixtureLimits.MaxReadBytes) throw new InvalidDataException("State row exceeds native read bound.");
		var bytes = new byte[checked((int)counter.Length)];
		using var stream = new MemoryStream(bytes, true);
		using (var writer = new BinaryWriter(stream, Utf8, true)) { writer.Write((byte)1); write(writer); }
		return bytes;
	}
	private static T Decode<T>(byte[] bytes, Func<BinaryReader, T> read)
	{
		if (bytes.Length > NativeFixtureLimits.MaxReadBytes) throw new InvalidDataException("State row exceeds native read bound.");
		using var stream = new MemoryStream(bytes, 0, bytes.Length, false, true);
		using var reader = new BinaryReader(stream, Utf8);
		try
		{
			if (reader.ReadByte() != 1) throw new InvalidDataException("Unknown state format.");
			var result = read(reader);
			if (stream.Position != stream.Length) throw new InvalidDataException("Trailing state bytes.");
			return result;
		}
		catch (EndOfStreamException e) { throw new InvalidDataException("Truncated state row.", e); }
		catch (FormatException e) { throw new InvalidDataException("Malformed string length.", e); }
		catch (DecoderFallbackException e) { throw new InvalidDataException("Malformed UTF8.", e); }
	}
	internal static byte[] Registry(NativeRegistryEntry entry) => Encode(w => { w.Write(entry.Id); w.Write(entry.Identity); });
	internal static NativeRegistryEntry Registry(byte[] bytes) => Decode(bytes, r => new NativeRegistryEntry(r.ReadUInt64(), String(r)));
	internal static byte[] Descriptor(VisibilityDescriptor value) => Encode(w => Visibility(w, value));
	internal static byte[] Catalog(NativeCatalogEntry entry) => Encode(w => { w.Write(entry.References); w.Write(entry.Descriptor.Length); w.Write(entry.Descriptor); });
	internal static NativeCatalogEntry Catalog(byte[] bytes) => Decode(bytes, r => new NativeCatalogEntry(r.ReadInt64(), Blob(r, NativeFixtureLimits.MaxDescriptorBytes)));
	internal static byte[] Summary(NativeSummaryState state) => Encode(w =>
	{
		var value = state.Summary;
		w.Write(value.Reference.Id); w.Write(value.NativeId); w.Write(value.Title); Visibility(w, value.Visibility);
		w.Write(value.Collection); w.Write(value.Deleted); w.Write(value.StateVersion);
		w.Write(state.CollectionId); w.Write(state.ClassId); w.Write(state.CommitSequence);
	});
	internal static NativeSummaryState Summary(byte[] bytes) => Decode(bytes, r =>
	{
		var summary = new NativePageSummary(new(String(r)), r.ReadUInt64(), String(r), Visibility(r), String(r), Boolean(r), r.ReadInt64());
		return new NativeSummaryState(summary, r.ReadUInt64(), r.ReadUInt64(), r.ReadInt64());
	});
	internal static byte[] Mutation(NativeMutationStatus status) => Encode(w =>
	{
		w.Write(status.MutationId.ToByteArray()); w.Write(status.RequestDigest);
		w.Write(status.Committed.StateVersion); w.Write(status.Committed.CommitSequence);
	});
	internal static NativeMutationStatus Mutation(byte[] bytes) => Decode(bytes, r =>
	{
		var guid = new Guid(r.ReadBytes(16)); var digest = String(r);
		return new NativeMutationStatus(guid, digest, new(guid, r.ReadInt64(), r.ReadInt64()));
	});
	private static void Visibility(BinaryWriter w, VisibilityDescriptor value)
	{
		w.Write(value.Namespace); Strings(w, value.Categories); Strings(w, value.RuleIds);
		w.Write(value.Published); w.Write(value.DraftAuthor is not null); if (value.DraftAuthor is not null) w.Write(value.DraftAuthor);
	}
	private static VisibilityDescriptor Visibility(BinaryReader r) => new(String(r), Strings(r), Strings(r), Boolean(r), Boolean(r) ? String(r) : null);
	private static string String(BinaryReader r)
	{
		var count = r.Read7BitEncodedInt();
		if (count < 0 || count > r.BaseStream.Length - r.BaseStream.Position) throw new InvalidDataException("Malformed string length.");
		var stream = (MemoryStream)r.BaseStream;
		var value = Utf8.GetString(stream.GetBuffer().AsSpan(checked((int)stream.Position), count));
		stream.Position += count;
		return value;
	}

	private static bool Boolean(BinaryReader r) => r.ReadByte() switch { 0 => false, 1 => true, _ => throw new InvalidDataException("Malformed Boolean.") };
	private static void Strings(BinaryWriter w, ImmutableArray<string> values) { w.Write(values.Length); foreach (var value in values) w.Write(value); }
	private static ImmutableArray<string> Strings(BinaryReader r)
	{
		var count = r.ReadInt32();
		if (count < 0 || count > NativeFixtureLimits.MaxDescriptorBytes / 2 || count > r.BaseStream.Length - r.BaseStream.Position)
			throw new InvalidDataException("Malformed structural count.");
		var values = ImmutableArray.CreateBuilder<string>(count);
		for (var i = 0; i < count; i++) values.Add(String(r));
		return values.MoveToImmutable();
	}
	private static byte[] Blob(BinaryReader r, int maximum)
	{
		var count = r.ReadInt32();
		if (count < 0 || count > maximum || count > r.BaseStream.Length - r.BaseStream.Position) throw new InvalidDataException("Malformed blob length.");
		return r.ReadBytes(count);
	}
	private sealed class Counter : Stream
	{
		private long _length;
		public override bool CanRead => false;
		public override bool CanSeek => false;
		public override bool CanWrite => true;
		public override long Length => _length;
		public override long Position { get => _length; set => throw new NotSupportedException(); }
		public override void Write(byte[] buffer, int offset, int count) => _length = checked(_length + count);
		public override void Write(ReadOnlySpan<byte> buffer) => _length = checked(_length + buffer.Length);
		public override void WriteByte(byte value) => _length = checked(_length + 1);
		public override void Flush() { }
		public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
		public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
		public override void SetLength(long value) => throw new NotSupportedException();
	}
}
