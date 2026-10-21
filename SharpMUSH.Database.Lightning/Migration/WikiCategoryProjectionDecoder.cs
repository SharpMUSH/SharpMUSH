using System.Buffers;
using System.Text;
using System.Text.Json;
using SharpMUSH.Library.Plugins.Storage.Lightning;

namespace SharpMUSH.Database.Lightning.Migration;

/// <summary>Decodes the managed wiki fields needed for category projection.</summary>
/// <remarks>
/// The caller owns the source array and must keep it immutable throughout decoding.
/// The working-byte budget models managed storage; it does not bound process RSS,
/// native work, or the duration of individual runtime primitives.
/// </remarks>
internal static class WikiCategoryProjectionDecoder
{
	private readonly record struct Token(int Offset, int Length, int Chars, bool Null);
	private readonly record struct StringState(byte[] Scratch, int Bytes, CancellationToken Cancellation);
	private static readonly SpanAction<char, StringState> WriteString = static (destination, state) =>
		Write(state.Scratch.AsSpan(0, state.Bytes), destination, state.Cancellation);

	/// <summary>Decodes using the caller's shared native working-allocation budget.</summary>
	/// <remarks>
	/// The immutable source is borrowed: its array and owner must already be reserved on this same
	/// budget, and the caller must keep that reservation alive throughout decoding. The projection
	/// retains neither the source nor scratch storage and owns its own retained-storage reservation.
	/// </remarks>
	internal static WikiCategoryProjection? Decode(byte[] ownedSource, NativeReadBudget budget)
	{
		ArgumentNullException.ThrowIfNull(ownedSource);
		ArgumentNullException.ThrowIfNull(budget);
		var cancellationToken = budget.CancellationToken;
		cancellationToken.ThrowIfCancellationRequested();
		try { return DecodeCore(ownedSource, 0, cancellationToken, budget); }
		catch (OverflowException) { throw new NativeReadLimitException("working-bytes"); }
		catch (JsonException) { throw new JsonException("Invalid wiki record JSON."); }
		catch (InvalidOperationException) { throw new JsonException("Invalid wiki record value."); }
	}

	public static WikiCategoryProjection? Decode(byte[] ownedSource, long maxWorkingBytes,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(ownedSource);
		cancellationToken.ThrowIfCancellationRequested();
		try { return DecodeCore(ownedSource, maxWorkingBytes, cancellationToken); }
		catch (OverflowException) { throw new WikiCategoryProjectionCapacityException(); }
		catch (JsonException) { throw new JsonException("Invalid wiki record JSON."); }
		catch (InvalidOperationException) { throw new JsonException("Invalid wiki record value."); }
	}

