using Microsoft.Extensions.Logging.Abstractions;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Plugins.Storage.Lightning;

namespace SharpMUSH.Tests.Database.Lightning;

/// <summary>
/// Compact-copy replacement at startup (#1465): the only way the world's file gives back what deleted
/// records freed. It keeps every record, keeps the original until an operator deletes it, never deletes
/// an earlier original on its own, and recovers from a crash at any step without losing the world.
/// </summary>
public class CompactionTests
{
	private static string TempPath() => Path.Join(Path.GetTempPath(), "sharpmush-lmdb-" + Guid.NewGuid().ToString("N"));

	private static LightningStoreOptions Options(string path) => new() { Path = path, MapSize = 256L << 20 };

	private static void Delete(params string[] paths)
	{
		foreach (var path in paths)
		{
			try
			{
				if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
			}
			catch (IOException)
			{
				// Best-effort: a lingering mdb.lck can outlive the writer thread's join.
			}
		}
	}

	/// <summary>A world that grew to hold many large records and then lost most of them.</summary>
	private static async Task<long> WorldWithFreedSpaceAsync(string path)
	{
		using var store = new LightningStore(Options(path));
		var value = new byte[8000];
		Random.Shared.NextBytes(value);
		await store.WriteAsync(tx =>
		{
			for (var i = 0; i < 2000; i++) tx.Put(Tables.Meta, Keys.Str($"bulk-{i:D5}"), value);
		});
		await store.WriteAsync(tx =>
		{
			for (var i = 0; i < 1990; i++) tx.Delete(Tables.Meta, Keys.Str($"bulk-{i:D5}"));
		});
		return store.Count(Tables.Meta);
	}

	[Test]
	public async Task CompactionShrinksTheFileAndKeepsEveryRecord()
	{
		var path = TempPath();
		try
		{
			var records = await WorldWithFreedSpaceAsync(path);
			var before = new FileInfo(Path.Join(path, "data.mdb")).Length;

			var compacted = LightningCompaction.CompactInPlace(Options(path), NullLogger.Instance).Expect<LightningCompaction.Compacted>();

			await Assert.That(compacted.BytesBefore).IsEqualTo(before);
			await Assert.That(compacted.BytesAfter).IsLessThan(before / 4);
			await Assert.That(new FileInfo(Path.Join(path, "data.mdb")).Length).IsEqualTo(compacted.BytesAfter);
			await Assert.That(File.Exists(Path.Join(path + LightningCompaction.OriginalSuffix, "data.mdb"))).IsTrue()
				.Because("the original stays until an operator deletes it");
			await Assert.That(Directory.Exists(path + LightningCompaction.CompactingSuffix)).IsFalse();

			using var reopened = new LightningStore(Options(path));
			await Assert.That(reopened.Count(Tables.Meta)).IsEqualTo(records);
			await Assert.That(reopened.Read(tx => tx.TryGet(Tables.Meta, Keys.Str("bulk-01999"), out var v) ? v.Length : 0))
				.IsEqualTo(8000);
		}
		finally
		{
			Delete(path, path + LightningCompaction.OriginalSuffix);
		}
	}

	/// <summary>An earlier original is the operator's to delete; a second compaction waits for them.</summary>
	[Test]
	public async Task AnEarlierOriginalIsNeverDeletedAutomatically()
	{
		var path = TempPath();
		var original = path + LightningCompaction.OriginalSuffix;
		try
		{
			await WorldWithFreedSpaceAsync(path);
			Directory.CreateDirectory(original);
			await File.WriteAllTextAsync(Path.Join(original, "marker"), "keep me");
			var before = new FileInfo(Path.Join(path, "data.mdb")).Length;

			var refused = LightningCompaction.CompactInPlace(Options(path), NullLogger.Instance).Expect<Error<string>>();

			await Assert.That(refused.Value).Contains(LightningCompaction.OriginalSuffix);
			await Assert.That(File.Exists(Path.Join(original, "marker"))).IsTrue();
			await Assert.That(new FileInfo(Path.Join(path, "data.mdb")).Length).IsEqualTo(before);
		}
		finally
		{
			Delete(path, original);
		}
	}

	[Test]
	public async Task TooLittleDiskLeavesTheWorldAlone()
	{
		var path = TempPath();
		try
		{
			await WorldWithFreedSpaceAsync(path);
			var before = new FileInfo(Path.Join(path, "data.mdb")).Length;

			var refused = LightningCompaction.CompactInPlace(Options(path), NullLogger.Instance, _ => 1024)
				.Expect<Error<string>>();

			await Assert.That(refused.Value).Contains("not enough disk");
			await Assert.That(new FileInfo(Path.Join(path, "data.mdb")).Length).IsEqualTo(before);
			await Assert.That(Directory.Exists(path + LightningCompaction.CompactingSuffix)).IsFalse();
		}
		finally
		{
			Delete(path);
		}
	}

	/// <summary>
	/// A crash between the two renames leaves no world and the original beside it: the next start puts the
	/// original back rather than trusting a copy it cannot vouch for. A crash mid-copy leaves the world
	/// untouched and a partial copy, which is deleted.
	/// </summary>
	[Test]
	public async Task RecoveryRestoresTheOriginalAfterACrashMidSwap()
	{
		var path = TempPath();
		var original = path + LightningCompaction.OriginalSuffix;
		var copy = path + LightningCompaction.CompactingSuffix;
		try
		{
			var records = await WorldWithFreedSpaceAsync(path);
			Directory.Move(path, original);
			Directory.CreateDirectory(copy);
			await File.WriteAllTextAsync(Path.Join(copy, "data.mdb"), "half a copy");

			LightningCompaction.Recover(path, NullLogger.Instance);

			await Assert.That(Directory.Exists(original)).IsFalse();
			await Assert.That(Directory.Exists(copy)).IsFalse();
			using var restored = new LightningStore(Options(path));
			await Assert.That(restored.Count(Tables.Meta)).IsEqualTo(records);
		}
		finally
		{
			Delete(path, original, copy);
		}
	}

	[Test]
	public async Task RecoveryDoesNothingWhenNothingWasInterrupted()
	{
		var path = TempPath();
		try
		{
			await WorldWithFreedSpaceAsync(path);
			var before = Directory.GetFileSystemEntries(Path.GetDirectoryName(path)!, Path.GetFileName(path) + "*");

			LightningCompaction.Recover(path, NullLogger.Instance);

			await Assert.That(Directory.GetFileSystemEntries(Path.GetDirectoryName(path)!, Path.GetFileName(path) + "*"))
				.IsEquivalentTo(before);
		}
		finally
		{
			Delete(path);
		}
	}
}
