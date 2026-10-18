using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Text;
using SharpMUSH.Library.Models.Wiki.Semantics;

namespace SharpMUSH.Database.Lightning.Semantics;

/// <summary>
/// Version byte, kind byte (facts=2, schema=3, projection=5), then explicit fields. Lengths/counts and signed
/// numbers are little-endian; keys use their separate big-endian order. Strings are strict UTF-8.
/// Decoded models need a caller-owned working-memory reservation before this pure codec is called.
/// </summary>
internal static class WikiSemanticCodec
{
	public const byte FormatVersion = 1;
	private static readonly UTF8Encoding Utf8 = new(false, true);
	internal const int RowLimit = WikiSemanticLimits.MaxReadBytes - 8;
	public static byte[] EncodeFacts(CanonicalFactSet facts) => Encode(w => WriteFacts(w, facts), RowLimit);
	internal static int MeasureFacts(CanonicalFactSet facts, int limit) => Measure(w => WriteFacts(w, facts), limit);
	internal static int MeasureSchema(SemanticSchema schema) => Measure(w => WriteSchema(w, schema), WikiSemanticLimits.MaxDeltaBytes);
	internal static byte[] SchemaIdentity(SemanticSchema schema) => Encode(w => WriteSchema(w, schema), WikiSemanticLimits.MaxDeltaBytes);
	internal static int DescriptorSize(VisibilityDescriptor descriptor) => Measure(w => WriteVisibility(w, descriptor), WikiSemanticLimits.MaxDescriptorBytes);

	internal static byte[] EncodeProjection(WikiPageProjection page) => Encode(w => WriteProjection(w, page), RowLimit);
	internal static int MeasureProjection(WikiPageProjection page) => Measure(w => WriteProjection(w, page), RowLimit);
	internal static WikiPageProjection DecodeProjection(ReadOnlySpan<byte> data)
	{
		var reader = new Reader(data, 5);
		var reference = new PageRef(reader.String());
		WikiSemanticSource source = reader.Byte() switch
		{
			0 => new LocalWikiSource(),
			1 => new PortableWikiSource(new(reader.String(), reader.String(), reader.String())),
			_ => throw new InvalidDataException("Unknown semantic source tag.")
		};
		var page = new WikiPageProjection(reference, source, reader.String(), reader.String(), reader.Visibility());
		reader.End();
		return page;
	}
	private static void WriteProjection(Writer writer, WikiPageProjection page)
	{
		ArgumentNullException.ThrowIfNull(page);
		writer.Byte(FormatVersion); writer.Byte(5); writer.String(page.Reference.Id);
		switch (page.Source)
		{
			case LocalWikiSource: writer.Byte(0); break;
			case PortableWikiSource portable:
				writer.Byte(1); writer.String(portable.Key.Package); writer.String(portable.Key.Namespace); writer.String(portable.Key.SourceKey); break;
			default: throw new ArgumentException("Unknown semantic source.");
		}
		writer.String(page.Title); writer.String(page.Collection); WriteVisibility(writer, page.Visibility);
	}

	internal static int SummarySize(WikiPageProjection page) => Measure(w =>
	{
		w.Byte(FormatVersion);
		w.Byte(4);
		w.String(page.Reference.Id);
		w.Int64(1); // native ID
		w.String(page.Title);
		WriteVisibility(w, page.Visibility);
		w.String(page.Collection);
		w.Byte(0); // deleted
		w.Int64(1); // native state version
	}, WikiSemanticLimits.MaxSummaryBytes);

	public static CanonicalFactSet DecodeFacts(ReadOnlySpan<byte> data)
	{
		var r = new Reader(data, 2);
		var version = r.Int64();
		var count = r.Count(WikiSemanticLimits.MaxFacts, 42);
		var facts = ImmutableArray.CreateBuilder<SemanticFact>(count);
		for (var i = 0; i < count; i++)
		{
			var subject = new PageRef(r.String());
			var property = r.String();
			SemanticValue value = (WikiSemanticScalarTag)r.Byte() switch
			{
				WikiSemanticScalarTag.PageRef => new PageRef(r.String()),
				WikiSemanticScalarTag.Integer => r.Int64(),
				WikiSemanticScalarTag.Boolean => r.Boolean(),
				WikiSemanticScalarTag.Keyword => new Keyword(r.String()),
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
	internal static void WriteVisibility(Writer w, VisibilityDescriptor descriptor)
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
		if (facts.Facts.IsDefault || facts.Facts.Length > WikiSemanticLimits.MaxFacts) throw new InvalidDataException("Fact count exceeds the prototype bound.");
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
				case PageRef reference: w.Byte((byte)WikiSemanticScalarTag.PageRef); w.String(reference.Id); break;
				case long integer: w.Byte((byte)WikiSemanticScalarTag.Integer); w.Int64(integer); break;
				case bool boolean: w.Byte((byte)WikiSemanticScalarTag.Boolean); w.Byte(boolean ? (byte)1 : (byte)0); break;
				case Keyword keyword: w.Byte((byte)WikiSemanticScalarTag.Keyword); w.String(keyword.Value); break;
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
		if (schema.Properties.IsDefault || schema.Properties.Length > WikiSemanticLimits.MaxDeltaBytes / 24)
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
				SemanticType.PageRef => (byte)WikiSemanticScalarTag.PageRef,
				SemanticType.Integer => (byte)WikiSemanticScalarTag.Integer,
				SemanticType.Boolean => (byte)WikiSemanticScalarTag.Boolean,
				SemanticType.Keyword => (byte)WikiSemanticScalarTag.Keyword,
				_ => throw new ArgumentException("Unknown schema type.")
			});
			if (!Enum.IsDefined(p.Cardinality)) throw new ArgumentException("Unknown cardinality.");
			w.Byte(p.Cardinality == Cardinality.Single ? (byte)1 : (byte)2);
			if (p.AllowedKeywords.IsDefault || p.AllowedKeywords.Length > WikiSemanticLimits.MaxDeltaBytes / 4)
				throw new InvalidDataException("Schema enum exceeds the job bound.");
			w.Int32(p.AllowedKeywords.Length);
			foreach (var keyword in p.AllowedKeywords) w.String(keyword.Value);
		}
	}
	internal static int Measure(Action<Writer> write, int limit)
	{
		var w = new Writer(null, limit);
		write(w);
		return w.Position;
	}
	internal static byte[] Encode(Action<Writer> write, int limit)
	{
		var size = Measure(write, limit);
		var bytes = new byte[size];
		var writer = new Writer(bytes, size);
		write(writer);
		if (writer.Position != size) throw new InvalidDataException("Encoded length changed.");
		return bytes;
	}
	internal sealed class Writer(byte[]? bytes, int limit)
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
	internal ref struct Reader
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
