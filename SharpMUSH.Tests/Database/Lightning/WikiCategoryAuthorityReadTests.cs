using System.Text;
using LightningDB;
using SharpMUSH.Database.Lightning.Migration;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Plugins.Storage.Lightning;

namespace SharpMUSH.Tests.Database.Lightning;

public class WikiCategoryAuthorityReadTests
{
	private readonly List<string> _paths = [];

	private string NewPath()
	{
		var scratch = Environment.GetEnvironmentVariable("TMPDIR")
			?? throw new InvalidOperationException("Configure disk-backed TMPDIR before running native migration tests.");
		Directory.CreateDirectory(scratch);
		var path = Path.Combine(scratch, "sharpmush-category-authority-" + Guid.NewGuid().ToString("N"));
		_paths.Add(path);
		return path;
	}

	private LightningStore Open(string? path = null) => new(new LightningStoreOptions { Path = path ?? NewPath(), MapSize = 256L << 20 });

	[After(Test)]
	public void Cleanup()
	{
		foreach (var path in _paths.Where(Directory.Exists)) Directory.Delete(path, recursive: true);
	}

	// Independent known keys: a swapped or misspelled production key must fail these reads.
	private static byte[] Key(bool completion) => Encoding.UTF8.GetBytes(completion
		? "mig:0004_wiki_category_value_v1"
		: "core.wiki.cat.value-upgrade/state");
	private static NativeReadBudget Budget(int probes = 8, long bytes = 1L << 20, long working = 1L << 20,
		int? records = null, CancellationToken ct = default) => new(probes, bytes, working, ct, records);
	private static Found<NativeReadValue> Read(LightningStore store, bool completion, NativeReadBudget budget)
		=> store.Read(tx =>
		{
			var reader = tx as IWikiCategoryMigrationReadTx
				?? throw new InvalidOperationException("Native category migration reads are required.");
			return completion ? reader.ReadWikiCategoryCompletion(budget) : reader.ReadWikiCategoryState(budget);
		});
	// Independent allocation model: owner + reservation + aligned array (including header).
	private static long OwnedBytes(int length) => 64 + 64 + 32 + ((length + 7L) / 8 * 8);
	private static void Check(bool condition, string message)
	{
		if (!condition) throw new InvalidOperationException(message);
	}
	private static Exception? Capture(Action action)
	{
		try { action(); return null; }
		catch (Exception error) { return error; }
	}
	private static void Counters(NativeReadBudget budget, int probes, long bytes, int records, long working)
		=> Check((budget.Probes, budget.Bytes, budget.Records, budget.WorkingBytes) == (probes, bytes, records, working),
			"Authority read accounting differs from independent expected counters.");
	private static void Limit(Action action, string dimension)
		=> Check(Capture(action) is NativeReadLimitException limit && limit.Dimension == dimension,
			"Expected native read limit: " + dimension);
	private static void ReadAndDispose(LightningStore store, bool completion, NativeReadBudget budget)
	{
		if (Read(store, completion, budget) is NativeReadValue owner) owner.Dispose();
	}

	[Test]
	[Arguments(false, 131072)]
	[Arguments(true, 1024)]
	public async Task ExactValueCapExcludesKnownKeyAndCopiesRawBytes(bool completion, int length)
	{
		using var store = Open();
		var key = Key(completion);
		var source = new byte[length];
		for (var i = 0; i < source.Length; i++) source[i] = (byte)(i % 251);
		await store.WriteAsync(tx =>
		{
			tx.Put(Tables.Meta, key, source);
			tx.Put(Tables.Meta, Key(!completion), [242]);
			tx.Put(Tables.WikiPage, key, [243]);
		});
		var budget = Budget(probes: 1, bytes: key.Length + length, working: OwnedBytes(length), records: 1);
		using var owner = Read(store, completion, budget).Expect<NativeReadValue>();
		Check(owner.Value.AsSpan().SequenceEqual(source), "Known-key read changed raw bytes or selected another record.");
		Counters(budget, 1, key.Length + length, 1, OwnedBytes(length));
		owner.Dispose();
		Counters(budget, 1, key.Length + length, 1, 0);
	}

