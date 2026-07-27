using System.Collections.Concurrent;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Services;

/// <summary>
/// In-process in-memory implementation of <see cref="IAccountSessionStore"/>.
/// Tokens slide their expiry on each use (rolling window).
/// </summary>
public sealed class InMemoryAccountSessionStore : IAccountSessionStore
{
	private readonly record struct Entry(string AccountId, DateTimeOffset Expiry, TimeSpan Ttl, string OriginIp,
		int? CharacterKey, long? CharacterCreationTime);

	private readonly ConcurrentDictionary<string, Entry> _tokens = new(StringComparer.Ordinal);

	public Task<string> CreateTokenAsync(string accountId, TimeSpan ttl, string originIp,
		int? characterKey = null, long? characterCreationTime = null, CancellationToken ct = default)
	{
		var token = Guid.NewGuid().ToString("N");
		_tokens[token] = new Entry(accountId, DateTimeOffset.UtcNow.Add(ttl), ttl, originIp,
			characterKey, characterCreationTime);
		return Task.FromResult(token);
	}

	public Task<IAccountSessionStore.SessionIdentity?> ValidateAsync(string token, CancellationToken ct = default)
	{
		if (!_tokens.TryGetValue(token, out var entry))
			return Task.FromResult<IAccountSessionStore.SessionIdentity?>(null);

		if (DateTimeOffset.UtcNow > entry.Expiry)
		{
			_tokens.TryRemove(token, out _);
			return Task.FromResult<IAccountSessionStore.SessionIdentity?>(null);
		}

		_tokens[token] = entry with { Expiry = DateTimeOffset.UtcNow.Add(entry.Ttl) };
		return Task.FromResult<IAccountSessionStore.SessionIdentity?>(
			new IAccountSessionStore.SessionIdentity(entry.AccountId, entry.CharacterKey, entry.CharacterCreationTime));
	}

	public Task RevokeAsync(string token, CancellationToken ct = default)
	{
		_tokens.TryRemove(token, out _);
		return Task.CompletedTask;
	}

	public Task RevokeAllForAccountAsync(string accountId, CancellationToken ct = default)
	{
		foreach (var pair in _tokens.Where(p => p.Value.AccountId == accountId))
			_tokens.TryRemove(pair.Key, out _);
		return Task.CompletedTask;
	}

	public Task RevokeAllForIpAsync(string originIp, CancellationToken ct = default)
	{
		foreach (var pair in _tokens.Where(p => p.Value.OriginIp == originIp))
			_tokens.TryRemove(pair.Key, out _);
		return Task.CompletedTask;
	}
}
