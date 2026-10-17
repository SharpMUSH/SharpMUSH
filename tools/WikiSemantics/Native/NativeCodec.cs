using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Text;

namespace SharpMUSH.Tools.WikiSemantics.Native;

/// <summary>
/// Version byte, kind byte (page=1, facts=2), then explicit fields. Lengths/counts and signed
/// numbers are little-endian; keys use their separate big-endian order. Strings are strict UTF-8.
/// Decoded models need a caller-owned working-memory reservation before this pure codec is called.
/// </summary>
public static class NativeCodec
{
	public const byte FormatVersion = 1;
	private static readonly UTF8Encoding Utf8 = new(false, true);
	private const int RowLimit = NativeFixtureLimits.MaxReadBytes - 8;
	public static byte[] EncodePage(PageSnapshot page) => Encode(w => WritePage(w, page), RowLimit);
	public static byte[] EncodeFacts(CanonicalFactSet facts) => Encode(w => WriteFacts(w, facts), RowLimit);
	internal static int MeasurePage(PageSnapshot page) => Measure(w => WritePage(w, page), RowLimit);
	internal static int MeasureFacts(CanonicalFactSet facts, int limit) => Measure(w => WriteFacts(w, facts), limit);
	internal static int MeasureSchema(SemanticSchema schema) => Measure(w => WriteSchema(w, schema), NativeFixtureLimits.MaxDeltaBytes);
	internal static byte[] SchemaIdentity(SemanticSchema schema) => Encode(w => WriteSchema(w, schema), NativeFixtureLimits.MaxDeltaBytes);
	internal static int DescriptorSize(VisibilityDescriptor descriptor) => Measure(w => WriteVisibility(w, descriptor), NativeFixtureLimits.MaxDescriptorBytes);
	internal static int SummarySize(PageSnapshot page) => Measure(w =>
	{
		w.Byte(FormatVersion);
		w.Byte(4);
		w.String(page.Reference.Id);
		w.Int64(1); // native ID
		w.String(page.Title);
		WriteVisibility(w, page.Visibility);
		w.String(page.Collections[0]);
		w.Byte(0); // deleted
		w.Int64(1); // native state version
	}, NativeFixtureLimits.MaxSummaryBytes);

