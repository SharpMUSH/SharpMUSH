using System.Collections.Concurrent;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.DiscriminatedUnions;

namespace SharpMUSH.Library.Services;

/// <summary>
/// In-process in-memory implementation of <see cref="IAccountSessionStore"/>.
/// Tokens slide their expiry on each use (rolling window).
/// </summary>
public sealed class InMemoryAccountSessionStore(TimeProvider time, AccountCredentialGate? credentialGate = null,
	IEnumerable<IAccountCredentialRevocation>? revocations = null) : IAccountSessionStore
{
	public InMemoryAccountSessionStore() : this(TimeProvider.System) { }
	private readonly AccountCredentialGate _credentials = credentialGate ?? new();
	private readonly IEnumerable<IAccountCredentialRevocation> _revocations = revocations ?? [];

	private readonly record struct Entry(string AccountId, DateTimeOffset Expiry, TimeSpan Ttl, string OriginIp,
		int? CharacterKey, long? CharacterCreationTime, bool Remembered = false);

	private readonly ConcurrentDictionary<string, Entry> _tokens = new(StringComparer.Ordinal);

	public Task<string> CreateTokenAsync(string accountId, TimeSpan ttl, string originIp,
		int? characterKey = null, long? characterCreationTime = null, CancellationToken ct = default)
	{
		var token = Guid.NewGuid().ToString("N");
		_tokens[AccountSessionDigest.Compute(token)] = new Entry(accountId, time.GetUtcNow().Add(ttl), ttl, originIp,
			characterKey, characterCreationTime);
		return Task.FromResult(token);
	}

	public Task<string> CreateRememberedLoginAsync(string accountId, TimeSpan ttl, string originIp, CancellationToken ct = default)
	{
		var token = Guid.NewGuid().ToString("N");
		_tokens[AccountSessionDigest.Compute(token)] = new Entry(accountId, time.GetUtcNow().Add(ttl), ttl, originIp, null, null, Remembered: true);
		return Task.FromResult(token);
	}

	public async Task<string?> RedeemRememberedLoginAsync(string token, CancellationToken ct = default)
		=> await ValidateAsync(token, remembered: true) is { } identity ? identity.AccountId : null;

	public Task<IAccountSessionStore.SessionIdentity?> ValidateAsync(string token, CancellationToken ct = default)
		=> ValidateAsync(token, remembered: false);

	public Task<Found<IAccountSessionStore.SessionIdentity>> InspectAsync(string token, CancellationToken ct = default)
		=> Task.FromResult<Found<IAccountSessionStore.SessionIdentity>>(
			_tokens.TryGetValue(AccountSessionDigest.Compute(token), out var entry) && !entry.Remembered && time.GetUtcNow() < entry.Expiry
				? new IAccountSessionStore.SessionIdentity(entry.AccountId, entry.CharacterKey, entry.CharacterCreationTime)
				: new NotFound());

	public Task<Found<IAccountSessionStore.SessionIdentity>> InspectDigestAsync(string sessionDigest, CancellationToken ct = default)
		=> Task.FromResult<Found<IAccountSessionStore.SessionIdentity>>(
			_tokens.TryGetValue(sessionDigest, out var entry) && !entry.Remembered && time.GetUtcNow() < entry.Expiry
				? new IAccountSessionStore.SessionIdentity(entry.AccountId, entry.CharacterKey, entry.CharacterCreationTime)
				: new NotFound());

	private Task<IAccountSessionStore.SessionIdentity?> ValidateAsync(string token, bool remembered)
	{
		while (_tokens.TryGetValue(AccountSessionDigest.Compute(token), out var entry) && entry.Remembered == remembered)
		{
			if (time.GetUtcNow() >= entry.Expiry)
			{
				if (!_tokens.TryRemove(new KeyValuePair<string, Entry>(AccountSessionDigest.Compute(token), entry))) continue;
				break;
			}

			// A concurrent revoke must never be undone by a renewal's insertion branch.
			if (!_tokens.TryUpdate(AccountSessionDigest.Compute(token), entry with { Expiry = time.GetUtcNow().Add(entry.Ttl) }, entry)) continue;
			return Task.FromResult<IAccountSessionStore.SessionIdentity?>(
				new IAccountSessionStore.SessionIdentity(entry.AccountId, entry.CharacterKey, entry.CharacterCreationTime));
		}
		return Task.FromResult<IAccountSessionStore.SessionIdentity?>(null);
	}

	public Task RevokeAsync(string token, CancellationToken ct = default)
		=> _credentials.RunAsync(() =>
		{
			_tokens.TryRemove(AccountSessionDigest.Compute(token), out _);
			foreach (var revocation in _revocations) revocation.RevokeSession(token);
			return Task.CompletedTask;
		}, ct);

	public Task RevokeAllForAccountAsync(string accountId, CancellationToken ct = default)
		=> _credentials.RunAsync(() =>
		{
			foreach (var pair in _tokens.Where(p => p.Value.AccountId == accountId)) _tokens.TryRemove(pair.Key, out _);
			foreach (var revocation in _revocations) revocation.RevokeAccount(accountId);
			return Task.CompletedTask;
		}, ct);

	public Task RevokeAllForIpAsync(string originIp, CancellationToken ct = default)
		=> _credentials.RunAsync(() =>
		{
			foreach (var pair in _tokens.Where(p => p.Value.OriginIp == originIp)) _tokens.TryRemove(pair.Key, out _);
			return Task.CompletedTask;
		}, ct);

	public Task<string[]> GetKnownOriginIpsAsync(CancellationToken ct = default)
		=> Task.FromResult<string[]>([
			.. _tokens.Values
				.Select(session => session.OriginIp)
				.Where(ip => !string.IsNullOrEmpty(ip))
				.Distinct(StringComparer.OrdinalIgnoreCase)
		]);

	public Task<IAccountSessionStore.SessionSweep> SweepExpiredAsync(int maxCount, CancellationToken ct = default)
	{
		var now = time.GetUtcNow();
		var expired = _tokens.Where(p => now >= p.Value.Expiry).Select(p => p.Key).ToList();
		var deleted = expired.Take(maxCount).Count(token => _tokens.TryRemove(token, out _));

		return Task.FromResult(new IAccountSessionStore.SessionSweep(deleted, Math.Max(0, expired.Count - deleted)));
	}
}
