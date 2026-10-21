using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Database.Lightning.Semantics;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Plugins.Storage.Lightning;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Wiki.Semantics;

/// <summary>
/// A semantic read certifies an answer only when every section saw one settled package generation, one
/// content generation and one authorization, and the reader was the same at its end as at its start.
/// </summary>
public partial class IntegratedWikiMutationTests
{
	private static readonly WikiReader ProbeReader = WikiReader.From(["wiki.read"], "#1");

	private async Task<SemanticWikiPrototypeHost> SettledHostAsync()
	{
		var host = NewHost();
		await host.OpenAsync();
		var installer = host.Services.GetRequiredService<IPackageInstallService>();
		await Coordinated(host, _ => installer.ApplyAsync(MatrixManifest(), MatrixRequest()));
		if (!Settled(host)) throw new InvalidOperationException("The fixture did not settle.");
		return host;
	}

	private static SemanticSectionStep<string> ProbePlan(SemanticReadSection section) => section.Index switch
	{
		0 => section.TryGet(Tables.Meta, Keys.Str("semantic-read-probe"), out _)
			? SemanticSectionStep<string>.Continue : SemanticSectionStep<string>.Continue,
		_ => SemanticSectionStep<string>.Answer("answer")
	};

	private static Task<SemanticReadResult<string>> ProbeReadAsync(SemanticWikiPrototypeHost host,
		Func<int, CancellationToken, ValueTask>? between = null, CancellationToken cancellationToken = default,
		Func<CancellationToken, ValueTask<WikiReader>>? reader = null)
		=> SemanticWikiReader.ReadAsync(host.Services.GetRequiredService<LightningDatabase>(),
			reader ?? (_ => ValueTask.FromResult(ProbeReader)), 2, ProbePlan, cancellationToken, between: between);

	[Test]
	public async Task ASettledReadIsCertifiedUnderItsStamp()
	{
		await using var host = await SettledHostAsync();
		var result = await ProbeReadAsync(host);
		await Assert.That(result is SemanticReadResult<string>.Certified { Value: "answer" } certified
			&& certified.Stamp.Fixture == host.FixtureId && certified.Stamp.PackageGeneration == PackageGeneration(host)
			&& ReferenceEquals(certified.Stamp.Reader, ProbeReader)).IsTrue();
		await host.CloseAsync();
	}

	[Test]
	public async Task AnUnsettledWorldIsIncomplete()
	{
		await using var host = NewHost();
		await host.OpenAsync();
		await Assert.That(await ProbeReadAsync(host) is SemanticReadResult<string>.Incomplete).IsTrue();
		await host.CloseAsync();
	}

