using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Library;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Plugins.Storage;
using SharpMUSH.Library.Plugins.Storage.Lightning;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Wiki.Semantics;

public partial class IntegratedWikiMutationTests
{
	[ClassDataSource<NatsTestServer>(Shared = SharedType.PerTestSession)]
	public required NatsTestServer NatsTestServer { get; init; }
	[ClassDataSource<MySqlTestServer>(Shared = SharedType.PerTestSession)]
	public required MySqlTestServer MySqlTestServer { get; init; }

	// The runner supplies the approved disk-backed TMPDIR/TMP/TEMP before starting this process.
	private static string ScratchRoot => Path.Join(Path.GetTempPath(), "semantic-wiki-first-atomic", "integrated-wiki-mutations");
	private static string ConfigFile => Path.Join(AppContext.BaseDirectory, "Configuration", "Testfile", "mushcnf.dst");

	private SemanticWikiPrototypeHost NewHost(Action<IServiceCollection>? configure = null)
	{
		Environment.SetEnvironmentVariable("NATS_URL", $"nats://localhost:{NatsTestServer.Instance.GetMappedPublicPort(4222)}");
		return new SemanticWikiPrototypeHost(ScratchRoot, MySqlTestServer.Instance.GetConnectionString(), ConfigFile, null, "mysql", configure);
	}

	[Test]
	public async Task ExplicitActivationCreatesFixtureProjectionLayout()
	{
		await Assert.That(Environment.GetEnvironmentVariable("SHARPMUSH_BOOTSTRAP_BUNDLED_PACKAGES")).IsEqualTo("false");
		await using var host = NewHost();
		await host.OpenAsync();
		var wiki = host.Services.GetRequiredService<IWikiService>();
		var home = (await wiki.GetBySlugAsync("home")).Expect<WikiPage>();
		var guide = (await wiki.GetBySlugAsync("markdown_guide", WikiNamespace.Help)).Expect<WikiPage>();
		await Assert.That(home.SourceLocale).IsEqualTo("en");
		await Assert.That(guide.SourceLocale).IsEqualTo("en");
		await host.CloseAsync();
		await AssertNoSemanticTables(host.WorldPath);

		await host.OpenAsync();
		await host.ActivateAtomicWikiProjectionAsync();
		await host.CloseAsync();
		await AssertFixtureProjectionLayout(host.WorldPath);

		await host.OpenAsync();
		var reopenedWiki = host.Services.GetRequiredService<IWikiService>();
		await AssertPageMatches((await reopenedWiki.GetByIdAsync(home.Id)).Expect<WikiPage>(), home);
		await AssertPageMatches((await reopenedWiki.GetByIdAsync(guide.Id)).Expect<WikiPage>(), guide);
		await host.CloseAsync();
		await AssertFixtureProjectionLayout(host.WorldPath);
	}

