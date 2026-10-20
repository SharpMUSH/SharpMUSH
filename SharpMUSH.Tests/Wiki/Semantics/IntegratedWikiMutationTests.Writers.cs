using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Database.Lightning.Semantics;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Database.Lightning.Records;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Models.Wiki.Semantics;
using SharpMUSH.Library.Plugins.Storage.Lightning;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Wiki.Semantics;

public partial class IntegratedWikiMutationTests
{
	[Test]
	public async Task InterleavedUnconditionalBodyThenTitleKeepsLatestBody()
	{
		await using var host = NewHost();
		await host.OpenAsync();
		var wiki = host.Services.GetRequiredService<IWikiService>();
		var page = (await wiki.CreateAsync("Interleaved target", "original", "#1", sourceLocale: "en")).Expect<WikiPage>();
		await host.ActivateAtomicWikiProjectionAsync();
		var provider = host.Services.GetRequiredService<LightningDatabase>();
		var sequence = ObserveSequence(provider);
		var bodyReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var titleReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		provider.AtomicLegacyPrepared = mutation =>
		{
			if (mutation.Kind == WikiLegacyMutationKind.Body) bodyReady.TrySetResult();
			if (mutation.Kind == WikiLegacyMutationKind.Title) titleReady.TrySetResult();
		};
		await provider.Store.PauseAndDrainWriterAsync();
		Task<Found<WikiPage>>? body = null;
		Task<FoundResult<WikiPage>>? title = null;
		try
		{
			body = wiki.UpdateAsync(page.Id, "latest committed body **text**", "#1", "interleaved body");
			await bodyReady.Task.WaitAsync(TimeSpan.FromSeconds(30));
			title = wiki.SetTitleAsync(page.Id, "Latest title");
			await titleReady.Task.WaitAsync(TimeSpan.FromSeconds(30));
			await Assert.That(body.IsCompleted || title.IsCompleted).IsFalse();
		}
		finally
		{
			provider.Store.ResumeWriter();
			if (body is not null) await body;
			if (title is not null) await title;
			provider.AtomicLegacyPrepared = null;
		}
		var edited = (await body!).Expect<WikiPage>();
		var titled = (await title!).Expect<WikiPage>();
		await AssertPageMatches(titled, edited with { Title = "Latest title" });
		var projection = await AssertIntegratedMutation(provider, titled, 3, sequence + 2, 2, "latest committed body");
		await AssertLatestReceipt(provider, titled, WikiLegacyMutationKind.Title, 3, sequence + 2, [page.Id, "Latest title", string.Empty]);
		var history = await wiki.GetRevisionsAsync(page.Id);
		await Assert.That(history.Count).IsEqualTo(2);
		await AssertSourceRevision(history[0], page.Id, 2, edited.MarkdownSource, "interleaved body");
		await AssertIntegratedReopen(host, titled, projection, history, 2);
	}

	[Test]
	public async Task SameNameConcurrentCreatorsHaveOneWinnerWithoutLeakedIdentity()
	{
		await using var host = NewHost();
		await host.OpenAsync();
		await host.ActivateAtomicWikiProjectionAsync();
		var wiki = host.Services.GetRequiredService<IWikiService>();
		var provider = host.Services.GetRequiredService<LightningDatabase>();
		var sequence = ObserveSequence(provider);
		var counter = ObserveWikiCounter(provider);
		var pages = provider.Store.Read(tx => tx.Count(Tables.WikiPage));
		var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var prepared = 0;
		provider.AtomicLegacyPrepared = _ => { if (Interlocked.Increment(ref prepared) == 2) ready.TrySetResult(); };
		await provider.Store.PauseAndDrainWriterAsync();
		Task<Result<WikiPage>>? first = null, second = null;
		try
		{
			first = wiki.CreateAsync("Concurrent same name", "first body", "#1", sourceLocale: "en");
			second = wiki.CreateAsync("Concurrent same name", "second body", "#1", sourceLocale: "en");
			await ready.Task.WaitAsync(TimeSpan.FromSeconds(30));
		}
		finally
		{
			provider.Store.ResumeWriter();
			if (first is not null) await first;
			if (second is not null) await second;
			provider.AtomicLegacyPrepared = null;
		}
		var outcomes = new[] { await first!, await second! };
		await Assert.That(outcomes.Count(result => result is WikiPage)).IsEqualTo(1);
		await Assert.That(outcomes.Count(result => result is Error<string>)).IsEqualTo(1);
		var winner = outcomes.Single(result => result is WikiPage).Expect<WikiPage>();
		await Assert.That(ObserveWikiCounter(provider)).IsEqualTo(counter + 1);
		await Assert.That(provider.Store.Read(tx => tx.Count(Tables.WikiPage))).IsEqualTo(pages + 1);
		await Assert.That(provider.Store.Read(tx => tx.Count(AtomicWikiLayout.Summaries))).IsEqualTo(pages + 1);
		var projection = await AssertIntegratedMutation(provider, winner, 1, sequence + 1, 1, "first body", "second body");
		var history = await wiki.GetRevisionsAsync(winner.Id);
		await Assert.That(history.Count).IsEqualTo(1);
		await AssertIntegratedReopen(host, winner, projection, history, 1);
	}

