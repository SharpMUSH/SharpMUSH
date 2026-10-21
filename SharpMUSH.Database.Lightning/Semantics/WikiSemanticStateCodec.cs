using System.Collections.Immutable;
using System.Text;
using SharpMUSH.Library.Models.Wiki.Semantics;

namespace SharpMUSH.Database.Lightning.Semantics;

internal sealed record WikiSemanticSummary(PageRef Reference, ulong NativeId, string Title, VisibilityDescriptor Visibility, string Collection, bool Deleted, long StateVersion);
internal sealed record WikiSemanticSummaryState(WikiSemanticSummary Summary, ulong CollectionId, ulong ClassId, long CommitSequence);
internal sealed record WikiSemanticProjectionState(WikiSemanticSummaryState State, WikiSemanticSource Source);
internal sealed record WikiSemanticRegistryEntry(ulong Id, string Identity);
internal sealed record WikiSemanticCatalogEntry(long References, byte[] Descriptor);

/// <summary>Private version-1 durable envelopes. Scalar fact encoding remains owned by WikiSemanticCodec.</summary>
internal static class WikiSemanticStateCodec
{
	private static readonly UTF8Encoding Utf8 = new(false, true);
	internal static byte[] Encode(Action<BinaryWriter> write)
	{
		using var counter = new Counter();
		using (var writer = new BinaryWriter(counter, Utf8, true)) { writer.Write((byte)1); write(writer); }
		if (counter.Length > WikiSemanticLimits.MaxReadBytes) throw new InvalidDataException("State row exceeds native read bound.");
		var bytes = new byte[checked((int)counter.Length)];
		using var stream = new MemoryStream(bytes, true);
		using (var writer = new BinaryWriter(stream, Utf8, true)) { writer.Write((byte)1); write(writer); }
		return bytes;
	}
	internal static T Decode<T>(byte[] bytes, Func<BinaryReader, T> read)
	{
		if (bytes.Length > WikiSemanticLimits.MaxReadBytes) throw new InvalidDataException("State row exceeds native read bound.");
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

	/// <summary>Integrated state carries explicit source identity; B1 Summary keeps its original compact bytes.</summary>
	internal static byte[] ProjectionState(WikiSemanticProjectionState state) => Encode(w =>
	{
		switch (state.Source)
		{
			case LocalWikiSource: w.Write((byte)0); break;
			case PortableWikiSource portable:
				w.Write((byte)1); w.Write(portable.Key.Package); w.Write(portable.Key.Namespace); w.Write(portable.Key.SourceKey); break;
			default: throw new ArgumentException("Unknown semantic source.");
		}
		// Compact Summary remains its own complete versioned envelope.
		w.Write(Summary(state.State));
	});
	internal static WikiSemanticProjectionState ProjectionState(byte[] bytes) => Decode(bytes, r =>
	{
		WikiSemanticSource source = r.ReadByte() switch
		{
			0 => new LocalWikiSource(),
			1 => new PortableWikiSource(new(String(r), String(r), String(r))),
			_ => throw new InvalidDataException("Unknown semantic source tag.")
		};
		var remaining = checked((int)(r.BaseStream.Length - r.BaseStream.Position));
		return new WikiSemanticProjectionState(Summary(r.ReadBytes(remaining)), source);
	});

	internal static byte[] Registry(WikiSemanticRegistryEntry entry) => Encode(w => { w.Write(entry.Id); w.Write(entry.Identity); });
	internal static WikiSemanticRegistryEntry Registry(byte[] bytes) => Decode(bytes, r => new WikiSemanticRegistryEntry(r.ReadUInt64(), String(r)));
	internal static byte[] Descriptor(VisibilityDescriptor value) => Encode(w => Visibility(w, value));
	internal static VisibilityDescriptor DescriptorValue(byte[] bytes) => Decode(bytes, Visibility);
	internal static byte[] Catalog(WikiSemanticCatalogEntry entry) => Encode(w => { w.Write(entry.References); w.Write(entry.Descriptor.Length); w.Write(entry.Descriptor); });
	internal static WikiSemanticCatalogEntry Catalog(byte[] bytes) => Decode(bytes, r => new WikiSemanticCatalogEntry(r.ReadInt64(), Blob(r, WikiSemanticLimits.MaxDescriptorBytes)));
	internal static byte[] Summary(WikiSemanticSummaryState state) => Encode(w =>
	{
		var value = state.Summary;
		w.Write(value.Reference.Id); w.Write(value.NativeId); w.Write(value.Title); Visibility(w, value.Visibility);
		w.Write(value.Collection); w.Write(value.Deleted); w.Write(value.StateVersion);
		w.Write(state.CollectionId); w.Write(state.ClassId); w.Write(state.CommitSequence);
	});
	internal static WikiSemanticSummaryState Summary(byte[] bytes) => Decode(bytes, r =>
	{
		var summary = new WikiSemanticSummary(new(String(r)), r.ReadUInt64(), String(r), Visibility(r), String(r), Boolean(r), r.ReadInt64());
		return new WikiSemanticSummaryState(summary, r.ReadUInt64(), r.ReadUInt64(), r.ReadInt64());
	});
	internal static void Visibility(BinaryWriter w, VisibilityDescriptor value)
	{
		w.Write(value.Namespace); Strings(w, value.Categories); Strings(w, value.RuleIds);
		w.Write(value.Published); w.Write(value.DraftAuthor is not null); if (value.DraftAuthor is not null) w.Write(value.DraftAuthor);
	}
	internal static VisibilityDescriptor Visibility(BinaryReader r) => new(String(r), Strings(r), Strings(r), Boolean(r), Boolean(r) ? String(r) : null);
	internal static string String(BinaryReader r)
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
		if (count < 0 || count > WikiSemanticLimits.MaxDescriptorBytes / 2 || count > r.BaseStream.Length - r.BaseStream.Position)
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