	[Test]
	public async Task AdmissionOnlyActualFacadeRoundTripKeepsSemanticsDisabled()
	{
		await Assert.That(Environment.GetEnvironmentVariable("SHARPMUSH_BOOTSTRAP_BUNDLED_PACKAGES")).IsEqualTo("false");
		await using var host = NewHost();
		await host.OpenAsync();
		await AssertActualForwarders(host.Services);
		var firstProvider = host.Services.GetRequiredService<LightningDatabase>();
		var wiki = host.Services.GetRequiredService<IWikiService>();
		await Assert.That(wiki is WikiStoreService).IsTrue();
		await Assert.That((await wiki.GetBySlugAsync("home")).Expect<WikiPage>().SourceLocale).IsEqualTo("en");
		await Assert.That((await wiki.GetBySlugAsync("markdown_guide", WikiNamespace.Help)).Expect<WikiPage>().SourceLocale).IsEqualTo("en");

		const string initialBody = "# Admission body one\n\nbody-marker-one with **bold** and <em>inline HTML</em>.\n";
		const string editedBody = "# Admission body two\n\nbody-marker-two with *emphasis* and <strong>changed HTML</strong>.\n";
		var created = (await wiki.CreateAsync("Atomic fixture page", initialBody, "#1",
			sourceLocale: "en", categories: [" Alpha ", "ALPHA"], editSummary: "create-marker")).Expect<WikiPage>();
		await Assert.That(created.Id.Length).IsGreaterThan(0);
		await Assert.That(created.Slug).IsEqualTo("atomic_fixture_page");
		await Assert.That(created.Title).IsEqualTo("Atomic fixture page");
		await Assert.That(created.Namespace).IsEqualTo("main");
		await Assert.That(created.SourceLocale).IsEqualTo("en");
		await Assert.That(created.AuthorDbref).IsEqualTo("#1");
		await Assert.That(created.LastEditorDbref).IsEqualTo("#1");
		await Assert.That(created.MarkdownSource).IsEqualTo(initialBody);
		await Assert.That(created.RevisionNumber).IsEqualTo(1);
		await Assert.That(created.Categories).IsEquivalentTo(new[] { "alpha" });
		await Assert.That(created.Published).IsTrue();
		await AssertPageMatches((await wiki.GetByIdAsync(created.Id)).Expect<WikiPage>(), created);
		var initialHistory = await wiki.GetRevisionsAsync(created.Id);
		await Assert.That(initialHistory.Count).IsEqualTo(1);
		await AssertSourceRevision(initialHistory[0], created.Id, 1, initialBody, "create-marker");

		var titled = (await wiki.SetTitleAsync(created.Id, "  Renamed fixture page  ")).Expect<WikiPage>();
		await AssertPageMatches(titled, created with { Title = "Renamed fixture page" });
		await Assert.That(await wiki.GetRevisionsAsync(created.Id)).IsEquivalentTo(initialHistory);

		var edited = (await wiki.UpdateAsync(created.Id, editedBody, "#1", "body-edit-marker")).Expect<WikiPage>();
		await AssertPageMatches(edited, titled with
		{
			MarkdownSource = editedBody,
			RenderedHtml = edited.RenderedHtml,
			PlainText = edited.PlainText,
			UpdatedAt = edited.UpdatedAt,
			RevisionNumber = 2
		});
		await Assert.That(edited.RenderedHtml.Contains("body-marker-two", StringComparison.Ordinal)).IsTrue();
		await Assert.That(edited.PlainText.Contains("body-marker-two", StringComparison.Ordinal)).IsTrue();
		await Assert.That(edited.RenderedHtml.Contains("body-marker-one", StringComparison.Ordinal)).IsFalse();
		await Assert.That(edited.PlainText.Contains("body-marker-one", StringComparison.Ordinal)).IsFalse();
		var editedHistory = await wiki.GetRevisionsAsync(created.Id);
		await Assert.That(editedHistory.Count).IsEqualTo(2);
		await AssertSourceRevision(editedHistory[0], created.Id, 2, editedBody, "body-edit-marker");
		await Assert.That(editedHistory[1]).IsEqualTo(initialHistory[0]);

		var metadata = (await wiki.SetMetadataAsync(created.Id, [" BETA ", "beta"], false)).Expect<WikiPage>();
		await AssertPageMatches(metadata, edited with { Categories = ["beta"], Published = false });
		await Assert.That(await wiki.GetRevisionsAsync(created.Id)).IsEquivalentTo(editedHistory);
		await AssertPersistedPage(wiki, metadata);

		await host.CloseAsync();
		await Assert.That(() => firstProvider.Store.Read(_ => true)).Throws<ObjectDisposedException>();
		await Assert.That(() => { _ = host.Services; }).Throws<InvalidOperationException>();
		await AssertNoSemanticTables(host.WorldPath);

		await host.OpenAsync();
		await AssertActualForwarders(host.Services);
		await Assert.That(ReferenceEquals(firstProvider, host.Services.GetRequiredService<LightningDatabase>())).IsFalse();
		var reopenedWiki = host.Services.GetRequiredService<IWikiService>();
		await Assert.That(reopenedWiki is WikiStoreService).IsTrue();
		await AssertPersistedPage(reopenedWiki, metadata);
		await Assert.That(await reopenedWiki.GetRevisionsAsync(created.Id)).IsEquivalentTo(editedHistory);
		await host.CloseAsync();
		await AssertNoSemanticTables(host.WorldPath);
	}

