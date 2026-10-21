using System.Text;
using System.Text.Json;
using SharpMUSH.Database.Lightning.Records;
using SharpMUSH.Database.Lightning.Semantics;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.Plugins.Storage.Lightning;

namespace SharpMUSH.Tests.Database.Lightning;

public class WikiDescriptorJsonTests
{
	private static byte[] Utf8(string json) => Encoding.UTF8.GetBytes(json);
	private static WikiDescriptorPage Read(string json, WikiDescriptorReadWork work) => WikiDescriptorJson.ReadPage(Utf8(json), work);
	private static async Task Limit(Action action, string dimension)
	{
		NativeReadLimitException? failure = null;
		try { action(); }
		catch (NativeReadLimitException error) { failure = error; }
		await Assert.That(failure?.Dimension).IsEqualTo(dimension);
	}

	[Test]
	public async Task ActualStoredRecordSkipsLargeBodiesAndOwnsOnlySourceProjection()
	{
		var bytes = Codec.Serialize(new WikiPageRecord
		{
			Namespace = "main", Slug = "guide", Title = "Literal title", AuthorDbref = "#123:456",
			Published = false, Categories = ["spells", "a\0b", "😀"],
			MarkdownSource = new string('m', 24000), RenderedHtml = new string('h', 24000), PlainText = new string('p', 24000)
		});
		using var work = new WikiDescriptorReadWork();
		var page = WikiDescriptorJson.ReadPage(bytes, work);
		var literal = page.Namespace == "main" && page.Slug == "guide" && page.Title == "Literal title"
			&& page.AuthorDbref == "#123:456" && !page.Published
			&& page.Categories.SequenceEqual(new[] { "spells", "a\0b", "😀" });
		var small = work.WorkingBytes < 4096 && work.MaterializedUtf16 < 128 && work.JsonBytes >= bytes.Length;
		Array.Fill(bytes, (byte)0); // Returned strings are owned, not references into the caller's raw storage.
		var stillOwned = page.Title == "Literal title";
		page.Dispose();
		await Assert.That(literal && small && stillOwned && work.WorkingBytes == 0 && work.OutstandingOwners == 0).IsTrue();
		await Assert.That(() => page.Title).Throws<ObjectDisposedException>();
	}

	[Test]
	public async Task EscapedSelectedNameAndNulLabelsPreserveExactIdentity()
	{
		using var work = new WikiDescriptorReadWork();
		using var page = Read("{\"Tit\\u006ce\":\"A\\uD83D\\uDE00\",\"Categories\":[\"a\\u0000b\",\"a\"],\"Future\":{\"x\":[1,true,null]}}", work);
		await Assert.That(page.Title == "A😀" && page.Categories.SequenceEqual(new[] { "a\0b", "a" })).IsTrue();
	}

	[Test]
	[Arguments("{}")]
	[Arguments("{\"Categories\":null,\"Published\":null}")]
	public async Task LiteralLegacyDefaultsMatchStoredRecordContract(string json)
	{
		using var work = new WikiDescriptorReadWork();
		using var page = Read(json, work);
		await Assert.That(page.Namespace == "main" && page.Slug == "" && page.Title == ""
			&& page.AuthorDbref == "" && page.Published && page.Categories.IsEmpty).IsTrue();
	}

	[Test]
	[Arguments("{\"Title\":\"first\",\"Tit\\u006ce\":\"second\"}")]
	[Arguments("{\"Namespace\":null}")]
	[Arguments("{\"Slug\":17}")]
	[Arguments("{\"Title\":false}")]
	[Arguments("{\"AuthorDbref\":null}")]
	[Arguments("{\"Published\":\"true\"}")]
	[Arguments("{\"Categories\":{}}")]
	[Arguments("{\"Categories\":[null]}")]
	[Arguments("{\"Categories\":[[\"nested\"]]}")]
	[Arguments("{\"Title\":\"allocated\",\"Unknown\":\"\\uD800\"}")]
	[Arguments("{\"Unknown\":{\"x\":\"\\uDC00\"}}")]
	[Arguments("{\"Unknown\":\"\\uD800\\u0041\"}")]
	[Arguments("{\"Unknown\":\"\\uZZZZ\"}")]
	[Arguments("[]")]
	[Arguments("{} {}")]
	[Arguments("{\"Title\":\"x\"")]
	public async Task MalformedCompleteSourceNeverEscapesPartialProjection(string json)
	{
		using var work = new WikiDescriptorReadWork();
		await Assert.That(() => Read(json, work)).Throws<JsonException>();
		await Assert.That(work.WorkingBytes == 0 && work.OutstandingOwners == 0).IsTrue();
	}

