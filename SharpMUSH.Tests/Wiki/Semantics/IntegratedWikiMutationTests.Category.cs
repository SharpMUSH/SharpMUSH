using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Database.Lightning.Migration;
using SharpMUSH.Database.Lightning.Semantics;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Plugins.Storage.Lightning;

namespace SharpMUSH.Tests.Wiki.Semantics;

// Intentionally shares the existing approved host, capability and bounded native observations.
// This file must be compiled alongside the unchanged IntegratedWikiMutationTests partials.
public partial class IntegratedWikiMutationTests
{
	[Test]
	[Arguments(0)]
	[Arguments(1)]
	[Arguments(2)]
	[Arguments(3)]
	[Arguments(4)]
	public async Task CategoryAtomicPreparedWritersUseV1AndReplayWithoutChangingAnyRow(int operation)
	{
		await using var host = NewHost();
		await host.OpenAsync();
		var provider = host.Services.GetRequiredService<LightningDatabase>();
		await CategoryAuthority(provider, 3);
		await host.ActivateAtomicWikiProjectionAsync();
		var page = await CategorySeed(host);
		PreparedLegacyWikiMutation request;
		IntegratedWikiCommitResult result;
		using (var admission = Task3Owner(host).EnterMutation(SemanticFixtureMutation.LegacyWiki))
		using (var scope = new LegacyWikiPreparationScope(admission, CancellationToken.None))
		{
			request = CategoryRequest(scope, operation, page);
			result = await provider.CommitLegacyFixtureAsync(request, CancellationToken.None);
		}
		var receipt = ReadTask3Receipt(provider, request.MutationId);
		if (operation == 4)
		{
			_ = result.Expect<IntegratedWikiDeleted>();
			await Assert.That(CategoryRows(provider, page.Id).Length).IsEqualTo(0);
			await Assert.That(CategoryIndexRows(provider, Tables.WikiRecent, page.Id).Length).IsEqualTo(0);
			await Assert.That(CategoryIndexRows(provider, Tables.WikiByNamespace, page.Id).Length).IsEqualTo(0);
		}
		else
		{
			page = result.Expect<IntegratedWikiCommitted>().Page;
			await CategoryAssertIndexValues(provider, page);
		}
		var before = Task3Snapshot(provider);
		for (var reopen = 0; reopen < 2; reopen++)
		{
			if (reopen == 1)
			{
				await host.CloseAsync();
				await host.OpenAsync();
				provider = host.Services.GetRequiredService<LightningDatabase>();
				await Assert.That(provider.AtomicWikiPhase).IsEqualTo(SemanticFixturePhase.Active);
				await Assert.That(Task3Snapshot(provider)).IsEquivalentTo(before);
			}
			var replay = (await RestoreTask3Request(host, request, request.MutationId)).Expect<IntegratedWikiReplayed>();
			await Assert.That(replay.Receipt).IsEqualTo(receipt);
			await Assert.That(Task3Snapshot(provider)).IsEquivalentTo(before);
			if (operation != 4) await CategoryAssertIndexValues(provider, page);
		}
	}