	private static async Task AssertActualForwarders(IServiceProvider services)
	{
		var provider = services.GetRequiredService<LightningDatabase>();
		foreach (var type in new[] { typeof(ISharpDatabase), typeof(IWikiStore), typeof(IDatabaseLifecycle),
			typeof(IPackageRegistryService), typeof(ILightningStorageAccessor) })
			await Assert.That(ReferenceEquals(provider, services.GetRequiredService(type))).IsTrue();
	}

	private static async Task AssertPersistedPage(IWikiService wiki, WikiPage expected)
	{
		await AssertPageMatches((await wiki.GetByIdAsync(expected.Id)).Expect<WikiPage>(), expected);
		await AssertPageMatches((await wiki.GetBySlugAsync(expected.Slug)).Expect<WikiPage>(), expected);
		await Assert.That((await wiki.GetByCategoryAsync("alpha")).Any(page => page.Id == expected.Id)).IsFalse();
		await Assert.That((await wiki.GetByCategoryAsync("beta")).Any(page => page.Id == expected.Id)).IsTrue();
	}

	private static async Task AssertPageMatches(WikiPage actual, WikiPage expected)
	{
		await Assert.That(actual.Id).IsEqualTo(expected.Id);
		await Assert.That(actual.Slug).IsEqualTo(expected.Slug);
		await Assert.That(actual.Title).IsEqualTo(expected.Title);
		await Assert.That(actual.Namespace).IsEqualTo(expected.Namespace);
		await Assert.That(actual.SourceLocale).IsEqualTo(expected.SourceLocale);
		await Assert.That(actual.MarkdownSource).IsEqualTo(expected.MarkdownSource);
		await Assert.That(actual.RenderedHtml).IsEqualTo(expected.RenderedHtml);
		await Assert.That(actual.PlainText).IsEqualTo(expected.PlainText);
		await Assert.That(actual.AuthorDbref).IsEqualTo(expected.AuthorDbref);
		await Assert.That(actual.LastEditorDbref).IsEqualTo(expected.LastEditorDbref);
		await Assert.That(actual.CreatedAt).IsEqualTo(expected.CreatedAt);
		await Assert.That(actual.UpdatedAt).IsEqualTo(expected.UpdatedAt);
		await Assert.That(actual.RevisionNumber).IsEqualTo(expected.RevisionNumber);
		await Assert.That(actual.Categories).IsEquivalentTo(expected.Categories);
		await Assert.That(actual.Published).IsEqualTo(expected.Published);
	}

	private static async Task AssertSourceRevision(WikiRevision revision, string pageId, int number, string markdown, string summary)
	{
		await Assert.That(revision.PageId).IsEqualTo(pageId);
		await Assert.That(revision.Locale).IsEqualTo(string.Empty);
		await Assert.That(revision.RevisionNumber).IsEqualTo(number);
		await Assert.That(revision.MarkdownSource).IsEqualTo(markdown);
		await Assert.That(revision.EditorDbref).IsEqualTo("#1");
		await Assert.That(revision.EditSummary).IsEqualTo(summary);
	}

	private static async Task AssertNoSemanticTables(string closedWorldPath)
		=> await Assert.That(IntegratedWikiBoundaryTests.OnDiskNames(closedWorldPath)
			.Any(name => name.Contains("semantic", StringComparison.OrdinalIgnoreCase))).IsFalse();

	private static async Task AssertFixtureProjectionLayout(string closedWorldPath)
		=> await Assert.That(IntegratedWikiBoundaryTests.OnDiskNames(closedWorldPath)
			.Where(name => name.StartsWith("prototype.semantic.b2.", StringComparison.Ordinal)))
			.IsEquivalentTo(new[]
			{
				"prototype.semantic.b2.v1.metadata",
				"prototype.semantic.b2.v1.registry",
				"prototype.semantic.b2.v1.summaries",
				"prototype.semantic.b2.v1.facts",
				"prototype.semantic.b2.v1.forward",
				"prototype.semantic.b2.v1.postings",
				"prototype.semantic.b2.v1.catalog",
				"prototype.semantic.b2.v1.partitions",
				"prototype.semantic.b2.v1.mutations"
			});
}
