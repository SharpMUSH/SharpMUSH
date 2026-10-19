using System.Globalization;
using System.Text;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Database.Lightning.Records;
using SharpMUSH.Database.Lightning.Semantics;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database.Wiki;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Plugins.Storage.Lightning;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Wiki.Semantics;

public partial class IntegratedWikiMutationTests
{
	[Test]
	[Arguments(0)]
	[Arguments(1)]
	[Arguments(2)]
	[Arguments(3)]
	public async Task Task3ReceiptReplayAfterLaterEditAndReopenDoesNotWrite(int operation)
	{
		await using var host = NewHost();
		await host.OpenAsync();
		await host.ActivateAtomicWikiProjectionAsync();
		var wiki = host.Services.GetRequiredService<IWikiService>();
		var provider = host.Services.GetRequiredService<LightningDatabase>();
		var page = (await wiki.CreateAsync("Replay target", "original body", "#1", sourceLocale: "en")).Expect<WikiPage>();
		var request = await CaptureTask3Request(provider, async () =>
		{
			page = operation switch
			{
				0 => (await wiki.CreateAsync("Replay created target", "created body", "#1", sourceLocale: "en")).Expect<WikiPage>(),
				1 => (await wiki.UpdateAsync(page.Id, "first edit body", "#1", "first edit summary")).Expect<WikiPage>(),
				2 => (await wiki.SetTitleAsync(page.Id, "First edit title")).Expect<WikiPage>(),
				_ => (await wiki.SetMetadataAsync(page.Id, ["first"], false)).Expect<WikiPage>()
			};
		});
		var original = ReadTask3Receipt(provider, request.MutationId);
		await Assert.That(original.PageId).IsEqualTo(page.Id);
		await Assert.That(original.BodyRevision).IsEqualTo(page.RevisionNumber);
		await Assert.That(original.RequestDigest).IsEqualTo(request.RequestDigest);
		var latest = (await wiki.UpdateAsync(page.Id, "later body must survive replay", "#1", "later summary")).Expect<WikiPage>();
		latest = (await wiki.SetTitleAsync(page.Id, "Later title must survive replay")).Expect<WikiPage>();
		var history = await wiki.GetRevisionsAsync(page.Id);
		var before = Task3Snapshot(provider);
		for (var reopen = 0; reopen < 2; reopen++)
		{
			if (reopen == 1)
			{
				await host.CloseAsync();
				await host.OpenAsync();
				provider = host.Services.GetRequiredService<LightningDatabase>();
				wiki = host.Services.GetRequiredService<IWikiService>();
				await Assert.That(Task3Snapshot(provider)).IsEquivalentTo(before);
			}
			var result = await RestoreTask3Request(host, request, request.MutationId);
			if (result is not IntegratedWikiReplayed replay) throw new InvalidOperationException("Expected the original compact receipt, not a fresh page result.");
			await Assert.That(replay.Receipt).IsEqualTo(original);
			await Assert.That(Task3Snapshot(provider)).IsEquivalentTo(before);
			await AssertPageMatches((await wiki.GetByIdAsync(page.Id)).Expect<WikiPage>(), latest);
			await Assert.That(await wiki.GetRevisionsAsync(page.Id)).IsEquivalentTo(history);
		}
	}

