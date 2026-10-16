using SharpMUSH.Library.Plugins.Storage.Lightning;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Tests.Database.Lightning;

public class StoredMarkupSafetyTests : LightningDatabaseFixture
{
	private sealed class ThrowingCodec : IMarkupCodec
	{
		public string Kind => "throwing-test-layer";
		public Type MarkupType => typeof(ProbeMarkup);
		public void Write(System.Text.Json.Utf8JsonWriter writer, IMarkup markup) => throw new InvalidOperationException("broken writer");
		public IMarkup Read(System.Text.Json.JsonElement element) => throw new InvalidOperationException("broken reader");
	}

	private sealed record ProbeMarkup : IMarkup;
	private sealed class CountingCodec : IMarkupCodec
	{
		public int Writes { get; private set; }
		public string Kind => "inspection-test-layer";
		public Type MarkupType => typeof(ProbeMarkup);
		public void Write(System.Text.Json.Utf8JsonWriter writer, IMarkup markup)
		{
			Writes++;
			writer.WriteNumber("data", 42);
		}
		public IMarkup Read(System.Text.Json.JsonElement element) => new ProbeMarkup();
	}

	[Test]
	public async Task DisplayReadDefersCodecRewriteInspectionUntilSafetyIsRequested()
	{
		var raw = MarkupTextSerializer.Serialize(MarkupText.Wrap(
			new UnknownMarkup("inspection-test-layer", "{\"k\":\"inspection-test-layer\",\"data\":42}"), MarkupText.Plain("display")));
		var codec = new CountingCodec();
		var stored = StoredMarkup.Read(raw, MarkupRegistry.Empty.With(codec));
		await Assert.That(stored.Value.ToPlainText()).IsEqualTo("display");
		await Assert.That(codec.Writes).IsEqualTo(0);
		await Assert.That(stored.CanRewrite).IsTrue();
		await Assert.That(codec.Writes).IsEqualTo(1);
	}

	[Test]
	public async Task ThrowingCodecRetainsOriginalRawLayerThroughStoredCopy()
	{
		var layer = new UnknownMarkup("throwing-test-layer", "{\"k\":\"throwing-test-layer\",\"custom\":42}");
		var raw = MarkupTextSerializer.Serialize(MarkupText.Wrap(layer, MarkupText.Plain("survives")));
		var registry = MarkupRegistry.Empty.With(new ThrowingCodec());
		var stored = StoredMarkup.Inspect(raw, registry);
		await Assert.That(stored.Status).IsEqualTo(MarkupReadStatus.Damaged);
		await Assert.That(stored.CanRewrite).IsFalse();
		await Assert.That(stored.Diagnostics.Count).IsGreaterThan(0);
		var god = (await Db.GetObjectNodeAsync(new DBRef(1))).Expect<SharpPlayer>();
		await Db.SetStoredAttributeAsync(new DBRef(0), ["THROWING_COPY"], stored, god, new HashSet<string>());
		var copy = await Db.GetAttributeAsync(new DBRef(0), ["THROWING_COPY"]).LastAsync();
		await Assert.That(copy.StoredValue!.Serialized).IsEqualTo(raw);
	}

	[Test]
	public async Task TruncatedRunIsDamagedAndCannotBeRewritten()
	{
		var value = MarkupText.Wrap(new UnknownMarkup("future-layer", "{\"k\":\"future-layer\"}"), MarkupText.Plain("long run"));
		var document = System.Text.Json.Nodes.JsonNode.Parse(MarkupTextSerializer.Serialize(value))!;
		document["t"] = "x";
		var stored = StoredMarkup.Inspect(document.ToJsonString());
		await Assert.That(stored.Status).IsEqualTo(MarkupReadStatus.Damaged);
		await Assert.That(stored.CanRewrite).IsFalse();
	}

	[Test]
	public async Task UnknownLayerRemainsOpaqueAndRewriteable()
	{
		var value = MarkupText.Wrap(new UnknownMarkup("future-layer", "{\"k\":\"future-layer\",\"data\":42}"), MarkupText.Plain("opaque"));
		var raw = MarkupTextSerializer.Serialize(value);
		var stored = StoredMarkup.Inspect(raw);
		await Assert.That(stored.Status).IsEqualTo(MarkupReadStatus.Opaque);
		await Assert.That(stored.CanRewrite).IsTrue();
		await Assert.That(MarkupTextSerializer.Serialize(stored.Value)).IsEqualTo(raw);
	}

	[Test]
	public async Task FutureWireBytesSurviveAttributeCopyAndRestart()
	{
		const string raw = "{ \"v\":999,\"t\":\"future display\",\"future\":{\"secret\":42} }";
		var god = (await Db.GetObjectNodeAsync(new DBRef(1))).Expect<SharpPlayer>();
		await Db.SetAttributeAsync(new DBRef(0), ["SAFETY_SOURCE"], MarkupText.Plain("initial"), god);
		await Db.Store.WriteAsync(tx => tx.Put(Tables.AttrVal, Keys.Attr(0, "SAFETY_SOURCE"), Keys.Str(raw)));
		var source = await Db.GetAttributeAsync(new DBRef(0), ["SAFETY_SOURCE"]).LastAsync();
		await Assert.That(source.StoredValue!.CanRewrite).IsFalse();
		await Assert.That(source.Value.ToPlainText()).IsEqualTo("future display");
		await Db.SetStoredAttributeAsync(new DBRef(0), ["SAFETY_COPY"], source.StoredValue, god, new HashSet<string>());
		await ReopenAsync();
		var copy = await Db.GetAttributeAsync(new DBRef(0), ["SAFETY_COPY"]).LastAsync();
		await Assert.That(copy.StoredValue!.Serialized).IsEqualTo(raw);
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task MalformedAttributeDoesNotAbortItsNeighbours(bool metadata)
	{
		var god = (await Db.GetObjectNodeAsync(new DBRef(1))).Expect<SharpPlayer>();
		foreach (var name in new[] { "SAFETY_A", "SAFETY_BAD", "SAFETY_Z" })
			await Db.SetAttributeAsync(new DBRef(0), [name], MarkupText.Plain(name), god);
		await Db.Store.WriteAsync(tx => tx.Put(metadata ? Tables.AttrMeta : Tables.AttrVal, Keys.Attr(0, "SAFETY_BAD"), Keys.Str("not JSON")));
		await ReopenAsync();
		await Assert.That(await Db.GetAttributeAsync(new DBRef(0), ["SAFETY_BAD"]).AnyAsync()).IsFalse();
		var attributes = await Db.GetAttributesAsync(new DBRef(0), "SAFETY_*").ToListAsync();
		await Assert.That(string.Join(' ', attributes.Select(a => a.Name))).IsEqualTo("SAFETY_A SAFETY_Z");
	}
}
