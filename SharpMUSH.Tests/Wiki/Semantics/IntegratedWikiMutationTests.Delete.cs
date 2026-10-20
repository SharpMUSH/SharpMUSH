using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Database.Lightning.Semantics;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.Commands.Database.Wiki;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Models.Wiki.Semantics;
using SharpMUSH.Library.Plugins.Storage.Lightning;
using SharpMUSH.Library.Queries.Database.Wiki;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Wiki.Semantics;

public partial class IntegratedWikiMutationTests
{
	[Test]
	public async Task DeleteThroughWikiServiceRemovesBoundedLegacyStateAndTombstonesTarget()
	{
		await using var host = NewHost();
		await host.OpenAsync();
		var wiki = host.Services.GetRequiredService<IWikiService>();
		await wiki.SetCategoryPinnedAsync("shared", true);
		await host.ActivateAtomicWikiProjectionAsync();
		var provider = host.Services.GetRequiredService<LightningDatabase>();
		var target = (await wiki.CreateAsync("Delete bounded target", "delete-original-body-marker", "#1",
			sourceLocale: "en", categories: ["shared", "old"])).Expect<WikiPage>();
		target = (await wiki.UpdateAsync(target.Id, "delete-latest-body-marker", "#1", "second source revision")).Expect<WikiPage>();
		target = (await wiki.SetTitleAsync(target.Id, "Delete latest title")).Expect<WikiPage>();
		target = (await wiki.SetMetadataAsync(target.Id, ["shared", "latest"], false)).Expect<WikiPage>();
		var neighbor = (await wiki.CreateAsync("Delete surviving neighbor", "neighbor-original", "#1",
			sourceLocale: "en", categories: ["shared"])).Expect<WikiPage>();
		neighbor = (await wiki.UpdateAsync(neighbor.Id, "neighbor-latest", "#1", "neighbor history")).Expect<WikiPage>();
		var neighborHistory = await wiki.GetRevisionsAsync(neighbor.Id);
		await Assert.That((await wiki.GetRevisionsAsync(target.Id)).Count).IsEqualTo(2);
		var previous = ObserveProjection(provider, target.Id);
		var before = Task3Snapshot(provider);
		var sequence = ObserveSequence(provider);
		var receipts = ObserveMutationCount(provider);
		var allocator = ObserveWikiCounter(provider);

		// Exercise the actual facade and owning provider transaction.
		await Assert.That(await wiki.DeleteAsync(target.Id, "#1") is None).IsTrue();
		await AssertDeleteDelta(provider, target, previous, before, sequence, receipts, allocator);
		await AssertDeletedConsumerState(wiki, target);
		await AssertPageMatches((await wiki.GetByIdAsync(neighbor.Id)).Expect<WikiPage>(), neighbor);
		await Assert.That(await wiki.GetRevisionsAsync(neighbor.Id)).IsEquivalentTo(neighborHistory);
		var committed = Task3Snapshot(provider);
		await host.CloseAsync();
		await host.OpenAsync();
		provider = host.Services.GetRequiredService<LightningDatabase>();
		wiki = host.Services.GetRequiredService<IWikiService>();
		await Assert.That(Task3Snapshot(provider)).IsEquivalentTo(committed);
		await AssertDeletedConsumerState(wiki, target);
		await AssertPageMatches((await wiki.GetByIdAsync(neighbor.Id)).Expect<WikiPage>(), neighbor);
		await Assert.That(await wiki.GetRevisionsAsync(neighbor.Id)).IsEquivalentTo(neighborHistory);
		await AssertDeleteFixtureBoundary(provider, host.FixtureId);
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task DeleteCommandRemovesPageRequirementAndInvalidatesCachedRequirements(bool bareId)
	{
		// Each argument invocation owns a fresh host and live target; neither repeats a previous delete.
		await using var host = NewHost();
		await host.OpenAsync();
		var wiki = host.Services.GetRequiredService<IWikiService>();
		var target = (await wiki.CreateAsync("Delete cached rules target", "rules body", "#1",
			sourceLocale: "en", categories: ["delete_rules"])).Expect<WikiPage>();
		var pageRule = WikiRuleTarget.ForPage(target.Id);
		var categoryRule = WikiRuleTarget.ForCategory("delete_rules");
		var namespaceRule = WikiRuleTarget.ForNamespace("main");
		foreach (var rule in new[] { pageRule, categoryRule, namespaceRule })
			await Assert.That(await wiki.SetRequirementsAsync(rule, WikiRequirementSet.Protection, "#1") is None).IsTrue();
		await host.ActivateAtomicWikiProjectionAsync();
		var provider = host.Services.GetRequiredService<LightningDatabase>();
		var mediator = host.Services.GetRequiredService<IMediator>();
		var certificate = DeleteCertificateSnapshot(provider);
		var previous = ObserveProjection(provider, target.Id);
		var sequence = ObserveSequence(provider);
		var receipts = ObserveMutationCount(provider);
		var allocator = ObserveWikiCounter(provider);
		var before = Task3Snapshot(provider);
		// Real cacheable/tagged query and actual facade; no replacement behavior/provider/cache.
		var primed = await mediator.Send(new GetWikiRequirementsQuery());
		await Assert.That(primed.Any(set => set.Target == pageRule)).IsTrue();
		await mediator.Send(new GetWikiRequirementsQuery());
		await Assert.That((await wiki.GetRequirementsAsync()).HasPageRules(target.Id)).IsTrue();
		var unrelated = primed.Where(set => set.Target != pageRule).ToArray();

		var commandId = bareId ? target.Id[10..] : target.Id;
		await Assert.That(await mediator.Send(new DeleteWikiPageCommand(commandId)) is None).IsTrue();
		await AssertDeleteDelta(provider, target, previous, before, sequence, receipts, allocator);
		await AssertDeleteRequirementsFresh(wiki, mediator, target.Id, unrelated);
		await Assert.That(DeleteCertificateSnapshot(provider)).IsEquivalentTo(certificate);
		var committed = Task3Snapshot(provider);
		await host.CloseAsync();
		await host.OpenAsync();
		provider = host.Services.GetRequiredService<LightningDatabase>();
		wiki = host.Services.GetRequiredService<IWikiService>();
		mediator = host.Services.GetRequiredService<IMediator>();
		await Assert.That(Task3Snapshot(provider)).IsEquivalentTo(committed);
		await AssertDeleteRequirementsFresh(wiki, mediator, target.Id, unrelated);
		await AssertDeletedConsumerState(wiki, target);
		await Assert.That(DeleteCertificateSnapshot(provider)).IsEquivalentTo(certificate);
		await AssertDeleteFixtureBoundary(provider, host.FixtureId);
		// Historical bootstrap requirements digest must not block later ordinary admitted writes.
		var later = (await wiki.CreateAsync("Delete rules later creation", "later initial", "#1", sourceLocale: "en")).Expect<WikiPage>();
		later = (await wiki.UpdateAsync(later.Id, "later body", "#1", "after reopen")).Expect<WikiPage>();
		later = (await wiki.SetTitleAsync(later.Id, "Delete rules later title")).Expect<WikiPage>();
		await AssertPageMatches((await wiki.GetByIdAsync(later.Id)).Expect<WikiPage>(), later);
		await AssertDeleteRequirementsFresh(wiki, mediator, target.Id, unrelated);
		await Assert.That(DeleteCertificateSnapshot(provider)).IsEquivalentTo(certificate);
		await AssertDeleteFixtureBoundary(provider, host.FixtureId);
	}

	[Test]
	public async Task DeleteRecreateKeepsOldTombstoneAndAllocatesNewIdentity()
	{
		await using var host = NewHost();
		await host.OpenAsync();
		await host.ActivateAtomicWikiProjectionAsync();
		var wiki = host.Services.GetRequiredService<IWikiService>();
		var provider = host.Services.GetRequiredService<LightningDatabase>();
		var original = (await wiki.CreateAsync("Delete recreation target", "old-incarnation-body-marker", "#1", sourceLocale: "en")).Expect<WikiPage>();
		var previous = ObserveProjection(provider, original.Id);
		var before = Task3Snapshot(provider);
		var sequence = ObserveSequence(provider);
		var receipts = ObserveMutationCount(provider);
		var allocator = ObserveWikiCounter(provider);
		await Assert.That(await wiki.DeleteAsync(original.Id, "#1") is None).IsTrue();
		await AssertDeleteDelta(provider, original, previous, before, sequence, receipts, allocator);
		var tombstone = ObserveProjection(provider, original.Id);
		var retainedReceipts = ReceiptSnapshot(provider);
		var replacement = (await wiki.CreateAsync(original.Title, "replacement-body-marker", "#1", sourceLocale: "en",
			slug: original.Slug)).Expect<WikiPage>();
		var live = ObserveProjection(provider, replacement.Id);
		await Assert.That(replacement.Id == original.Id).IsFalse();
		await Assert.That(live.State.State.Summary.Reference == tombstone.State.State.Summary.Reference).IsFalse();
		await Assert.That(live.State.State.Summary.NativeId == tombstone.State.State.Summary.NativeId).IsFalse();
		await Assert.That(live.State.State.Summary.Deleted).IsFalse();
		await Assert.That(ObserveWikiCounter(provider)).IsEqualTo(allocator + 1);
		await Assert.That(ObserveProjection(provider, original.Id).SummaryBytes).IsEquivalentTo(tombstone.SummaryBytes);
		await Assert.That(retainedReceipts.Except(ReceiptSnapshot(provider))).IsEmpty();
		await AssertPageMatches((await wiki.GetBySlugAsync(original.Slug)).Expect<WikiPage>(), replacement);
		var committed = Task3Snapshot(provider);
		for (var reopen = 0; reopen < 2; reopen++)
		{
			if (reopen == 1)
			{
				await host.CloseAsync();
				await host.OpenAsync();
				provider = host.Services.GetRequiredService<LightningDatabase>();
				wiki = host.Services.GetRequiredService<IWikiService>();
				await Assert.That(Task3Snapshot(provider)).IsEquivalentTo(committed);
			}
			foreach (var id in new[] { original.Id, original.Id[10..] })
			{
				await Assert.That(await wiki.SetTitleAsync(id, "must not resurrect") is NotFound).IsTrue();
				await Assert.That(await wiki.UpdateAsync(id, "must not resurrect", "#1", "old incarnation") is NotFound).IsTrue();
				await Assert.That(await wiki.SetMetadataAsync(id, ["must_not_resurrect"], false) is NotFound).IsTrue();
				await Assert.That(Task3Snapshot(provider)).IsEquivalentTo(committed);
			}
			await AssertDeletedConsumerState(wiki, original, slugReused: true);
			await AssertPageMatches((await wiki.GetBySlugAsync(original.Slug)).Expect<WikiPage>(), replacement);
			await Assert.That(ObserveProjection(provider, original.Id).SummaryBytes).IsEquivalentTo(tombstone.SummaryBytes);
			await Assert.That(ObserveProjection(provider, replacement.Id).SummaryBytes).IsEquivalentTo(live.SummaryBytes);
			await Assert.That(retainedReceipts.Except(ReceiptSnapshot(provider))).IsEmpty();
			await AssertDeleteFixtureBoundary(provider, host.FixtureId);
		}
	}

	private static async Task AssertDeletedConsumerState(IWikiService wiki, WikiPage page, bool slugReused = false)
	{
		foreach (var id in new[] { page.Id, page.Id[10..] })
		{
			await Assert.That(await wiki.GetByIdAsync(id) is NotFound).IsTrue();
			await Assert.That(await wiki.GetRevisionsAsync(id)).IsEmpty();
		}
		if (!slugReused) await Assert.That(await wiki.GetBySlugAsync(page.Slug) is NotFound).IsTrue();
		await Assert.That((await wiki.GetByNamespaceAsync(WikiNamespace.Main)).Any(row => row.Id == page.Id)).IsFalse();
		await Assert.That((await wiki.GetRecentChangesAsync()).Any(row => row.Id == page.Id)).IsFalse();
		foreach (var category in page.Categories)
			await Assert.That((await wiki.GetByCategoryAsync(category)).Any(row => row.Id == page.Id)).IsFalse();
	}

	private static async Task AssertDeleteRequirementsFresh(IWikiService wiki, IMediator mediator, string id,
		IReadOnlyList<WikiRequirementSet> unrelated)
	{
		var rows = await mediator.Send(new GetWikiRequirementsQuery());
		await Assert.That(rows.Any(set => set.Target == WikiRuleTarget.ForPage(id))).IsFalse();
		await Assert.That(rows.Any(set => set.Target == WikiRuleTarget.ForPage(id[10..]))).IsFalse();
		await Assert.That(rows.Count).IsEqualTo(unrelated.Count);
		var facade = await wiki.GetRequirementsAsync();
		await Assert.That(facade.HasPageRules(id)).IsFalse();
		await Assert.That(facade.HasPageRules(id[10..])).IsFalse();
		await Assert.That(facade.Sets.Count).IsEqualTo(unrelated.Count);
		foreach (var expected in unrelated)
		{
			var actual = rows.Single(set => set.Target == expected.Target);
			await Assert.That(actual.UpdatedBy).IsEqualTo(expected.UpdatedBy);
			await Assert.That(actual.UpdatedAt).IsEqualTo(expected.UpdatedAt);
			foreach (var action in Enum.GetValues<WikiAction>())
			{
				await Assert.That(actual.For(action)).IsEquivalentTo(expected.For(action));
				await Assert.That(facade.For(expected.Target)!.For(action)).IsEquivalentTo(expected.For(action));
			}
		}
	}

	private static async Task AssertDeleteFixtureBoundary(LightningDatabase provider, Guid fixtureId)
	{
		// Decode rejects QueryReady=true, unknown schema/generation and mismatched fixture identities.
		await Assert.That(ObserveActivation(provider, fixtureId).Active).IsTrue();
		await Assert.That(AtomicWikiLayout.Schema.Properties.Length).IsEqualTo(0);
		await Assert.That(provider.AtomicWikiPhase).IsEqualTo(SemanticFixturePhase.Active);
	}

	private static string[] DeleteCertificateSnapshot(LightningDatabase provider)
	{
		var identities = new[]
		{
			DeleteRowIdentity(Tables.Meta, AtomicWikiLayout.MarkerKey),
			DeleteRowIdentity(AtomicWikiLayout.Metadata, AtomicWikiLayout.RootKey),
			DeleteRowIdentity(AtomicWikiLayout.Metadata, AtomicWikiLayout.CertificateKey)
		};
		return Task3Snapshot(provider).Where(row => identities.Any(identity => row.StartsWith(identity + ":", StringComparison.Ordinal))).ToArray();
	}

	private static string DeleteRowIdentity(TableDef table, byte[] key) => table.Name + ":" + Convert.ToHexString(key);

	private static async Task AssertDeleteDelta(LightningDatabase provider, WikiPage page, ProjectionObservation previous,
		string[] before, long sequence, long receipts, long allocator)
	{
		var projection = ObserveProjection(provider, page.Id);
		var old = previous.State.State;
		var summary = projection.State.State.Summary;
		await Assert.That(summary.Deleted).IsTrue();
		await Assert.That(summary.Reference).IsEqualTo(old.Summary.Reference);
		await Assert.That(summary.NativeId).IsEqualTo(old.Summary.NativeId);
		await Assert.That(summary.Title).IsEqualTo(old.Summary.Title);
		await Assert.That(summary.Collection).IsEqualTo("game");
		await Assert.That(projection.State.Source is LocalWikiSource).IsTrue();
		await Assert.That(projection.State.State.ClassId).IsEqualTo(0UL);
		await Assert.That(projection.State.State.CollectionId).IsEqualTo(old.CollectionId);
		await Assert.That(projection.Facts.SchemaVersion).IsEqualTo(AtomicWikiLayout.Schema.Version);
		await Assert.That(projection.Facts.Facts.Length).IsEqualTo(0);
		await Assert.That(summary.StateVersion).IsEqualTo(old.Summary.StateVersion + 1);
		await Assert.That(projection.State.State.CommitSequence).IsEqualTo(sequence + 1);
		await Assert.That(ObserveSequence(provider)).IsEqualTo(sequence + 1);
		await Assert.That(ObserveMutationCount(provider)).IsEqualTo(receipts + 1);
		await Assert.That(ObserveWikiCounter(provider)).IsEqualTo(allocator);
		// The receipt records the original canonical target and positive source body revision.
		await AssertLatestReceipt(provider, page, WikiLegacyMutationKind.Delete, old.Summary.StateVersion + 1,
			sequence + 1, new string?[] { page.Id, string.Empty });

		var key = Keys.Dbref(long.Parse(page.Id.AsSpan(10), CultureInfo.InvariantCulture));
		var historyPrefix = Keys.Concat(Keys.Str(page.Id), Keys.Sep);
		var remove = new HashSet<string>(StringComparer.Ordinal)
		{
			DeleteRowIdentity(Tables.WikiPage, key),
			DeleteRowIdentity(Tables.WikiSlug, Keys.Lower(page.Namespace + ":" + page.Slug)),
			DeleteRowIdentity(Tables.WikiRequirement, Keys.Composite("page", page.Id)),
			DeleteRowIdentity(Tables.WikiRecent, Keys.Concat(Keys.Dbref(page.UpdatedAt.UtcTicks), key)),
			DeleteRowIdentity(Tables.WikiByNamespace, Keys.Concat(Encoding.BigEndianUnicode.GetBytes(page.Namespace),
				new byte[] { 0, 0 }, Encoding.BigEndianUnicode.GetBytes(page.Slug), new byte[] { 0, 0 }, key))
		};
		foreach (var category in page.Categories.DistinctBy(value => value.ToUpperInvariant()))
			remove.Add(DeleteRowIdentity(Tables.WikiByCategory, Keys.Concat(Keys.Upper(category), Keys.Sep,
				Encoding.BigEndianUnicode.GetBytes(page.Title), new byte[] { 0, 0 }, key)));
		var revisionRows = before.Where(row => row.StartsWith(Tables.WikiRev.Name + ":" + Convert.ToHexString(historyPrefix), StringComparison.Ordinal)).ToArray();
		await Assert.That(revisionRows.Length).IsEqualTo(page.RevisionNumber);
		foreach (var row in revisionRows) remove.Add(row[..row.LastIndexOf(':')]);
		var summaryIdentity = DeleteRowIdentity(AtomicWikiLayout.Summaries, WikiSemanticKeys.Subject(summary.NativeId));
		var sequenceIdentity = DeleteRowIdentity(AtomicWikiLayout.Metadata, new byte[] { 2 });
		remove.Add(summaryIdentity);
		remove.Add(sequenceIdentity);
		var expected = before.Where(row => !remove.Any(identity => row.StartsWith(identity + ":", StringComparison.Ordinal))).ToList();
		var expectedState = previous.State with
		{
			State = old with
			{
				Summary = old.Summary with { Deleted = true, StateVersion = old.Summary.StateVersion + 1 },
				CommitSequence = sequence + 1
			}
		};
		expected.Add(summaryIdentity + ":" + Convert.ToHexString(WikiSemanticStateCodec.ProjectionState(expectedState)));
		var sequenceBytes = new byte[8];
		BinaryPrimitives.WriteUInt64BigEndian(sequenceBytes, checked((ulong)(sequence + 1)));
		expected.Add(sequenceIdentity + ":" + Convert.ToHexString(sequenceBytes));
		var after = Task3Snapshot(provider);
		var addedReceipt = after.Except(before).Single(row => row.StartsWith(AtomicWikiLayout.Mutations.Name + ":", StringComparison.Ordinal));
		expected.Add(addedReceipt);
		// Exact row bytes for every observed table: registry, zero facts, neighbors, pins, other rules,
		// activation identities, old receipts and allocator all remain unchanged.
		await Assert.That(after).IsEquivalentTo(expected);
		await Assert.That(DeleteCertificateSnapshot(provider).Length).IsEqualTo(3);
		await Assert.That(Encoding.UTF8.GetString(projection.SummaryBytes)).DoesNotContain(page.MarkdownSource);
	}
}