	[Test]
	[Arguments("EDA080", false)]
	[Arguments("EDA080", true)]
	[Arguments("C0AF", false)]
	[Arguments("F0", false)]
	[Arguments("FF", true)]
	public async Task InvalidUtf8InsideEvenSkippedStringsIsRejected(string hex, bool property)
	{
		var prefix = Utf8(property ? "{\"" : "{\"Future\":\"");
		var suffix = Utf8(property ? "\":1}" : "\"}");
		var bytes = prefix.Concat(Convert.FromHexString(hex)).Concat(suffix).ToArray();
		using var work = new WikiDescriptorReadWork();
		await Assert.That(() => WikiDescriptorJson.ReadPage(bytes, work)).Throws<JsonException>();
		await Assert.That(work.WorkingBytes).IsEqualTo(0);
	}

	[Test]
	[Arguments("Namespace", 64)]
	[Arguments("Slug", 256)]
	[Arguments("Title", 1024)]
	[Arguments("AuthorDbref", 64)]
	public async Task SelectedUtf16BoundaryIsCheckedBeforeMaterialization(string field, int limit)
	{
		using var work = new WikiDescriptorReadWork();
		using (Read("{\"" + field + "\":\"" + new string('x', limit) + "\"}", work)) { }
		var before = work.MaterializedUtf16;
		await Limit(() => Read("{\"" + field + "\":\"" + new string('x', limit + 1) + "\"}", work), "descriptor-string");
		await Assert.That(work.MaterializedUtf16 == before && work.WorkingBytes == 0).IsTrue();
	}

	[Test]
	public async Task SupplementaryCharacterCountsAsTwoUtf16Units()
	{
		using var work = new WikiDescriptorReadWork();
		var json = "{\"Namespace\":\"" + new string('x', 63) + "\\uD83D\\uDE00\"}";
		await Limit(() => Read(json, work), "descriptor-string");
		await Assert.That(work.MaterializedUtf16 == 0 && work.WorkingBytes == 0).IsTrue();
	}

	[Test]
	public async Task CategoryCardinalityCountsDuplicatesBeforeAnyDeduplication()
	{
		using var work = WikiDescriptorReadWork.ForTests(new ControlledClock());
		var accepted = "{\"Categories\":[" + string.Join(',', Enumerable.Repeat("\"same\"", 128)) + "]}";
		using (var page = Read(accepted, work))
			await Assert.That(page.Categories.Length).IsEqualTo(128);
		var rejected = "{\"Categories\":[" + string.Join(',', Enumerable.Repeat("\"same\"", 129)) + "]}";
		await Limit(() => Read(rejected, work), "descriptor-categories");
		await Assert.That(work.WorkingBytes).IsEqualTo(0);
	}

	[Test]
	public async Task UnknownMetadataDepthAndAllPropertyNamesRemainBounded()
	{
		using var work = WikiDescriptorReadWork.ForTests(new ControlledClock());
		using (Read("{\"future\":" + new string('[', 31) + "0" + new string(']', 31) + "}", work)) { }
		await Assert.That(() => Read("{\"future\":" + new string('[', 32) + "0" + new string(']', 32) + "}", work)).Throws<JsonException>();
		using (Read("{\"" + new string('p', 128) + "\":0}", work)) { }
		await Limit(() => Read("{\"" + new string('p', 129) + "\":0}", work), "descriptor-property-name");
		await Assert.That(work.WorkingBytes).IsEqualTo(0);
	}

	[Test]
	public async Task CumulativeTokenCharacterAndByteWorkCannotResetBetweenPages()
	{
		using var tokens = WikiDescriptorReadWork.ForTests(new ControlledClock(), tokens: 4);
		using (Read("{}", tokens)) { }
		using (Read("{}", tokens)) { }
		await Limit(() => Read("{}", tokens), "descriptor-json-tokens");
		using var characters = WikiDescriptorReadWork.ForTests(new ControlledClock(), characters: 2);
		using (Read("{\"Title\":\"ab\"}", characters)) { }
		await Limit(() => Read("{\"Title\":\"c\"}", characters), "descriptor-json-utf16");
		using var bytes = WikiDescriptorReadWork.ForTests(new ControlledClock(), bytes: 4);
		using (Read("{}", bytes)) { }
		using (Read("{}", bytes)) { }
		await Limit(() => Read("{}", bytes), "descriptor-json-bytes");
		await Assert.That(tokens.WorkingBytes + characters.WorkingBytes + bytes.WorkingBytes).IsEqualTo(0);
	}

	[Test]
	public async Task UnknownNestedTokensAndCategoryUtf16AreNotUnbudgetedSkipPaths()
	{
		using var tokens = WikiDescriptorReadWork.ForTests(new ControlledClock(), tokens: 6);
		await Limit(() => Read("{\"Future\":[0,0,0,0]}", tokens), "descriptor-json-tokens");
		using var work = WikiDescriptorReadWork.ForTests(new ControlledClock());
		using (Read("{\"Categories\":[\"" + new string('x', 256) + "\"]}", work)) { }
		await Limit(() => Read("{\"Categories\":[\"" + new string('x', 257) + "\"]}", work), "descriptor-string");
		await Assert.That(tokens.WorkingBytes + work.WorkingBytes).IsEqualTo(0);
	}