	[Test]
	[Arguments(false, 131073)]
	[Arguments(true, 1025)]
	public async Task ValueOneAboveCapIsFixedCorruptionBeforeValueAdmission(bool completion, int length)
	{
		using var store = Open();
		var key = Key(completion);
		await store.WriteAsync(tx => tx.Put(Tables.Meta, key, Enumerable.Repeat((byte)97, length).ToArray()));
		var budget = Budget(bytes: key.Length, working: 1, records: 0);
		var first = Capture(() => ReadAndDispose(store, completion, budget));
		Check(first is InvalidDataException { InnerException: null }, "Oversized authority must be fixed corruption, before configured budget limits.");
		Counters(budget, 1, key.Length, 0, 0);
		await store.WriteAsync(tx => tx.Put(Tables.Meta, key, Enumerable.Repeat((byte)122, length + 17).ToArray()));
		var secondBudget = Budget(bytes: key.Length, working: 1, records: 0);
		var second = Capture(() => ReadAndDispose(store, completion, secondBudget));
		Check(second is InvalidDataException { InnerException: null } && first!.Message == second.Message,
			"Authority corruption diagnostic depends on native contents or size.");
		Counters(secondBudget, 1, key.Length, 0, 0);
	}

	[Test]
	public async Task PublicMetaReadStillEnforcesCombined128KiBCap()
	{
		using var store = Open();
		var key = Key(false);
		await store.WriteAsync(tx => tx.Put(Tables.Meta, key, new byte[131072]));
		var budget = Budget();
		Limit(() => store.Read(tx =>
		{
			var reader = tx as IBoundedReadTx ?? throw new InvalidOperationException("Public bounded reads are required.");
			return reader.ReadValue(Tables.Meta, key, budget);
		}), "record-bytes");
		Check(budget.Probes == 1 && budget.Records == 0 && budget.WorkingBytes == 0,
			"Public combined record cap changed or admitted a copy.");
		using var owner = Read(store, false, Budget()).Expect<NativeReadValue>();
		Check(owner.Value.Length == 131072, "Private state read incorrectly inherited public combined record cap.");
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task AbsentAndPresentEmptyAuthorityRemainDistinct(bool completion)
	{
		using var store = Open();
		var key = Key(completion);
		await store.WriteAsync(tx => tx.Put(Tables.Meta, Key(!completion), [99]));
		var missing = Budget(probes: 1, bytes: 1, working: 1, records: 0);
		Check(Read(store, completion, missing) is NotFound, "Missing known key was inferred from another authority record.");
		Counters(missing, 1, 0, 0, 0);
		await store.WriteAsync(tx => tx.Put(Tables.Meta, key, []));
		var present = Budget(probes: 1, bytes: key.Length, working: OwnedBytes(0), records: 1);
		using var owner = Read(store, completion, present).Expect<NativeReadValue>();
		Check(owner.Value.Length == 0, "Present empty raw authority was hidden or interpreted by reader.");
		Counters(present, 1, key.Length, 1, OwnedBytes(0));
		owner.Dispose();
		Counters(present, 1, key.Length, 1, 0);
	}

	[Test]
	[Arguments(false, "key-bytes")]
	[Arguments(true, "key-bytes")]
	[Arguments(false, "value-bytes")]
	[Arguments(true, "value-bytes")]
	[Arguments(false, "records")]
	[Arguments(true, "records")]
	[Arguments(false, "working-bytes")]
	[Arguments(true, "working-bytes")]
	public async Task AdmissionFailuresChargeOnlyAttemptedWork(bool completion, string boundary)
	{
		using var store = Open();
		var key = Key(completion);
		await store.WriteAsync(tx => tx.Put(Tables.Meta, key, new byte[17]));
		var budget = Budget(bytes: boundary switch
		{
			"key-bytes" => key.Length - 1,
			"value-bytes" => key.Length + 16,
			_ => key.Length + 17
		}, working: boundary == "working-bytes" ? OwnedBytes(17) - 1 : OwnedBytes(17),
			records: boundary == "records" ? 0 : 1);
		Limit(() => ReadAndDispose(store, completion, budget), boundary is "key-bytes" or "value-bytes" ? "bytes" : boundary);
		Counters(budget, 1, boundary switch
		{
			"key-bytes" => 0,
			"value-bytes" => key.Length,
			_ => key.Length + 17
		}, boundary == "working-bytes" ? 1 : 0, 0);
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task ProbeAndWorkingFailuresPreserveIndependentPriorOwner(bool completion)
	{
		using var store = Open();
		var key = Key(completion);
		await store.WriteAsync(tx => tx.Put(Tables.Meta, key, [7]));
		var probes = Budget(probes: 1);
		using var first = Read(store, completion, probes).Expect<NativeReadValue>();
		Limit(() => ReadAndDispose(store, completion, probes), "probes");
		Counters(probes, 1, key.Length + 1, 1, OwnedBytes(1));
		Check(first.Value[0] == 7, "Probe rejection released prior authority owner.");
		var working = Budget(working: OwnedBytes(1));
		using var prior = Read(store, completion, working).Expect<NativeReadValue>();
		Limit(() => ReadAndDispose(store, completion, working), "working-bytes");
		Counters(working, 2, 2 * (key.Length + 1), 2, OwnedBytes(1));
		Check(prior.Value[0] == 7, "Working rejection released prior authority owner.");
		prior.Dispose();
		Counters(working, 2, 2 * (key.Length + 1), 2, 0);
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task OwnerSurvivesTransactionOverwriteAndStoreDisposal(bool completion)
	{
		var budget = Budget();
		NativeReadValue owner;
		using (var store = Open())
		{
			var key = Key(completion);
			await store.WriteAsync(tx => tx.Put(Tables.Meta, key, [0, 255, 128, 7]));
			owner = Read(store, completion, budget).Expect<NativeReadValue>();
			await store.WriteAsync(tx => tx.Put(Tables.Meta, key, [9]));
			Check(owner.Value.AsSpan().SequenceEqual(new byte[] { 0, 255, 128, 7 }), "Authority owner borrows overwritten native memory.");
		}
		using (owner)
		{
			Check(owner.Value.AsSpan().SequenceEqual(new byte[] { 0, 255, 128, 7 }), "Store disposal invalidated caller-owned bytes.");
			Counters(budget, 1, Key(completion).Length + 4, 1, OwnedBytes(4));
			owner.Dispose();
			owner.Dispose();
			Counters(budget, 1, Key(completion).Length + 4, 1, 0);
			Check(Capture(() => { _ = owner.Value; }) is ObjectDisposedException, "Disposed authority bytes remain accessible.");
		}
	}

	[Test]
	public async Task StateAndCompletionOwnersReleaseOnlyTheirOwnSharedBudgetLease()
	{
		using var store = Open();
		var stateKey = Key(false);
		var completionKey = Key(true);
		await store.WriteAsync(tx =>
		{
			tx.Put(Tables.Meta, stateKey, [0, 255, 7]);
			tx.Put(Tables.Meta, completionKey, [128, 0, 9, 4, 3, 2, 1, 255, 8]);
		});
		var bytes = stateKey.Length + 3 + completionKey.Length + 9;
		var budget = Budget(probes: 2, bytes: bytes, working: OwnedBytes(3) + OwnedBytes(9), records: 2);
		using var state = Read(store, false, budget).Expect<NativeReadValue>();
		using var completion = Read(store, true, budget).Expect<NativeReadValue>();
		Counters(budget, 2, bytes, 2, OwnedBytes(3) + OwnedBytes(9));
		Check(state.Value.AsSpan().SequenceEqual(new byte[] { 0, 255, 7 }), "Shared-budget state bytes differ.");
		state.Dispose();
		state.Dispose();
		Counters(budget, 2, bytes, 2, OwnedBytes(9));
		Check(completion.Value.AsSpan().SequenceEqual(new byte[] { 128, 0, 9, 4, 3, 2, 1, 255, 8 }),
			"State disposal invalidated independently owned completion bytes.");
		completion.Dispose();
		completion.Dispose();
		Counters(budget, 2, bytes, 2, 0);
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public void PreCancellationPreservesTokenWithoutAdmission(bool completion)
	{
		using var store = Open();
		using var cancellation = new CancellationTokenSource();
		cancellation.Cancel();
		var budget = Budget(ct: cancellation.Token);
		Check(Capture(() => ReadAndDispose(store, completion, budget)) is OperationCanceledException error
			&& error.CancellationToken == cancellation.Token, "Authority read ignored or replaced cancellation token.");
		Counters(budget, 0, 0, 0, 0);
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public void NullBudgetRejectsBeforeNativeRead(bool completion)
	{
		using var store = Open();
		Check(Capture(() => ReadAndDispose(store, completion, null!)) is ArgumentNullException, "Null authority budget accepted.");
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public void PersistedDuplicateMetaFlagsRejectBeforeNativeProbe(bool completion)
	{
		var path = NewPath();
		Directory.CreateDirectory(path);
		using (var environment = new LightningEnvironment(path) { MapSize = 256L << 20, MaxDatabases = 256 })
		{
			environment.Open();
			using var tx = environment.BeginTransaction();
			using var meta = tx.OpenDatabase(Tables.Meta.Name,
				new DatabaseConfiguration { Flags = DatabaseOpenFlags.Create | DatabaseOpenFlags.DuplicatesSort });
			var commit = tx.Commit();
			if (commit != MDBResultCode.Success) throw LightningStoreException.From(commit, "seed authority flags fixture");
		}
		using var store = Open(path);
		var budget = Budget();
		Check(Capture(() => ReadAndDispose(store, completion, budget)) is NotSupportedException,
			"Known-key authority read trusted requested plain flags instead of actual persisted flags.");
		Counters(budget, 0, 0, 0, 0);
	}
}
