namespace SharpMUSH.Library.Models;

/// <summary>
/// How much of one kind of history survives a retention pass. The default keeps everything: a game
/// that never configures retention never loses a version, which is what permanent archival means here.
///
/// <para>A version is purgeable only when every bound that is set says so — it is past the newest
/// <see cref="KeepNewest"/> of its stream <i>and</i> older than <see cref="MaxAge"/>. A version the
/// store marks as pinned (what a page or pose shows now, and anything a redo can still reach) is never
/// purgeable, whatever the bounds.</para>
/// </summary>
public sealed record HistoryRetentionRule
{
	/// <summary>No bound at all: nothing is ever purged.</summary>
	public static readonly HistoryRetentionRule KeepEverything = new();

	/// <summary>
	/// The newest versions of each stream that always survive. Zero (the default) sets no count bound;
	/// with <see cref="MaxAge"/> also unset, nothing is purged.
	/// </summary>
	public int KeepNewest { get; init; }

	/// <summary>
	/// The youngest a version can be and still be purged. Null (the default) sets no age bound; with
	/// <see cref="KeepNewest"/> also unset, nothing is purged.
	/// </summary>
	public TimeSpan? MaxAge { get; init; }

	/// <summary>True when neither bound is set, so a pass has nothing to do and need not scan.</summary>
	public bool KeepsEverything => KeepNewest <= 0 && MaxAge is null;

	/// <summary>
	/// Whether one version may be purged.
	/// </summary>
	/// <param name="rankNewestFirst">Its 0-based position in its stream, newest first.</param>
	/// <param name="at">When it was written.</param>
	/// <param name="pinned">Whether the store must keep it regardless — the current version, or one a redo
	/// can still reach.</param>
	/// <param name="now">The pass's clock, so every version in a pass is judged against the same instant.</param>
	public bool IsPurgeable(int rankNewestFirst, DateTimeOffset at, bool pinned, DateTimeOffset now)
	{
		if (pinned || KeepsEverything) return false;
		if (KeepNewest > 0 && rankNewestFirst < KeepNewest) return false;
		return MaxAge is not { } age || at <= now - age;
	}

	/// <summary>The bounds in a few words, for a wizard reading the policy.</summary>
	public string Describe() => (KeepNewest > 0, MaxAge) switch
	{
		(false, null) => "keep everything",
		(true, null) => $"keep the newest {KeepNewest}",
		(false, { } age) => $"purge versions older than {DescribeAge(age)}",
		(true, { } age) => $"keep the newest {KeepNewest}, purge the rest once older than {DescribeAge(age)}"
	};

	private static string DescribeAge(TimeSpan age) => age >= TimeSpan.FromDays(1) && age.Ticks % TimeSpan.TicksPerDay == 0
		? $"{age.TotalDays:0}d"
		: age.ToString();
}
