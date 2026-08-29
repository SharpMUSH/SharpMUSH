using System.Collections.Concurrent;
using System.Diagnostics;

namespace SharpMUSH.Library.Behaviors;

/// <summary>
/// When each cache key was last invalidated, so a read can tell whether the answer it just computed
/// was already out of date by the time it came back.
/// </summary>
/// <remarks>
/// <c>IFusionCache.RemoveAsync</c> drops only what is in the cache at that instant, so it cannot stop a
/// read that queried the database <em>before</em> a write from storing its pre-write answer
/// <em>after</em> the write — and that entry then serves every later reader until something else
/// invalidates the key. Nothing does, so it never heals. Issue #838 is that shape.
/// <para>
/// FusionCache solves the same problem for tags by resolving an invalidation against when an entry's
/// factory began rather than when it stored. This is that comparison for a single key, which is all a
/// key-targeted invalidation needs — tagging every entry with its own key would buy the same guarantee
/// by writing a tag marker per object, and tags are meant to name a category, not an identity.
/// </para>
/// </remarks>
public interface ICacheInvalidationClock
{
	/// <summary>A stamp to take before reading, to hand back to <see cref="InvalidatedSince"/>.</summary>
	long Now();

	/// <summary>Records that <paramref name="keys"/> have just been invalidated.</summary>
	void Invalidated(IReadOnlyList<string> keys);

	/// <summary>Whether <paramref name="key"/> was invalidated after <paramref name="stamp"/> was taken.</summary>
	bool InvalidatedSince(string key, long stamp);
}

/// <inheritdoc />
/// <remarks>
/// Bounded on purpose. A running game invalidates a great many distinct keys, and an entry is only
/// useful for as long as a read taken before it could still be in flight. Anything older than
/// <see cref="Retention"/> is swept, and <c>_sweptThrough</c> remembers how far the history was
/// discarded so that a read older than that is treated as invalidated rather than assumed clean —
/// losing a cache fill is cheap, serving a stale entry forever is not.
/// </remarks>
public sealed class CacheInvalidationClock : ICacheInvalidationClock
{
	/// <summary>Comfortably longer than any single query, and far shorter than an entry's lifetime.</summary>
	private static readonly long Retention = Stopwatch.Frequency * 120;

	/// <summary>Sweeping walks the whole dictionary, so do it rarely and in proportion to the mess.</summary>
	private const int SweepThreshold = 8192;

	private readonly ConcurrentDictionary<string, long> _invalidatedAt = new(StringComparer.Ordinal);
	private long _sweptThrough = long.MinValue;

	public long Now() => Stopwatch.GetTimestamp();

	public void Invalidated(IReadOnlyList<string> keys)
	{
		if (keys.Count == 0) return;

		var now = Stopwatch.GetTimestamp();
		foreach (var key in keys)
		{
			// Max rather than overwrite: two writers invalidating the same key must not let the slower
			// one's earlier stamp mask the faster one's later invalidation.
			_invalidatedAt.AddOrUpdate(key, now, (_, previous) => Math.Max(previous, now));
		}

		if (_invalidatedAt.Count > SweepThreshold) Sweep(now);
	}

	public bool InvalidatedSince(string key, long stamp)
		// A stamp older than the swept history cannot be cleared, because the record that would have
		// cleared it is gone.
		=> stamp <= Volatile.Read(ref _sweptThrough)
			 || (_invalidatedAt.TryGetValue(key, out var at) && at > stamp);

	private void Sweep(long now)
	{
		var cutoff = now - Retention;

		foreach (var (key, at) in _invalidatedAt)
		{
			// TryRemove with the value compares before removing, so an invalidation landing mid-sweep
			// is kept rather than dropped.
			if (at <= cutoff) _invalidatedAt.TryRemove(new KeyValuePair<string, long>(key, at));
		}

		// Only after the sweep: until an entry is actually gone, reads can still be answered from it.
		InterlockedMax(ref _sweptThrough, cutoff);
	}

	private static void InterlockedMax(ref long target, long value)
	{
		var seen = Volatile.Read(ref target);
		while (seen < value)
		{
			var actual = Interlocked.CompareExchange(ref target, value, seen);
			if (actual == seen) return;
			seen = actual;
		}
	}
}
