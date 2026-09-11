using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Services;

/// <summary>
/// Everything about taking a backup that is the same whatever the database is: writing the copy
/// aside, moving it into place only once complete, repointing <c>latest</c>, applying retention, and
/// listing what is on disk. A provider supplies only the part that differs — filling one directory
/// with its own world — as the <c>writePayload</c> delegate.
///
/// <para>Shared rather than reimplemented per provider because these are the parts that are easy to
/// get subtly wrong and expensive to get wrong quietly: a half-written copy a snapshot tool picks up,
/// or retention deleting the copy just made. One implementation, one set of tests.</para>
/// </summary>
public sealed partial class WorldBackupWriter(
	WorldBackupOptions options,
	Func<string, CancellationToken, ValueTask> writePayload,
	ILogger logger)
{
	/// <summary>Prefix for a copy still being written. Hidden, and never matched by <see cref="NameRegex"/>.</summary>
	private const string IncomingPrefix = ".incoming-";

	/// <summary>Name of the pointer at the newest copy.</summary>
	private const string LatestName = "latest";

	/// <summary>Written instead of the pointer where the platform refuses symlinks.</summary>
	private const string LatestFallbackName = "latest.txt";

	private const string StampFormat = "yyyyMMdd-HHmmss-fff";

	/// <summary>
	/// Serialises runs. The scheduled backup and a wizard typing the command can arrive together, and
	/// two copies at once would double the disk traffic for no gain — and race over retention.
	/// </summary>
	private readonly SemaphoreSlim _oneAtATime = new(1, 1);

	public string Root => options.Root;

	public int Keep => Math.Max(options.Keep, 1);

	public TimeSpan ScheduledInterval => options.Interval;

	public async ValueTask<Result<WorldBackup>> CreateAsync(CancellationToken ct = default)
	{
		await _oneAtATime.WaitAsync(ct);
		try
		{
			var staging = Path.Combine(options.Root, IncomingPrefix + Guid.NewGuid().ToString("N")[..8]);
			try
			{
				// Inside the handler: an unwritable or unreachable root is a failure to report like any
				// other, not an exception thrown out of the command a wizard just typed.
				Directory.CreateDirectory(options.Root);
				Directory.CreateDirectory(staging);
				logger.LogInformation("Writing a world backup into {Path}", staging);
				await writePayload(staging, ct);

				var final = Path.Combine(options.Root, NextName());
				Directory.Move(staging, final);
				PointLatestAt(final);
				Prune();

				var backup = Describe(new DirectoryInfo(final));
				logger.LogInformation("World backup {Name} written ({Bytes} bytes)", backup.Name, backup.SizeBytes);
				return backup;
			}
			catch (OperationCanceledException)
			{
				// Shutdown, not a failure to report: clean up and let it through.
				TryDelete(staging);
				throw;
			}
			catch (Exception ex)
			{
				TryDelete(staging);
				logger.LogError(ex, "World backup into {Root} failed", options.Root);
				return new Error<string>(ex.Message);
			}
		}
		finally
		{
			_oneAtATime.Release();
		}
	}

	public IReadOnlyList<WorldBackup> List()
	{
		if (!Directory.Exists(options.Root)) return [];

		return new DirectoryInfo(options.Root)
			.EnumerateDirectories()
			// The timestamped name is what makes a directory a backup, and everything else in the root
			// is deliberately named so it fails this: the `latest` pointer (one of these copies under
			// another name, which counted twice would let retention delete a real one in its place) and
			// a copy still being written.
			.Where(d => NameRegex().IsMatch(d.Name))
			.OrderByDescending(d => d.Name, StringComparer.Ordinal)
			.Select(Describe)
			.ToArray();
	}

	/// <summary>
	/// A UTC timestamp, which sorts chronologically as text — so retention and "newest first" both
	/// come from an ordinal sort of the names, with no stat call. Suffixed on the vanishing chance two
	/// copies land in the same millisecond.
	/// </summary>
	private string NextName()
	{
		var stamp = DateTime.UtcNow.ToString(StampFormat, CultureInfo.InvariantCulture);
		var name = stamp;
		for (var n = 1; Directory.Exists(Path.Combine(options.Root, name)); n++)
		{
			name = $"{stamp}-{n}";
		}

		return name;
	}

	/// <summary>
	/// Repoints <c>latest</c> at <paramref name="target"/>, relatively, so the pointer still resolves
	/// when the directory is mounted somewhere else — which is the whole point on a container volume.
	/// Where symlinks are not permitted (Windows without developer mode), a <c>latest.txt</c> naming
	/// the directory takes their place: a convenience for whoever is looking, never something the
	/// backup itself depends on, so failing to write it must not fail the run.
	/// </summary>
	private void PointLatestAt(string target)
	{
		var name = Path.GetFileName(target);
		var link = Path.Combine(options.Root, LatestName);
		try
		{
			var existing = new DirectoryInfo(link);
			// Directory.Delete removes the link itself, never what it points at.
			if (existing.Exists) existing.Delete();

			Directory.CreateSymbolicLink(link, name);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
		{
			logger.LogDebug(ex, "Could not point {Link} at {Name}; writing {Fallback} instead", link, name,
				LatestFallbackName);
			try
			{
				File.WriteAllText(Path.Combine(options.Root, LatestFallbackName), name);
			}
			catch (Exception fallback) when (fallback is IOException or UnauthorizedAccessException)
			{
				logger.LogWarning(fallback, "Could not record the newest backup's name in {Root}", options.Root);
			}
		}
	}

	/// <summary>
	/// Deletes everything past <see cref="Keep"/>, oldest first. Best-effort per copy: one directory
	/// that will not delete — a snapshot tool holding it open, say — must not stop the others going,
	/// and must not fail a backup that has already been written.
	/// </summary>
	private void Prune()
	{
		foreach (var stale in List().Skip(Keep))
		{
			logger.LogInformation("Deleting superseded world backup {Name}", stale.Name);
			TryDelete(stale.Path);
		}
	}

	private void TryDelete(string path)
	{
		if (!Directory.Exists(path)) return;
		try
		{
			Directory.Delete(path, recursive: true);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			logger.LogWarning(ex, "Could not delete {Path}", path);
		}
	}

	private static WorldBackup Describe(DirectoryInfo directory)
	{
		var createdAt = DateTime.TryParseExact(directory.Name[..Math.Min(StampFormat.Length, directory.Name.Length)],
			StampFormat, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
			out var stamp)
			? new DateTimeOffset(stamp, TimeSpan.Zero)
			: new DateTimeOffset(directory.CreationTimeUtc, TimeSpan.Zero);

		var size = directory.EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);

		return new WorldBackup(directory.Name, directory.FullName, createdAt, size);
	}

	/// <summary>Matches <see cref="StampFormat"/> plus the same-millisecond suffix.</summary>
	[GeneratedRegex(@"^\d{8}-\d{6}-\d{3}(-\d+)?$")]
	private static partial Regex NameRegex();
}