	[Test]
	[Arguments(0, 0)]
	[Arguments(0, 1)]
	[Arguments(0, 2)]
	[Arguments(1, 0)]
	[Arguments(1, 1)]
	[Arguments(1, 2)]
	[Arguments(2, 0)]
	[Arguments(2, 1)]
	[Arguments(2, 2)]
	[Arguments(3, 0)]
	[Arguments(3, 1)]
	[Arguments(3, 2)]
	[Arguments(4, 0)]
	[Arguments(4, 1)]
	[Arguments(4, 2)]
	public async Task CategoryAtomicAuthorityRejectsFreshAndReceiptReplayBeforePlanning(int operation, int authority)
	{
		await using var host = NewHost();
		await host.OpenAsync();
		var provider = host.Services.GetRequiredService<LightningDatabase>();
		await CategoryAuthority(provider, 3);
		await host.ActivateAtomicWikiProjectionAsync();
		var page = await CategorySeed(host);
		PreparedLegacyWikiMutation original;
		using (var admission = Task3Owner(host).EnterMutation(SemanticFixtureMutation.LegacyWiki))
		using (var scope = new LegacyWikiPreparationScope(admission, CancellationToken.None))
		{
			original = CategoryRequest(scope, operation, page);
			_ = await provider.CommitLegacyFixtureAsync(original, CancellationToken.None);
		}
		await CategoryAuthority(provider, authority);
		var before = Task3Snapshot(provider);
		var applied = 0;
		provider.AtomicLegacyPutApplied = _ => applied++;
		provider.AtomicLegacyDeleteApplied = _ => applied++;
		try
		{
			for (var replay = 0; replay < 2; replay++)
			{
				using var admission = Task3Owner(host).EnterMutation(SemanticFixtureMutation.LegacyWiki);
				using var scope = new LegacyWikiPreparationScope(admission, CancellationToken.None);
				var request = replay == 0 ? CategoryRequest(scope, operation, page)
					: PreparedLegacyWikiMutation.RestoreForReplay(scope, original, original.MutationId);
				// No spare working memory for CreateLegacyWork: authority must be checked first.
				// 1024 bytes remain for the two tiny capped authority rows and their owned read leases.
				scope.Lifetime.ReserveAdditional(4 * 1024 * 1024 - scope.Budget.WorkingBytes - 1024);
				await CategoryAssertAuthorityFailure(async () => { await provider.CommitLegacyFixtureAsync(request, CancellationToken.None); }, authority);
				await Assert.That(applied).IsEqualTo(0);
				await Assert.That(Task3Snapshot(provider)).IsEquivalentTo(before);
			}
		}
		finally { provider.AtomicLegacyPutApplied = null; provider.AtomicLegacyDeleteApplied = null; }
		await Assert.That(provider.AtomicWikiPhase).IsEqualTo(SemanticFixturePhase.Active);
	}

	[Test]
	[Arguments(0)]
	[Arguments(1)]
	[Arguments(2)]
	public async Task CategoryActivationRefusesAuthorityBeforeFingerprintOrLayout(int authority)
	{
		await using var host = NewHost();
		await host.OpenAsync();
		var provider = host.Services.GetRequiredService<LightningDatabase>();
		await CategoryAuthority(provider, authority);
		var before = WikiFingerprint(provider, false);
		var hooks = 0;
		provider.AtomicActivationHook = _ => { hooks++; return Task.CompletedTask; };
		try
		{
			// BeginActivation would reject this genuine in-flight inactive lease as Unavailable.
			// Getting the category authority exception proves readiness ran before that phase change.
			using (var inactive = Task3Owner(host).EnterMutation(SemanticFixtureMutation.Unsupported))
			{
				await CategoryAssertAuthorityFailure(() => host.ActivateAtomicWikiProjectionAsync(), authority);
				await Assert.That(provider.AtomicWikiLegacyMutationsInFlight).IsEqualTo(1);
			}
			await Assert.That(hooks).IsEqualTo(0);
			await Assert.That(provider.AtomicWikiPreflightRead is null).IsTrue();
			await Assert.That(provider.AtomicWikiFinalValidationRead is null).IsTrue();
			await Assert.That(provider.AtomicWikiPhase).IsEqualTo(SemanticFixturePhase.Inactive);
			await Assert.That(WikiFingerprint(provider, false)).IsEqualTo(before);
			using var stillInactive = Task3Owner(host).EnterMutation(SemanticFixtureMutation.Unsupported);
			await Assert.That(stillInactive.Phase).IsEqualTo(SemanticFixturePhase.Inactive);
		}
		finally { provider.AtomicActivationHook = null; }
		await host.CloseAsync();
		await AssertNoSemanticTables(host.WorldPath);
	}

	[Test]
	[Arguments(0)]
	[Arguments(1)]
	public async Task CategoryActiveStartupCannotResumeAnAbsentOrIncompleteMigration(int authority)
	{
		await using var host = NewHost();
		await host.OpenAsync();
		var provider = host.Services.GetRequiredService<LightningDatabase>();
		await CategoryAuthority(provider, 3);
		await host.ActivateAtomicWikiProjectionAsync();
		// A real active lineage cannot acquire the Unsupported lease needed for migration.
		await AssertFixtureFailure(() =>
		{
			using var forbidden = Task3Owner(host).EnterMutation(SemanticFixtureMutation.Unsupported);
			return Task.CompletedTask;
		}, WikiFixtureMutationFailure.UnsupportedOperation);
		await CategoryAuthority(provider, authority);
		await host.CloseAsync();
		var before = SHA256.HashData(File.ReadAllBytes(Path.Join(host.WorldPath, "data.mdb")));
		Exception? failure = null;
		try { await host.OpenAsync(); }
		catch (Exception error) { failure = error; }
		await Assert.That(CategoryStartupRefusedUnsupportedMutation(failure)).IsTrue();
		await host.CloseAsync();
		await Assert.That(SHA256.HashData(File.ReadAllBytes(Path.Join(host.WorldPath, "data.mdb")))).IsEquivalentTo(before);
	}