	[Test]
	[Arguments(0)]
	[Arguments(1)]
	[Arguments(2)]
	public async Task Task3SameMutationChangedBodyTitleOrMetadataIsMismatch(int operation)
	{
		await using var host = NewHost();
		await host.OpenAsync();
		await host.ActivateAtomicWikiProjectionAsync();
		var wiki = host.Services.GetRequiredService<IWikiService>();
		var provider = host.Services.GetRequiredService<LightningDatabase>();
		var page = (await wiki.CreateAsync("Mismatch target", "original", "#1", sourceLocale: "en")).Expect<WikiPage>();
		var original = await CaptureTask3Request(provider, async () =>
		{
			if (operation == 0) await wiki.UpdateAsync(page.Id, "first body", "#1", "first summary");
			else if (operation == 1) await wiki.SetTitleAsync(page.Id, "First title");
			else await wiki.SetMetadataAsync(page.Id, ["first"], false);
		});
		await host.CloseAsync();
		await host.OpenAsync();
		provider = host.Services.GetRequiredService<LightningDatabase>();
		var before = Task3Snapshot(provider);
		using var admission = Task3Owner(host).EnterMutation(SemanticFixtureMutation.LegacyWiki);
		using var scope = new LegacyWikiPreparationScope(admission, CancellationToken.None);
		PreparedLegacyWikiMutation changed = original switch
		{
			PreparedLegacyWikiMutation.Body body => PreparedLegacyWikiMutation.Body.Prepare(scope, body.Id,
				new("different body", "different HTML", "different plain"), body.Editor, body.EditSummary, body.At),
			PreparedLegacyWikiMutation.Title title => PreparedLegacyWikiMutation.Title.Prepare(scope, title.Id, "Different title"),
			PreparedLegacyWikiMutation.Metadata metadata => PreparedLegacyWikiMutation.Metadata.Prepare(scope, metadata.Id, ["different"], metadata.Published),
			_ => throw new InvalidOperationException("Unexpected mismatch request.")
		};
		await Assert.That(changed.RequestDigest == original.RequestDigest).IsFalse();
		var restored = PreparedLegacyWikiMutation.RestoreForReplay(scope, changed, original.MutationId);
		await AssertFixtureFailure(async () => { await provider.CommitLegacyFixtureAsync(restored, CancellationToken.None); }, WikiFixtureMutationFailure.MutationMismatch);
		await Assert.That(Task3Snapshot(provider)).IsEquivalentTo(before);
		await Assert.That(provider.AtomicWikiPhase).IsEqualTo(SemanticFixturePhase.Active);
	}

