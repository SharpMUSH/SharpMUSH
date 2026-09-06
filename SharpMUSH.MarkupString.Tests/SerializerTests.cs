using System.Buffers;
using System.Text;
using System.Text.Json;

public class SerializerTests
{
	private sealed record Tag(string Name) : IMarkup;

	private sealed record Uncodeced(string Name) : IMarkup;

	/// <summary>A minimal codec: kind <c>"tag"</c>, one property <c>"n"</c>.</summary>
	private sealed class TagCodec : IMarkupCodec
	{
		public string Kind => "tag";
		public Type MarkupType => typeof(Tag);
		public void Write(Utf8JsonWriter writer, IMarkup markup) => writer.WriteString("n", ((Tag)markup).Name);
		public IMarkup Read(JsonElement element) => new Tag(element.GetProperty("n").GetString() ?? string.Empty);
	}

	private static readonly MarkupRegistry Registry = MarkupRegistry.Empty.With(new TagCodec());

	// ── Writing ──────────────────────────────────────────────────────────────────

	[Test]
	public async Task Serialize_PlainText_WritesTextOnly()
		=> await Assert.That(MarkupTextSerializer.Serialize(MarkupText.Plain("hi"), Registry)).IsEqualTo("""{"t":"hi"}""");

	[Test]
	public async Task Serialize_Empty_WritesEmptyObject()
		=> await Assert.That(MarkupTextSerializer.Serialize(MarkupText.Empty, Registry)).IsEqualTo("{}");

	[Test]
	public async Task Serialize_GapThenRun_UsesPaletteSlotZeroForTheGap()
	{
		var text = MarkupText.Concat(MarkupText.Plain("ab"), MarkupText.Wrap(new Tag("b"), "c"));
		await Assert.That(MarkupTextSerializer.Serialize(text, Registry))
			.IsEqualTo("""{"t":"abc","p":[null,[{"k":"tag","n":"b"}]],"r":[2,0,1,1]}""");
	}

	[Test]
	public async Task Serialize_NestedSet_WritesInnermostFirst()
	{
		var text = MarkupText.Wrap(new Tag("o"), MarkupText.Wrap(new Tag("i"), "ab"));
		await Assert.That(MarkupTextSerializer.Serialize(text, Registry))
			.IsEqualTo("""{"t":"ab","p":[null,[{"k":"tag","n":"i"},{"k":"tag","n":"o"}]],"r":[2,1]}""");
	}

	[Test]
	public async Task Serialize_EqualSets_ShareOnePaletteEntry()
	{
		var text = MarkupText.Concat([
			MarkupText.Wrap(new Tag("b"), "a"),
			MarkupText.Plain("-"),
			MarkupText.Wrap(new Tag("b"), "c")]);
		await Assert.That(MarkupTextSerializer.Serialize(text, Registry))
			.IsEqualTo("""{"t":"a-c","p":[null,[{"k":"tag","n":"b"}]],"r":[1,1,1,0,1,1]}""");
	}

	[Test]
	public async Task Serialize_NonAsciiText_StaysLiteralUtf8()
		=> await Assert.That(MarkupTextSerializer.Serialize(MarkupText.Wrap(new Tag("b"), "日本語"), Registry))
			.IsEqualTo("""{"t":"日本語","p":[null,[{"k":"tag","n":"b"}]],"r":[3,1]}""");

	[Test]
	public async Task Serialize_Neutral_WritesBuiltInKindWithoutACodec()
		=> await Assert.That(MarkupTextSerializer.Serialize(MarkupText.Wrap(NeutralMarkup.Instance, "x"), MarkupRegistry.Empty))
			.IsEqualTo("""{"t":"x","p":[null,[{"k":"neutral"}]],"r":[1,1]}""");

	/// <summary>
	/// Nothing configures <see cref="MarkupRegistry.Default"/> in this assembly, so a null registry
	/// throwing would show up here. Neutral needs no codec, so nothing should be resolved.
	/// </summary>
	[Test]
	public async Task Serialize_NeutralOnly_WithNoRegistry_DoesNotNeedTheDefault()
		=> await Assert.That(MarkupTextSerializer.Serialize(MarkupText.Wrap(NeutralMarkup.Instance, "x")))
			.IsEqualTo("""{"t":"x","p":[null,[{"k":"neutral"}]],"r":[1,1]}""");

	[Test]
	public async Task Deserialize_NeutralOnly_WithNoRegistry_DoesNotNeedTheDefault()
	{
		var text = MarkupTextSerializer.Deserialize("""{"t":"x","p":[null,[{"k":"neutral"}]],"r":[1,1]}""");
		await Assert.That(text.Runs[0].Markups[0]).IsSameReferenceAs(NeutralMarkup.Instance);
	}