	[Test]
	public async Task CategoryAtomicSharedReadBudgetFailureKeepsSourceProjectionAndReceiptUnchanged()
	{
		await using var host = NewHost();
		await host.OpenAsync();
		var provider = host.Services.GetRequiredService<LightningDatabase>();
		await CategoryAuthority(provider, 3);
		await host.ActivateAtomicWikiProjectionAsync();
		var page = await CategorySeed(host);
		var before = Task3Snapshot(provider);
		using var admission = Task3Owner(host).EnterMutation(SemanticFixtureMutation.LegacyWiki);
		using var scope = new LegacyWikiPreparationScope(admission, CancellationToken.None);
		var request = CategoryRequest(scope, 3, page);
		// Spend the existing operation budget; do not replace it or raise any production quota.
		scope.Budget.ChargeBytes(128 * 1024);
		await AssertFixtureFailure(async () => { await provider.CommitLegacyFixtureAsync(request, CancellationToken.None); }, WikiFixtureMutationFailure.WorkLimit);
		await Assert.That(Task3Snapshot(provider)).IsEquivalentTo(before);
		scope.Dispose();
		admission.Dispose();
		await Assert.That(scope.Budget.WorkingBytes).IsEqualTo(0L);
		await host.CloseAsync();
		await host.OpenAsync();
		await Assert.That(Task3Snapshot(host.Services.GetRequiredService<LightningDatabase>())).IsEquivalentTo(before);
	}

	[Test]
	public async Task CategoryAtomicCancellationAfterActualCategoryPutAbortsSourceProjectionAndReceipt()
	{
		await using var host = NewHost();
		await host.OpenAsync();
		var provider = host.Services.GetRequiredService<LightningDatabase>();
		await CategoryAuthority(provider, 3);
		await host.ActivateAtomicWikiProjectionAsync();
		var page = await CategorySeed(host);
		var before = Task3Snapshot(provider);
		using var cancellation = new CancellationTokenSource();
		using var admission = Task3Owner(host).EnterMutation(SemanticFixtureMutation.LegacyWiki);
		using var scope = new LegacyWikiPreparationScope(admission, cancellation.Token);
		var request = CategoryRequest(scope, 3, page);
		var reached = false;
		provider.AtomicLegacyPutApplied = table =>
		{
			if (table.Name != Tables.WikiByCategory.Name) return;
			reached = true;
			cancellation.Cancel();
		};
		try
		{
			await Assert.That(async () => { await provider.CommitLegacyFixtureAsync(request, cancellation.Token); }).Throws<OperationCanceledException>();
			await Assert.That(reached).IsTrue();
			await Assert.That(Task3Snapshot(provider)).IsEquivalentTo(before);
		}
		finally { provider.AtomicLegacyPutApplied = null; }
		scope.Dispose();
		admission.Dispose();
		await Assert.That(scope.Budget.WorkingBytes).IsEqualTo(0L);
		await host.CloseAsync();
		await host.OpenAsync();
		await Assert.That(Task3Snapshot(host.Services.GetRequiredService<LightningDatabase>())).IsEquivalentTo(before);
	}

