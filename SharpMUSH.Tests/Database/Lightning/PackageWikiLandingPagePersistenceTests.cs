using System.Text;
using System.Text.Json;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Plugins.Storage.Lightning;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Tests.Database.Lightning;

/// <summary>Additive landing metadata persists without rewriting genuine pre-field native records.</summary>
public class PackageWikiLandingPagePersistenceTests : LightningDatabaseFixture
{
	private static readonly JsonSerializerOptions SnapshotJson = new(JsonSerializerDefaults.Web);
	private static readonly DateTimeOffset Anchor = new(2026, 6, 12, 12, 0, 0, TimeSpan.Zero);

	private sealed record BindingValue(string? Namespace, string? Slug, string? PageId);

	private static BindingValue? Binding<T>(T value)
	{
		var json = JsonSerializer.SerializeToElement(value);
		if (!json.TryGetProperty("WikiLandingPage", out var binding) || binding.ValueKind == JsonValueKind.Null) return null;
		static string? Text(JsonElement element, string name)
			=> element.TryGetProperty(name, out var text) && text.ValueKind == JsonValueKind.String ? text.GetString() : null;
		return new(Text(binding, "Namespace"), Text(binding, "Slug"), Text(binding, "PageId"));
	}

	private static T WithBinding<T>(T record, BindingValue? binding) where T : class
	{
		var json = JsonSerializer.SerializeToNode(record) ?? throw new InvalidOperationException("The fixture record could not serialize.");
		json["WikiLandingPage"] = JsonSerializer.SerializeToNode(binding);
		return json.Deserialize<T>() ?? throw new InvalidOperationException("The fixture record could not deserialize.");
	}

	private string RawInstalled(string id) => Db.Store.Read(tx => tx.TryGet(Tables.Pkg, Keys.Str(id), out var bytes)
		? Encoding.UTF8.GetString(bytes) : throw new InvalidOperationException("The native installed record is missing."));

	[Test]
	public async Task LiteralPreFieldInstalledAndSnapshotJsonRemainAbsentAndUnmodifiedAfterNativeReopen()
	{
		const string id = "legacy-landing-json";
		// These bytes predate the additive property; no current record serializer manufactures the legacy row.
		const string installedJson = """
			{"PackageId":"legacy-landing-json","Version":"1.0.0","SourceRepo":"https://example.invalid/legacy","SourcePath":"legacy/","InstalledCommit":"legacy-commit","PinnedBranch":"main","InstalledAt":"2026-06-12T12:00:00.0000000+00:00","CurrentRevision":1}
			""";
		const string snapshotJson = """
			{"version":"1.0.0","objects":[],"attributes":[]}
			""";
		await Db.Store.WriteAsync(tx => tx.Put(Tables.Pkg, Keys.Str(id), Encoding.UTF8.GetBytes(installedJson)));
		await Db.AddPackageRevisionAsync(new PackageRevisionRecord(id, 1, PackageRevisionKind.Install, "1.0.0", "legacy-commit",
			snapshotJson, "{}", "[]", Anchor));
		var beforeRevisions = Dump(Tables.PkgRev);

		await ReopenAsync();

		var installed = (await Db.GetInstalledPackageAsync(id)).Expect<InstalledPackageRecord>();
		await Assert.That(installed.Version).IsEqualTo("1.0.0");
		await Assert.That(installed.CurrentRevision).IsEqualTo(1);
		await Assert.That(Binding(installed)).IsNull();
		await Assert.That(Binding((await Db.GetInstalledPackagesAsync()).Single(package => package.Id == id))).IsNull();
		await Assert.That(RawInstalled(id)).IsEqualTo(installedJson);
		await Assert.That(Dump(Tables.PkgRev)).IsEquivalentTo(beforeRevisions);
		using var native = JsonDocument.Parse(RawInstalled(id));
		await Assert.That(native.RootElement.TryGetProperty("WikiLandingPage", out _)).IsFalse();
		var revision = (await Db.GetPackageRevisionAsync(id, 1)).Expect<PackageRevisionRecord>();
		await Assert.That(revision.ManifestSnapshotJson).IsEqualTo(snapshotJson);
		var snapshot = JsonSerializer.Deserialize<PackageRevisionSnapshot>(revision.ManifestSnapshotJson, SnapshotJson)
			?? throw new InvalidOperationException("The legacy snapshot did not deserialize.");
		await Assert.That(Binding(snapshot)).IsNull();
	}

