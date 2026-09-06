using Microsoft.Extensions.Caching.Memory;
using SharpMUSH.Library.Attributes;
using ZiggyCreatures.Caching.Fusion;

namespace SharpMUSH.Library.Behaviors;

/// <summary>
/// The FusionCache entry options behind each <see cref="CacheEntryProfile"/>, and the memory-cache
/// bound they are sized against. One shared instance per profile: FusionCache copies the options it
/// is handed before a factory can change them, so these are never mutated.
/// </summary>
/// <remarks>
/// Invalidation in this engine is explicit - every write goes through a command that removes its
/// keys or tags - so <see cref="FusionCacheEntryOptions.Duration"/> is a safety net for a missed
/// invalidation, not the freshness mechanism. That is what lets it be minutes rather than seconds.
/// Jitter spreads the expiry of entries loaded together, such as a room and everything in it on
/// <c>look</c>.
/// <para>
/// Two FusionCache behaviours are turned off everywhere a tag can invalidate. A foreground factory's
/// entry is stamped when the factory starts, which is what lets a tag removed mid-read expire the
/// result (the mechanism #854 relies on). A factory completed in the background - an eager refresh,
/// or a timed-out factory allowed to finish - is stamped when it is stored, after the tag, and so
/// survives it holding pre-write data. Tagged entries therefore get no eager refresh, and no
/// profile lets a timed-out factory complete in the background.
/// </para>
/// </remarks>
public static class CacheEntryProfiles
{
	/// <summary>
	/// The engine cache's memory bound, in entry units: one per document, and one per element for a
	/// cached list. A full-database sweep (<c>@find</c>, <c>lattr</c> over everything) fills the cache
	/// instead of growing the process without limit; the least recently used tenth is compacted out.
	/// </summary>
	public const long MemoryCacheSizeLimit = 250_000;

	public const double MemoryCacheCompactionPercentage = 0.1;

	private static readonly TimeSpan LongDuration = TimeSpan.FromMinutes(10);
	private static readonly TimeSpan LongJitter = TimeSpan.FromSeconds(60);
	private static readonly TimeSpan HardTimeout = TimeSpan.FromSeconds(5);

	/// <summary>See <see cref="CacheEntryProfile.Object"/>.</summary>
	public static readonly FusionCacheEntryOptions Object = new()
	{
		Duration = LongDuration,
		JitterMaxDuration = LongJitter,
		EagerRefreshThreshold = 0.85f,
		IsFailSafeEnabled = true,
		FailSafeMaxDuration = TimeSpan.FromHours(1),
		FailSafeThrottleDuration = TimeSpan.FromSeconds(15),
		// A slow database answers with the last known value at once and refreshes behind it; a hung
		// one stops holding the command (and the per-key lock behind it) after the hard timeout.
		FactorySoftTimeout = TimeSpan.FromMilliseconds(150),
		FactoryHardTimeout = HardTimeout,
		// A timed-out read is discarded, not stored later: by then a write may have removed the key.
		AllowTimedOutFactoryBackgroundCompletion = false,
		Priority = CacheItemPriority.Normal,
		Size = 1,
	};

	/// <summary>See <see cref="CacheEntryProfile.Tagged"/>. No eager refresh and no fail-safe; see the class remarks.</summary>
	public static readonly FusionCacheEntryOptions Tagged = new()
	{
		Duration = LongDuration,
		JitterMaxDuration = LongJitter,
		IsFailSafeEnabled = false,
		FactoryHardTimeout = HardTimeout,
		AllowTimedOutFactoryBackgroundCompletion = false,
		Priority = CacheItemPriority.Normal,
		Size = 1,
	};

	/// <summary>See <see cref="CacheEntryProfile.Scan"/>.</summary>
	public static readonly FusionCacheEntryOptions Scan = new()
	{
		Duration = TimeSpan.FromSeconds(60),
		JitterMaxDuration = TimeSpan.FromSeconds(10),
		IsFailSafeEnabled = false,
		FactoryHardTimeout = TimeSpan.FromSeconds(10),
		AllowTimedOutFactoryBackgroundCompletion = false,
		Priority = CacheItemPriority.Low,
		Size = 1,
	};

	public static FusionCacheEntryOptions For(CacheEntryProfile profile) => profile switch
	{
		CacheEntryProfile.Object => Object,
		CacheEntryProfile.Tagged => Tagged,
		CacheEntryProfile.Scan => Scan,
		_ => throw new ArgumentOutOfRangeException(nameof(profile), profile, null),
	};
}