	private static WikiCategoryProjection? DecodeCore(byte[] source, long maximum, CancellationToken ct,
		NativeReadBudget? nativeBudget = null)
	{
		// Preserve the numeric model's initial admission before preflight. The native path's
		// caller already owns the source; only real reservations admit decoder allocations.
		var initial = 0L;
		if (nativeBudget is null)
		{
			initial = checked(ArrayCharge(source.Length) * 2 + 256);
			if (initial > maximum) throw new WikiCategoryProjectionCapacityException();
		}
		PreflightLiterals(source, ct);
		var budget = nativeBudget is null ? new ProjectionBudget(maximum, initial) : null;
		using var scratchLease = nativeBudget?.ReserveWorking(ArrayCharge(source.Length));
		var scratch = new byte[source.Length];
		var reader = Reader(source);
		Read(ref reader, ct);
		if (reader.TokenType == JsonTokenType.Null)
		{
			if (Next(ref reader, ct)) throw Invalid();
			return null;
		}
		if (reader.TokenType != JsonTokenType.StartObject) throw Invalid();
		Token title = default, author = default, categories = default;
		var titleSet = false;
		var authorSet = false;
		var categoriesNull = true;
		var categoryCount = 0;
		var categoryChars = 0;
		bool? published = null;
		while (Next(ref reader, ct) && reader.TokenType != JsonTokenType.EndObject)
		{
			if (reader.TokenType != JsonTokenType.PropertyName) throw Invalid();
			var nameLength = UnescapeName(reader.ValueSpan, scratch, ct);
			var name = scratch.AsSpan(0, nameLength);
			var field = Field(name);
			Read(ref reader, ct);
			if (field == 0)
			{
				Skip(ref reader, ct);
				continue;
			}
			if (field == 2)
			{
				if (reader.TokenType == JsonTokenType.Null) published = null;
				else if (reader.TokenType == JsonTokenType.True) published = true;
				else if (reader.TokenType == JsonTokenType.False) published = false;
				else throw Invalid();
			}
			else if (field == 3)
			{
				if (reader.TokenType != JsonTokenType.Number || !reader.TryGetInt32(out _)) throw Invalid();
			}
			else if (field == 4)
			{
				categoriesNull = reader.TokenType == JsonTokenType.Null;
				categoryCount = 0;
				categoryChars = 0;
				if (!categoriesNull)
				{
					if (reader.TokenType != JsonTokenType.StartArray) throw Invalid();
					var start = checked((int)reader.TokenStartIndex);
					while (Next(ref reader, ct) && reader.TokenType != JsonTokenType.EndArray)
					{
						var token = ValidateString(ref reader, scratch, ct);
						categoryCount = checked(categoryCount + 1);
						categoryChars = checked(categoryChars + token.Chars);
					}
					if (reader.TokenType != JsonTokenType.EndArray) throw Invalid();
					categories = new Token(start, checked((int)reader.BytesConsumed - start), 0, false);
				}
			}
			else
			{
				var token = ValidateString(ref reader, scratch, ct);
				if (field == 5) { title = token; titleSet = true; }
				if (field == 6) { author = token; authorSet = true; }
			}
		}
		if (reader.TokenType != JsonTokenType.EndObject || Next(ref reader, ct)) throw Invalid();
		var retained = checked(128L + ArrayCharge(checked(categoryCount * 16L)) +
			ArrayCharge(checked(categoryChars * 2L)) * 2 + ArrayCharge(checked(categoryCount * 4L)) + ArrayCharge(categoryCount));
		if (titleSet && !title.Null) retained = checked(retained + StringCharge(title.Chars));
		if (authorSet && !author.Null) retained = checked(retained + StringCharge(author.Chars));
		budget?.Add(retained - 128);
		var retainedLease = nativeBudget?.ReserveWorking(retained);
		try
		{
			ct.ThrowIfCancellationRequested();
			var slices = new CategorySlice[categoryCount];
			var chars = new char[categoryChars];
			var folded = new char[categoryChars];
			var indices = new int[categoryCount];
			var first = new byte[categoryCount];
			var hasNull = false;
			if (!categoriesNull)
			{
				var values = Reader(source.AsSpan(categories.Offset, categories.Length));
				Read(ref values, ct);
				var offset = 0;
				for (var i = 0; i < categoryCount; i++)
				{
					Read(ref values, ct);
					var token = ValidateString(ref values, scratch, ct);
					slices[i] = new CategorySlice(offset, token.Chars, token.Null ? 1 : 0, i);
					indices[i] = i;
					if (token.Null) { hasNull = true; continue; }
					var bytes = Copy(ref values, scratch, ct);
					Write(scratch.AsSpan(0, bytes), chars.AsSpan(offset, token.Chars), ct);
					ct.ThrowIfCancellationRequested();
					var written = ((ReadOnlySpan<char>)chars.AsSpan(offset, token.Chars)).ToUpperInvariant(folded.AsSpan(offset, token.Chars));
					ct.ThrowIfCancellationRequested();
					if (written != token.Chars) throw Invalid();
					offset = checked(offset + token.Chars);
				}
			}
			var distinct = hasNull ? 0 : Deduplicate(slices, folded, indices, first, ct);
			var finalTitle = !titleSet ? "" : Materialize(source, scratch, title, ct);
			var finalAuthor = !authorSet ? "" : Materialize(source, scratch, author, ct);
			ct.ThrowIfCancellationRequested();
			var projection = new WikiCategoryProjection(finalTitle, finalAuthor, published, categoriesNull,
				slices, chars, folded, indices, first, distinct, hasNull, budget?.Peak ?? 0, retained, retainedLease);
			retainedLease = null;
			return projection;
		}
		finally
		{
			retainedLease?.Dispose();
		}
	}

	// The reader's malformed-literal diagnostic can copy the entire remaining input.
	// Reject only those mismatched prefixes before allocating proportional storage.
	// This is deliberately not a JSON parser: the reader owns all other syntax.
	private static void PreflightLiterals(ReadOnlySpan<byte> source, CancellationToken ct)
	{
		var quoted = false;
		for (var i = 0; i < source.Length; i++)
		{
			ct.ThrowIfCancellationRequested();
			var value = source[i];
			if (quoted)
			{
				if (value == '\\')
				{
					// Escaped quotes do not terminate the string; malformed escapes
					// and a final backslash remain the reader's responsibility.
					if (i + 1 < source.Length) i++;
				}
				else if (value == '"') quoted = false;
				continue;
			}
			if (value == '"') { quoted = true; continue; }
			ReadOnlySpan<byte> literal = value switch
			{
				(byte)'t' => "true"u8,
				(byte)'f' => "false"u8,
				(byte)'n' => "null"u8,
				_ => default
			};
			if (literal.IsEmpty) continue;
			if (!source[i..].StartsWith(literal)) throw Invalid();
			i += literal.Length - 1;
		}
		ct.ThrowIfCancellationRequested();
	}

