using System.Reflection;
using LightningDB;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Plugins.Storage.Lightning;

namespace SharpMUSH.Tests.Database.Lightning;

public class CursorFailureTests
{
	private readonly List<string> _paths = [];

	private string NewPath()
	{
		var scratch = Environment.GetEnvironmentVariable("TMPDIR") ?? Path.Combine(AppContext.BaseDirectory, "work");
		Directory.CreateDirectory(scratch);
		var path = Path.Combine(scratch, "sharpmush-cursor-" + Guid.NewGuid().ToString("N"));
		_paths.Add(path);
		return path;
	}

	private LightningStore Open() => new(new LightningStoreOptions { Path = NewPath(), MapSize = 256L << 20 });

	[After(Test)]
	public void Cleanup()
	{
		foreach (var path in _paths.Where(Directory.Exists)) Directory.Delete(path, recursive: true);
	}

	// A supported invalid exact key in a separate owned native environment. No corruption, pointer access,
	// mapped mutation, reset, stale handle or fault hook. Compare the provider to this observed native code.
	private MDBResultCode ObserveEmptyNativeSet()
	{
		var path = NewPath();
		Directory.CreateDirectory(path);
		using var environment = new LightningEnvironment(path) { MapSize = 16L << 20, MaxDatabases = 1 };
		environment.Open();
		using var transaction = environment.BeginTransaction();
		using var database = transaction.OpenDatabase("duplicates", new DatabaseConfiguration { Flags = DatabaseOpenFlags.Create | DatabaseOpenFlags.DuplicatesSort });
		var put = transaction.Put(database, new byte[] { 1 }, new byte[] { 2 });
		if (put != MDBResultCode.Success) throw LightningStoreException.From(put, "seed native cursor observation");
		MDBResultCode code;
		using (var cursor = transaction.CreateCursor(database)) code = cursor.Set(ReadOnlySpan<byte>.Empty);
		var commit = transaction.Commit();
		if (commit != MDBResultCode.Success) throw LightningStoreException.From(commit, "commit native cursor observation");
		Console.WriteLine($"Observed installed Linux native empty exact-key cursor.Set: {code} ({(int)code})");
		return code;
	}

	private static Exception? Capture(Action action)
	{
		try { action(); return null; }
		catch (Exception error) { return error; }
	}

	private static string Rows(IEnumerable<(byte[] Key, byte[] Value)> rows)
		=> string.Join(",", rows.Select(row => $"{Convert.ToHexString(row.Key)}:{Convert.ToHexString(row.Value)}"));

	private static ValueTask Seed(LightningStore store) => store.WriteAsync(tx =>
	{
		tx.Put(Tables.Meta, [1, 1], [11]);
		tx.Put(Tables.Meta, [1, 3], [13]);
		tx.Put(Tables.Meta, [2, 1], [21]);
		tx.Put(Tables.Meta, [255, 255], [99]);
		tx.Put(Tables.RevLocation, [1], [1]);
		tx.Put(Tables.RevLocation, [1], [3]);
		tx.Put(Tables.RevLocation, [1], [5]);
		tx.Put(Tables.RevLocation, [2], [2]);
	});

	[Test]
	public async Task NativeEmptyExactKeyIsAnErrorNotEnd()
	{
		var observed = ObserveEmptyNativeSet();
		await Assert.That(observed == MDBResultCode.Success || observed == MDBResultCode.NotFound).IsFalse();
		using var store = Open();
		await Seed(store);
		var error = Capture(() => store.Read(tx => tx.Dups(Tables.RevLocation, []).ToArray()));
		await Assert.That(error is LightningStoreException native && native.Code == observed).IsTrue();
	}

	[Test]
	public async Task CountDupsReturnsObservedNativeErrorInsteadOfZero()
	{
		var observed = ObserveEmptyNativeSet();
		await Assert.That(observed == MDBResultCode.Success || observed == MDBResultCode.NotFound).IsFalse();
		using var store = Open();
		await Seed(store);
		var result = store.Read(tx => tx.CountDups(Tables.RevLocation, []));
		await Assert.That(result is Error<string> error && error.Value.Contains(observed.ToString(), StringComparison.Ordinal)).IsTrue();
	}

