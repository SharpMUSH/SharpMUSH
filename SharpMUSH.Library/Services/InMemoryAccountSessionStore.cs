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
		int? CharacterKey, long? CharacterCreationTime, bool Remembered = false);

	private readonly ConcurrentDictionary<string, Entry> _tokens = new(StringComparer.Ordinal);

	public Task<string> CreateTokenAsync(string accountId, TimeSpan ttl, string originIp,
		int? characterKey = null, long? characterCreationTime = null, CancellationToken ct = default)
	{
		var token = Guid.NewGuid().ToString("N");
		_tokens[token] = new Entry(accountId, DateTimeOffset.UtcNow.Add(ttl), ttl, originIp,
			characterKey, characterCreationTime);
		return Task.FromResult(token);
	}

	public Task<string> CreateRememberedLoginAsync(string accountId, TimeSpan ttl, string originIp, CancellationToken ct = default)
	{
		var token = Guid.NewGuid().ToString("N");
		_tokens[token] = new Entry(accountId, DateTimeOffset.UtcNow.Add(ttl), ttl, originIp, null, null, Remembered: true);
		return Task.FromResult(token);
	}

	public async Task<string?> RedeemRememberedLoginAsync(string token, CancellationToken ct = default)
		=> await ValidateAsync(token, remembered: true) is { } identity ? identity.AccountId : null;

	public Task<IAccountSessionStore.SessionIdentity?> ValidateAsync(string token, CancellationToken ct = default)
		=> ValidateAsync(token, remembered: false);

	private Task<IAccountSessionStore.SessionIdentity?> ValidateAsync(string token, bool remembered)
	{
		if (!_tokens.TryGetValue(token, out var entry) || entry.Remembered != remembered)
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

	public Task<string[]> GetKnownOriginIpsAsync(CancellationToken ct = default)
		=> Task.FromResult<string[]>([
			.. _tokens.Values
				.Select(session => session.OriginIp)
				.Where(ip => !string.IsNullOrEmpty(ip))
				.Distinct(StringComparer.OrdinalIgnoreCase)
		]);

	public Task<IAccountSessionStore.SessionSweep> SweepExpiredAsync(int maxCount, CancellationToken ct = default)
	{
		var now = DateTimeOffset.UtcNow;
		var expired = _tokens.Where(p => now > p.Value.Expiry).Select(p => p.Key).ToList();
		var deleted = expired.Take(maxCount).Count(token => _tokens.TryRemove(token, out _));

		return Task.FromResult(new IAccountSessionStore.SessionSweep(deleted, Math.Max(0, expired.Count - deleted)));
	}
}
