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
}
