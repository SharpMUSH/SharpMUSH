using Microsoft.Extensions.Logging;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Database.Lightning;

/// <summary>
/// Replaces a world's file with a compacted copy of itself, before the server opens it — the one way to
/// give the disk back what deleted records freed, because LMDB reuses free pages but never shrinks its
/// file. Run at startup when <c>SHARPMUSH_LIGHTNING_COMPACT_ON_START</c> is set, so nothing else has the
/// environment open and no write can land between the copy and the swap.
/// </summary>
/// <remarks>
/// <para>The sequence, and what a crash at each point leaves:</para>
/// <list type="number">
///   <item>Copy the world, compacting, into <c>&lt;world&gt;.compacting</c>. A crash here leaves the live
///   world untouched and a partial copy, which the next start deletes.</item>
///   <item>Open the copy and check every table holds as many entries as the original.</item>
///   <item>Rename the world to <c>&lt;world&gt;.precompact</c>, then the copy to the world. A crash between
///   the two leaves no world and both directories; the next start puts the original back.</item>
/// </list>
/// <para>The original is kept at <c>&lt;world&gt;.precompact</c> until an operator deletes it, and a
/// compaction is refused while one is there: an earlier original is never deleted automatically.</para>
/// </remarks>
public static class LightningCompaction
{
	/// <summary>The suffix of the copy being written.</summary>
	public const string CompactingSuffix = ".compacting";

	/// <summary>The suffix the replaced original is kept under.</summary>
	public const string OriginalSuffix = ".precompact";

	/// <summary>A finished compaction: the file's size before and after, and where the original is.</summary>
	public readonly record struct Compacted(long BytesBefore, long BytesAfter, string OriginalPath);

	/// <summary>
	/// Finishes or undoes whatever an interrupted compaction left, so the world the server is about to open
	/// is the one it had. Safe to run on every start: with nothing left over it does nothing.
	/// </summary>
	public static void Recover(string worldPath, ILogger logger)
	{
		var world = Trim(worldPath);
		var copy = world + CompactingSuffix;
		var original = world + OriginalSuffix;

		if (!Directory.Exists(world) && Directory.Exists(original))
		{
			// Interrupted between the two renames: the original is whole, the copy may as well be, but only
			// the original is known good. Put it back.
			logger.LogWarning("A compaction of {World} was interrupted mid-swap; restoring the original from {Original}",
				world, original);
			Directory.Move(original, world);
		}

		if (Directory.Exists(copy))
		{
			logger.LogWarning("Deleting the unfinished compacted copy {Copy}; the world it was taken from is untouched", copy);
			Directory.Delete(copy, recursive: true);
		}
	}

	/// <summary>
	/// Compacts the world at <paramref name="options"/>'s path in place. Reports why it did nothing rather
	/// than throwing for the expected refusals — no world yet, an earlier original still kept, too little
	/// disk — so the server starts on the uncompacted world either way.
	/// </summary>
	public static Result<Compacted> CompactInPlace(LightningStoreOptions options, ILogger logger,
		Func<string, long>? freeBytes = null)
	{
		var world = Trim(options.Path);
		var copy = world + CompactingSuffix;
		var original = world + OriginalSuffix;
		var dataFile = Path.Join(world, "data.mdb");

		Recover(world, logger);

		if (!File.Exists(dataFile)) return new Error<string>($"there is no world at {world} to compact yet");
		if (Directory.Exists(original))
		{
			return new Error<string>(
				$"{original} still holds the world a previous compaction replaced. Delete it once you are satisfied "
				+ "with the compacted world, then compact again");
		}

		var before = new FileInfo(dataFile).Length;
		IReadOnlyDictionary<string, long> expected;
		using (var store = new LightningStore(options))
		{
			var usage = store.Usage();
			var required = WorldBackupWriter.RequiredFreeBytes(usage.CopyBytes(compact: true));
			var free = (freeBytes ?? DiskSpace.FreeBytes)(world);
			if (free >= 0 && free < required)
			{
				return new Error<string>(
					$"not enough disk to compact {world}: the copy needs about {required} bytes free and {free} are");
			}

			expected = store.EntryCounts();
			logger.LogInformation("Compacting {World} ({Bytes} bytes, {Live} of them live) into {Copy}", world, before,
				usage.UsedPages * usage.PageSize, copy);
			try
			{
				store.CopyTo(copy, compact: true);
			}
			catch (Exception ex) when (ex is LightningStoreException or IOException)
			{
				// A disk that fills mid-copy, most likely: the partial copy goes, the world was only read.
				DeleteIfPresent(copy);
				return new Error<string>($"the compacted copy could not be written ({ex.Message}); the world is unchanged");
			}
		}

		if (Verify(options with { Path = copy }, expected) is { Length: > 0 } mismatch)
		{
			DeleteIfPresent(copy);
			return new Error<string>($"the compacted copy did not match the world ({mismatch}); the world is unchanged");
		}

		Directory.Move(world, original);
		Directory.Move(copy, world);

		var after = new FileInfo(dataFile).Length;
		logger.LogWarning(
			"Compacted {World} from {Before} to {After} bytes. The original is kept at {Original}: delete it once the "
			+ "game is verified, and unset SHARPMUSH_LIGHTNING_COMPACT_ON_START", world, before, after, original);
		return new Compacted(before, after, original);
	}

	/// <summary>Opens the copy and compares every table's entry count; empty when they all match.</summary>
	private static string Verify(LightningStoreOptions copy, IReadOnlyDictionary<string, long> expected)
	{
		IReadOnlyDictionary<string, long> actual;
		try
		{
			using var store = new LightningStore(copy);
			actual = store.EntryCounts();
		}
		catch (Exception ex) when (ex is LightningStoreException or LightningDB.LightningException)
		{
			return $"it does not open: {ex.Message}";
		}

		var wrong = expected
			.Where(entry => !actual.TryGetValue(entry.Key, out var count) || count != entry.Value)
			.Select(entry => $"{(entry.Key.Length == 0 ? "catalogue" : entry.Key)}: {entry.Value} expected, "
				+ $"{actual.GetValueOrDefault(entry.Key)} found")
			.ToList();
		return string.Join("; ", wrong);
	}

	private static void DeleteIfPresent(string path)
	{
		if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
	}

	private static string Trim(string path) => path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}
