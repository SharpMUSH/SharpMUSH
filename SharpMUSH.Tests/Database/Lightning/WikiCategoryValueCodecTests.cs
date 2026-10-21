using System.Buffers.Binary;
using System.Text;
using SharpMUSH.Database.Lightning.Migration;
using SharpMUSH.Library.Plugins.Storage.Lightning;

namespace SharpMUSH.Tests.Database.Lightning;

public class WikiCategoryValueCodecTests
{
	// Fixtures deliberately use the current legacy expressions, never codec output.
	private static byte[] LegacyPrefix(string label) => Keys.Concat(Keys.Upper(label), Keys.Sep);
	private static byte[] LegacyKey(string label, string title, long pageKey)
		=> Keys.Concat(LegacyPrefix(label), Encoding.BigEndianUnicode.GetBytes(title), [0, 0], Keys.Dbref(pageKey));
	private static byte[] LegacyValue(bool? published, string author)
		=> Keys.Concat([(published ?? true) ? (byte)1 : (byte)0], Encoding.UTF8.GetBytes(author));
	private static byte[] VersionedValue(string label, bool? published, string author)
	{
		var legacy = LegacyValue(published, author);
		var result = new byte[legacy.Length + 8];
		legacy.CopyTo(result, 0);
		BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(legacy.Length, 4), checked((uint)(LegacyPrefix(label).Length - 1)));
		new byte[] { 0xFF, 0x43, 0, 1 }.CopyTo(result, result.Length - 4);
		return result;
	}

	[Test]
	[Arguments("", "P", 0L)]
	[Arguments("a", "P", 0L)]
	[Arguments("a\0b", "P\0Q", -1L)]
	[Arguments("\0", "😀", long.MinValue)]
	[Arguments("a\0\0b\0", "", long.MaxValue)]
	[Arguments("éıſσ😀", "\0😀\uE000Q", 42L)]
	public async Task PrefixAndKeyMatchIndependentLegacyBytes(string label, string title, long pageKey)
	{
		var expectedPrefix = LegacyPrefix(label);
		var prefix = WikiCategoryValueCodec.LabelPrefix(label);
		var originalPrefix = prefix.ToArray();
		var key = WikiCategoryValueCodec.Key(prefix, title, pageKey);
		var prefixMatches = prefix.AsSpan().SequenceEqual(expectedPrefix);
		var keyMatches = key.AsSpan().SequenceEqual(LegacyKey(label, title, pageKey));
		var inputUnchanged = prefix.AsSpan().SequenceEqual(originalPrefix);
		var decoded = Snapshot(key, VersionedValue(label, true, "#1"));
		await Assert.That(prefixMatches && keyMatches && inputUnchanged).IsTrue();
		await Assert.That(decoded.Label.AsSpan().SequenceEqual(expectedPrefix.AsSpan(0, expectedPrefix.Length - 1))).IsTrue();
		await Assert.That(decoded.LabelLength).IsEqualTo(expectedPrefix.Length - 1);
		await Assert.That(decoded.PageKey).IsEqualTo(pageKey);
	}

	[Test]
	[Arguments("empty")]
	[Arguments("legacy-three")]
	[Arguments("legacy-eleven")]
	[Arguments("legacy-thirteen")]
	[Arguments("unicode-nul")]
	[Arguments("long")]
	public async Task ValuePreservesWholeLegacyVisibilityAndAddsExactlyEightBytes(string scenario)
	{
		var (published, author) = scenario switch
		{
			"empty" => ((bool?)null, ""),
			"legacy-three" => ((bool?)true, "#1"),
			"legacy-eleven" => ((bool?)false, "1234567890"),
			"legacy-thirteen" => ((bool?)null, "123456789012"),
			"unicode-nul" => ((bool?)false, "作者\0😀not-a-dbref\uD800"),
			"long" => ((bool?)true, new string('x', 8193)),
			_ => throw new ArgumentOutOfRangeException(nameof(scenario))
		};
		const string label = "é\0b";
		var prefix = LegacyPrefix(label);
		var originalPrefix = prefix.ToArray();
		var value = WikiCategoryValueCodec.Value(prefix, published, author);
		var expected = VersionedValue(label, published, author);
		var legacy = LegacyValue(published, author);
		var exactBytes = value.AsSpan().SequenceEqual(expected);
		var inputUnchanged = prefix.AsSpan().SequenceEqual(originalPrefix);
		var decoded = Snapshot(LegacyKey(label, "Title", 0), value);
		await Assert.That(exactBytes && inputUnchanged).IsTrue();
		await Assert.That(value.Length).IsEqualTo(legacy.Length + 8);
		await Assert.That(decoded.Visibility.AsSpan().SequenceEqual(legacy)).IsTrue();
	}

	[Test]
	public async Task RuntimeUnpairedSurrogatesRetainLegacyEncoderFallback()
	{
		// Build at runtime: attribute SerString encoding cannot preserve malformed UTF-16 reliably.
		var label = "a" + new string((char)0xD800, 1) + "\0b";
		var title = "P" + new string((char)0xDC00, 1) + "\0Q";
		var author = "author" + new string((char)0xD800, 1) + "\0end";
		var expectedPrefix = LegacyPrefix(label);
		var prefix = WikiCategoryValueCodec.LabelPrefix(label);
		var key = WikiCategoryValueCodec.Key(prefix, title, -1);
		var value = WikiCategoryValueCodec.Value(prefix, null, author);
		var exactBytes = prefix.AsSpan().SequenceEqual(expectedPrefix)
			&& key.AsSpan().SequenceEqual(LegacyKey(label, title, -1))
			&& value.AsSpan().SequenceEqual(VersionedValue(label, null, author));
		var decoded = Snapshot(key, value);
		var decodedMatches = decoded.Label.AsSpan().SequenceEqual(expectedPrefix.AsSpan(0, expectedPrefix.Length - 1))
			&& decoded.Visibility.AsSpan().SequenceEqual(LegacyValue(null, author));
		await Assert.That(exactBytes && decodedMatches).IsTrue();
		await Assert.That(decoded.LabelLength).IsEqualTo(expectedPrefix.Length - 1);
		await Assert.That(decoded.PageKey).IsEqualTo(-1L);
	}

	// Copy only after decoding synchronously; no ref struct crosses an await or lambda boundary.
	private static (byte[] Label, byte[] Visibility, int LabelLength, long PageKey) Snapshot(byte[] key, byte[] value)
	{
		var decoded = WikiCategoryValueCodec.Decode(key, value);
		return (decoded.LabelBytes.ToArray(), decoded.Visibility.ToArray(), decoded.LabelByteLength, decoded.PageKey);
	}

	[Test]
	public async Task DecodeDoesNotAddUtf16TitleValidation()
	{
		const string label = "a\0b";
		var prefix = LegacyPrefix(label);
		var key = LegacyKey(label, "P", -9);
		// Preserve even title byte length while inserting an unpaired UTF-16 high surrogate.
		// Encoding.GetBytes would replace it and would not exercise this persisted byte shape.
		key[prefix.Length] = 0xD8;
		key[prefix.Length + 1] = 0;
		var value = VersionedValue(label, false, "author\0tail");
		var decoded = Snapshot(key, value);
		var unchangedFields = decoded.Label.AsSpan().SequenceEqual(prefix.AsSpan(0, prefix.Length - 1))
			&& decoded.Visibility.AsSpan().SequenceEqual(LegacyValue(false, "author\0tail"));
		await Assert.That(unchangedFields).IsTrue();
		await Assert.That(decoded.LabelLength).IsEqualTo(prefix.Length - 1);
		await Assert.That(decoded.PageKey).IsEqualTo(-9L);
	}

	[Test]
	public async Task DecodeBorrowsBothInputsAndUsesRecordedLengthPastEmbeddedNuls()
	{
		var borrowed = VerifyBorrowed();
		await Assert.That(borrowed).IsTrue();
	}

	private static bool VerifyBorrowed()
	{
		var key = LegacyKey("a\0b\0c", "P\0Q", -7);
		var value = VersionedValue("a\0b\0c", false, "who\0ever");
		var legacy = LegacyValue(false, "who\0ever");
		var decoded = WikiCategoryValueCodec.Decode(key, value);
		var correct = decoded.LabelByteLength == 5 && decoded.PageKey == -7
			&& decoded.LabelBytes.SequenceEqual(new byte[] { 65, 0, 66, 0, 67 })
			&& decoded.Visibility.SequenceEqual(legacy);
		// Valid same-length mutations expose a copy disguised as a borrowed span.
		key[4] = (byte)'Z';
		value[1] = (byte)'W';
		return correct && decoded.LabelBytes[4] == (byte)'Z' && decoded.Visibility[1] == (byte)'W';
	}

	[Test]
	public async Task KeysRetainOrdinalTitleOrderAndStablePageIdTieBreak()
	{
		var titles = new[] { "Q", "P😀", "P", "P\0", "P\uE000", "P\0Q" };
		var expected = titles.OrderBy(title => title, StringComparer.Ordinal).ToArray();
		var prefix = LegacyPrefix("category");
		var actual = titles.OrderBy(title => WikiCategoryValueCodec.Key(prefix, title, 0), ByteOrder.Instance).ToArray();
		var ordered = actual.SequenceEqual(expected);
		var zero = WikiCategoryValueCodec.Key(prefix, "P", 0);
		var one = WikiCategoryValueCodec.Key(prefix, "P", 1);
		var two = WikiCategoryValueCodec.Key(prefix, "P", 2);
		await Assert.That(ordered && ByteOrder.Instance.Compare(zero, one) < 0 && ByteOrder.Instance.Compare(one, two) < 0).IsTrue();
	}

	private sealed class ByteOrder : IComparer<byte[]>
	{
		internal static readonly ByteOrder Instance = new();
		public int Compare(byte[]? x, byte[]? y) => x.AsSpan().SequenceCompareTo(y.AsSpan());
	}

	[Test]
	[Arguments("legacy")]
	[Arguments("truncated")]
	[Arguments("unknown-magic")]
	[Arguments("unknown-version")]
	[Arguments("publication-two")]
	[Arguments("publication-255")]
	[Arguments("label-length-over-int")]
	[Arguments("label-length-past-key")]
	[Arguments("missing-label-boundary")]
	[Arguments("missing-final-separator")]
	[Arguments("odd-title")]
	[Arguments("invalid-label-utf8")]
	[Arguments("invalid-author-utf8")]
	[Arguments("short-key")]
	public async Task DecodeRejectsMalformedInputWithoutLegacyFallback(string scenario)
	{
		var (key, value) = Corrupt(scenario);
		var failure = DecodeFailure(key, value);
		var (baselineKey, baselineValue) = Corrupt("truncated");
		var baseline = DecodeFailure(baselineKey, baselineValue);
		await Assert.That(failure is InvalidDataException).IsTrue();
		await Assert.That(failure?.Message).IsEqualTo(baseline?.Message);
	}

	// Every invocation constructs fresh arrays; cases never share mutable test data.
	private static (byte[] Key, byte[] Value) Corrupt(string scenario)
	{
		var key = LegacyKey("ab", "PQ", 0);
		var value = VersionedValue("ab", true, "#1");
		switch (scenario)
		{
			case "legacy": value = LegacyValue(true, "#12345678901234567890"); break;
			case "truncated": value = value[..8]; break;
			case "unknown-magic": value[^4] ^= 1; break;
			case "unknown-version": value[^1] = 2; break;
			case "publication-two": value[0] = 2; break;
			case "publication-255": value[0] = 255; break;
			case "label-length-over-int": BinaryPrimitives.WriteUInt32BigEndian(value.AsSpan(value.Length - 8, 4), 0x80000000); break;
			case "label-length-past-key": BinaryPrimitives.WriteUInt32BigEndian(value.AsSpan(value.Length - 8, 4), 100); break;
			case "missing-label-boundary": key[2] = 1; break;
			case "missing-final-separator": key[^9] = 1; break;
			case "odd-title": key = key[..3].Concat(key[4..]).ToArray(); break;
			case "invalid-label-utf8": key[0] = 0xC0; key[1] = 0xAF; break;
			case "invalid-author-utf8": value[1] = 0xED; value[2] = 0xA0; break;
			case "short-key": key = key[..7]; break;
			default: throw new ArgumentOutOfRangeException(nameof(scenario));
		}
		return (key, value);
	}

	private static Exception? DecodeFailure(byte[] key, byte[] value, CancellationToken token = default)
	{
		try { _ = WikiCategoryValueCodec.Decode(key, value, token); return null; }
		catch (Exception failure) { return failure; }
	}

	[Test]
	public async Task PreCancelledDecodeThrowsExactOperationCanceledExceptionWithCallerToken()
	{
		using var cancellation = new CancellationTokenSource();
		cancellation.Cancel();
		var failure = DecodeFailure(LegacyKey("a", "P", 0), VersionedValue("a", true, "#1"), cancellation.Token);
		var exactTypeAndToken = failure?.GetType() == typeof(OperationCanceledException)
			&& ((OperationCanceledException)failure).CancellationToken == cancellation.Token;
		await Assert.That(exactTypeAndToken).IsTrue();
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task KeyRejectsEmptyOrUnterminatedPrefix(bool unterminated)
	{
		var prefix = unterminated ? new byte[] { 65 } : [];
		Exception? failure = null;
		try { _ = WikiCategoryValueCodec.Key(prefix, "Title", 0); }
		catch (Exception error) { failure = error; }
		await Assert.That(failure is ArgumentException).IsTrue();
	}

	[Test]
	public async Task ValidLegacyAuthorCannotImpersonateVersionedCategoryVisibility()
	{
		// Valid UTF-8, including NULs: the former ASCII trailer misread this author as "who".
		const string author = "who\0\0\0\u0002WC\0\u0001";
		var failure = DecodeFailure(LegacyKey("ab", "P", 0), LegacyValue(true, author));
		Console.WriteLine("Legacy footer collision: " + (failure?.GetType().Name ?? "accepted"));
		await Assert.That(failure).IsNotNull();
		await Assert.That(failure is InvalidDataException).IsTrue();
	}

	[Test]
	public async Task VersionedTrailerCannotBeValidLegacyUtf8()
	{
		var value = WikiCategoryValueCodec.Value(LegacyPrefix("ab"), true, "who");
		var rejectedByStrictUtf8 = false;
		try { _ = new UTF8Encoding(false, true).GetCharCount(value.AsSpan(value.Length - 4)); }
		catch (DecoderFallbackException) { rejectedByStrictUtf8 = true; }
		await Assert.That(rejectedByStrictUtf8).IsTrue();
	}
}
