using System.Collections.Concurrent;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Services;

/// <summary>
/// In-process in-memory implementation of <see cref="IRefreshTokenStore"/>.
/// Tokens do NOT slide their expiry — they are single-use (the caller must revoke after
/// issuing a new pair).  Expired entries are lazily reaped on any access.
/// </summary>
public sealed class InMemoryRefreshTokenStore : IRefreshTokenStore
{
	private readonly record struct Entry(string AccountId, DBRef CharacterRef, DateTimeOffset Expiry);

	private readonly ConcurrentDictionary<string, Entry> _tokens = new(StringComparer.Ordinal);

	/// <inheritdoc />
	public Task<string> CreateTokenAsync(string accountId, DBRef characterRef, TimeSpan ttl,
		CancellationToken ct = default)
	{
		var token = Guid.NewGuid().ToString("N");
		_tokens[token] = new Entry(accountId, characterRef, DateTimeOffset.UtcNow.Add(ttl));
		return Task.FromResult(token);
	}

	/// <inheritdoc />
	public Task<(string AccountId, DBRef CharacterRef)?> ValidateAsync(string token,
		CancellationToken ct = default)
	{
		if (!_tokens.TryGetValue(token, out var entry))
			return Task.FromResult<(string, DBRef)?>(null);

		if (DateTimeOffset.UtcNow > entry.Expiry)
		{
			_tokens.TryRemove(token, out _);
			return Task.FromResult<(string, DBRef)?>(null);
		}

		return Task.FromResult<(string AccountId, DBRef CharacterRef)?>((entry.AccountId, entry.CharacterRef));
	}

	/// <inheritdoc />
	public Task RevokeAsync(string token, CancellationToken ct = default)
	{
		_tokens.TryRemove(token, out _);
		return Task.CompletedTask;
	}

	/// <inheritdoc />
	public Task RevokeAllForAccountAsync(string accountId, CancellationToken ct = default)
	{
		foreach (var key in _tokens.Keys.ToList())
		{
			if (_tokens.TryGetValue(key, out var entry)
			    && string.Equals(entry.AccountId, accountId, StringComparison.Ordinal))
				_tokens.TryRemove(key, out _);
		}

		return Task.CompletedTask;
	}
}
