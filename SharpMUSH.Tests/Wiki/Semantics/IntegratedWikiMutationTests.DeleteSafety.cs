using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Database.Lightning.Records;
using SharpMUSH.Database.Lightning.Semantics;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Models.Wiki.Semantics;
using SharpMUSH.Library.Plugins.Storage.Lightning;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Wiki.Semantics;

public partial class IntegratedWikiMutationTests
{
	[Test]
	public async Task DeleteReceiptReplaysAfterRecreationAndReopen()
	{
		await using var host = NewHost();
		await host.OpenAsync();
		await host.ActivateAtomicWikiProjectionAsync();
		var wiki = host.Services.GetRequiredService<IWikiService>();
		var provider = host.Services.GetRequiredService<LightningDatabase>();
		var page = (await wiki.CreateAsync("Delete replay target", "original", "#1", sourceLocale: "en")).Expect<WikiPage>();
		var create = await CaptureTask3Request(provider, async () => { await wiki.SetTitleAsync(page.Id, "Delete replay title"); });
		var oldReceipt = ReadTask3Receipt(provider, create.MutationId);
		var request = await CaptureTask3Request(provider, async () => { await wiki.DeleteAsync(page.Id, "#1"); });
		var receipt = ReadTask3Receipt(provider, request.MutationId);
		var replacement = (await wiki.CreateAsync(page.Title, "replacement", "#1", sourceLocale: "en", slug: page.Slug)).Expect<WikiPage>();
		var committed = Task3Snapshot(provider);
		for (var reopen = 0; reopen < 2; reopen++)
		{
			if (reopen == 1) { await host.CloseAsync(); await host.OpenAsync(); }
			provider = host.Services.GetRequiredService<LightningDatabase>();
			var replay = (await RestoreTask3Request(host, request, request.MutationId)).Expect<IntegratedWikiReplayed>();
			await Assert.That(replay.Receipt).IsEqualTo(receipt);
			var oldReplay = (await RestoreTask3Request(host, create, create.MutationId)).Expect<IntegratedWikiReplayed>();
			await Assert.That(oldReplay.Receipt).IsEqualTo(oldReceipt);
			using (var admission = Task3Owner(host).EnterMutation(SemanticFixtureMutation.LegacyWiki))
			using (var scope = new LegacyWikiPreparationScope(admission, CancellationToken.None))
			{
				var alias = PreparedLegacyWikiMutation.Delete.Prepare(scope, page.Id[10..]);
				await Assert.That(alias.Id).IsEqualTo(page.Id);
				await Assert.That(alias.RequestDigest).IsEqualTo(request.RequestDigest);
				var restored = PreparedLegacyWikiMutation.RestoreForReplay(scope, alias, request.MutationId);
				var aliasReplay = (await provider.CommitLegacyFixtureAsync(restored, CancellationToken.None)).Expect<IntegratedWikiReplayed>();
				await Assert.That(aliasReplay.Receipt).IsEqualTo(receipt);
			}
			await Assert.That(Task3Snapshot(provider)).IsEquivalentTo(committed);
			await AssertPageMatches((await provider.GetPageBySlugAsync(page.Namespace, page.Slug)).Expect<WikiPage>(), replacement);
			await AssertDeleteFixtureBoundary(provider, host.FixtureId);
		}
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task DeleteGuidReusedForAnotherTargetOrOperationIsMismatch(bool anotherOperation)
	{
		await using var host = NewHost();
		await host.OpenAsync();
		await host.ActivateAtomicWikiProjectionAsync();
		var wiki = host.Services.GetRequiredService<IWikiService>();
		var provider = host.Services.GetRequiredService<LightningDatabase>();
		var page = (await wiki.CreateAsync("Delete mismatch target", "body", "#1", sourceLocale: "en")).Expect<WikiPage>();
		var other = (await wiki.CreateAsync("Delete mismatch other", "body", "#1", sourceLocale: "en")).Expect<WikiPage>();
		var original = await CaptureTask3Request(provider, async () => { await wiki.DeleteAsync(page.Id, "#1"); });
		using var admission = Task3Owner(host).EnterMutation(SemanticFixtureMutation.LegacyWiki);
		using var scope = new LegacyWikiPreparationScope(admission, CancellationToken.None);
		PreparedLegacyWikiMutation changed = anotherOperation
			? PreparedLegacyWikiMutation.Title.Prepare(scope, page.Id, "Different operation")
			: PreparedLegacyWikiMutation.Delete.Prepare(scope, other.Id);
		var restored = PreparedLegacyWikiMutation.RestoreForReplay(scope, changed, original.MutationId);
		var before = Task3Snapshot(provider);
		await AssertFixtureFailure(async () => { await provider.CommitLegacyFixtureAsync(restored, CancellationToken.None); }, WikiFixtureMutationFailure.MutationMismatch);
		await Assert.That(Task3Snapshot(provider)).IsEquivalentTo(before);
	}

	[Test]
	public async Task DeleteReceiptWithValidWrongPageIdIsUnavailable()
	{
		await using var host = NewHost();
		await host.OpenAsync();
		await host.ActivateAtomicWikiProjectionAsync();
		var wiki = host.Services.GetRequiredService<IWikiService>();
		var provider = host.Services.GetRequiredService<LightningDatabase>();
		var page = (await wiki.CreateAsync("Delete wrong receipt target", "body", "#1", sourceLocale: "en")).Expect<WikiPage>();
		var request = await CaptureTask3Request(provider, async () => { await wiki.DeleteAsync(page.Id, "#1"); });
		var receipt = ReadTask3Receipt(provider, request.MutationId);
		var damaged = IntegratedWikiReceiptCodec.Encode(receipt with { PageId = "wiki_page/999999" });
		await Assert.That(IntegratedWikiReceiptCodec.Decode(damaged).PageId).IsEqualTo("wiki_page/999999");
		await provider.Store.WriteAsync(tx => tx.Put(AtomicWikiLayout.Mutations, request.MutationId.ToByteArray(), damaged));
		var before = Task3Snapshot(provider);
		await AssertFixtureFailure(async () => { await RestoreTask3Request(host, request, request.MutationId); }, WikiFixtureMutationFailure.Unavailable);
		await Assert.That(Task3Snapshot(provider)).IsEquivalentTo(before);
		await host.CloseAsync();
		await host.OpenAsync();
		await Assert.That(Task3Snapshot(host.Services.GetRequiredService<LightningDatabase>())).IsEquivalentTo(before);
	}

	[Test]
	public async Task DeleteFreshRepeatAndUnknownIdsAreNotFoundWithoutAllocation()
	{
		await using var host = NewHost();
		await host.OpenAsync();
		await host.ActivateAtomicWikiProjectionAsync();
		var wiki = host.Services.GetRequiredService<IWikiService>();
		var provider = host.Services.GetRequiredService<LightningDatabase>();
		var page = (await wiki.CreateAsync("Delete repeat target", "body", "#1", sourceLocale: "en")).Expect<WikiPage>();
		await wiki.DeleteAsync(page.Id, "#1");
		var before = Task3Snapshot(provider);
		foreach (var id in new[] { page.Id, page.Id[10..], "wiki_page/999999", "999999", "bad/id", "-1" })
		{
			await Assert.That(await provider.DeletePageAsync(id) is NotFound).IsTrue();
			await Assert.That(Task3Snapshot(provider)).IsEquivalentTo(before);
		}
		await host.CloseAsync();
		await host.OpenAsync();
		provider = host.Services.GetRequiredService<LightningDatabase>();
		await Assert.That(await provider.DeletePageAsync(page.Id) is NotFound).IsTrue();
		await Assert.That(Task3Snapshot(provider)).IsEquivalentTo(before);
	}

	[Test]
	[Arguments(0)]
	[Arguments(1)]
	[Arguments(2)]
	[Arguments(3)]
	[Arguments(4)]
	[Arguments(5)]
	[Arguments(6)]
	[Arguments(7)]
	[Arguments(8)]
	[Arguments(9)]
	[Arguments(10)]
	[Arguments(11)]
	[Arguments(12)]
	[Arguments(13)]
	[Arguments(14)]
	[Arguments(15)]
	[Arguments(16)]
	[Arguments(17)]
	[Arguments(18)]
	[Arguments(19)]
	[Arguments(20)]
	public async Task DeleteRejectsContradictoryManagedStateBeforeApply(int damage)
	{
		await using var host = NewHost();
		await host.OpenAsync();
		await host.ActivateAtomicWikiProjectionAsync();
		var wiki = host.Services.GetRequiredService<IWikiService>();
		var provider = host.Services.GetRequiredService<LightningDatabase>();
		var page = (await wiki.CreateAsync("Delete invariant target", "body", "#1", sourceLocale: "en")).Expect<WikiPage>();
		if (damage is >= 15 and <= 18) await wiki.DeleteAsync(page.Id, "#1");
		var observation = ObserveProjection(provider, page.Id);
		var state = observation.State;
		var subject = WikiSemanticKeys.Subject(state.State.Summary.NativeId);
		var pageKey = Keys.Dbref(long.Parse(page.Id.AsSpan(10), CultureInfo.InvariantCulture));
		// Controlled application-row corruption in a disposable owned host. Native rows/handles remain valid.
		await provider.Store.WriteAsync(tx =>
		{
			if (damage == 0) tx.Put(AtomicWikiLayout.Facts, subject, WikiSemanticCodec.EncodeFacts(new(2, [])));
			else if (damage == 1) tx.Put(AtomicWikiLayout.Facts, subject, WikiSemanticCodec.EncodeFacts(new(1,
				[new(new(page.Id), "fixture:unexpected", new Keyword("value"), new("fixture", "commit", "path", "field", false))])));
			else if (damage == 2) tx.Put(AtomicWikiLayout.Summaries, subject, WikiSemanticStateCodec.ProjectionState(state with { State = state.State with { ClassId = 1 } }));
			else if (damage == 3) tx.Put(AtomicWikiLayout.Summaries, subject, WikiSemanticStateCodec.ProjectionState(state with { Source = new PortableWikiSource(new("fixture", "main", "other")) }));
			else if (damage == 4) tx.Put(AtomicWikiLayout.Summaries, subject, WikiSemanticStateCodec.ProjectionState(state with { State = state.State with { Summary = state.State.Summary with { NativeId = state.State.Summary.NativeId + 1 } } }));
			else if (damage == 5) tx.Delete(AtomicWikiLayout.Facts, subject);
			else if (damage == 6) tx.Delete(AtomicWikiLayout.Summaries, subject);
			else if (damage == 7) tx.Delete(AtomicWikiLayout.Registry, WikiSemanticKeys.RegistryLookup(WikiSemanticRegistryKind.Page, page.Id));
			else if (damage == 8) tx.Delete(Tables.WikiPage, pageKey); // contradictory live summary
			else if (damage == 9) tx.Put(AtomicWikiLayout.Summaries, subject, WikiSemanticStateCodec.ProjectionState(state with { State = state.State with { Summary = state.State.Summary with { Deleted = true } } }));
			else if (damage == 10)
			{
				tx.Delete(Tables.WikiPage, pageKey);
				tx.Delete(AtomicWikiLayout.Registry, WikiSemanticKeys.RegistryLookup(WikiSemanticRegistryKind.Page, page.Id));
			}
			else if (damage == 11) tx.Put(Tables.WikiSlug, Keys.Lower(page.Namespace + ":" + page.Slug), Keys.Dbref(999999));
			else if (damage == 12) tx.Put(AtomicWikiLayout.Summaries, subject, WikiSemanticStateCodec.ProjectionState(state with { State = state.State with { Summary = state.State.Summary with { Reference = new("wiki_page/999999") } } }));
			else if (damage == 13) tx.Put(AtomicWikiLayout.Summaries, subject, WikiSemanticStateCodec.ProjectionState(state with { State = state.State with { Summary = state.State.Summary with { Collection = "other" } } }));
			else if (damage == 14) tx.Delete(AtomicWikiLayout.Registry, WikiSemanticKeys.RegistryReverse(WikiSemanticRegistryKind.Page, state.State.Summary.NativeId));
			else if (damage == 15) tx.Delete(AtomicWikiLayout.Facts, subject);
			else if (damage == 16) tx.Put(AtomicWikiLayout.Facts, subject, WikiSemanticCodec.EncodeFacts(new(2, [])));
			else if (damage == 17) tx.Put(AtomicWikiLayout.Summaries, subject, WikiSemanticStateCodec.ProjectionState(state with { State = state.State with { ClassId = 1 } }));
			else if (damage == 18) tx.Put(AtomicWikiLayout.Summaries, subject, WikiSemanticStateCodec.ProjectionState(state with { Source = new PortableWikiSource(new("fixture", "main", "other")) }));
			else if (damage == 19)
			{
				tx.Delete(Tables.WikiPage, pageKey);
				tx.Delete(AtomicWikiLayout.Registry, WikiSemanticKeys.RegistryLookup(WikiSemanticRegistryKind.Page, page.Id));
				tx.Delete(AtomicWikiLayout.Registry, WikiSemanticKeys.RegistryReverse(WikiSemanticRegistryKind.Page, state.State.Summary.NativeId));
			}
			else tx.Put(AtomicWikiLayout.Metadata, [1], WikiSemanticCodec.SchemaIdentity(new(2, [])));
		});
		var before = Task3Snapshot(provider);
		var applied = 0;
		provider.AtomicLegacyPutApplied = _ => applied++;
		provider.AtomicLegacyDeleteApplied = _ => applied++;
		try
		{
			await AssertFixtureFailure(async () => { await provider.DeletePageAsync(page.Id); }, WikiFixtureMutationFailure.Unavailable);
			await Assert.That(applied).IsEqualTo(0);
			await Assert.That(Task3Snapshot(provider)).IsEquivalentTo(before);
		}
		finally { provider.AtomicLegacyPutApplied = null; provider.AtomicLegacyDeleteApplied = null; }
		await host.CloseAsync();
		if (damage == 20)
		{
			// The deliberately changed schema also invalidates the durable activation certificate.
			// Corroborate the expected startup refusal without repairing the row or bypassing admission.
			var worldFile = Path.Join(host.WorldPath, "data.mdb");
			var persisted = System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(worldFile));
			var rejected = false;
			try { await host.OpenAsync(); }
			catch (InvalidOperationException error) when (error.Message == "Semantic activation certificate is missing or incompatible.")
			{ rejected = true; }
			await Assert.That(rejected).IsTrue();
			// The complete managed-table snapshot was unchanged above; the closed native image must
			// also remain byte-identical across the refused reopen (same oracle as partial-layout tests).
			await Assert.That(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(worldFile))).IsEquivalentTo(persisted);
			return;
		}
		await host.OpenAsync();
		await Assert.That(Task3Snapshot(host.Services.GetRequiredService<LightningDatabase>())).IsEquivalentTo(before);
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task DeleteFaultOrCancellationAfterRealLegacyDeleteAbortsActualParent(bool cancel)
	{
		await using var host = NewHost();
		await host.OpenAsync();
		await host.ActivateAtomicWikiProjectionAsync();
		var wiki = host.Services.GetRequiredService<IWikiService>();
		var provider = host.Services.GetRequiredService<LightningDatabase>();
		var page = (await wiki.CreateAsync("Delete abort target", "body", "#1", sourceLocale: "en")).Expect<WikiPage>();
		var before = Task3Snapshot(provider);
		using var cancellation = new CancellationTokenSource();
		using var admission = Task3Owner(host).EnterMutation(SemanticFixtureMutation.LegacyWiki);
		using var scope = new LegacyWikiPreparationScope(admission, cancellation.Token);
		var intent = PreparedLegacyWikiMutation.Delete.Prepare(scope, page.Id);
		var tables = new List<string>();
		provider.AtomicLegacyDeleteApplied = table =>
		{
			tables.Add(table.Name);
			if (cancel) cancellation.Cancel();
			else throw new Task3ManagedAbort();
		};
		try
		{
			if (cancel) await Assert.That(async () => { await provider.CommitLegacyFixtureAsync(intent, cancellation.Token); }).Throws<OperationCanceledException>();
			else await Assert.That(async () => { await provider.CommitLegacyFixtureAsync(intent, CancellationToken.None); }).Throws<Task3ManagedAbort>();
			// This is the original existing revision row, deleted successfully in the actual parent.
			await Assert.That(tables).IsEquivalentTo(new[] { Tables.WikiRev.Name });
			await Assert.That(Task3Snapshot(provider)).IsEquivalentTo(before);
		}
		finally { provider.AtomicLegacyDeleteApplied = null; }
		scope.Dispose();
		admission.Dispose();
		await Assert.That(scope.Budget.WorkingBytes).IsEqualTo(0L);
		await host.CloseAsync();
		await host.OpenAsync();
		provider = host.Services.GetRequiredService<LightningDatabase>();
		await Assert.That(Task3Snapshot(provider)).IsEquivalentTo(before);
		await Assert.That(await provider.DeletePageAsync(page.Id) is None).IsTrue();
	}