	[Test]
	public async Task ActiveNameCategoriesUsesActualFacadeAndNextBootstrapSequence()
	{
		await using var host = NewHost();
		await host.OpenAsync();
		await host.ActivateAtomicWikiProjectionAsync();
		var provider = host.Services.GetRequiredService<LightningDatabase>();
		var bootstrap = ObserveActivation(provider, host.FixtureId);
		var wiki = host.Services.GetRequiredService<IWikiService>();
		var page = (await wiki.NameCategoriesAsync(["Active Places"], [], "#1", "en")).Single();
		var projection = await AssertIntegratedMutation(provider, page, 1, bootstrap.Sequence + 1, 1);
		await Assert.That(projection.State.Source is LocalWikiSource).IsTrue();
		await Assert.That(projection.State.State.Summary.Visibility.Namespace).IsEqualTo("category");
		await Assert.That((await wiki.NameCategoriesAsync(["Active Places"], [], "#1", "en")).Count).IsEqualTo(0);
		await Assert.That(ObserveSequence(provider)).IsEqualTo(bootstrap.Sequence + 1);
		await AssertIntegratedReopen(host, page, projection, await wiki.GetRevisionsAsync(page.Id), 1);
	}

	[Test]
	public async Task ActiveMissingAndDuplicateKeepLegacyOutcomesWithoutSemanticCommit()
	{
		await using var host = NewHost();
		await host.OpenAsync();
		await host.ActivateAtomicWikiProjectionAsync();
		var wiki = host.Services.GetRequiredService<IWikiService>();
		var provider = host.Services.GetRequiredService<LightningDatabase>();
		var home = (await wiki.GetBySlugAsync("home")).Expect<WikiPage>();
		var before = WikiFingerprint(provider, true);
		await Assert.That(await provider.CreatePageAsync(home) is Error<string>).IsTrue();
		await Assert.That(await provider.SetPageTitleAsync("wiki_page/999999", "Missing") is NotFound).IsTrue();
		await Assert.That(await provider.SetPageMetadataAsync("wiki_page/999999", [], true) is NotFound).IsTrue();
		await Assert.That(await provider.UpdatePageBodyAsync("wiki_page/999999", new("x", "x", "x"), "#1", null, DateTimeOffset.UtcNow) is NotFound).IsTrue();
		await Assert.That(WikiFingerprint(provider, true)).IsEqualTo(before);
	}