	[Test]
	public async Task NativeInstalledMappingAndSnapshotRetainExactBindingAndPersistItsRemoval()
	{
		const string id = "bound-landing-json";
		const string slug = "bound_landing_json";
		var wiki = new WikiStoreService(Db, new WikiMarkdigPipeline());
		var page = (await wiki.CreateAsync("Bound Entrance", "Persisted body.\n", "#1", slug: slug)).Expect<WikiPage>();
		var expected = new BindingValue("main", slug, page.Id);
		var installed = WithBinding(new InstalledPackageRecord(id, "1.0.0", "https://example.invalid/persisted", id + "/",
			"commit-1", "main", Anchor, 1), expected);
		var snapshot = WithBinding(new PackageRevisionSnapshot("1.0.0", [], []), expected);
		await Db.UpsertPackageWikiPageAsync(new PackageWikiPageRecord(id, page.Id, "main", slug, page.Title, page.MarkdownSource,
			page.Categories, page.Published, PackageWikiRequires.None, false, "1.0.0"));
		await Db.UpsertInstalledPackageAsync(installed);
		await Db.AddPackageRevisionAsync(new PackageRevisionRecord(id, 1, PackageRevisionKind.Install, "1.0.0", "commit-1",
			JsonSerializer.Serialize(snapshot, SnapshotJson), "{}", "[]", Anchor));

		await ReopenAsync();

		var owner = (await Db.GetPackageWikiPageAsync(id, "main", slug)).Expect<PackageWikiPageRecord>();
		var live = (await Db.GetPageByIdAsync(owner.PageId)).Expect<WikiPage>();
		await Assert.That(live.Id).IsEqualTo(page.Id);
		await Assert.That(live.Namespace).IsEqualTo(expected.Namespace);
		await Assert.That(live.Slug).IsEqualTo(expected.Slug);
		var fetched = (await Db.GetInstalledPackageAsync(id)).Expect<InstalledPackageRecord>();
		await Assert.That(Binding(fetched)).IsEqualTo(expected);
		await Assert.That(Binding((await Db.GetInstalledPackagesAsync()).Single(package => package.Id == id))).IsEqualTo(expected);
		var revision = (await Db.GetPackageRevisionAsync(id, 1)).Expect<PackageRevisionRecord>();
		var storedSnapshot = JsonSerializer.Deserialize<PackageRevisionSnapshot>(revision.ManifestSnapshotJson, SnapshotJson)
			?? throw new InvalidOperationException("The bound snapshot did not deserialize.");
		await Assert.That(Binding(storedSnapshot)).IsEqualTo(expected);
		await Db.UpsertInstalledPackageAsync(WithBinding(fetched with { Version = "1.1.0", CurrentRevision = 2 }, null));
		await Db.AddPackageRevisionAsync(new PackageRevisionRecord(id, 2, PackageRevisionKind.Upgrade, "1.1.0", "commit-2",
			JsonSerializer.Serialize(WithBinding(new PackageRevisionSnapshot("1.1.0", [], []), null), SnapshotJson), "{}", "[]", Anchor));
		await ReopenAsync();

		await Assert.That(Binding((await Db.GetInstalledPackageAsync(id)).Expect<InstalledPackageRecord>())).IsNull();
		var clearedRevision = (await Db.GetPackageRevisionAsync(id, 2)).Expect<PackageRevisionRecord>();
		var clearedSnapshot = JsonSerializer.Deserialize<PackageRevisionSnapshot>(clearedRevision.ManifestSnapshotJson, SnapshotJson)
			?? throw new InvalidOperationException("The cleared snapshot did not deserialize.");
		await Assert.That(Binding(clearedSnapshot)).IsNull();
		await Assert.That((await Db.GetPackageWikiPageAsync(id, "main", slug)).Expect<PackageWikiPageRecord>().PageId).IsEqualTo(page.Id);
		await Assert.That((await Db.GetPageByIdAsync(page.Id)).Expect<WikiPage>().MarkdownSource).IsEqualTo(page.MarkdownSource);
		var historical = (await Db.GetPackageRevisionAsync(id, 1)).Expect<PackageRevisionRecord>();
		var historicalSnapshot = JsonSerializer.Deserialize<PackageRevisionSnapshot>(historical.ManifestSnapshotJson, SnapshotJson)
			?? throw new InvalidOperationException("The historical snapshot did not deserialize.");
		await Assert.That(Binding(historicalSnapshot)).IsEqualTo(expected);
	}
}
