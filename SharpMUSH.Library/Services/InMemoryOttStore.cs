using System.Collections.Concurrent;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Services;

/// <summary>
/// In-process in-memory implementation of <see cref="IOttStore"/>.
/// Suitable for single-server deployments.  For multi-instance deployments a shared
/// backing store (Redis / NATS KV) would be needed — the interface makes that swap trivial.
/// </summary>
public sealed class InMemoryOttStore : IOttStore
{
	private readonly record struct Entry(DBRef PlayerRef, DateTimeOffset Expiry);

	private readonly ConcurrentDictionary<string, Entry> _tokens = new(StringComparer.Ordinal);

	public Task<string> CreateTokenAsync(DBRef playerRef, TimeSpan ttl, CancellationToken ct = default)
	{
		var token = Guid.NewGuid().ToString("N"); // 32 hex chars, no dashes
		var entry = new Entry(playerRef, DateTimeOffset.UtcNow.Add(ttl));
		_tokens[token] = entry;
		return Task.FromResult(token);
	}

	public Task<DBRef?> ValidateAndConsumeAsync(string token, CancellationToken ct = default)
	{
		if (!_tokens.TryRemove(token, out var entry))
			return Task.FromResult<DBRef?>(null);

		if (DateTimeOffset.UtcNow > entry.Expiry)
			return Task.FromResult<DBRef?>(null);

		return Task.FromResult<DBRef?>(entry.PlayerRef);
	}
}
