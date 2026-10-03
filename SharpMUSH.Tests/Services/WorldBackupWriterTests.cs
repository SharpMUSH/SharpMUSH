using Microsoft.Extensions.Logging.Abstractions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// Naming and retention in the shared backup writer, with the clock frozen so that "several copies in
/// one millisecond" is the setup rather than a matter of how fast the machine is.
/// </summary>
public class WorldBackupWriterTests
{
	private sealed class FrozenClock(DateTimeOffset now) : TimeProvider
	{
		public override DateTimeOffset GetUtcNow() => now;
	}

	/// <summary>Partway through a millisecond, which the name cannot carry.</summary>
	private static readonly DateTimeOffset Instant =
		new DateTimeOffset(2026, 9, 27, 22, 27, 26, 200, TimeSpan.Zero).AddTicks(5_000);

	private static string TempPath() =>
		Path.Combine(Path.GetTempPath(), "sharpmush-backup-" + Guid.NewGuid().ToString("N"));

	private static WorldBackupWriter Writer(string root, int keep, TimeProvider clock) =>
		new(new WorldBackupOptions { Root = root, Keep = keep },
			(dir, _) =>
			{
				File.WriteAllText(Path.Combine(dir, "world"), "x");
				return ValueTask.CompletedTask;
			},
			NullLogger.Instance,
			clock);

	/// <summary>
	/// Retention frees the oldest name. A copy that reused it would sort oldest and be pruned the
	/// moment it was written.
	/// </summary>
	[Test]
	public async Task CopiesInOneMillisecondKeepTheNewest()
	{
		const int keep = 2;
		var root = TempPath();
		var writer = Writer(root, keep, new FrozenClock(Instant));
		try
		{
			var names = new List<string>();
			for (var i = 0; i < keep + 2; i++)
			{
				names.Add((await writer.CreateAsync()).Expect<WorldBackup>().Name);
			}

			var kept = writer.List().Select(b => b.Name).ToArray();
			// Newest first: the last two taken, and the copy just made among them.
			await Assert.That(string.Join(" ", kept)).IsEqualTo($"{names[^1]} {names[^2]}");
		}
		finally
		{
			if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
		}
	}

	/// <summary>Ordinal order is the chronological order, however many copies share a millisecond.</summary>
	[Test]
	public async Task NamesSortInTheOrderTheyWereTaken()
	{
		var root = TempPath();
		var writer = Writer(root, keep: 20, new FrozenClock(Instant));
		try
		{
			var names = new List<string>();
			for (var i = 0; i < 12; i++)
			{
				names.Add((await writer.CreateAsync()).Expect<WorldBackup>().Name);
			}

			await Assert.That(string.Join(" ", names.Order(StringComparer.Ordinal))).IsEqualTo(string.Join(" ", names));
			await Assert.That(names.Distinct().Count()).IsEqualTo(names.Count);
		}
		finally
		{
			if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
		}
	}

	/// <summary>
	/// A run checks the disk before it writes anything (#1465): the copies already kept are pruned only once
	/// the new one is complete, so a new copy that cannot fit beside them must be refused up front — not
	/// discovered half-way through a write that fills the disk.
	/// </summary>
	[Test]
	public async Task ARunThatCannotFitIsRefusedBeforeAnythingIsWritten()
	{
		var root = TempPath();
		var payloadRan = false;
		var writer = new WorldBackupWriter(new WorldBackupOptions { Root = root, Keep = 2 },
			(dir, _) =>
			{
				payloadRan = true;
				File.WriteAllText(Path.Join(dir, "world"), "x");
				return ValueTask.CompletedTask;
			},
			NullLogger.Instance,
			new FrozenClock(Instant))
		{
			EstimateCopyBytes = () => 100L << 20,
			FreeBytes = _ => 50L << 20
		};
		try
		{
			var result = await writer.CreateAsync();

			var error = result.Expect<Error<string>>();
			await Assert.That(error.Value).Contains("not enough disk");
			await Assert.That(payloadRan).IsFalse().Because("the copy must not start");
			await Assert.That(Directory.EnumerateFileSystemEntries(root).Any()).IsFalse()
				.Because("no staging directory and no latest pointer are left behind");
		}
		finally
		{
			if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
		}
	}

	/// <summary>The margin the check asks for on top of the copy: a tenth of it, never less than 16 MiB.</summary>
	[Test]
	[Arguments(0L, 16L << 20)]
	[Arguments(100L << 20, (100L << 20) + (16L << 20))]
	[Arguments(1L << 30, (1L << 30) + ((1L << 30) / 10))]
	public async Task RequiredFreeSpaceIsTheCopyPlusAMargin(long copy, long required)
		=> await Assert.That(WorldBackupWriter.RequiredFreeBytes(copy)).IsEqualTo(required);

	/// <summary>
	/// A run that does fit goes ahead, and a disk that fills part-way leaves the earlier copies exactly as
	/// they were: the half-written copy goes, and the failure says the disk was the problem.
	/// </summary>
	[Test]
	public async Task ADiskThatFillsMidCopyLeavesTheKeptCopiesAndSaysWhy()
	{
		var root = TempPath();
		var fail = false;
		var diskFull = false;
		var writer = new WorldBackupWriter(new WorldBackupOptions { Root = root, Keep = 2 },
			(dir, _) =>
			{
				File.WriteAllText(Path.Join(dir, "world"), "x");
				if (fail)
				{
					diskFull = true;
					throw new IOException("No space left on device");
				}

				return ValueTask.CompletedTask;
			},
			NullLogger.Instance,
			new FrozenClock(Instant))
		{
			EstimateCopyBytes = () => 1L << 20,
			// Plenty at the check; the write itself is what fills the disk.
			FreeBytes = _ => diskFull ? 1L << 20 : 1L << 40
		};
		try
		{
			var first = (await writer.CreateAsync()).Expect<WorldBackup>();
			fail = true;

			var error = (await writer.CreateAsync()).Expect<Error<string>>();

			await Assert.That(error.Value).Contains("No space left on device");
			await Assert.That(error.Value).Contains("not enough disk");
			await Assert.That(writer.List().Select(b => b.Name)).IsEquivalentTo([first.Name]);
			await Assert.That(Directory.EnumerateDirectories(root, WorldBackupWriter.IncomingPrefix + "*").Any()).IsFalse();
		}
		finally
		{
			if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
		}
	}
}
