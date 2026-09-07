using System.Globalization;
using System.Text.RegularExpressions;

namespace SharpMUSH.Library.Models;

/// <summary>
/// Where copies of the world go, how many are kept and how often one is taken on a schedule. Shared
/// by every provider that can back itself up, because none of this is provider-specific: it is a
/// deployment concern — the size of the disk the copies land on — rather than game configuration.
/// </summary>
public sealed partial record WorldBackupOptions
{
	/// <summary>Directory the timestamped copies are written into. Created on first use.</summary>
	public required string Root { get; init; }

	/// <summary>
	/// How many copies survive a run. Kept small on purpose: each is a full world, and the point of
	/// the directory is to give a snapshot tool something consistent to read, not to be the archive.
	/// Values below one are treated as one — a run never deletes the copy it just made.
	/// </summary>
	public int Keep { get; init; } = 2;

	/// <summary>
	/// How often the scheduled backup runs. Zero (the default) leaves scheduling off; the command
	/// still works. Set it to comfortably less than the snapshot tool's own period so the snapshot
	/// always finds a recent copy.
	/// </summary>
	public TimeSpan Interval { get; init; } = TimeSpan.Zero;

	/// <summary>
	/// The backup root for a world at <paramref name="worldPath"/>: <c>&lt;worldPath&gt;.backups</c>,
	/// following the same convention as a Lightning staging promotion's <c>.previous</c>. Beside the
	/// world, so a deployment mounting one volume for its data gets both under that mount without
	/// configuring anything, and named after it, so two worlds on one box never share a root.
	/// </summary>
	public static string DefaultRootFor(string worldPath)
		=> worldPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + ".backups";

	/// <summary>
	/// Longest interval that counts as a setting rather than a typo. Past this it is rejected: a
	/// backup schedule measured in years is nobody's intent, and the arithmetic that produces such a
	/// number overflows into a negative <see cref="TimeSpan"/> that <see cref="PeriodicTimer"/>
	/// refuses — which would take the host down at startup rather than the setting.
	/// </summary>
	private const long MaxIntervalSeconds = 365L * 86400;

	/// <summary>
	/// Parses a PennMUSH-style interval — <c>6h</c>, <c>30m</c>, <c>1h30m</c>, <c>2d</c>, <c>45s</c> —
	/// or a bare count of seconds. Empty, absent and <c>0</c> all mean "no scheduled backup" and parse
	/// successfully; anything else it cannot read, and anything past
	/// <see cref="MaxIntervalSeconds"/>, is rejected — leaving <paramref name="interval"/> zero, so a
	/// typo turns scheduling off loudly rather than picking a duration nobody asked for.
	/// </summary>
	public static bool TryParseInterval(string? setting, out TimeSpan interval)
	{
		interval = TimeSpan.Zero;
		var text = setting?.Trim();
		if (string.IsNullOrEmpty(text)) return true;

		if (long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds))
		{
			if (seconds > MaxIntervalSeconds) return false;
			interval = TimeSpan.FromSeconds(seconds);
			return true;
		}

		if (!IntervalRegex().IsMatch(text)) return false;

		var total = 0L;
		foreach (Match part in IntervalPartRegex().Matches(text))
		{
			if (!long.TryParse(part.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var value))
			{
				return false;
			}

			var unitSeconds = part.Groups[2].Value.ToLowerInvariant() switch
			{
				"d" => 86400L,
				"h" => 3600L,
				"m" => 60L,
				_ => 1L
			};

			// Checked before multiplying, so an absurd count is rejected rather than wrapping.
			if (value > MaxIntervalSeconds / unitSeconds) return false;
			total += value * unitSeconds;
			if (total > MaxIntervalSeconds) return false;
		}

		interval = TimeSpan.FromSeconds(total);
		return true;
	}

	/// <summary>Anchored, so a setting with anything else in it is rejected rather than part-read.</summary>
	[GeneratedRegex(@"^(\d+[dhms])+$", RegexOptions.IgnoreCase)]
	private static partial Regex IntervalRegex();

	[GeneratedRegex(@"(\d+)([dhms])", RegexOptions.IgnoreCase)]
	private static partial Regex IntervalPartRegex();
}