	[Test]
	public async Task ActiveQueuedScopeSurvivesCancelledWaiterUntilActualParentCompletes()
	{
		await using var host = NewHost();
		await host.OpenAsync();
		var wiki = host.Services.GetRequiredService<IWikiService>();
		var page = (await wiki.GetBySlugAsync("home")).Expect<WikiPage>();
		await host.ActivateAtomicWikiProjectionAsync();
		var provider = host.Services.GetRequiredService<LightningDatabase>();
		var prepared = new TaskCompletionSource<PreparedLegacyWikiMutation>(TaskCreationOptions.RunContinuationsAsynchronously);
		provider.AtomicLegacyPrepared = mutation => prepared.TrySetResult(mutation);
		await provider.Store.PauseAndDrainWriterAsync();
		Task<FoundResult<WikiPage>>? parent = null;
		LegacyWikiPreparationScope? scope = null;
		try
		{
			parent = wiki.SetTitleAsync(page.Id, "Retained through actual parent");
			scope = (await prepared.Task.WaitAsync(TimeSpan.FromSeconds(30))).Scope;
			using var cancellation = new CancellationTokenSource();
			var waiter = parent.WaitAsync(cancellation.Token);
			cancellation.Cancel();
			await Assert.That(() => waiter).Throws<OperationCanceledException>();
			await Assert.That(parent.IsCompleted).IsFalse();
			await Assert.That(scope.Budget.WorkingBytes).IsGreaterThan(1024L);
		}
		finally
		{
			provider.Store.ResumeWriter();
			if (parent is not null) await parent;
			provider.AtomicLegacyPrepared = null;
		}
		await Assert.That(scope!.Budget.WorkingBytes).IsEqualTo(0L);
		await Assert.That(ObserveMutationCount(provider)).IsEqualTo(1L);
	}

	[Test]
	[Arguments(0)]
	[Arguments(1)]
	[Arguments(2)]
	public async Task ActivePreparationRejectsGiantCallerFieldsBeforeSubmission(int field)
	{
		await using var host = NewHost();
		await host.OpenAsync();
		var wiki = host.Services.GetRequiredService<IWikiService>();
		var home = (await wiki.GetBySlugAsync("home")).Expect<WikiPage>();
		await host.ActivateAtomicWikiProjectionAsync();
		var provider = host.Services.GetRequiredService<LightningDatabase>();
		var before = WikiFingerprint(provider, true);
		var submitted = false;
		provider.AtomicLegacyPrepared = _ => submitted = true;
		try
		{
			await AssertFixtureFailure(async () =>
			{
				if (field == 0) await provider.SetPageMetadataAsync(home.Id, Enumerable.Repeat("category", 129).ToArray(), true);
				else if (field == 1) await provider.CreatePageAsync(home with { Slug = "giant-summary", Title = "Giant summary" }, new string('s', 3 * 1024 * 1024));
				else await provider.SetPageTitleAsync(home.Id, new string('"', 3 * 1024 * 1024));
			}, WikiFixtureMutationFailure.Quota);
			await Assert.That(submitted).IsFalse();
			await Assert.That(WikiFingerprint(provider, true)).IsEqualTo(before);
		}
		finally { provider.AtomicLegacyPrepared = null; }
	}

	private static long ObserveWikiCounter(LightningDatabase provider) => provider.Store.Read(tx =>
	{
		if (((IBoundedReadTx)tx).ReadValue(Tables.Meta, System.Text.Encoding.UTF8.GetBytes("next_wiki"),
			new NativeReadBudget(2, 1024, 4096, CancellationToken.None)) is not NativeReadValue value) return 0;
		using (value) return System.Buffers.Binary.BinaryPrimitives.ReadInt64BigEndian(value.Value);
	});

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task EscapedCallerJsonAndRenderedHtmlRespectSerializerReservation(bool controlCharacters)
	{
		await using var host = NewHost();
		await host.OpenAsync();
		var wiki = host.Services.GetRequiredService<IWikiService>();
		var home = (await wiki.GetBySlugAsync("home")).Expect<WikiPage>();
		await host.ActivateAtomicWikiProjectionAsync();
		var provider = host.Services.GetRequiredService<LightningDatabase>();
		var sequence = ObserveSequence(provider);
		var counter = ObserveWikiCounter(provider);
		var candidate = home with
		{
			Slug = "escaped-json-input",
			Title = "Escaped input",
			MarkdownSource = "small Markdown",
			RenderedHtml = new string(controlCharacters ? '\u0001' : '"', 40 * 1024),
			PlainText = "small plain text"
		};
		await AssertFixtureFailure(async () => { await provider.CreatePageAsync(candidate, "escaped input"); }, WikiFixtureMutationFailure.Quota);
		await Assert.That(ObserveSequence(provider)).IsEqualTo(sequence);
		await Assert.That(ObserveWikiCounter(provider)).IsEqualTo(counter);
		await Assert.That(ObserveMutationCount(provider)).IsEqualTo(0L);
		await Assert.That(await provider.GetPageBySlugAsync(candidate.Namespace, candidate.Slug) is NotFound).IsTrue();
		await Assert.That(provider.AtomicWikiPhase).IsEqualTo(SemanticFixturePhase.Active);
	}