	[Test]
	[Arguments("from-key")]
	[Arguments("count-range")]
	[Arguments("resume")]
	[Arguments("reverse-before")]
	public async Task EmptyExactSeekCannotBecomeEmptyOrLastFallback(string operation)
	{
		var observed = ObserveEmptyNativeSet();
		using var store = Open();
		await Seed(store);
		var error = Capture(() => store.Read(tx => operation switch
		{
			"from-key" => tx.RangeFromKey(Tables.Meta, []).Count(),
			"count-range" => tx.CountRange(Tables.Meta, [], []),
			"resume" => tx.RangeFrom(Tables.Meta, [], [], null).Count(),
			"reverse-before" => tx.RangeReverseBefore(Tables.Meta, [], []).Count(),
			_ => throw new ArgumentOutOfRangeException(nameof(operation))
		}));
		await Assert.That(error is LightningStoreException native && native.Code == observed).IsTrue();
	}

	[Test]
	[Arguments(true, 3)]
	[Arguments(false, 1)]
	public async Task CountDupsUsesPersistedFlagsInsteadOfCallerAliases(bool nativeDuplicates, int expected)
	{
		using var store = Open();
		await Seed(store);
		var table = nativeDuplicates ? Tables.RevLocation : Tables.Meta;
		var alias = TableDef.Index(table.Name, duplicates: !nativeDuplicates);
		var key = nativeDuplicates ? new byte[] { 1 } : new byte[] { 1, 1 };
		var result = store.Read(tx => tx.CountDups(alias, key));
		Console.WriteLine(result switch
		{
			long count => $"Native duplicates={nativeDuplicates}, alias duplicates={alias.Duplicates}, count={count}",
			Error<string> error => $"Native duplicates={nativeDuplicates}, alias duplicates={alias.Duplicates}, error={error.Value}"
		});
		await Assert.That(result.Expect<long>()).IsEqualTo((long)expected);
	}

	[Test]
	public async Task PrefixFootprintEmptyPrefixCountsTheWholeNativeTable()
	{
		using var store = Open();
		await Seed(store);
		var all = store.PrefixFootprint(Tables.Meta, []);
		await Assert.That(all.Entries).IsEqualTo(4L);
		await Assert.That(all.KeyBytes).IsEqualTo(8L);
	}

	[Test]
	public async Task PrefixFootprintKeepsPrefixBoundaryAndMissingResults()
	{
		using var store = Open();
		await Assert.That(store.PrefixFootprint(Tables.Meta, []).Entries).IsEqualTo(0L);
		await Seed(store);
		var matching = store.PrefixFootprint(Tables.Meta, [1]);
		await Assert.That(matching.Entries).IsEqualTo(2L);
		await Assert.That(matching.KeyBytes).IsEqualTo(4L);
		await Assert.That(store.PrefixFootprint(Tables.Meta, [1, 2]).Entries).IsEqualTo(0L);
		await Assert.That(store.PrefixFootprint(Tables.Meta, [255, 255, 255]).Entries).IsEqualTo(0L);
	}

	[Test]
	public async Task DeletePrefixDoesNotHideReadOnlyNativeDeleteFailure()
	{
		using var store = Open();
		await Seed(store);
		var observed = Capture(() => store.Read(tx => tx.Delete(Tables.Meta, new byte[] { 1, 1 })));
		if (observed is not LightningStoreException native) throw new InvalidOperationException("Native read-only delete must refuse the write.", observed);
		var deleted = -1;
		var error = Capture(() => deleted = store.Read(tx => tx.DeletePrefix(Tables.Meta, [1])));
		var remaining = store.Read(tx => tx.Count(Tables.Meta));
		Console.WriteLine($"Observed native read-only Delete: {native.Code} ({(int)native.Code}); DeletePrefix returned={deleted}, error={error?.GetType().Name ?? "none"}, remaining={remaining}");
		await Assert.That(error is LightningStoreException prefixError && prefixError.Code == native.Code).IsTrue();
		await Assert.That(remaining).IsEqualTo(4L);
	}