	private static int Field(ReadOnlySpan<byte> name)
	{
		if (name.SequenceEqual("Published"u8)) return 2;
		if (name.SequenceEqual("RevisionNumber"u8)) return 3;
		if (name.SequenceEqual("Categories"u8)) return 4;
		if (name.SequenceEqual("Title"u8)) return 5;
		if (name.SequenceEqual("AuthorDbref"u8)) return 6;
		return name.SequenceEqual("Slug"u8) || name.SequenceEqual("Namespace"u8) || name.SequenceEqual("MarkdownSource"u8) ||
			name.SequenceEqual("RenderedHtml"u8) || name.SequenceEqual("PlainText"u8) || name.SequenceEqual("LastEditorDbref"u8) ||
			name.SequenceEqual("CreatedAt"u8) || name.SequenceEqual("UpdatedAt"u8) || name.SequenceEqual("SourceLocale"u8) ? 1 : 0;
	}

	private static Token ValidateString(ref Utf8JsonReader reader, byte[] scratch, CancellationToken ct)
	{
		var start = checked((int)reader.TokenStartIndex);
		if (reader.TokenType == JsonTokenType.Null) return new Token(start, 4, 0, true);
		if (reader.TokenType != JsonTokenType.String) throw Invalid();
		var bytes = Copy(ref reader, scratch, ct);
		var remaining = scratch.AsSpan(0, bytes);
		var count = 0;
		while (!remaining.IsEmpty)
		{
			ct.ThrowIfCancellationRequested();
			if (Rune.DecodeFromUtf8(remaining, out var rune, out var consumed) != OperationStatus.Done) throw Invalid();
			count = checked(count + rune.Utf16SequenceLength);
			remaining = remaining[consumed..];
		}
		return new Token(start, checked((int)reader.BytesConsumed - start), count, false);
	}
	private static int Copy(ref Utf8JsonReader reader, byte[] scratch, CancellationToken ct)
	{
		ct.ThrowIfCancellationRequested();
		var bytes = reader.CopyString(scratch);
		ct.ThrowIfCancellationRequested();
		return bytes;
	}
	private static void Write(ReadOnlySpan<byte> input, Span<char> output, CancellationToken ct)
	{
		while (!input.IsEmpty)
		{
			ct.ThrowIfCancellationRequested();
			if (Rune.DecodeFromUtf8(input, out var rune, out var consumed) != OperationStatus.Done) throw Invalid();
			var written = rune.EncodeToUtf16(output);
			input = input[consumed..];
			output = output[written..];
		}
		ct.ThrowIfCancellationRequested();
	}
	private static string? Materialize(byte[] source, byte[] scratch, Token token, CancellationToken ct)
	{
		if (token.Null) return null;
		var reader = Reader(source.AsSpan(token.Offset, token.Length));
		Read(ref reader, ct);
		var bytes = Copy(ref reader, scratch, ct);
		ct.ThrowIfCancellationRequested();
		var result = string.Create(token.Chars, new StringState(scratch, bytes, ct), WriteString);
		ct.ThrowIfCancellationRequested();
		return result;
	}
	private static Utf8JsonReader Reader(ReadOnlySpan<byte> source) => new(source, new JsonReaderOptions { MaxDepth = 64 });
	private static bool Next(ref Utf8JsonReader reader, CancellationToken ct)
	{
		ct.ThrowIfCancellationRequested();
		var result = reader.Read();
		ct.ThrowIfCancellationRequested();
		return result;
	}
	private static void Read(ref Utf8JsonReader reader, CancellationToken ct) { if (!Next(ref reader, ct)) throw Invalid(); }
	private static void Skip(ref Utf8JsonReader reader, CancellationToken ct)
	{
		if (reader.TokenType is not (JsonTokenType.StartArray or JsonTokenType.StartObject)) return;
		var depth = reader.CurrentDepth;
		do { Read(ref reader, ct); } while (reader.CurrentDepth != depth || reader.TokenType is not (JsonTokenType.EndArray or JsonTokenType.EndObject));
	}
	private static int UnescapeName(ReadOnlySpan<byte> input, Span<byte> output, CancellationToken ct)
	{
		var pos = 0;
		for (var i = 0; i < input.Length; i++)
		{
			ct.ThrowIfCancellationRequested();
			var b = input[i];
			if (b != '\\') { output[pos++] = b; continue; }
			if (++i >= input.Length) throw Invalid();
			b = input[i];
			if (b != 'u')
			{
				output[pos++] = b switch { (byte)'"' => (byte)'"', (byte)'\\' => (byte)'\\', (byte)'/' => (byte)'/', (byte)'b' => 8, (byte)'f' => 12, (byte)'n' => 10, (byte)'r' => 13, (byte)'t' => 9, _ => throw Invalid() };
				continue;
			}
			var scalar = Hex(input, ref i);
			if (scalar is >= 0xD800 and <= 0xDBFF)
			{
				if (i + 2 >= input.Length || input[++i] != '\\' || input[++i] != 'u') throw Invalid();
				var low = Hex(input, ref i);
				if (low is < 0xDC00 or > 0xDFFF) throw Invalid();
				scalar = 0x10000 + ((scalar - 0xD800) << 10) + low - 0xDC00;
			}
			else if (scalar is >= 0xDC00 and <= 0xDFFF) throw Invalid();
			pos += new Rune(scalar).EncodeToUtf8(output[pos..]);
		}
		return pos;
	}
	private static int Hex(ReadOnlySpan<byte> input, ref int i)
	{
		var value = 0;
		for (var j = 0; j < 4; j++)
		{
			if (++i >= input.Length) throw Invalid();
			var b = input[i];
			var digit = b is >= (byte)'0' and <= (byte)'9' ? b - '0' : b is >= (byte)'a' and <= (byte)'f' ? b - 'a' + 10 : b is >= (byte)'A' and <= (byte)'F' ? b - 'A' + 10 : -1;
			if (digit < 0) throw Invalid();
			value = value * 16 + digit;
		}
		return value;
	}
	private static int Deduplicate(CategorySlice[] slices, char[] folded, int[] indices, byte[] first, CancellationToken ct)
	{
		// Heap sort bounds stack use independently of category count.
		for (var root = indices.Length / 2 - 1; root >= 0; root--) Sift(root, indices.Length, slices, folded, indices, ct);
		for (var end = indices.Length - 1; end > 0; end--)
		{
			ct.ThrowIfCancellationRequested();
			(indices[0], indices[end]) = (indices[end], indices[0]);
			Sift(0, end, slices, folded, indices, ct);
		}
		for (var i = 0; i < indices.Length; i++)
		{
			ct.ThrowIfCancellationRequested();
			if (i == 0 || Compare(indices[i - 1], indices[i], slices, folded, ct, false) != 0) first[indices[i]] = 1;
		}
		var count = 0;
		for (var i = 0; i < first.Length; i++) { ct.ThrowIfCancellationRequested(); if (first[i] != 0) indices[count++] = i; }
		return count;
	}
	private static void Sift(int root, int count, CategorySlice[] slices, char[] folded, int[] indices, CancellationToken ct)
	{
		while (root < count / 2)
		{
			ct.ThrowIfCancellationRequested();
			var child = root * 2 + 1;
			if (child + 1 < count && Compare(indices[child], indices[child + 1], slices, folded, ct, true) < 0) child++;
			if (Compare(indices[root], indices[child], slices, folded, ct, true) >= 0) return;
			(indices[root], indices[child]) = (indices[child], indices[root]);
			root = child;
		}
	}
	private static int Compare(int a, int b, CategorySlice[] slices, char[] folded, CancellationToken ct, bool tie)
	{
		var x = slices[a];
		var y = slices[b];
		for (var i = 0; i < Math.Min(x.Length, y.Length); i++)
		{
			ct.ThrowIfCancellationRequested();
			var result = folded[x.Offset + i].CompareTo(folded[y.Offset + i]);
			if (result != 0) return result;
		}
		ct.ThrowIfCancellationRequested();
		var length = x.Length.CompareTo(y.Length);
		return length != 0 || !tie ? length : a.CompareTo(b);
	}
	private static JsonException Invalid() => new("Invalid wiki record value.");
	internal static long ArrayCharge(long payload) => checked(32 + ((payload + 7) & ~7L));
	private static long StringCharge(int chars) => checked(64 + ((2L * chars + 2 + 7) & ~7L));
}
