using System.Text;
using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Database.Lightning.Semantics;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Models.Wiki.Semantics;
using SharpMUSH.Library.Plugins.Storage.Lightning;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Wiki.Semantics;

public partial class IntegratedWikiMutationTests
{
	[Test]
	public async Task CreateThroughWikiServiceHasBodylessProjectionAndReceipt()
	{
		await using var host = NewHost();
		await host.OpenAsync();
		await host.ActivateAtomicWikiProjectionAsync();
		var provider = host.Services.GetRequiredService<LightningDatabase>();
		var sequence = ObserveSequence(provider);
		var receipts = ObserveMutationCount(provider);
		var wiki = host.Services.GetRequiredService<IWikiService>();
		var page = (await wiki.CreateAsync("Integrated creation", "creation-body-only-marker **Markdown** <em>HTML</em>", "#1",
			sourceLocale: "en", categories: ["alpha"], editSummary: "created through actual facade")).Expect<WikiPage>();
		await Assert.That(page.RevisionNumber).IsEqualTo(1);
		var projection = await AssertIntegratedMutation(provider, page, 1, sequence + 1, receipts + 1, "creation-body-only-marker");
		await AssertLatestReceipt(provider, page, WikiLegacyMutationKind.Create, 1, sequence + 1,
			new string?[] { page.Slug, page.Title, page.Namespace, page.MarkdownSource, page.RenderedHtml, page.PlainText,
				page.AuthorDbref, page.LastEditorDbref, page.SourceLocale, page.CreatedAt.ToString("O", CultureInfo.InvariantCulture),
				page.UpdatedAt.ToString("O", CultureInfo.InvariantCulture), "1", "created through actual facade", string.Empty, "1", "alpha" });
		var history = await wiki.GetRevisionsAsync(page.Id);
		await Assert.That(history.Count).IsEqualTo(1);
		await AssertSourceRevision(history[0], page.Id, 1, page.MarkdownSource, "created through actual facade");
		await AssertIntegratedReopen(host, page, projection, history, receipts + 1);
	}

	[Test]
	public async Task TitleOfZeroFactTargetMovesDurableState()
	{
		await using var host = NewHost();
		await host.OpenAsync();
		var wiki = host.Services.GetRequiredService<IWikiService>();
		var page = (await wiki.CreateAsync("Before integrated title", "title-body-only-marker", "#1", sourceLocale: "en")).Expect<WikiPage>();
		var history = await wiki.GetRevisionsAsync(page.Id);
		await host.ActivateAtomicWikiProjectionAsync();
		var provider = host.Services.GetRequiredService<LightningDatabase>();
		var before = ObserveProjection(provider, page.Id);
		var sequence = ObserveSequence(provider);
		var receipts = ObserveMutationCount(provider);
		var titled = (await wiki.SetTitleAsync(page.Id, "  After integrated title  ")).Expect<WikiPage>();
		await AssertPageMatches(titled, page with { Title = "After integrated title" });
		await Assert.That(await wiki.GetRevisionsAsync(page.Id)).IsEquivalentTo(history);
		var projection = await AssertIntegratedMutation(provider, titled, before.State.State.Summary.StateVersion + 1, sequence + 1, receipts + 1, "title-body-only-marker");
		await AssertLatestReceipt(provider, titled, WikiLegacyMutationKind.Title, before.State.State.Summary.StateVersion + 1,
			sequence + 1, [page.Id, "After integrated title", string.Empty]);
		await Assert.That(projection.State.State.Summary.NativeId).IsEqualTo(before.State.State.Summary.NativeId);
		await AssertIntegratedReopen(host, titled, projection, history, receipts + 1);
	}