	[Test]
	[Arguments(0, 0)]
	[Arguments(0, 1)]
	[Arguments(0, 2)]
	[Arguments(1, 0)]
	[Arguments(1, 1)]
	[Arguments(1, 2)]
	public async Task CategoryIncompleteBatchRefusesActiveSameOwnerOrMissingAdmission(int authority, int leaseKind)
	{
		await using var host = NewHost();
		await host.OpenAsync();
		var provider = host.Services.GetRequiredService<LightningDatabase>();
		await CategoryAuthority(provider, 3);
		await host.ActivateAtomicWikiProjectionAsync();
		_ = await CategorySeed(host);
		await CategoryAuthority(provider, authority);
		var expected = authority == 0 ? Array.Empty<byte>() : CategoryCheckpoint(false, provider.Store.MaxKeySize);
		var epoch = provider.Store.Epoch;
		var before = Task3Snapshot(provider);
		var options = WikiCategoryMigrationOptions.Parse(_ => null);
		var budget = options.CreateBudget(CancellationToken.None);
		// Existing approved fixture owner only: the lease records phase/owner, not its operation.
		// LegacyWiki and Pin are real permissible Active admissions, but neither admits migration.
		using var admission = leaseKind == 2 ? null : Task3Owner(host).EnterMutation(
			leaseKind == 0 ? SemanticFixtureMutation.LegacyWiki : SemanticFixtureMutation.Pin);
		if (admission is not null) await Assert.That(admission.Phase).IsEqualTo(SemanticFixturePhase.Active);
		NativeReadValue? candidate = null;
		WikiFixtureMutationFailure? reason = null;
		try
		{
			await provider.Store.WriteAsync(tx =>
			{
				candidate = provider.ApplyWikiCategoryMigrationBatch(tx, options, authority != 0,
					expected, epoch, admission, budget);
			});
		}
		catch (WikiFixtureMutationException error) { reason = error.Reason; }
		finally { candidate?.Dispose(); }
		await Assert.That(reason is WikiFixtureMutationFailure.UnsupportedOperation or WikiFixtureMutationFailure.Unavailable).IsTrue();
		await Assert.That(Task3Snapshot(provider)).IsEquivalentTo(before);
		await Assert.That(provider.AtomicWikiPhase).IsEqualTo(SemanticFixturePhase.Active);
		await Assert.That(provider.Store.Epoch).IsEqualTo(epoch);
		await Assert.That(budget.WorkingBytes).IsEqualTo(0L);
	}

	[Test]
	public async Task CategoryActivationMapsOversizedSharedInspectionToWorkLimitBeforeBeginActivation()
	{
		await using var host = NewHost();
		await host.OpenAsync();
		var provider = host.Services.GetRequiredService<LightningDatabase>();
		// Exactly the private state VALUE ceiling: the key charge makes the shared 128 KiB
		// activation inspection budget fail before copying or structurally decoding this payload.
		var state = new byte[128 * 1024];
		await provider.Store.WriteAsync(tx => tx.Put(Tables.Meta, WikiCategoryCheckpointCodec.StateKey, state));
		var before = CategoryLargeStateActivationSnapshot(provider);
		var hooks = 0;
		provider.AtomicActivationHook = _ => { hooks++; return Task.CompletedTask; };
		try
		{
			// BeginActivation would instead reject this genuine inactive admission as Unavailable.
			using (var inactive = Task3Owner(host).EnterMutation(SemanticFixtureMutation.Unsupported))
			{
				await AssertFixtureFailure(() => host.ActivateAtomicWikiProjectionAsync(), WikiFixtureMutationFailure.WorkLimit);
				await Assert.That(provider.AtomicWikiLegacyMutationsInFlight).IsEqualTo(1);
			}
			await Assert.That(hooks).IsEqualTo(0);
			await Assert.That(provider.AtomicWikiPreflightRead is null).IsTrue();
			await Assert.That(provider.AtomicWikiFinalValidationRead is null).IsTrue();
			await Assert.That(provider.AtomicWikiPhase).IsEqualTo(SemanticFixturePhase.Inactive);
			await Assert.That(CategoryLargeStateActivationSnapshot(provider)).IsEquivalentTo(before);
		}
		finally { provider.AtomicActivationHook = null; }
		await host.CloseAsync();
		await AssertNoSemanticTables(host.WorldPath);
	}

	// Public bounded reads impose a COMBINED record cap and cannot observe this deliberately
	// legal private 128 KiB state value. Admit that one known key through its real capped reader;
	// split the ordinary Meta scan around it, retaining all other legacy rows in the fingerprint.
	private static string[] CategoryLargeStateActivationSnapshot(LightningDatabase provider)
		=> provider.Store.Read(tx =>
		{
			var budget = new NativeReadBudget(4096, 1024 * 1024, 16 * 1024 * 1024, CancellationToken.None);
			var result = new List<string>();
			if (((IWikiCategoryMigrationReadTx)tx).ReadWikiCategoryState(budget) is not NativeReadValue state)
				throw new InvalidOperationException("Missing oversized owned fixture authority.");
			using (state) result.Add(Tables.Meta.Name + ":state:" + Convert.ToHexString(state.Value));
			var stateKey = WikiCategoryCheckpointCodec.StateKey.ToArray();
			foreach (var table in new[] { Tables.Meta, Tables.WikiPage, Tables.WikiSlug, Tables.WikiRev, Tables.WikiRecent,
				Tables.WikiByNamespace, Tables.WikiByCategory, Tables.WikiRequirement, Tables.WikiPin, Tables.WikiTr, Tables.PkgWiki })
			{
				var sections = table.Name == Tables.Meta.Name ? 2 : 1;
				for (var section = 0; section < sections; section++)
				{
					using var rows = ((IBoundedReadTx)tx).ReadRange(table, [], [],
						table.Name == Tables.Meta.Name && section == 0 ? stateKey : [], section == 1 ? stateKey : [], budget);
					if (!rows.Exhausted) throw new InvalidOperationException("Large-state fixture observation exceeded its bound.");
					foreach (var row in rows.Entries)
						result.Add(table.Name + ":" + Convert.ToHexString(row.Key) + ":" + Convert.ToHexString(row.Value));
				}
			}
			return result.ToArray();
		});