	[Test]
	public async Task TinyTitleEditOfOversizedOwnedLegacyRowStopsAtCheckedRead()
	{
		await using var host = NewHost();
		await host.OpenAsync();
		var wiki = host.Services.GetRequiredService<IWikiService>();
		var page = (await wiki.CreateAsync("Large old row", "small original", "#1", sourceLocale: "en")).Expect<WikiPage>();
		await host.ActivateAtomicWikiProjectionAsync();
		var provider = host.Services.GetRequiredService<LightningDatabase>();
		var sequence = ObserveSequence(provider);
		var projection = ObserveProjection(provider, page.Id);
		var key = Keys.Dbref(long.Parse(page.Id.AsSpan(10), System.Globalization.CultureInfo.InvariantCulture));
		var original = provider.Store.Read(tx =>
		{
			using var value = ((IBoundedReadTx)tx).ReadValue(Tables.WikiPage, key,
				new NativeReadBudget(2, 128 * 1024, 4 * 1024 * 1024, CancellationToken.None)).Expect<NativeReadValue>();
			return Codec.Deserialize<WikiPageRecord>(value.Value);
		});
		// A valid large JSON row in this owned fixture exercises old-row admission. No projection is fabricated.
		var body = new string('x', 160 * 1024);
		await provider.Store.WriteAsync(tx => tx.Put(Tables.WikiPage, key, Codec.Serialize(original with { MarkdownSource = body })));
		await AssertFixtureFailure(async () => { await provider.SetPageTitleAsync(page.Id, "Tiny title"); }, WikiFixtureMutationFailure.WorkLimit);
		await Assert.That(ObserveSequence(provider)).IsEqualTo(sequence);
		await Assert.That(ObserveMutationCount(provider)).IsEqualTo(0L);
		await Assert.That(ObserveProjection(provider, page.Id).SummaryBytes).IsEquivalentTo(projection.SummaryBytes);
		var retained = (await wiki.GetByIdAsync(page.Id)).Expect<WikiPage>();
		await Assert.That(retained.Title).IsEqualTo(page.Title);
		await Assert.That(retained.MarkdownSource).IsEqualTo(body);
		await Assert.That(provider.AtomicWikiPhase).IsEqualTo(SemanticFixturePhase.Active);
	}

	[Test]
	public async Task ActiveMetadataFreezesCallerCategoryVectorBeforeQueue()
	{
		await using var host = NewHost();
		await host.OpenAsync();
		var wiki = host.Services.GetRequiredService<IWikiService>();
		var page = (await wiki.GetBySlugAsync("home")).Expect<WikiPage>();
		await host.ActivateAtomicWikiProjectionAsync();
		var provider = host.Services.GetRequiredService<LightningDatabase>();
		var sequence = ObserveSequence(provider);
		var ready = NewSignal();
		provider.AtomicLegacyPrepared = _ => ready.TrySetResult();
		var categories = new[] { "before" };
		await provider.Store.PauseAndDrainWriterAsync();
		Task<Found<WikiPage>>? parent = null;
		try
		{
			parent = provider.SetPageMetadataAsync(page.Id, categories, false);
			await ready.Task.WaitAsync(TimeSpan.FromSeconds(30));
			categories[0] = "after";
		}
		finally
		{
			provider.Store.ResumeWriter();
			if (parent is not null) await parent;
			provider.AtomicLegacyPrepared = null;
		}
		var updated = (await parent!).Expect<WikiPage>();
		await Assert.That(updated.Categories).IsEquivalentTo(new[] { "before" });
		await AssertIntegratedMutation(provider, updated, 2, sequence + 1, 1);
		await AssertLatestReceipt(provider, updated, WikiLegacyMutationKind.Metadata, 2, sequence + 1, [page.Id, "0", string.Empty, "1", "before"]);
	}
}