	[Test]
	public async Task BodyEditAppendsHistoryWithSameIdentity()
	{
		await using var host = NewHost();
		await host.OpenAsync();
		var wiki = host.Services.GetRequiredService<IWikiService>();
		var page = (await wiki.CreateAsync("Integrated body target", "body-only-old-marker", "#1", sourceLocale: "en", editSummary: "original body")).Expect<WikiPage>();
		var originalHistory = await wiki.GetRevisionsAsync(page.Id);
		await host.ActivateAtomicWikiProjectionAsync();
		var provider = host.Services.GetRequiredService<LightningDatabase>();
		var before = ObserveProjection(provider, page.Id);
		var sequence = ObserveSequence(provider);
		var receipts = ObserveMutationCount(provider);
		var edited = (await wiki.UpdateAsync(page.Id, "body-only-new-marker **Markdown** <strong>HTML</strong>", "#1", "actual body edit")).Expect<WikiPage>();
		await Assert.That(edited.Id).IsEqualTo(page.Id);
		await Assert.That(edited.Slug).IsEqualTo(page.Slug);
		await Assert.That(edited.SourceLocale).IsEqualTo(page.SourceLocale);
		await Assert.That(edited.RevisionNumber).IsEqualTo(2);
		var history = await wiki.GetRevisionsAsync(page.Id);
		await Assert.That(history.Count).IsEqualTo(2);
		await AssertSourceRevision(history[0], page.Id, 2, edited.MarkdownSource, "actual body edit");
		await Assert.That(history[1]).IsEqualTo(originalHistory[0]);
		var projection = await AssertIntegratedMutation(provider, edited, before.State.State.Summary.StateVersion + 1, sequence + 1, receipts + 1, "body-only-old-marker", "body-only-new-marker");
		await AssertLatestReceipt(provider, edited, WikiLegacyMutationKind.Body, before.State.State.Summary.StateVersion + 1,
			sequence + 1, [page.Id, edited.MarkdownSource, edited.RenderedHtml, edited.PlainText, "#1", "actual body edit",
				edited.UpdatedAt.ToString("O", CultureInfo.InvariantCulture), string.Empty]);
		await Assert.That(projection.State.State.Summary.NativeId).IsEqualTo(before.State.State.Summary.NativeId);
		await AssertIntegratedReopen(host, edited, projection, history, receipts + 1);
	}

	[Test]
	public async Task MetadataChangesProjectionWithoutAppendingHistory()
	{
		await using var host = NewHost();
		await host.OpenAsync();
		var wiki = host.Services.GetRequiredService<IWikiService>();
		var page = (await wiki.CreateAsync("Integrated metadata target", "metadata-body-only-marker", "#1", sourceLocale: "en", categories: ["alpha"])).Expect<WikiPage>();
		var history = await wiki.GetRevisionsAsync(page.Id);
		await host.ActivateAtomicWikiProjectionAsync();
		var provider = host.Services.GetRequiredService<LightningDatabase>();
		var before = ObserveProjection(provider, page.Id);
		var sequence = ObserveSequence(provider);
		var receipts = ObserveMutationCount(provider);
		var metadata = (await wiki.SetMetadataAsync(page.Id, ["beta"], false)).Expect<WikiPage>();
		await AssertPageMatches(metadata, page with { Categories = ["beta"], Published = false });
		await Assert.That(await wiki.GetRevisionsAsync(page.Id)).IsEquivalentTo(history);
		var projection = await AssertIntegratedMutation(provider, metadata, before.State.State.Summary.StateVersion + 1, sequence + 1, receipts + 1, "metadata-body-only-marker");
		await AssertLatestReceipt(provider, metadata, WikiLegacyMutationKind.Metadata, before.State.State.Summary.StateVersion + 1,
			sequence + 1, [page.Id, "0", string.Empty, "1", "beta"]);
		await Assert.That(projection.State.State.Summary.NativeId).IsEqualTo(before.State.State.Summary.NativeId);
		await AssertIntegratedReopen(host, metadata, projection, history, receipts + 1);
	}

	private static async Task<ProjectionObservation> AssertIntegratedMutation(LightningDatabase provider, WikiPage page, long stateVersion,
		long sequence, long receipts, params string[] bodyMarkers)
	{
		var projection = ObserveProjection(provider, page.Id);
		var summary = projection.State.State.Summary;
		await Assert.That(projection.State.Source is LocalWikiSource).IsTrue();
		await Assert.That(summary.Reference.Id).IsEqualTo(page.Id);
		await Assert.That(summary.Title).IsEqualTo(page.Title);
		await Assert.That(summary.Collection).IsEqualTo("game");
		await Assert.That(summary.Deleted).IsFalse();
		await Assert.That(summary.Visibility.Namespace).IsEqualTo(page.Namespace);
		await Assert.That(summary.Visibility.Categories).IsEquivalentTo(page.Categories);
		await Assert.That(summary.Visibility.Published).IsEqualTo(page.Published);
		await Assert.That(summary.StateVersion).IsEqualTo(stateVersion);
		await Assert.That(projection.State.State.CommitSequence).IsEqualTo(sequence);
		await Assert.That(projection.Facts.Facts.Length).IsEqualTo(0);
		await Assert.That(ObserveSequence(provider)).IsEqualTo(sequence);
		await Assert.That(ObserveMutationCount(provider)).IsEqualTo(receipts);
		var copiedSemanticRows = provider.Store.Read(tx =>
		{
			var budget = new NativeReadBudget(2048, 128 * 1024, 4 * 1024 * 1024, CancellationToken.None);
			var rows = new List<byte[]>();
			foreach (var table in AtomicWikiLayout.All)
			{
				using var batch = ((IBoundedReadTx)tx).ReadRange(table, [], [], [], [], budget);
				if (!batch.Exhausted) throw new InvalidOperationException("Semantic fixture exceeds bounded observation.");
				foreach (var row in batch.Entries) { rows.Add(row.Key); rows.Add(row.Value); }
			}
			return rows;
		});
		foreach (var marker in bodyMarkers)
			await Assert.That(copiedSemanticRows.Any(row => Encoding.UTF8.GetString(row).Contains(marker, StringComparison.Ordinal))).IsFalse();
		return projection;
	}