	[Test]
	[Arguments(0)]
	[Arguments(1)]
	[Arguments(2)]
	[Arguments(3)]
	[Arguments(4)]
	[Arguments(5)]
	public async Task AChangeBetweenSectionsIsNeverCertified(int change)
	{
		await using var host = await SettledHostAsync();
		var services = host.Services;
		var wiki = services.GetRequiredService<IWikiService>();
		var calls = 0;
		var result = await ProbeReadAsync(host, async (_, _) =>
		{
			switch (change)
			{
				case 0: await services.GetRequiredService<LightningDatabase>().Store.WriteAsync(tx => ProbePackageRow(tx, "between")); break;
				case 1: await wiki.SetCategoryPinnedAsync("lore", pinned: true); break;
				case 2: await wiki.CreateAsync("Portal Edit", "Edited between sections.", "#1"); break;
				case 3: await services.GetRequiredService<IRoleRegistryService>().SetObjectOverrideAsync(1, "wiki.read", PermissionState.Deny); break;
				case 4:
					await wiki.SetRequirementsAsync(new WikiRuleTarget(WikiRuleScope.Namespace, "main"),
						new Dictionary<WikiAction, IReadOnlyList<string>> { [WikiAction.Read] = ["lore.read"] }, "#1");
					break;
			}
		}, reader: _ => ValueTask.FromResult(change == 5 && ++calls > 1 ? WikiReader.From(["wiki.read", "wiki.drafts"], "#1") : ProbeReader));

		var expected = change is 0 or 1 or 2 ? typeof(SemanticReadResult<string>.DataChanged) : typeof(SemanticReadResult<string>.AuthorizationChanged);
		await Assert.That(result.GetType()).IsEqualTo(expected);
		await host.CloseAsync();
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task CancellationOrAnExpiredBudgetBetweenSectionsIsCancelled(bool budget)
	{
		await using var host = await SettledHostAsync();
		using var request = new CancellationTokenSource();
		using var budgetCancellation = new CancellationTokenSource();
		using var deadline = new ExecutionBudget(TimeSpan.FromMinutes(5), budgetCancellation.Token);
		SemanticReadResult<string> result;
		using (deadline.Enter())
			result = await ProbeReadAsync(host, async (_, _) =>
			{
				if (budget) await budgetCancellation.CancelAsync(); else await request.CancelAsync();
			}, request.Token);
		await Assert.That(result is SemanticReadResult<string>.Cancelled).IsTrue();
		await host.CloseAsync();
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task CancellationDuringTheFinalReaderResolutionIsCancelled(bool budget)
	{
		await using var host = await SettledHostAsync();
		using var request = new CancellationTokenSource();
		using var budgetCancellation = new CancellationTokenSource();
		using var deadline = new ExecutionBudget(TimeSpan.FromMinutes(5), budgetCancellation.Token);
		var calls = 0;
		SemanticReadResult<string> result;
		using (deadline.Enter())
			result = await ProbeReadAsync(host, cancellationToken: request.Token, reader: async _ =>
			{
				if (++calls == 2)
				{
					if (budget) await budgetCancellation.CancelAsync(); else await request.CancelAsync();
				}
				return ProbeReader;
			});
		await Assert.That(calls).IsEqualTo(2);
		await Assert.That(result is SemanticReadResult<string>.Cancelled).IsTrue();
		await host.CloseAsync();
	}

	/// <summary>Reads through the store, but fails any unbounded copy.</summary>
	private sealed class BoundedOnlyTx(ITx inner) : ITx, IBoundedReadTx
	{
		internal long? RefusedAt { get; private set; }
		public bool TryGet(TableDef table, ReadOnlySpan<byte> key, out byte[] value) => throw new InvalidOperationException("An unbounded copy.");
		public void Put(TableDef table, ReadOnlySpan<byte> key, ReadOnlySpan<byte> value) => throw new NotSupportedException();
		public bool Delete(TableDef table, ReadOnlySpan<byte> key) => throw new NotSupportedException();
		public bool Delete(TableDef table, ReadOnlySpan<byte> key, ReadOnlySpan<byte> value) => throw new NotSupportedException();
		public int DeletePrefix(TableDef table, byte[] prefix) => throw new NotSupportedException();
		public long Count(TableDef table) => inner.Count(table);
		public IEnumerable<(byte[] Key, byte[] Value)> Range(TableDef table, byte[] prefix) => throw new InvalidOperationException("An unbounded copy.");
		public IEnumerable<(byte[] Key, byte[] Value)> RangeFrom(TableDef table, byte[] prefix, byte[] afterKey, byte[]? afterValue) => throw new InvalidOperationException("An unbounded copy.");
		public IEnumerable<(byte[] Key, byte[] Value)> RangeFromKey(TableDef table, byte[] startKey) => throw new InvalidOperationException("An unbounded copy.");
		public IEnumerable<(byte[] Key, byte[] Value)> RangeReverse(TableDef table, byte[] prefix) => throw new InvalidOperationException("An unbounded copy.");
		public IEnumerable<byte[]> Dups(TableDef table, byte[] key) => throw new InvalidOperationException("An unbounded copy.");
		public Result<long> CountDups(TableDef table, ReadOnlySpan<byte> key) => inner.CountDups(table, key);
		public Found<NativeReadValue> ReadValue(TableDef table, ReadOnlySpan<byte> key, NativeReadBudget budget)
		{
			try { return ((IBoundedReadTx)inner).ReadValue(table, key, budget); }
			catch (NativeReadLimitException) { RefusedAt = budget.WorkingBytes; throw; }
		}
		public NativeReadBatch ReadRange(TableDef table, ReadOnlySpan<byte> prefix, ReadOnlySpan<byte> inclusiveStart, ReadOnlySpan<byte> exclusiveEnd, ReadOnlySpan<byte> afterKey, NativeReadBudget budget)
			=> ((IBoundedReadTx)inner).ReadRange(table, prefix, inclusiveStart, exclusiveEnd, afterKey, budget);
	}

	[Test]
	public async Task AnOversizedRowIsAWorkLimitRefusedBeforeItsCopyIsAllocated()
	{
		await using var host = await SettledHostAsync();
		var provider = host.Services.GetRequiredService<LightningDatabase>();
		await provider.Store.WriteAsync(tx => tx.Put(Tables.Meta, Keys.Str("semantic-read-oversized"), new byte[4096]));
		var limits = new SemanticReadLimits(8, 1024, 64, 4096);
		var (outcome, refusedAt) = provider.Store.Read(tx =>
		{
			var bounded = new BoundedOnlyTx(tx);
			var section = new SemanticReadSection(bounded, limits, new SemanticReadBudget(limits), ProbeReader, 0);
			try
			{
				section.TryGet(Tables.Meta, Keys.Str("semantic-read-oversized"), out _);
				return ("read", bounded.RefusedAt);
			}
			catch (SemanticReadWorkLimitException) { return ("work-limit", bounded.RefusedAt); }
		});
		await Assert.That(outcome).IsEqualTo("work-limit");
		// Refused by the native length check: no working allocation was reserved for the copy.
		await Assert.That(refusedAt).IsEqualTo(0L);
		var result = await SemanticWikiReader.ReadAsync(provider, _ => ValueTask.FromResult(ProbeReader), 1, section =>
		{
			section.TryGet(Tables.Meta, Keys.Str("semantic-read-oversized"), out _);
			return SemanticSectionStep<string>.Answer("never");
		}, CancellationToken.None, limits);
		await Assert.That(result is SemanticReadResult<string>.WorkLimit).IsTrue();
		await host.CloseAsync();
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task ASectionOrARequestOverItsLimitIsAWorkLimitWithNoAnswer(bool request)
	{
		await using var host = await SettledHostAsync();
		var limits = request ? new SemanticReadLimits(2, 1024, 3, 4096) : new SemanticReadLimits(2, 1024, 64, 4096);
		var result = await SemanticWikiReader.ReadAsync(host.Services.GetRequiredService<LightningDatabase>(),
			_ => ValueTask.FromResult(ProbeReader), 2, section =>
			{
				for (var row = 0; row < (request ? 2 : 3); row++) section.TryGet(Tables.Meta, Keys.Str("probe-" + row), out _);
				return section.Index == 1 ? SemanticSectionStep<string>.Answer("never") : SemanticSectionStep<string>.Continue;
			}, CancellationToken.None, limits);
		await Assert.That(result is SemanticReadResult<string>.WorkLimit).IsTrue();
		await host.CloseAsync();
	}

	[Test]
	public async Task ADeniedAndAMissingTargetTakeTheSamePathToTheSameAnswer()
	{
		await using var host = await SettledHostAsync();
		var provider = host.Services.GetRequiredService<LightningDatabase>();
		async Task<(SemanticReadResult<string> Result, int Sections, int Planned)> Run(int unavailableAt)
		{
			var sections = 0;
			var planned = 0;
			var result = await SemanticWikiReader.ReadAsync(provider, _ => ValueTask.FromResult(ProbeReader), 3, section =>
			{
				planned++;
				section.TryGet(Tables.Meta, Keys.Str("probe"), out _);
				return section.Index == unavailableAt ? SemanticSectionStep<string>.Unavailable : SemanticSectionStep<string>.Continue;
			}, CancellationToken.None, between: (_, _) => { sections++; return ValueTask.CompletedTask; });
			return (result, sections + 1, planned);
		}

		var denied = await Run(0);
		var missing = await Run(2);
		await Assert.That(ReferenceEquals(denied.Result, missing.Result)).IsTrue();
		await Assert.That(denied.Result is SemanticReadResult<string>.Unavailable).IsTrue();
		await Assert.That(denied.Sections).IsEqualTo(missing.Sections);
		await Assert.That(denied.Sections).IsEqualTo(3);
		await host.CloseAsync();
	}

	[Test]
	public async Task AnAdmittedReadRefusesCloseAndActivationAndClosingRefusesNewReads()
	{
		await using var host = await SettledHostAsync();
		var result = await ProbeReadAsync(host, async (_, _) =>
		{
			await Assert.That(async () => await host.CloseAsync()).Throws<SemanticFixtureOperationInProgressException>();
			await Assert.That(async () => await host.ActivateAtomicWikiProjectionAsync()).Throws<SemanticFixtureOperationInProgressException>();
		});
		await Assert.That(result is SemanticReadResult<string>.Certified).IsTrue();

		var provider = host.Services.GetRequiredService<LightningDatabase>();
		provider.PrepareSemanticFixtureClose();
		await Assert.That(async () => await ProbeReadAsync(host)).Throws<Exception>();
		await host.CloseAsync();
	}
}