	private static async Task<WikiPage> CategorySeed(SemanticWikiPrototypeHost host)
	{
		var provider = host.Services.GetRequiredService<LightningDatabase>();
		var home = (await provider.GetPageBySlugAsync("main", "home")).Expect<WikiPage>();
		using var admission = Task3Owner(host).EnterMutation(SemanticFixtureMutation.LegacyWiki);
		using var scope = new LegacyWikiPreparationScope(admission, CancellationToken.None);
		var page = home with
		{
			Id = string.Empty,
			Slug = "category-atomic-seed",
			Title = "Category\0seed 🚀",
			MarkdownSource = "category seed",
			RenderedHtml = "category seed",
			PlainText = "category seed",
			AuthorDbref = "#123\0é🚀",
			LastEditorDbref = "#123\0é🚀",
			Categories = ["alpha\0tail", "βeta"],
			Published = false,
			SourceLocale = "en"
		};
		return (await provider.CommitLegacyFixtureAsync(PreparedLegacyWikiMutation.Create.Prepare(scope, page, null), CancellationToken.None))
			.Expect<IntegratedWikiCommitted>().Page;
	}

	private static PreparedLegacyWikiMutation CategoryRequest(LegacyWikiPreparationScope scope, int operation, WikiPage page)
		=> operation switch
		{
			0 => PreparedLegacyWikiMutation.Create.Prepare(scope, page with { Slug = "category-atomic-created", Title = "Created\0🚀" }, "created"),
			1 => PreparedLegacyWikiMutation.Body.Prepare(scope, page.Id, new("new body", "new HTML", "new plain"), "#1", "body", page.UpdatedAt.AddSeconds(1)),
			2 => PreparedLegacyWikiMutation.Title.Prepare(scope, page.Id, "Moved\0🚀"),
			3 => PreparedLegacyWikiMutation.Metadata.Prepare(scope, page.Id, ["replacement\0tail", "Ω"], true),
			4 => PreparedLegacyWikiMutation.Delete.Prepare(scope, page.Id),
			_ => throw new ArgumentOutOfRangeException(nameof(operation))
		};

	private static async Task CategoryAssertAuthorityFailure(Func<Task> operation, int authority)
	{
		if (authority == 2) await Assert.That(operation).Throws<InvalidDataException>();
		else await Assert.That(operation).Throws<InvalidOperationException>();
	}

	private static bool CategoryStartupRefusedUnsupportedMutation(Exception? error)
	{
		if (error is WikiFixtureMutationException { Reason: WikiFixtureMutationFailure.UnsupportedOperation }) return true;
		if (error is AggregateException aggregate && aggregate.InnerExceptions.Any(CategoryStartupRefusedUnsupportedMutation)) return true;
		return error?.InnerException is { } inner && CategoryStartupRefusedUnsupportedMutation(inner);
	}

	// Owned native setup only. No consumer permit, provider readiness flag or capability issuance.
	// 0 = absent, 1 = valid Clearing, 2 = malformed state, 3 = valid Complete pair.
	private static async Task CategoryAuthority(LightningDatabase provider, int authority)
	{
		var state = CategoryCheckpoint(authority == 3, provider.Store.MaxKeySize);
		await provider.Store.WriteAsync(tx =>
		{
			tx.Delete(Tables.Meta, WikiCategoryCheckpointCodec.StateKey);
			tx.Delete(Tables.Meta, WikiCategoryCheckpointCodec.CompletionKey);
			if (authority == 0) return;
			tx.Put(Tables.Meta, WikiCategoryCheckpointCodec.StateKey, authority == 2 ? new byte[] { 0xff } : state);
			if (authority == 3) tx.Put(Tables.Meta, WikiCategoryCheckpointCodec.CompletionKey,
				"{\"Id\":\"0004_wiki_category_value_v1\",\"AppliedUnixMs\":17}"u8);
		});
	}