	private static async Task AssertIntegratedReopen(SemanticWikiPrototypeHost host, WikiPage page, ProjectionObservation projection,
		IReadOnlyList<WikiRevision> history, long receipts)
	{
		var receiptBytes = ReceiptSnapshot(host.Services.GetRequiredService<LightningDatabase>());
		await host.CloseAsync();
		await AssertFixtureProjectionLayout(host.WorldPath);
		await host.OpenAsync();
		var wiki = host.Services.GetRequiredService<IWikiService>();
		await AssertPageMatches((await wiki.GetByIdAsync(page.Id)).Expect<WikiPage>(), page);
		await Assert.That(await wiki.GetRevisionsAsync(page.Id)).IsEquivalentTo(history);
		var provider = host.Services.GetRequiredService<LightningDatabase>();
		await Assert.That(ObserveProjection(provider, page.Id).SummaryBytes).IsEquivalentTo(projection.SummaryBytes);
		await Assert.That(ObserveMutationCount(provider)).IsEqualTo(receipts);
		await Assert.That(ReceiptSnapshot(provider)).IsEquivalentTo(receiptBytes);
	}

	private static string[] ReceiptSnapshot(LightningDatabase provider) => provider.Store.Read(tx =>
	{
		using var rows = ((IBoundedReadTx)tx).ReadRange(AtomicWikiLayout.Mutations, [], [], [], [],
			new NativeReadBudget(1024, 128 * 1024, 4 * 1024 * 1024, CancellationToken.None, maxRecords: 128));
		if (!rows.Exhausted) throw new InvalidOperationException("Receipt observation exceeds its section.");
		return rows.Entries.Select(row => Convert.ToHexString(row.Key) + ":" + Convert.ToHexString(row.Value)).ToArray();
	});
	private static async Task AssertLatestReceipt(LightningDatabase provider, WikiPage page, WikiLegacyMutationKind kind,
		long stateVersion, long sequence, IEnumerable<string?> fields)
	{
		var receipts = provider.Store.Read(tx =>
		{
			using var rows = ((IBoundedReadTx)tx).ReadRange(AtomicWikiLayout.Mutations, [], [], [], [],
				new NativeReadBudget(1024, 128 * 1024, 4 * 1024 * 1024, CancellationToken.None, maxRecords: 128));
			if (!rows.Exhausted) throw new InvalidOperationException("Receipt observation exceeds its section.");
			return rows.Entries.Select(row => (Key: row.Key, Receipt: IntegratedWikiReceiptCodec.Decode(row.Value))).ToArray();
		});
		await Assert.That(receipts.Select(row => row.Receipt.MutationId).Distinct().Count()).IsEqualTo(receipts.Length);
		foreach (var row in receipts) await Assert.That(row.Key).IsEquivalentTo(row.Receipt.MutationId.ToByteArray());
		var receipt = receipts.Single(row => row.Receipt.CommitSequence == sequence).Receipt;
		await Assert.That(receipt.MutationId == Guid.Empty).IsFalse();
		await Assert.That(receipt.PageId).IsEqualTo(page.Id);
		await Assert.That(receipt.StateVersion).IsEqualTo(stateVersion);
		await Assert.That(receipt.BodyRevision).IsEqualTo(page.RevisionNumber);
		await Assert.That(receipt.Kind).IsEqualTo(kind);
		using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
		var frame = new byte[8];
		BinaryPrimitives.WriteInt32LittleEndian(frame, 1);
		BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(4), (int)kind);
		digest.AppendData(frame);
		foreach (var value in fields)
		{
			BinaryPrimitives.WriteInt64LittleEndian(frame, value is null ? -1 : Encoding.UTF8.GetByteCount(value));
			digest.AppendData(frame);
			if (value is not null) digest.AppendData(Encoding.UTF8.GetBytes(value));
		}
		await Assert.That(receipt.RequestDigest).IsEqualTo(Convert.ToHexStringLower(digest.GetHashAndReset()));
	}
}