	[Test]
	public async Task Serialize_MarkupWithNoCodec_Throws()
		=> await Assert.That(() => MarkupTextSerializer.Serialize(MarkupText.Wrap(new Uncodeced("x"), "ab"), Registry))
			.Throws<InvalidOperationException>();

	[Test]
	public async Task Serialize_ToBufferWriter_MatchesStringOverload()
	{
		var text = MarkupText.Concat(MarkupText.Plain("ab"), MarkupText.Wrap(new Tag("b"), "c"));
		var buffer = new ArrayBufferWriter<byte>();
		MarkupTextSerializer.Serialize(text, buffer, Registry);
		await Assert.That(Encoding.UTF8.GetString(buffer.WrittenSpan)).IsEqualTo(MarkupTextSerializer.Serialize(text, Registry));
	}

	// ── Reading ──────────────────────────────────────────────────────────────────

	[Test]
	public async Task Deserialize_EmptyString_IsEmpty()
		=> await Assert.That(MarkupTextSerializer.Deserialize("", Registry)).IsSameReferenceAs(MarkupText.Empty);

	[Test]
	public async Task Deserialize_EmptyObject_IsEmpty()
		=> await Assert.That(MarkupTextSerializer.Deserialize("{}", Registry)).IsSameReferenceAs(MarkupText.Empty);

	[Test]
	public async Task Deserialize_NoPaletteOrCover_IsPlain()
	{
		var text = MarkupTextSerializer.Deserialize("""{"t":"hi"}""", Registry);
		await Assert.That(text.Text).IsEqualTo("hi");
		await Assert.That(text.Runs.Length).IsEqualTo(0);
	}

	[Test]
	public async Task Deserialize_Utf8Span_MatchesStringOverload()
	{
		const string json = """{"t":"abc","p":[null,[{"k":"tag","n":"b"}]],"r":[2,0,1,1]}""";
		var fromBytes = MarkupTextSerializer.Deserialize(Encoding.UTF8.GetBytes(json), Registry);
		await Assert.That(fromBytes.Text).IsEqualTo("abc");
		await Assert.That(fromBytes.Runs).IsEquivalentTo(MarkupTextSerializer.Deserialize(json, Registry).Runs);
	}

	[Test]
	public async Task RoundTrip_ThroughCodec_PreservesTextAndRuns()
	{
		var original = MarkupText.Concat([
			MarkupText.Plain("plain "),
			MarkupText.Wrap(new Tag("o"), MarkupText.Wrap(new Tag("i"), "nested")),
			MarkupText.Plain(" tail"),
			MarkupText.Wrap(new Tag("o"), "end")]);
		var round = MarkupTextSerializer.Deserialize(MarkupTextSerializer.Serialize(original, Registry), Registry);
		await Assert.That(round.Text).IsEqualTo(original.Text);
		await Assert.That(round.Runs).IsEquivalentTo(original.Runs);
	}

	[Test]
	public async Task Deserialize_NeutralKind_IsTheSingleton()
	{
		var text = MarkupTextSerializer.Deserialize("""{"t":"x","p":[null,[{"k":"neutral"}]],"r":[1,1]}""", MarkupRegistry.Empty);
		await Assert.That(text.Runs[0].Markups[0]).IsSameReferenceAs(NeutralMarkup.Instance);
	}

	[Test]
	public async Task Deserialize_LegacyNeutralEntry_IsNeutral()
	{
		var text = MarkupTextSerializer.Deserialize("""{"t":"x","p":[null,[{"n":1}]],"r":[1,1]}""", MarkupRegistry.Empty);
		await Assert.That(text.Runs[0].Markups[0]).IsSameReferenceAs(NeutralMarkup.Instance);
	}

	// ── Legacy entries and unknown kinds ─────────────────────────────────────────

	[Test]
	public async Task Deserialize_LegacyHtmlEntry_WithNoCodec_IsUnknownHtml()
	{
		const string json = """{"t":"ab","p":[null,[{"h":"send","a":"href=\"x\""}]],"r":[2,1]}""";
		var text = MarkupTextSerializer.Deserialize(json, Registry);
		await Assert.That(text.Runs[0].Markups[0])
			.IsEqualTo((IMarkup)new UnknownMarkup("html", """{"h":"send","a":"href=\"x\""}"""));
	}

	[Test]
	public async Task Serialize_UnknownMarkup_WritesTheRawObjectVerbatim()
	{
		const string json = """{"t":"ab","p":[null,[{"h":"send","a":"href=\"x\""}]],"r":[2,1]}""";
		var text = MarkupTextSerializer.Deserialize(json, Registry);
		await Assert.That(MarkupTextSerializer.Serialize(text, Registry)).IsEqualTo(json);
	}

	[Test]
	public async Task Deserialize_EntryWithNoDiscriminator_IsAnsi()
	{
		var text = MarkupTextSerializer.Deserialize("""{"t":"ab","p":[null,[{"f":"#ff0000"}]],"r":[2,1]}""", Registry);
		await Assert.That(text.Runs[0].Markups[0])
			.IsEqualTo((IMarkup)new UnknownMarkup("ansi", """{"f":"#ff0000"}"""));
	}

