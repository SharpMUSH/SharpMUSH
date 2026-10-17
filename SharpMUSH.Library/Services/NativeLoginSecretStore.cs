using System.Security.Cryptography;
using System.Text;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Services;

/// <summary>Bounded transient native credentials. Only digests are retained; restart revokes every outstanding secret.</summary>
public sealed class NativeLoginSecretStore : INativeLoginSecretStore, IDisposable
{
	private sealed record Entry(string Id, byte[] Digest, NativeLoginAuthority Authority, DateTimeOffset ExpiresAt);
	private sealed record Counter(DateTimeOffset StartedAt, int Count);
	private readonly object _sync = new();
	private readonly TimeProvider _time;
	private readonly ITimer _cleanup;
	private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
	private readonly Dictionary<string, string> _digests = new(StringComparer.Ordinal);
	private readonly Dictionary<string, Counter> _rates = new(StringComparer.Ordinal);
	private const int MaximumPending = 10_000;
	private const int MaximumRateBuckets = 20_000;
	private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(2);
	private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

	public NativeLoginSecretStore(TimeProvider? timeProvider = null)
	{
		_time = timeProvider ?? TimeProvider.System;
		_cleanup = _time.CreateTimer(_ => { lock (_sync) Cleanup(_time.GetUtcNow()); }, null,
			TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
	}

	private void Cleanup(DateTimeOffset now)
	{
		foreach (var entry in _entries.Values.Where(entry => now >= entry.ExpiresAt).ToArray()) Remove(entry);
		foreach (var key in _rates.Where(pair => now - pair.Value.StartedAt >= Window).Select(pair => pair.Key).ToArray()) _rates.Remove(key);
	}

	private bool RateAllows(string key, int maximum)
		=> _rates.TryGetValue(key, out var counter) ? counter.Count < maximum : _rates.Count < MaximumRateBuckets;

	private void Count(string key, DateTimeOffset now)
		=> _rates[key] = _rates.TryGetValue(key, out var current) ? current with { Count = current.Count + 1 } : new(now, 1);

	private void Remove(Entry entry)
	{
		_entries.Remove(entry.Id);
		_digests.Remove(Convert.ToHexString(entry.Digest));
	}

	public Result<NativeLoginSecretMint> Issue(NativeLoginAuthority authority, string originIp)
	{
		lock (_sync)
		{
			var now = _time.GetUtcNow();
			Cleanup(now);
			if (authority.Character.CreationMilliseconds is null || string.IsNullOrEmpty(authority.AccountId)
				|| authority.SessionDigest.Length != 64) return new Error<string>("Invalid native login authority.");
			var accountRate = "mint-account:" + authority.AccountId;
			var ipRate = "mint-ip:" + originIp;
			if (_entries.Count >= MaximumPending || _entries.Values.Count(entry => entry.Authority.AccountId == authority.AccountId) >= 10)
				return new Error<string>("Too many outstanding native logins. Revoke one or wait for expiry.");
			if (!RateAllows(accountRate, 5) || !RateAllows(ipRate, 10) || _rates.Count > MaximumRateBuckets - 2)
				return new Error<string>("Native login generation limit reached. Try again shortly.");
			var secret = "nc1_" + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
			var digest = SHA256.HashData(Encoding.UTF8.GetBytes(secret));
			var id = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
			var entry = new Entry(id, digest, authority, now + Lifetime);
			_entries.Add(id, entry);
			_digests.Add(Convert.ToHexString(digest), id);
			Count(accountRate, now);
			Count(ipRate, now);
			return new NativeLoginSecretMint(id, secret, authority.Character, entry.ExpiresAt);
		}
	}

	public Found<NativeLoginSecretLease> Consume(string secret, string sourceIp, string connectionId)
	{
		lock (_sync)
		{
			var now = _time.GetUtcNow();
			Cleanup(now);
			var ipRate = "failure-ip:" + sourceIp;
			var connectionRate = "failure-connection:" + connectionId;
			if (!RateAllows(ipRate, 10) || !RateAllows(connectionRate, 10) || _rates.Count > MaximumRateBuckets - 2) return new NotFound();
			if (secret.Length == 47 && secret.StartsWith("nc1_", StringComparison.Ordinal)
				&& secret.AsSpan(4).IndexOfAnyExcept("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_".AsSpan()) < 0)
			{
				var digest = SHA256.HashData(Encoding.UTF8.GetBytes(secret));
				if (_digests.TryGetValue(Convert.ToHexString(digest), out var id) && _entries.TryGetValue(id, out var entry)
					&& CryptographicOperations.FixedTimeEquals(digest, entry.Digest))
				{
					Remove(entry);
					return new NativeLoginSecretLease(entry.Authority, entry.ExpiresAt);
				}
			}
			Count(ipRate, now);
			Count(connectionRate, now);
			return new NotFound();
		}
	}

	/// <summary>Counts a recognized token that failed a later authority, policy or final-binding check.</summary>
	public void RecordFailure(string sourceIp, string connectionId)
	{
		lock (_sync)
		{
			var now = _time.GetUtcNow();
			Cleanup(now);
			var ipRate = "failure-ip:" + sourceIp;
			var connectionRate = "failure-connection:" + connectionId;
			if (!RateAllows(ipRate, 10) || !RateAllows(connectionRate, 10) || _rates.Count > MaximumRateBuckets - 2) return;
			Count(ipRate, now);
			Count(connectionRate, now);
		}
	}

	public IReadOnlyList<NativeLoginSecretStatus> List(string accountId)
	{
		lock (_sync)
		{
			Cleanup(_time.GetUtcNow());
			return _entries.Values.Where(entry => entry.Authority.AccountId == accountId)
				.Select(entry => new NativeLoginSecretStatus(entry.Id, entry.Authority.Character, entry.ExpiresAt)).ToArray();
		}
	}

	public bool Revoke(string accountId, string id)
	{
		lock (_sync)
		{
			if (!_entries.TryGetValue(id, out var entry) || entry.Authority.AccountId != accountId) return false;
			Remove(entry);
			return true;
		}
	}

	private void RevokeWhere(Func<Entry, bool> predicate)
	{
		lock (_sync)
			foreach (var entry in _entries.Values.Where(predicate).ToArray()) Remove(entry);
	}

	public void RevokeAccount(string accountId) => RevokeWhere(entry => entry.Authority.AccountId == accountId);
	public void RevokeSession(string sessionToken) => RevokeWhere(entry => entry.Authority.SessionDigest == AccountSessionDigest.Compute(sessionToken));
	public void RevokeCharacter(DBRef character) => RevokeWhere(entry => entry.Authority.Character == character);
	public void Dispose() => _cleanup.Dispose();
}
