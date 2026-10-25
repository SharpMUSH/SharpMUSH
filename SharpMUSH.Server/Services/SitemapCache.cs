namespace SharpMUSH.Server.Services;

/// <summary>One page in the sitemap: its public path, when it last changed, and the locales a reader may ask for.</summary>
public sealed record SitemapEntry(string Path, DateTimeOffset UpdatedAt, IReadOnlyList<string> Locales);

/// <summary>
/// The sitemap's page list, built at most once per <see cref="Lifetime"/>. Building it reads every published page
/// and its translations, so a crawler fetching <c>/sitemap.xml</c> again and again would otherwise scan the whole
/// wiki each time. Requests that arrive during a build wait for that build. The list holds paths, not URLs: the
/// scheme and host come from each request, and keying the cache on them would let any <c>Host</c> header add an entry.
/// </summary>
public sealed class SitemapCache(TimeProvider time)
{
	/// <summary>How long a built list is served; <c>/sitemap.xml</c> tells caches it is good for an hour.</summary>
	public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

	private sealed record Built(IReadOnlyList<SitemapEntry> Entries, DateTimeOffset At);

	private readonly SemaphoreSlim _building = new(1, 1);
	private volatile Built? _built;

	public async Task<IReadOnlyList<SitemapEntry>> GetAsync(
		Func<CancellationToken, Task<IReadOnlyList<SitemapEntry>>> build, CancellationToken ct)
	{
		if (_built is { } built && IsFresh(built)) return built.Entries;

		await _building.WaitAsync(ct);
		try
		{
			if (_built is { } current && IsFresh(current)) return current.Entries;

			var entries = await build(ct);
			_built = new Built(entries, time.GetUtcNow());
			return entries;
		}
		finally
		{
			_building.Release();
		}
	}

	private bool IsFresh(Built built) => time.GetUtcNow() - built.At < Lifetime;
}