	[Test]
	[Arguments(0)]
	[Arguments(1)]
	[Arguments(2)]
	public async Task Task3MalformedOrIncompatibleReceiptIsUnavailableWithoutWrites(int damage)
	{
		await using var host = NewHost();
		await host.OpenAsync();
		await host.ActivateAtomicWikiProjectionAsync();
		var wiki = host.Services.GetRequiredService<IWikiService>();
		var provider = host.Services.GetRequiredService<LightningDatabase>();
		var request = await CaptureTask3Request(provider, async () =>
		{
			await wiki.CreateAsync("Receipt validation target", "body", "#1", sourceLocale: "en");
		});
		var receipt = ReadTask3Receipt(provider, request.MutationId);
		var bytes = IntegratedWikiReceiptCodec.Encode(receipt);
		if (damage == 0) bytes[0] = 2;
		else if (damage == 1) bytes = [.. bytes, 0];
		else bytes = IntegratedWikiReceiptCodec.Encode(receipt with { MutationId = Guid.NewGuid() });
		// Valid native storage containing deliberately invalid managed receipt data, in this owned fixture only.
		await provider.Store.WriteAsync(tx => tx.Put(AtomicWikiLayout.Mutations, request.MutationId.ToByteArray(), bytes));
		var before = Task3Snapshot(provider);
		await AssertFixtureFailure(async () => { await RestoreTask3Request(host, request, request.MutationId); }, WikiFixtureMutationFailure.Unavailable);
		await Assert.That(Task3Snapshot(provider)).IsEquivalentTo(before);
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task Task3MalformedManagedPageOrProjectionIsUnavailableWithoutWrites(bool projection)
	{
		await using var host = NewHost();
		await host.OpenAsync();
		await host.ActivateAtomicWikiProjectionAsync();
		var provider = host.Services.GetRequiredService<LightningDatabase>();
		var page = (await provider.GetPageBySlugAsync("main", "home")).Expect<WikiPage>();
		var table = projection ? AtomicWikiLayout.Summaries : Tables.WikiPage;
		var key = projection
			? provider.Store.Read(tx => tx.Range(AtomicWikiLayout.Summaries, [])
				.Single(row => WikiSemanticStateCodec.ProjectionState(row.Value).State.Summary.Reference.Id == page.Id).Key)
			: Keys.Dbref(long.Parse(page.Id.AsSpan(10), CultureInfo.InvariantCulture));
		var invalid = provider.Store.Read(tx =>
		{
			if (!tx.TryGet(table, key, out var bytes)) throw new InvalidOperationException("Missing setup row.");
			if (!projection) return Codec.Serialize(Codec.Deserialize<WikiPageRecord>(bytes) with { Title = string.Empty });
			var encoded = WikiSemanticStateCodec.ProjectionState(WikiSemanticStateCodec.ProjectionState(bytes));
			encoded[0] = 2; // otherwise valid managed envelope, unsupported version
			return encoded;
		});
		// Invalid application data only; valid owned native rows, transactions and handles.
		await provider.Store.WriteAsync(tx => tx.Put(table, key, invalid));
		var before = Task3Snapshot(provider);
		await AssertFixtureFailure(async () => { await provider.SetPageTitleAsync(page.Id, "Rejected title"); }, WikiFixtureMutationFailure.Unavailable);
		await Assert.That(Task3Snapshot(provider)).IsEquivalentTo(before);
	}

	[Test]
	public async Task Task3EscapedBodyOverCompleteDeltaStopsAtEarlierWorkingBound()
	{
		await using var host = NewHost();
		await host.OpenAsync();
		var provider = host.Services.GetRequiredService<LightningDatabase>();
		var home = (await provider.GetPageBySlugAsync("main", "home")).Expect<WikiPage>();
		var categories = Enumerable.Range(0, 128).Select(i => "c" + i.ToString("D3", CultureInfo.InvariantCulture)).ToArray();
		var page = (await provider.CreatePageAsync(home with
		{
			Slug = "whole-delta-target",
			Title = new string('t', 100),
			MarkdownSource = "small",
			RenderedHtml = "small",
			PlainText = "small",
			Categories = categories
		})).Expect<WikiPage>();
		await host.ActivateAtomicWikiProjectionAsync();
		var key = Keys.Dbref(long.Parse(page.Id.AsSpan(10), CultureInfo.InvariantCulture));
		var old = provider.Store.Read(tx =>
		{
			if (!tx.TryGet(Tables.WikiPage, key, out var value)) throw new InvalidOperationException("Missing setup page.");
			return Codec.Deserialize<WikiPageRecord>(value);
		});
		// 20 Ki UTF16 control characters occupy 120 KiB in each actual JSON body/history row.
		// The 128 distinct existing category keys are <= 215 bytes, well below native key limits.
		var body = new string('\u0001', 20 * 1024);
		var at = DateTimeOffset.UtcNow;
		var updated = old with
		{
			MarkdownSource = body,
			RenderedHtml = string.Empty,
			PlainText = string.Empty,
			UpdatedAt = at.ToString("O", CultureInfo.InvariantCulture),
			LastEditorDbref = "#1",
			RevisionNumber = old.RevisionNumber + 1
		};
		var history = new WikiRevisionRecord
		{
			PageId = page.Id, Locale = string.Empty, RevisionNumber = updated.RevisionNumber, MarkdownSource = body,
			EditorDbref = "#1", Timestamp = updated.UpdatedAt, EditSummary = "escaped history"
		};
		var pageBytes = Codec.Serialize(updated);
		var historyBytes = Codec.Serialize(history);
		var historyKey = Keys.Composite(page.Id, string.Empty, (uint)updated.RevisionNumber);
		var categoryRows = provider.Store.Read(tx => tx.Range(Tables.WikiByCategory, []).Where(row => row.Key.AsSpan().EndsWith(key)).ToArray());
		await Assert.That(categoryRows.Length).IsEqualTo(128);
		await Assert.That(categoryRows.All(row => row.Key.Length <= provider.Store.MaxKeySize)).IsTrue();
		await Assert.That(pageBytes.Length + key.Length < 128 * 1024).IsTrue();
		await Assert.That(historyBytes.Length + historyKey.Length < 128 * 1024).IsTrue();
		// A lower bound on the final coalesced delta: each category Put survives once, plus page/history.
		var exactLowerBound = pageBytes.LongLength + key.Length + historyBytes.LongLength + historyKey.Length
			+ categoryRows.Sum(row => (long)row.Key.Length + row.Value.Length);
		await Assert.That(exactLowerBound > 256 * 1024).IsTrue();
		var before = Task3Snapshot(provider);
		string? dimension = null;
		var applied = 0;
		provider.AtomicLegacyCapacityObserved = value => dimension = value;
		provider.AtomicLegacyPutApplied = _ => applied++;
		try
		{
			await AssertFixtureFailure(async () => { await provider.UpdatePageBodyAsync(page.Id, new(body, string.Empty, string.Empty), "#1", history.EditSummary, at); }, WikiFixtureMutationFailure.Quota);
			await Assert.That(dimension).IsEqualTo("json-working");
			await Assert.That(applied).IsEqualTo(0);
			await Assert.That(Task3Snapshot(provider)).IsEquivalentTo(before);
		}
		finally { provider.AtomicLegacyCapacityObserved = null; provider.AtomicLegacyPutApplied = null; }
		await host.CloseAsync();
		await host.OpenAsync();
		await Assert.That(Task3Snapshot(host.Services.GetRequiredService<LightningDatabase>())).IsEquivalentTo(before);
	}

	[Test]
	public async Task Task3CreateCommandOverCompleteDeltaRejectsBeforeNativeApply()
	{
		await using var host = NewHost();
		await host.OpenAsync();
		await host.ActivateAtomicWikiProjectionAsync();
		var provider = host.Services.GetRequiredService<LightningDatabase>();
		var mediator = host.Services.GetRequiredService<IMediator>();
		var home = (await provider.GetPageBySlugAsync("main", "home")).Expect<WikiPage>();
		// Distinct single-character category keys keep retained UTF16 input small while remaining valid native keys.
		var categories = Enumerable.Range(0, 128).Select(i => ((char)(0x4E00 + i)).ToString()).ToArray();
		var at = new DateTimeOffset(2026, 10, 19, 0, 0, 0, TimeSpan.Zero);
		// The real typed command accepts already-rendered fields; this does not assert facade renderer output.
		var page = home with
		{
			Id = string.Empty,
			Slug = "d",
			Title = new string('t', 248),
			Namespace = "main",
			MarkdownSource = new string('\u0001', 16_232),
			RenderedHtml = string.Empty,
			PlainText = string.Empty,
			AuthorDbref = "#1",
			LastEditorDbref = "#1",
			CreatedAt = at,
			UpdatedAt = at,
			RevisionNumber = 1,
			Categories = categories,
			Published = true,
			SourceLocale = "en"
		};
		var nextId = provider.Store.Read(tx =>
		{
			if (!tx.TryGet(Tables.Meta, Keys.Str("next_wiki"), out var bytes)) throw new InvalidOperationException("Missing wiki allocator.");
			return Keys.ReadDbref(bytes);
		});
		var key = Keys.Dbref(nextId);
		var pageId = "wiki_page/" + nextId.ToString(CultureInfo.InvariantCulture);
		var stamp = at.ToString("O", CultureInfo.InvariantCulture);
		var record = new WikiPageRecord
		{
			Slug = page.Slug, Title = page.Title, Namespace = page.Namespace, MarkdownSource = page.MarkdownSource,
			RenderedHtml = page.RenderedHtml, PlainText = page.PlainText, AuthorDbref = page.AuthorDbref,
			LastEditorDbref = page.LastEditorDbref, CreatedAt = stamp, UpdatedAt = stamp, RevisionNumber = 1,
			Categories = categories, Published = page.Published, SourceLocale = page.SourceLocale
		};
		var revision = new WikiRevisionRecord
		{
			PageId = pageId, Locale = string.Empty, RevisionNumber = 1, MarkdownSource = page.MarkdownSource,
			EditorDbref = page.AuthorDbref, Timestamp = stamp, EditSummary = null
		};
		var pageBytes = Codec.Serialize(record);
		var revisionBytes = Codec.Serialize(revision);
		var revisionKey = Keys.Composite(pageId, string.Empty, (uint)1);
		var visibility = Keys.Concat([1], Keys.Str(page.AuthorDbref));
		var categoryKeys = categories.Select(category => Keys.Concat(Keys.Upper(category), Keys.Sep,
			Encoding.BigEndianUnicode.GetBytes(page.Title), new byte[] { 0, 0 }, key)).ToArray();
		await Assert.That(categoryKeys.Length).IsEqualTo(128);
		await Assert.That(categoryKeys.Select(indexKey => Convert.ToHexString(indexKey)).Distinct(StringComparer.Ordinal).Count()).IsEqualTo(128);
		await Assert.That(categoryKeys.All(indexKey => indexKey.Length <= provider.Store.MaxKeySize)).IsTrue();
		await Assert.That(pageBytes.Length + key.Length < 128 * 1024).IsTrue();
		await Assert.That(revisionBytes.Length + revisionKey.Length < 128 * 1024).IsTrue();
		var categoryDeltaBytes = categoryKeys.Sum(indexKey => (long)indexKey.Length + visibility.Length);
		var exactLowerBound = pageBytes.LongLength + key.Length + revisionBytes.LongLength + revisionKey.Length + categoryDeltaBytes;
		await Assert.That(exactLowerBound > 256 * 1024).IsTrue();
		var before = Task3Snapshot(provider);
		PreparedLegacyWikiMutation? prepared = null;
		string? dimension = null;
		long? workingAtRejection = null;
		var applied = 0;
		provider.AtomicLegacyPrepared = request => prepared = request;
		provider.AtomicLegacyCapacityObserved = value =>
		{
			dimension = value;
			workingAtRejection = prepared?.Scope.Budget.WorkingBytes;
		};
		provider.AtomicLegacyPutApplied = _ => applied++;
		try
		{
			await AssertFixtureFailure(async () => { await mediator.Send(new CreateWikiPageCommand(page, null)); }, WikiFixtureMutationFailure.Quota);
			Console.WriteLine($"Task3 create delta: page={pageBytes.Length}, pageKey={key.Length}, revision={revisionBytes.Length}, revisionKey={revisionKey.Length}, categories={categoryDeltaBytes}, lowerBound={exactLowerBound}, workingAtRejection={workingAtRejection}, dimension={dimension}");
			await Assert.That(dimension).IsEqualTo("delta-bytes");
			await Assert.That(applied).IsEqualTo(0);
			await Assert.That(prepared is not null).IsTrue();
			await Assert.That(prepared!.Scope.Budget.WorkingBytes).IsEqualTo(0L);
			await Assert.That(Task3Snapshot(provider)).IsEquivalentTo(before);
		}
		finally { provider.AtomicLegacyPrepared = null; provider.AtomicLegacyCapacityObserved = null; provider.AtomicLegacyPutApplied = null; }
		await host.CloseAsync();
		await host.OpenAsync();
		await Assert.That(Task3Snapshot(host.Services.GetRequiredService<LightningDatabase>())).IsEquivalentTo(before);
	}

	[Test]
	public async Task Task3SharedDeltaCoalescesThenRejectsAggregateBeforeNativeApply()
	{
		await using var host = NewHost();
		await host.OpenAsync();
		var provider = host.Services.GetRequiredService<LightningDatabase>();
		var table = provider.OpenTable("task3.delta-observation", false);
		await provider.Store.WriteAsync(tx => tx.Put(table, [9], [7]));
		await host.ActivateAtomicWikiProjectionAsync();
		var before = Task3Snapshot(provider, table);
		using var lifetime = new NativeReadBudget(1024, 128 * 1024, 4 * 1024 * 1024, CancellationToken.None).ReserveWorking(1024);
		var delta = new WikiSemanticPlannedDelta(lifetime, CancellationToken.None);
		var value = new byte[96 * 1024];
		string? dimension = null;
		try
		{
			await provider.Store.WriteAsync(tx =>
			{
				delta.Put(table, [1], value);
				delta.Put(table, [1], value); // replacement counts once, not twice
				if (delta.Bytes != 98_305) throw new InvalidOperationException("Replacement did not coalesce.");
				delta.Delete(table, [1]);
				if (delta.Bytes != 1) throw new InvalidOperationException("Delete retained the replaced value's charge.");
				delta.Put(table, [1], value);
				delta.Put(table, [2], value);
				delta.Put(table, [3], value); // 294,915 exact bytes; each key/value row is only 98,305
				delta.Apply(tx, CancellationToken.None);
			});
		}
		catch (WikiSemanticCapacityException error) { dimension = error.Dimension; }
		await Assert.That(dimension).IsEqualTo("delta-bytes");
		await Assert.That(delta.Bytes).IsEqualTo(196_610L);
		await Assert.That(Task3Snapshot(provider, table)).IsEquivalentTo(before);
		delta.Clear();
		await host.CloseAsync();
		await host.OpenAsync();
		provider = host.Services.GetRequiredService<LightningDatabase>();
		table = provider.OpenTable(table.Name, false);
		await Assert.That(Task3Snapshot(provider, table)).IsEquivalentTo(before);
	}

	[Test]
	public async Task Task3ManagedFaultAfterFirstLegacyPutAbortsActualParent()
	{
		await using var host = NewHost();
		await host.OpenAsync();
		await host.ActivateAtomicWikiProjectionAsync();
		var provider = host.Services.GetRequiredService<LightningDatabase>();
		var wiki = host.Services.GetRequiredService<IWikiService>();
		var before = Task3Snapshot(provider);
		var putTables = new List<string>();
		PreparedLegacyWikiMutation? prepared = null;
		provider.AtomicLegacyPrepared = request => prepared = request;
		provider.AtomicLegacyPutApplied = table =>
		{
			putTables.Add(table.Name);
			if (!table.Name.StartsWith(AtomicWikiLayout.Prefix, StringComparison.Ordinal)) throw new Task3ManagedAbort();
		};
		try
		{
			await Assert.That(async () => { await wiki.CreateAsync("Aborted allocation", "must not persist", "#1", sourceLocale: "en"); }).Throws<Task3ManagedAbort>();
			await Assert.That(putTables).IsEquivalentTo(new[] { Tables.Meta.Name });
			await Assert.That(prepared is not null).IsTrue();
			await Assert.That(prepared!.Scope.Budget.WorkingBytes).IsEqualTo(0L);
			await Assert.That(Task3Snapshot(provider)).IsEquivalentTo(before);
		}
		finally { provider.AtomicLegacyPrepared = null; provider.AtomicLegacyPutApplied = null; }
		await host.CloseAsync();
		await host.OpenAsync();
		provider = host.Services.GetRequiredService<LightningDatabase>();
		await Assert.That(Task3Snapshot(provider)).IsEquivalentTo(before);
		wiki = host.Services.GetRequiredService<IWikiService>();
		var page = (await wiki.CreateAsync("Aborted allocation", "healthy retry", "#1", sourceLocale: "en")).Expect<WikiPage>();
		await Assert.That(page.MarkdownSource).IsEqualTo("healthy retry");
		await Assert.That(ObserveMutationCount(provider)).IsEqualTo(1L);
	}

	private sealed class Task3ManagedAbort : Exception;

	private static SemanticFixtureCapability Task3Owner(SemanticWikiPrototypeHost host)
		=> ((SemanticWikiPackageAdmission)host.Services.GetRequiredService<IPackageInstallService>()).Owner;

	private static async Task<PreparedLegacyWikiMutation> CaptureTask3Request(LightningDatabase provider, Func<Task> operation)
	{
		PreparedLegacyWikiMutation? captured = null;
		provider.AtomicLegacyPrepared = request => captured = request;
		try { await operation(); }
		finally { provider.AtomicLegacyPrepared = null; }
		return captured ?? throw new InvalidOperationException("The real writer did not prepare a request.");
	}

	private static async Task<IntegratedWikiCommitResult> RestoreTask3Request(SemanticWikiPrototypeHost host,
		PreparedLegacyWikiMutation request, Guid mutationId)
	{
		using var admission = Task3Owner(host).EnterMutation(SemanticFixtureMutation.LegacyWiki);
		using var scope = new LegacyWikiPreparationScope(admission, CancellationToken.None);
		var restored = PreparedLegacyWikiMutation.RestoreForReplay(scope, request, mutationId);
		return await host.Services.GetRequiredService<LightningDatabase>().CommitLegacyFixtureAsync(restored, CancellationToken.None);
	}

	private static IntegratedWikiReceipt ReadTask3Receipt(LightningDatabase provider, Guid id) => provider.Store.Read(tx =>
	{
		if (!tx.TryGet(AtomicWikiLayout.Mutations, id.ToByteArray(), out var bytes)) throw new InvalidOperationException("Missing receipt.");
		return IntegratedWikiReceiptCodec.Decode(bytes);
	});

	// Include all touched legacy list indexes, which the older compact fingerprint deliberately omits.
	// Independent bounded test observation; it does not borrow or increase an operation's own budget.
	private static string[] Task3Snapshot(LightningDatabase provider, TableDef? extra = null) => provider.Store.Read(tx =>
	{
		var tables = new[] { Tables.Meta, Tables.WikiPage, Tables.WikiSlug, Tables.WikiRev, Tables.WikiRecent,
			Tables.WikiByNamespace, Tables.WikiByCategory, Tables.WikiRequirement, Tables.WikiPin, Tables.WikiTr, Tables.PkgWiki }
			.Concat(AtomicWikiLayout.All).Concat(extra is TableDef extraTable ? new[] { extraTable } : Array.Empty<TableDef>());
		var result = new List<string>();
		foreach (var table in tables)
		{
			var budget = new NativeReadBudget(4096, 1024 * 1024, 16 * 1024 * 1024, CancellationToken.None, maxRecords: 2048);
			byte[] after = [];
			result.Add(table.Name);
			while (true)
			{
				using var rows = ((IBoundedReadTx)tx).ReadRange(table, [], [], [], after, budget);
				foreach (var row in rows.Entries) result.Add(table.Name + ":" + Convert.ToHexString(row.Key) + ":" + Convert.ToHexString(row.Value));
				if (rows.Exhausted) break;
				if (rows.Entries.Length == 0) throw new InvalidOperationException("Task 3 table observation made no progress.");
				after = rows.Entries[^1].Key.ToArray();
			}
		}
		return result.ToArray();
	});
}
