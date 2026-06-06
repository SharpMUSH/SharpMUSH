using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace SharpMUSH.Server.Services;

/// <summary>
/// Cache for pre-rendered HTML pages served to bots.
/// TTL: 1 hour per entry.  Entries are invalidated by key when relevant content changes.
/// </summary>
public interface IPrerenderCacheService
{
	/// <summary>Returns cached HTML for the given URL path, or null if not cached.</summary>
	string? Get(string path);

	/// <summary>Stores pre-rendered HTML for the given URL path.</summary>
	void Set(string path, string html);

	/// <summary>Removes the cached entry for a specific path.</summary>
	void Invalidate(string path);

	/// <summary>
	/// Invalidates all cached paths whose key contains the given prefix
	/// (e.g. "/wiki/" to evict all wiki pre-renders after a wiki edit).
	/// </summary>
	void InvalidatePrefix(string prefix);
}

/// <summary>
/// In-memory pre-render cache backed by <see cref="IMemoryCache"/>.
/// </summary>
public sealed class PrerenderCacheService(IMemoryCache memoryCache, ILogger<PrerenderCacheService> logger)
	: IPrerenderCacheService
{
	private static readonly TimeSpan Ttl = TimeSpan.FromHours(1);

	// Track cached keys so we can scan for prefix-based invalidation.
	private readonly HashSet<string> _keys = new(StringComparer.OrdinalIgnoreCase);
	private readonly Lock _keyLock = new();

	public string? Get(string path)
	{
		if (memoryCache.TryGetValue(CacheKey(path), out string? html))
			return html;
		return null;
	}

	public void Set(string path, string html)
	{
		var key = CacheKey(path);
		memoryCache.Set(key, html, new MemoryCacheEntryOptions
		{
			AbsoluteExpirationRelativeToNow = Ttl,
		});

		lock (_keyLock)
			_keys.Add(path);

		logger.LogDebug("PrerenderCache: stored {Path} (TTL 1h)", path);
	}

	public void Invalidate(string path)
	{
		memoryCache.Remove(CacheKey(path));
		lock (_keyLock)
			_keys.Remove(path);
		logger.LogDebug("PrerenderCache: invalidated {Path}", path);
	}

	public void InvalidatePrefix(string prefix)
	{
		List<string> toRemove;
		lock (_keyLock)
		{
			toRemove = _keys.Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList();
			foreach (var k in toRemove)
				_keys.Remove(k);
		}

		foreach (var k in toRemove)
			memoryCache.Remove(CacheKey(k));

		if (toRemove.Count > 0)
			logger.LogDebug("PrerenderCache: invalidated {Count} entries with prefix '{Prefix}'", toRemove.Count, prefix);
	}

	private static string CacheKey(string path) => $"prerender:{path}";
}