	[Test]
	public async Task WorkingFailureReleasesPartialStringsAndOwnerAndWorkDisposesOutstandingPages()
	{
		using var limited = WikiDescriptorReadWork.ForTests(new ControlledClock(), working: 1300);
		await Limit(() => Read("{\"Title\":\"abc\",\"Categories\":[\"x\"]}", limited), "working-bytes");
		await Assert.That(limited.MaterializedUtf16 == 3 && limited.WorkingBytes == 0 && limited.OutstandingOwners == 0).IsTrue();
		var work = new WikiDescriptorReadWork();
		var page = Read("{\"Title\":\"held\"}", work);
		work.Dispose();
		page.Dispose();
		await Assert.That(work.WorkingBytes == 0 && work.OutstandingOwners == 0).IsTrue();
		await Assert.That(() => page.Namespace).Throws<ObjectDisposedException>();
	}

	[Test]
	public async Task DirectRecordCeilingIsEnforcedBeforeScanOrOwnerAdmission()
	{
		using var work = WikiDescriptorReadWork.ForTests(new ControlledClock());
		var exact = Utf8("{}" + new string(' ', 128 * 1024 - 2));
		using (WikiDescriptorJson.ReadPage(exact, work)) { }
		var beforeBytes = work.JsonBytes;
		var beforeTokens = work.Tokens;
		var tooLarge = exact.Concat(new byte[] { (byte)' ' }).ToArray();
		await Limit(() => WikiDescriptorJson.ReadPage(tooLarge, work), "descriptor-record-bytes");
		await Assert.That(work.JsonBytes == beforeBytes && work.Tokens == beforeTokens
			&& work.OutstandingOwners == 0 && work.WorkingBytes == 0).IsTrue();
	}

	[Test]
	public async Task EscapedTitleBoundaryUsesBoundedByteScratchAndLiteralPropertyName()
	{
		using var work = WikiDescriptorReadWork.ForTests(new ControlledClock());
		var prefix = "{\"Tit\\u006ce\":\"";
		var escaped = string.Concat(Enumerable.Repeat("\\u0061", 1024));
		using (var page = Read(prefix + escaped + "\"}", work))
			await Assert.That(page.Title).IsEqualTo(new string('a', 1024));
		await Limit(() => Read(prefix + escaped + "\\u0061\"}", work), "descriptor-string");
		await Assert.That(work.WorkingBytes == 0 && work.OutstandingOwners == 0).IsTrue();
	}

	[Test]
	public async Task EscapedStringTemporaryOverlapCannotBypassWorkingAllowance()
	{
		// Owner+final1024-unit string fits4000; simultaneous3072-byte scratch+lease cannot.
		using var work = WikiDescriptorReadWork.ForTests(new ControlledClock(), working: 4000);
		var escaped = string.Concat(Enumerable.Repeat("\\u0061", 1024));
		await Limit(() => Read("{\"Tit\\u006ce\":\"" + escaped + "\"}", work), "working-bytes");
		await Assert.That(work.WorkingBytes == 0 && work.OutstandingOwners == 0).IsTrue();
	}

	[Test]
	public async Task CancellationDuringSkippedBodyPreservesOriginalTokenAndReleasesOwner()
	{
		using var cancel = new CancellationTokenSource();
		var clock = new ControlledClock { Cancellation = cancel, CancelAt = 100 };
		using var work = WikiDescriptorReadWork.ForTests(clock, cancel.Token);
		OperationCanceledException? caught = null;
		try { using var page = Read("{\"Title\":\"owned\",\"MarkdownSource\":\"" + new string('x', 60000) + "\"}", work); }
		catch (OperationCanceledException error) { caught = error; }
		await Assert.That(caught is not null && caught.CancellationToken == cancel.Token
			&& work.MaterializedUtf16 == 5 && work.WorkingBytes == 0 && work.OutstandingOwners == 0).IsTrue();
	}

	[Test]
	public async Task WholeRequestDeadlineIsMonotonicAndNotResetPerPage()
	{
		var clock = new ControlledClock();
		using var work = WikiDescriptorReadWork.ForTests(clock);
		using (Read("{}", work)) { }
		clock.Advance(TimeSpan.FromMilliseconds(999));
		using (Read("{}", work)) { }
		clock.Advance(TimeSpan.FromMilliseconds(1));
		await Limit(() => Read("{}", work), "descriptor-deadline");
		await Assert.That(work.WorkingBytes).IsEqualTo(0);
	}

	private sealed class ControlledClock : TimeProvider
	{
		private long _ticks;
		private int _reads;
		internal CancellationTokenSource? Cancellation;
		internal int CancelAt = int.MaxValue;
		public override long TimestampFrequency => TimeSpan.TicksPerSecond;
		public override long GetTimestamp() { if (++_reads == CancelAt) Cancellation?.Cancel(); return _ticks; }
		internal void Advance(TimeSpan elapsed) => _ticks += elapsed.Ticks;
	}
}