	private static byte[] CategoryCheckpoint(bool complete, int maxKeySize)
		=> WikiCategoryCheckpointCodec.Encode(new(complete ? WikiCategoryMigrationPhase.Complete : WikiCategoryMigrationPhase.Clearing,
			1, 0, 0, 0, [], [], [], [], 0, complete ? 17 : 0), maxKeySize);

	private static (byte[] Key, byte[] Value)[] CategoryRows(LightningDatabase provider, string pageId)
		=> CategoryIndexRows(provider, Tables.WikiByCategory, pageId);

	private static (byte[] Key, byte[] Value)[] CategoryIndexRows(LightningDatabase provider, TableDef table, string pageId)
		=> provider.Store.Read(tx =>
		{
			var pageKey = Keys.Dbref(long.Parse(pageId.AsSpan(10), CultureInfo.InvariantCulture));
			using var rows = ((IBoundedReadTx)tx).ReadRange(table, [], [], [], [],
				new NativeReadBudget(256, 16 * 1024, 128 * 1024, CancellationToken.None));
			if (!rows.Exhausted) throw new InvalidOperationException("Category fixture index observation exceeded its bound.");
			return rows.Entries.Where(row => row.Key.AsSpan().EndsWith(pageKey))
				.Select(row => (row.Key.ToArray(), row.Value.ToArray())).ToArray();
		});

	private static async Task CategoryAssertIndexValues(LightningDatabase provider, WikiPage page)
	{
		var rows = CategoryRows(provider, page.Id);
		await Assert.That(rows.Length).IsEqualTo(page.Categories.Count);
		var legacy = new byte[1 + Encoding.UTF8.GetByteCount(page.AuthorDbref)];
		legacy[0] = page.Published ? (byte)1 : (byte)0;
		Encoding.UTF8.GetBytes(page.AuthorDbref).CopyTo(legacy, 1);
		foreach (var category in page.Categories)
		{
			var prefix = WikiCategoryValueCodec.LabelPrefix(category);
			var key = WikiCategoryValueCodec.Key(prefix, page.Title, long.Parse(page.Id.AsSpan(10), CultureInfo.InvariantCulture));
			var row = rows.Single(entry => entry.Key.SequenceEqual(key));
			await Assert.That(row.Value.Length).IsEqualTo(legacy.Length + 8);
			await Assert.That(Convert.ToHexString(row.Value)).IsEqualTo(CategoryExpectedValue(prefix.Length - 1, legacy));
			await Assert.That(Convert.ToHexString(row.Value)).IsEqualTo(Convert.ToHexString(WikiCategoryValueCodec.Value(prefix, page.Published, page.AuthorDbref)));
			await Assert.That(CategoryDecoded(row.Key, row.Value)).IsEqualTo((Encoding.UTF8.GetString(prefix, 0, prefix.Length - 1), Convert.ToHexString(legacy), long.Parse(page.Id.AsSpan(10), CultureInfo.InvariantCulture)));
		}
		// Only category values grow: recent and namespace keep the full legacy visibility bytes.
		var other = new[] { Tables.WikiRecent, Tables.WikiByNamespace }
			.SelectMany(table => CategoryIndexRows(provider, table, page.Id)).Select(row => Convert.ToHexString(row.Value)).ToArray();
		await Assert.That(other.Length).IsEqualTo(2);
		await Assert.That(other.All(value => value == Convert.ToHexString(legacy))).IsTrue();
	}

	private static string CategoryExpectedValue(int labelBytes, byte[] visibility)
	{
		var bytes = new byte[visibility.Length + 8];
		visibility.CopyTo(bytes, 0);
		BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(visibility.Length), checked((uint)labelBytes));
		new byte[] { 0xff, 0x43, 0, 1 }.CopyTo(bytes, bytes.Length - 4);
		return Convert.ToHexString(bytes);
	}

	// Borrowed codec results stay within this synchronous helper, never an async state machine.
	private static (string Label, string Visibility, long PageKey) CategoryDecoded(byte[] key, byte[] value)
	{
		var decoded = WikiCategoryValueCodec.Decode(key, value);
		return (Encoding.UTF8.GetString(decoded.LabelBytes), Convert.ToHexString(decoded.Visibility), decoded.PageKey);
	}
}