	[Test]
	public async Task Deserialize_UnknownKind_KeepsKindAndRawJson()
	{
		var text = MarkupTextSerializer.Deserialize("""{"t":"ab","p":[null,[{"k":"zzz","q":7}]],"r":[2,1]}""", Registry);
		await Assert.That(text.Runs[0].Markups[0])
			.IsEqualTo((IMarkup)new UnknownMarkup("zzz", """{"k":"zzz","q":7}"""));
	}

	// ── Malformed covers ─────────────────────────────────────────────────────────

	[Test]
	public async Task Deserialize_TruncatedCoverPair_StopsAtTheLastCompletePair()
	{
		var text = MarkupTextSerializer.Deserialize("""{"t":"abcd","p":[null,[{"k":"tag","n":"b"}]],"r":[2,1,2]}""", Registry);
		await Assert.That(text.Runs.Length).IsEqualTo(1);
		await Assert.That(text.Runs[0]).IsEqualTo(new Run(0, 2, MarkupSet.Of(new Tag("b"))));
	}

	[Test]
	public async Task Deserialize_CoverPastTheText_IsClipped()
	{
		var text = MarkupTextSerializer.Deserialize("""{"t":"ab","p":[null,[{"k":"tag","n":"b"}]],"r":[9,1]}""", Registry);
		await Assert.That(text.Runs.Length).IsEqualTo(1);
		await Assert.That(text.Runs[0].Length).IsEqualTo(2);
	}

	[Test]
	public async Task Deserialize_PaletteIndexOutOfRange_IsTreatedAsAGap()
	{
		var text = MarkupTextSerializer.Deserialize("""{"t":"ab","p":[null,[{"k":"tag","n":"b"}]],"r":[2,7]}""", Registry);
		await Assert.That(text.Text).IsEqualTo("ab");
		await Assert.That(text.Runs.Length).IsEqualTo(0);
	}

	[Test]
	public async Task Deserialize_NonNumericCoverEntry_StopsReading()
	{
		var text = MarkupTextSerializer.Deserialize("""{"t":"ab","p":[null,[{"k":"tag","n":"b"}]],"r":["x",1]}""", Registry);
		await Assert.That(text.Text).IsEqualTo("ab");
		await Assert.That(text.Runs.Length).IsEqualTo(0);
	}

	[Test]
	public async Task Deserialize_NegativeCoverLength_StopsReading()
	{
		var text = MarkupTextSerializer.Deserialize("""{"t":"abcdef","p":[null,[{"k":"tag","n":"b"}]],"r":[3,1,-3,1,3,1]}""", Registry);
		await Assert.That(text.Runs.Length).IsEqualTo(1);
		await Assert.That(text.Runs[0]).IsEqualTo(new Run(0, 3, MarkupSet.Of(new Tag("b"))));
	}

	[Test]
	public async Task Deserialize_PaletteAndCoverNotArrays_IsPlain()
	{
		var text = MarkupTextSerializer.Deserialize("""{"t":"ab","p":5,"r":7}""", Registry);
		await Assert.That(text.Text).IsEqualTo("ab");
		await Assert.That(text.Runs.Length).IsEqualTo(0);
	}

	[Test]
	public async Task Deserialize_NonStringText_IsEmpty()
		=> await Assert.That(MarkupTextSerializer.Deserialize("""{"t":5}""", Registry)).IsSameReferenceAs(MarkupText.Empty);

	/// <summary>
	/// Two overflowing lengths accumulate past <see cref="int.MaxValue"/> and wrap position negative,
	/// which used to sort runs out of order and trip <c>MarkupText</c>'s overlap check. The cover
	/// stops as soon as it reaches the end of the text, so only the first (clipped) run survives.
	/// </summary>
	[Test]
	public async Task Deserialize_OverflowingCoverLengths_ClipsAndStopsInsteadOfThrowing()
	{
		var text = MarkupTextSerializer.Deserialize(
			"""{"t":"abcd","p":[null,[{"k":"neutral"}]],"r":[2147483647,1,2147483647,1,5,1]}""",
			Registry);
		await Assert.That(text.Text).IsEqualTo("abcd");
		await Assert.That(text.Runs.Length).IsEqualTo(1);
		await Assert.That(text.Runs[0]).IsEqualTo(new Run(0, 4, MarkupSet.Of(NeutralMarkup.Instance)));
	}

	[Test]
	public async Task Deserialize_Utf8Span_WithTrailingContent_Throws()
		=> await Assert.That(() => MarkupTextSerializer.Deserialize(Encoding.UTF8.GetBytes("""{"t":"ab"} xyz"""), Registry))
			.Throws<JsonException>();
}