	public static PageSnapshot DecodePage(ReadOnlySpan<byte> data)
	{
		var r = new Reader(data, 1);
		var reference = new PageRef(r.String());
		var key = new PortableKey(r.String(), r.String(), r.String());
		var title = r.String();
		var body = r.String();
		var collections = r.Strings();
		var visibility = r.Visibility();
		var stamp = new ExperimentalStamp(r.String(), r.Int64(), r.Int64(), r.Int64(), r.Int64(), r.Int64(), r.Int64(), r.Int64());
		r.End();
		return new(reference, key, title, body, collections, visibility, stamp);
	}
	public static CanonicalFactSet DecodeFacts(ReadOnlySpan<byte> data)
	{
		var r = new Reader(data, 2);
		var version = r.Int64();
		var count = r.Count(128, 42);
		var facts = ImmutableArray.CreateBuilder<SemanticFact>(count);
		for (var i = 0; i < count; i++)
		{
			var subject = new PageRef(r.String());
			var property = r.String();
			SemanticValue value = (NativeScalarTag)r.Byte() switch
			{
				NativeScalarTag.PageRef => new PageRef(r.String()),
				NativeScalarTag.Integer => r.Int64(),
				NativeScalarTag.Boolean => r.Boolean(),
				NativeScalarTag.Keyword => new Keyword(r.String()),
				_ => throw new InvalidDataException("Unknown scalar tag.")
			};
			var package = r.String();
			var commit = r.String();
			var path = r.String();
			var field = r.String();
			var local = r.Boolean();
			facts.Add(new(subject, property, value, new(package, commit, path, field, local, r.String(), r.String(), r.String(), r.String())));
		}
		r.End();
		return new(version, facts.MoveToImmutable());
	}
	private static void WritePage(Writer w, PageSnapshot page)
	{
		ArgumentNullException.ThrowIfNull(page);
		w.Byte(FormatVersion);
		w.Byte(1);
		w.String(page.Reference.Id);
		w.String(page.Key.Package);
		w.String(page.Key.Namespace);
		w.String(page.Key.SourceKey);
		w.String(page.Title);
		w.String(page.Body);
		w.Strings(page.Collections);
		WriteVisibility(w, page.Visibility);
		ArgumentNullException.ThrowIfNull(page.Stamp);
		w.String(page.Stamp.SemanticCommit);
		w.Int64(page.Stamp.IndexGeneration);
		w.Int64(page.Stamp.SchemaVersion);
		w.Int64(page.Stamp.RequirementsVersion);
		w.Int64(page.Stamp.CollectionVersion);
		w.Int64(page.Stamp.PartitionVersion);
		w.Int64(page.Stamp.PageVersion);
		w.Int64(page.Stamp.TranslationVersion);
	}
	private static void WriteVisibility(Writer w, VisibilityDescriptor descriptor)
	{
		ArgumentNullException.ThrowIfNull(descriptor);
		w.String(descriptor.Namespace);
		w.Strings(descriptor.Categories);
		w.Strings(descriptor.RuleIds);
		w.Byte(descriptor.Published ? (byte)1 : (byte)0);
		w.Byte(descriptor.DraftAuthor is null ? (byte)0 : (byte)1);
		if (descriptor.DraftAuthor is not null) w.String(descriptor.DraftAuthor);
	}
	private static void WriteFacts(Writer w, CanonicalFactSet facts)
	{
		ArgumentNullException.ThrowIfNull(facts);
		if (facts.Facts.IsDefault || facts.Facts.Length > NativeFixtureLimits.MaxFacts) throw new InvalidDataException("Fact count exceeds the prototype bound.");
		w.Byte(FormatVersion);
		w.Byte(2);
		w.Int64(facts.SchemaVersion);
		w.Int32(facts.Facts.Length);
		foreach (var fact in facts.Facts)
		{
			ArgumentNullException.ThrowIfNull(fact);
			w.String(fact.Subject.Id);
			w.String(fact.Property);
			switch (fact.Value)
			{
				case PageRef reference: w.Byte((byte)NativeScalarTag.PageRef); w.String(reference.Id); break;
				case long integer: w.Byte((byte)NativeScalarTag.Integer); w.Int64(integer); break;
				case bool boolean: w.Byte((byte)NativeScalarTag.Boolean); w.Byte(boolean ? (byte)1 : (byte)0); break;
				case Keyword keyword: w.Byte((byte)NativeScalarTag.Keyword); w.String(keyword.Value); break;
				default: throw new InvalidDataException("Unknown scalar type.");
			}
			ArgumentNullException.ThrowIfNull(fact.Provenance);
			var p = fact.Provenance;
			w.String(p.Package);
			w.String(p.SourceCommit);
			w.String(p.SourcePath);
			w.String(p.AuthoredField);
			w.Byte(p.LocalOverride ? (byte)1 : (byte)0);
			w.String(p.PackageVersion);
			w.String(p.SourceBlobHash);
			w.String(p.ExtractionRuleVersion);
			w.String(p.EvidenceLocation);
		}
	}
	private static void WriteSchema(Writer w, SemanticSchema schema)
	{
		ArgumentNullException.ThrowIfNull(schema);
		if (schema.Properties.IsDefault || schema.Properties.Length > NativeFixtureLimits.MaxDeltaBytes / 24)
			throw new InvalidDataException("Schema initialization exceeds the job bound.");
		w.Byte(FormatVersion);
		w.Byte(3);
		w.Int64(schema.Version);
		w.Int32(schema.Properties.Length);
		foreach (var p in schema.Properties)
		{
			ArgumentNullException.ThrowIfNull(p);
			w.String(p.Id);
			w.Int64(p.SchemaVersion);
			w.Byte(p.Type switch
			{
				SemanticType.PageRef => (byte)NativeScalarTag.PageRef,
				SemanticType.Integer => (byte)NativeScalarTag.Integer,
				SemanticType.Boolean => (byte)NativeScalarTag.Boolean,
				SemanticType.Keyword => (byte)NativeScalarTag.Keyword,
				_ => throw new ArgumentException("Unknown schema type.")
			});
			if (!Enum.IsDefined(p.Cardinality)) throw new ArgumentException("Unknown cardinality.");
			w.Byte(p.Cardinality == Cardinality.Single ? (byte)1 : (byte)2);
			if (p.AllowedKeywords.IsDefault || p.AllowedKeywords.Length > NativeFixtureLimits.MaxDeltaBytes / 4)
				throw new InvalidDataException("Schema enum exceeds the job bound.");
			w.Int32(p.AllowedKeywords.Length);
			foreach (var keyword in p.AllowedKeywords) w.String(keyword.Value);
		}
	}
	private static int Measure(Action<Writer> write, int limit)
	{
		var w = new Writer(null, limit);
		write(w);
		return w.Position;
	}
	private static byte[] Encode(Action<Writer> write, int limit)
	{
		var size = Measure(write, limit);
		var bytes = new byte[size];
		var writer = new Writer(bytes, size);
		write(writer);
		if (writer.Position != size) throw new InvalidDataException("Encoded length changed.");
		return bytes;
	}
	private sealed class Writer(byte[]? bytes, int limit)
	{
		public int Position { get; private set; }
		private int Take(int count)
		{
			if (count < 0 || count > limit - Position) throw new InvalidDataException("Encoded record exceeds the prototype byte bound.");
			var start = Position;
			Position += count;
			return start;
		}
		public void Byte(byte value)
		{
			var at = Take(1);
			if (bytes is not null) bytes[at] = value;
		}
		public void Int32(int value)
		{
			var at = Take(4);
			if (bytes is not null) BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(at), value);
		}
		public void Int64(long value)
		{
			var at = Take(8);
			if (bytes is not null) BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(at), value);
		}
		public void String(string value)
		{
			ArgumentNullException.ThrowIfNull(value);
			if (value.Length > limit - Position - 4) throw new InvalidDataException("String exceeds the prototype byte bound.");
			var count = Utf8.GetByteCount(value);
			Int32(count);
			var at = Take(count);
			if (bytes is not null) Utf8.GetBytes(value, bytes.AsSpan(at, count));
		}
		public void Strings(ImmutableArray<string> values)
		{
			if (values.IsDefault || values.Length > (limit - Position - 4) / 4) throw new InvalidDataException("String array exceeds the prototype byte bound.");
			Int32(values.Length);
			foreach (var value in values) String(value);
		}
	}
	private ref struct Reader
	{
		private readonly ReadOnlySpan<byte> _data;
		private int _position;
		public Reader(ReadOnlySpan<byte> data, byte kind)
		{
			_data = data;
			_position = 0;
			if (data.Length > RowLimit || Byte() != FormatVersion || Byte() != kind) throw new InvalidDataException("Unsupported record version/kind or size.");
		}
		private ReadOnlySpan<byte> Take(int count)
		{
			if (count < 0 || count > _data.Length - _position) throw new InvalidDataException("Truncated record or invalid length.");
			var value = _data.Slice(_position, count);
			_position += count;
			return value;
		}
		public byte Byte() => Take(1)[0];
		public bool Boolean() => Byte() switch { 0 => false, 1 => true, _ => throw new InvalidDataException("Invalid Boolean encoding.") };
		public long Int64() => BinaryPrimitives.ReadInt64LittleEndian(Take(8));
		public int Count(int maximum, int minimumBytes)
		{
			var count = BinaryPrimitives.ReadInt32LittleEndian(Take(4));
			if (count < 0 || count > maximum || count > (_data.Length - _position) / minimumBytes) throw new InvalidDataException("Invalid count.");
			return count;
		}
		public string String()
		{
			var length = BinaryPrimitives.ReadInt32LittleEndian(Take(4));
			var bytes = Take(length);
			try { return Utf8.GetString(bytes); }
			catch (DecoderFallbackException e) { throw new InvalidDataException("Invalid UTF-8.", e); }
		}
		public ImmutableArray<string> Strings()
		{
			var count = Count(RowLimit / 4, 4);
			var result = ImmutableArray.CreateBuilder<string>(count);
			for (var i = 0; i < count; i++) result.Add(String());
			return result.MoveToImmutable();
		}
		public VisibilityDescriptor Visibility()
		{
			var ns = String();
			var categories = Strings();
			var rules = Strings();
			var published = Boolean();
			return new(ns, categories, rules, published, Boolean() ? String() : null);
		}
		public void End()
		{
			if (_position != _data.Length) throw new InvalidDataException("Trailing record bytes.");
		}
	}
}