	[Test]
	public async Task DeletePrefixUsesNativeFlagsAndCountsOnlyRemovedRows()
	{
		using var store = Open();
		await Seed(store);
		var duplicateAlias = TableDef.Node(Tables.RevLocation.Name);
		var plainAlias = TableDef.Index(Tables.Meta.Name, duplicates: true);
		await Assert.That(await store.WriteAsync(tx => tx.DeletePrefix(duplicateAlias, [1]))).IsEqualTo(3);
		await Assert.That(await store.WriteAsync(tx => tx.DeletePrefix(plainAlias, [1]))).IsEqualTo(2);
		await Assert.That(await store.WriteAsync(tx => tx.DeletePrefix(plainAlias, [1]))).IsEqualTo(0);
		await Assert.That(store.Read(tx => Rows(tx.Range(Tables.RevLocation, [])))).IsEqualTo("02:02");
		await Assert.That(store.Read(tx => Rows(tx.Range(Tables.Meta, [])))).IsEqualTo("0201:15,FFFF:63");
	}

	[Test]
	public async Task EveryNativeStatusIsClassifiedWithoutHidingErrors()
	{
		// Classification evidence only: this does not inject native cursor iteration failures.
		var classify = typeof(LightningStore).GetMethod("FoundNative", BindingFlags.NonPublic | BindingFlags.Static)
			?? throw new InvalidOperationException("Native status classifier missing.");
		foreach (var code in Enum.GetValues<MDBResultCode>().Append((MDBResultCode)int.MinValue).Distinct())
		{
			object? result = null;
			var error = Capture(() => result = classify.Invoke(null, [code, "classification evidence"]));
			if (code == MDBResultCode.Success) await Assert.That(result is true).IsTrue();
			else if (code == MDBResultCode.NotFound) await Assert.That(result is false).IsTrue();
			else await Assert.That(error is TargetInvocationException { InnerException: LightningStoreException native } && native.Code == code).IsTrue();
		}
	}

	[Test]
	public async Task EmptyTablesAndMissingKeysAreNormalEndAcrossAllDirections()
	{
		using var store = Open();
		var rows = store.Read(tx => new[]
		{
			Rows(tx.Range(Tables.Meta, [])), Rows(tx.Range(Tables.Meta, [1])),
			Rows(tx.RangeFrom(Tables.Meta, [1], [1], null)), Rows(tx.RangeFromKey(Tables.Meta, [1])),
			Rows(tx.RangeReverse(Tables.Meta, [])), Rows(tx.RangeReverse(Tables.Meta, [255])),
			Rows(tx.RangeReverseBefore(Tables.Meta, [], [1]))
		});
		await Assert.That(rows.All(row => row.Length == 0)).IsTrue();
		await Assert.That(store.Read(tx => tx.CountRange(Tables.Meta, [], [1]))).IsEqualTo(0L);
		await Assert.That(store.Read(tx => tx.Dups(Tables.RevLocation, [1]).Count())).IsEqualTo(0);
		await Assert.That(store.Read(tx => tx.CountDups(Tables.RevLocation, [1])).Expect<long>()).IsEqualTo(0L);
	}