	[Test]
	public async Task DeleteAbsentOptionalRequirementDoesNotFireSuccessfulDeleteHook()
	{
		await using var host = NewHost();
		await host.OpenAsync();
		await host.ActivateAtomicWikiProjectionAsync();
		var wiki = host.Services.GetRequiredService<IWikiService>();
		var provider = host.Services.GetRequiredService<LightningDatabase>();
		var page = (await wiki.CreateAsync("Delete absent rule target", "body", "#1", sourceLocale: "en")).Expect<WikiPage>();
		var ruleKey = Keys.Composite("page", page.Id);
		await Assert.That(provider.Store.Read(tx => tx.TryGet(Tables.WikiRequirement, ruleKey, out _))).IsFalse();
		var tables = new List<string>();
		provider.AtomicLegacyDeleteApplied = table => tables.Add(table.Name);
		try { await Assert.That(await provider.DeletePageAsync(page.Id) is None).IsTrue(); }
		finally { provider.AtomicLegacyDeleteApplied = null; }
		await Assert.That(tables.Contains(Tables.WikiRequirement.Name)).IsFalse();
		await Assert.That(tables.Count(table => table == Tables.WikiRev.Name)).IsEqualTo(1);
		await Assert.That(tables.Count(table => table == Tables.WikiPage.Name)).IsEqualTo(1);
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task DeleteQueuedWriterOrderUsesLatestStateWithoutResurrection(bool deleteFirst)
	{
		await using var host = NewHost();
		await host.OpenAsync();
		await host.ActivateAtomicWikiProjectionAsync();
		var wiki = host.Services.GetRequiredService<IWikiService>();
		var provider = host.Services.GetRequiredService<LightningDatabase>();
		var page = (await wiki.CreateAsync("Delete ordered target", "old body", "#1", sourceLocale: "en")).Expect<WikiPage>();
		var original = ObserveProjection(provider, page.Id);
		var sequence = ObserveSequence(provider);
		var receipts = ObserveMutationCount(provider);
		var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var count = 0;
		provider.AtomicLegacyPrepared = _ => { if (Interlocked.Increment(ref count) == 3) ready.TrySetResult(); };
		provider.Store.PauseWriter();
		Task<Found<None>>? deletion = null;
		Task<Found<WikiPage>>? body = null, title = null;
		try
		{
			if (deleteFirst) deletion = provider.DeletePageAsync(page.Id);
			body = provider.UpdatePageBodyAsync(page.Id, new("latest body", "latest html", "latest plain"), "#1", "latest revision", DateTimeOffset.UtcNow);
			title = provider.SetPageTitleAsync(page.Id, "Latest ordered title");
			if (!deleteFirst) deletion = provider.DeletePageAsync(page.Id);
			await ready.Task.WaitAsync(TimeSpan.FromSeconds(30));
		}
		finally
		{
			provider.Store.ResumeWriter();
			if (body is not null) await body;
			if (title is not null) await title;
			if (deletion is not null) await deletion;
			provider.AtomicLegacyPrepared = null;
		}
		await Assert.That(await deletion! is None).IsTrue();
		await Assert.That(await body! is NotFound).IsEqualTo(deleteFirst);
		await Assert.That(await title! is NotFound).IsEqualTo(deleteFirst);
		var tombstone = ObserveProjection(provider, page.Id);
		await Assert.That(tombstone.State.State.Summary.Title).IsEqualTo(deleteFirst ? page.Title : "Latest ordered title");
		await Assert.That(tombstone.State.State.Summary.StateVersion).IsEqualTo(original.State.State.Summary.StateVersion + (deleteFirst ? 1 : 3));
		await Assert.That(ObserveSequence(provider)).IsEqualTo(sequence + (deleteFirst ? 1 : 3));
		await Assert.That(ObserveMutationCount(provider)).IsEqualTo(receipts + (deleteFirst ? 1 : 3));
		await Assert.That(await wiki.GetRevisionsAsync(page.Id)).IsEmpty();
		await AssertDeletedConsumerState(wiki, page);
		var before = Task3Snapshot(provider);
		await host.CloseAsync();
		await host.OpenAsync();
		await Assert.That(Task3Snapshot(host.Services.GetRequiredService<LightningDatabase>())).IsEquivalentTo(before);
	}

	[Test]
	public async Task DeleteConcurrentTargetsHaveOneCommitAndOneNotFound()
	{
		await using var host = NewHost();
		await host.OpenAsync();
		await host.ActivateAtomicWikiProjectionAsync();
		var provider = host.Services.GetRequiredService<LightningDatabase>();
		var page = (await provider.GetPageBySlugAsync("main", "home")).Expect<WikiPage>();
		var sequence = ObserveSequence(provider);
		var receipts = ObserveMutationCount(provider);
		var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var count = 0;
		provider.AtomicLegacyPrepared = _ => { if (Interlocked.Increment(ref count) == 2) ready.TrySetResult(); };
		provider.Store.PauseWriter();
		Task<Found<None>>? first = null, second = null;
		try
		{
			first = provider.DeletePageAsync(page.Id);
			second = provider.DeletePageAsync(page.Id);
			await ready.Task.WaitAsync(TimeSpan.FromSeconds(30));
		}
		finally
		{
			provider.Store.ResumeWriter();
			if (first is not null) await first;
			if (second is not null) await second;
			provider.AtomicLegacyPrepared = null;
		}
		var results = new[] { await first!, await second! };
		await Assert.That(results.Count(result => result is None)).IsEqualTo(1);
		await Assert.That(results.Count(result => result is NotFound)).IsEqualTo(1);
		await Assert.That(ObserveSequence(provider)).IsEqualTo(sequence + 1);
		await Assert.That(ObserveMutationCount(provider)).IsEqualTo(receipts + 1);
	}

	[Test]
	[Arguments(0)]
	[Arguments(1)]
	[Arguments(2)]
	[Arguments(3)]
	[Arguments(4)]
	public async Task DeleteResourceLimitsRejectBeforeAnyApplyAndPreserveCompleteHistory(int limit)
	{
		await using var host = NewHost();
		await host.OpenAsync();
		var wiki = host.Services.GetRequiredService<IWikiService>();
		var provider = host.Services.GetRequiredService<LightningDatabase>();
		var page = (await wiki.CreateAsync("Delete resource target", "small original", "#1", sourceLocale: "en")).Expect<WikiPage>();
		var neighbor = (await wiki.CreateAsync("Delete resource neighbor", "neighbor body", "#1", sourceLocale: "en")).Expect<WikiPage>();
		if (limit == 0)
		{
			// Every row is small; a complete stream exceeds the native batch and shared record allowance.
			for (var revision = 0; revision < 129; revision++)
				await provider.UpdatePageBodyAsync(page.Id, new("small history " + revision, "small", "small"), "#1", null, DateTimeOffset.UtcNow);
		}
		else if (limit == 1)
		{
			// Three individually legal values exceed the native range's local byte batch. The real
			// range returns Exhausted=false before copying the third body; the owning delete must refuse.
			for (var revision = 0; revision < 3; revision++)
				await provider.UpdatePageBodyAsync(page.Id, new(new string('h', 50_000), string.Empty, string.Empty), "#1", null, DateTimeOffset.UtcNow);
			await provider.UpdatePageBodyAsync(page.Id, new("small final", "small", "small"), "#1", null, DateTimeOffset.UtcNow);
		}
		else if (limit == 4)
		{
			// A local range of two 60 KiB histories fits, but its original section also owns a 10 KiB
			// current page and identity/state reads. The shared cumulative byte guard rejects first.
			for (var revision = 0; revision < 2; revision++)
				await provider.UpdatePageBodyAsync(page.Id, new(new string('h', 60_000), string.Empty, string.Empty), "#1", null, DateTimeOffset.UtcNow);
			await provider.UpdatePageBodyAsync(page.Id, new(new string('f', 10_000), string.Empty, string.Empty), "#1", null, DateTimeOffset.UtcNow);
		}
		await host.ActivateAtomicWikiProjectionAsync();
		var key = Keys.Dbref(long.Parse(page.Id.AsSpan(10), CultureInfo.InvariantCulture));
		var rowBytes = 0;
		if (limit is 2 or 3)
		{
			// Valid large JSON application row in an owned host, preserving the descriptor and identity.
			var old = provider.Store.Read(tx =>
			{
				if (!tx.TryGet(Tables.WikiPage, key, out var value)) throw new InvalidOperationException("Missing resource target.");
				return Codec.Deserialize<WikiPageRecord>(value);
			});
			var encoded = Codec.Serialize(old with { MarkdownSource = new string('b', limit == 2 ? 140_000 : 128_000) });
			rowBytes = encoded.Length;
			if (limit == 2) await Assert.That(rowBytes + key.Length > NativeReadBudget.MaxRecordBytes).IsTrue();
			else
			{
				await Assert.That(rowBytes + key.Length < NativeReadBudget.MaxRecordBytes).IsTrue();
				// The old-row decoder's 32x reservation plus its still-owned native copy already exceed 4 MiB.
				await Assert.That(33L * rowBytes > 4 * 1024 * 1024).IsTrue();
			}
			await provider.Store.WriteAsync(tx => tx.Put(Tables.WikiPage, key, encoded));
		}
		// This observation deliberately uses the independent test reader: it can snapshot an oversized
		// setup row that the production checked reader must refuse. It never changes the delete budget.
		var before = DeleteSafetySnapshot(provider);
		PreparedLegacyWikiMutation? request = null;
		var applied = 0;
		provider.AtomicLegacyPrepared = value => request = value;
		provider.AtomicLegacyPutApplied = _ => applied++;
		provider.AtomicLegacyDeleteApplied = _ => applied++;
		try
		{
			await AssertFixtureFailure(async () => { await provider.DeletePageAsync(page.Id); }, WikiFixtureMutationFailure.WorkLimit);
			await Assert.That(applied).IsEqualTo(0);
			if (request is null) throw new InvalidOperationException("Delete did not reach real preparation.");
			await Assert.That(request.Scope.Budget.Records <= 128).IsTrue();
			await Assert.That(request.Scope.Budget.Bytes <= 128 * 1024).IsTrue();
			await Assert.That(request.Scope.Budget.WorkingBytes).IsEqualTo(0L);
			if (limit == 0) await Assert.That(request.Scope.Budget.Records).IsEqualTo(128);
			if (limit is 1 or 4) await Assert.That(request.Scope.Budget.Records < 128).IsTrue();
			Console.WriteLine($"Delete limit={limit}, oldRowBytes={rowBytes}, copiedRecords={request.Scope.Budget.Records}, inspectedBytes={request.Scope.Budget.Bytes}, releasedWorking={request.Scope.Budget.WorkingBytes}");
			await Assert.That(DeleteSafetySnapshot(provider)).IsEquivalentTo(before);
			await AssertPageMatches((await provider.GetPageBySlugAsync(neighbor.Namespace, neighbor.Slug)).Expect<WikiPage>(), neighbor);
		}
		finally { provider.AtomicLegacyPrepared = null; provider.AtomicLegacyPutApplied = null; provider.AtomicLegacyDeleteApplied = null; }
		await host.CloseAsync();
		await host.OpenAsync();
		provider = host.Services.GetRequiredService<LightningDatabase>();
		await Assert.That(DeleteSafetySnapshot(provider)).IsEquivalentTo(before);
		await AssertDeleteFixtureBoundary(provider, host.FixtureId);
	}

	[Test]
	public async Task DeleteBoundedLongHistoryRemovesOnlyItsExactPagePrefix()
	{
		await using var host = NewHost();
		await host.OpenAsync();
		var wiki = host.Services.GetRequiredService<IWikiService>();
		var provider = host.Services.GetRequiredService<LightningDatabase>();
		var page = (await wiki.CreateAsync("Delete long accepted history", "initial", "#1", sourceLocale: "en")).Expect<WikiPage>();
		for (var revision = 0; revision < 80; revision++)
			page = (await provider.UpdatePageBodyAsync(page.Id, new("revision " + revision, "small", "small"), "#1", null, DateTimeOffset.UtcNow)).Expect<WikiPage>();
		var neighbor = (await wiki.CreateAsync("Delete long history neighbor", "neighbor original", "#1", sourceLocale: "en")).Expect<WikiPage>();
		neighbor = (await wiki.UpdateAsync(neighbor.Id, "neighbor last", "#1")).Expect<WikiPage>();
		await host.ActivateAtomicWikiProjectionAsync();
		var before = Task3Snapshot(provider);
		var old = ObserveProjection(provider, page.Id);
		var sequence = ObserveSequence(provider);
		var receipts = ObserveMutationCount(provider);
		var allocator = ObserveWikiCounter(provider);
		await Assert.That(await provider.DeletePageAsync(page.Id) is None).IsTrue();
		await AssertDeleteDelta(provider, page, old, before, sequence, receipts, allocator);
		await Assert.That((await wiki.GetRevisionsAsync(neighbor.Id)).Count).IsEqualTo(2);
		var committed = Task3Snapshot(provider);
		await host.CloseAsync();
		await host.OpenAsync();
		await Assert.That(Task3Snapshot(host.Services.GetRequiredService<LightningDatabase>())).IsEquivalentTo(committed);
	}

	[Test]
	public async Task DeleteInactiveQueuedParentHoldsActivationFenceThroughCancelledWaiter()
	{
		await using var host = NewHost();
		await host.OpenAsync();
		var wiki = host.Services.GetRequiredService<IWikiService>();
		var provider = host.Services.GetRequiredService<LightningDatabase>();
		var page = (await wiki.CreateAsync("Delete inactive queued target", "original", "#1", sourceLocale: "en")).Expect<WikiPage>();
		provider.Store.PauseWriter();
		Task<Found<None>>? parent = null;
		try
		{
			parent = provider.DeletePageAsync(page.Id);
			await WaitForLease(provider);
			await AssertFixtureFailure(() => host.ActivateAtomicWikiProjectionAsync(), WikiFixtureMutationFailure.Unavailable);
			using var cancellation = new CancellationTokenSource();
			var waiter = parent.WaitAsync(cancellation.Token);
			cancellation.Cancel();
			await Assert.That(() => waiter).Throws<OperationCanceledException>();
			await Assert.That(parent.IsCompleted).IsFalse();
			await Assert.That(provider.AtomicWikiLegacyMutationsInFlight).IsEqualTo(1);
		}
		finally { provider.Store.ResumeWriter(); if (parent is not null) await parent; }
		await Assert.That(await parent! is None).IsTrue();
		await Assert.That(provider.AtomicWikiLegacyMutationsInFlight).IsEqualTo(0);
		await host.ActivateAtomicWikiProjectionAsync();
		await Assert.That(await provider.GetPageByIdAsync(page.Id) is NotFound).IsTrue();
		await AssertDeleteFixtureBoundary(provider, host.FixtureId);
	}

	[Test]
	public async Task DeleteActiveQueuedScopeOutlivesCancelledFacadeWaiter()
	{
		await using var host = NewHost();
		await host.OpenAsync();
		await host.ActivateAtomicWikiProjectionAsync();
		var wiki = host.Services.GetRequiredService<IWikiService>();
		var provider = host.Services.GetRequiredService<LightningDatabase>();
		var page = (await wiki.CreateAsync("Delete active queued target", "original", "#1", sourceLocale: "en")).Expect<WikiPage>();
		var ready = new TaskCompletionSource<PreparedLegacyWikiMutation>(TaskCreationOptions.RunContinuationsAsynchronously);
		provider.AtomicLegacyPrepared = value => ready.TrySetResult(value);
		provider.Store.PauseWriter();
		Task<Found<None>>? parent = null;
		LegacyWikiPreparationScope? scope = null;
		try
		{
			parent = wiki.DeleteAsync(page.Id, "#1");
			scope = (await ready.Task.WaitAsync(TimeSpan.FromSeconds(30))).Scope;
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
		await Assert.That(await parent! is None).IsTrue();
		await Assert.That(scope!.Budget.WorkingBytes).IsEqualTo(0L);
		await Assert.That(provider.AtomicWikiLegacyMutationsInFlight).IsEqualTo(0);
	}

	private static string[] DeleteSafetySnapshot(LightningDatabase provider) => provider.Store.Read(tx =>
	{
		var tables = new[] { Tables.Meta, Tables.WikiPage, Tables.WikiSlug, Tables.WikiRev, Tables.WikiRecent,
			Tables.WikiByNamespace, Tables.WikiByCategory, Tables.WikiRequirement, Tables.WikiPin, Tables.WikiTr, Tables.PkgWiki }.Concat(AtomicWikiLayout.All);
		return tables.SelectMany(table => new[] { table.Name }.Concat(tx.Range(table, [])
			.Select(row => table.Name + ":" + Convert.ToHexString(row.Key) + ":" + Convert.ToHexString(row.Value)))).ToArray();
	});
}