	[Test]
	public async Task ForwardRangesKeepPrefixInclusiveSeekAndStrictResume()
	{
		using var store = Open();
		await Seed(store);
		await Assert.That(store.Read(tx => Rows(tx.Range(Tables.Meta, [])))).IsEqualTo("0101:0B,0103:0D,0201:15,FFFF:63");
		await Assert.That(store.Read(tx => Rows(tx.Range(Tables.Meta, [1])))).IsEqualTo("0101:0B,0103:0D");
		await Assert.That(store.Read(tx => Rows(tx.Range(Tables.Meta, [1, 2])))).IsEqualTo("");
		await Assert.That(store.Read(tx => Rows(tx.RangeFromKey(Tables.Meta, [1, 2])))).IsEqualTo("0103:0D,0201:15,FFFF:63");
		await Assert.That(store.Read(tx => Rows(tx.RangeFromKey(Tables.Meta, [255, 255, 255])))).IsEqualTo("");
		await Assert.That(store.Read(tx => Rows(tx.RangeFrom(Tables.Meta, [1], [1, 1], null)))).IsEqualTo("0103:0D");
		await Assert.That(store.Read(tx => Rows(tx.RangeFrom(Tables.Meta, [1], [0], null)))).IsEqualTo("0101:0B,0103:0D");
		await Assert.That(store.Read(tx => Rows(tx.RangeFrom(Tables.Meta, [1], [1, 2], null)))).IsEqualTo("0103:0D");
		await Assert.That(store.Read(tx => tx.CountRange(Tables.Meta, [1], [1, 2]))).IsEqualTo(1L);
		await Assert.That(store.Read(tx => tx.CountRange(Tables.Meta, [1], [2]))).IsEqualTo(0L);
	}

	[Test]
	public async Task ReverseRangesKeepStrictBoundaryAndTerminalFallback()
	{
		using var store = Open();
		await Seed(store);
		await Assert.That(store.Read(tx => Rows(tx.RangeReverse(Tables.Meta, [1])))).IsEqualTo("0103:0D,0101:0B");
		await Assert.That(store.Read(tx => Rows(tx.RangeReverse(Tables.Meta, [255])))).IsEqualTo("FFFF:63");
		await Assert.That(store.Read(tx => Rows(tx.RangeReverse(Tables.Meta, [3])))).IsEqualTo("");
		await Assert.That(store.Read(tx => Rows(tx.RangeReverseBefore(Tables.Meta, [1], [1, 3])))).IsEqualTo("0101:0B");
		await Assert.That(store.Read(tx => Rows(tx.RangeReverseBefore(Tables.Meta, [1], [1, 2])))).IsEqualTo("0101:0B");
		await Assert.That(store.Read(tx => Rows(tx.RangeReverseBefore(Tables.Meta, [1], [1, 1])))).IsEqualTo("");
		await Assert.That(store.Read(tx => Rows(tx.RangeReverseBefore(Tables.Meta, [], [255, 255, 255])))).IsEqualTo("FFFF:63,0201:15,0103:0D,0101:0B");
	}

	[Test]
	public async Task DuplicateValuesAndResumedDuplicateBoundariesStayOrdered()
	{
		using var store = Open();
		await Seed(store);
		await Assert.That(store.Read(tx => Rows(tx.RangeFrom(Tables.RevLocation, [], [1], [3])))).IsEqualTo("01:05,02:02");
		await Assert.That(store.Read(tx => Rows(tx.RangeFrom(Tables.RevLocation, [], [1], [4])))).IsEqualTo("01:05,02:02");
		await Assert.That(store.Read(tx => Rows(tx.RangeFrom(Tables.RevLocation, [], [1], [5])))).IsEqualTo("02:02");
		await Assert.That(store.Read(tx => Rows(tx.RangeFrom(Tables.RevLocation, [], [2], [2])))).IsEqualTo("");
		await Assert.That(store.Read(tx => Rows(tx.RangeFrom(Tables.RevLocation, [], [1], null)))).IsEqualTo("02:02");
		await Assert.That(store.Read(tx => Rows(tx.RangeFrom(Tables.RevLocation, [], [3], null)))).IsEqualTo("");
		await Assert.That(store.Read(tx => string.Join(",", tx.Dups(Tables.RevLocation, [1]).Select(Convert.ToHexString)))).IsEqualTo("01,03,05");
		await Assert.That(store.Read(tx => tx.CountDups(Tables.RevLocation, [1])).Expect<long>()).IsEqualTo(3L);
		await Assert.That(store.Read(tx => tx.CountDups(Tables.Meta, [1, 1])).Expect<long>()).IsEqualTo(1L);
	}
}
