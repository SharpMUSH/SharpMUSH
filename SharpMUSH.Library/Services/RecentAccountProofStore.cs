using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Services;

/// <summary>Digest-only, fixed-lifetime proof grants. Restart invalidates every pending grant.</summary>
public sealed class RecentAccountProofStore(TimeProvider time, int capacity = 10_000) : IRecentAccountProofStore, IAccountCredentialRevocation
{
	public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);
	private sealed record Entry(string AccountId, string SessionDigest, AccountSecurityOperation Operation,
		AccountProofAuthority Authority, DateTimeOffset ExpiresAt);

	private readonly Lock _sync = new();
	private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);

	public int Count { get { lock (_sync) { Sweep(); return _entries.Count; } } }

	public Result<RecentAccountProof> Issue(string accountId, string sessionToken, AccountSecurityOperation operation,
		AccountProofAuthority authority)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
		ArgumentException.ThrowIfNullOrWhiteSpace(sessionToken);
		ArgumentException.ThrowIfNullOrWhiteSpace(authority.Method);
		lock (_sync)
		{
			Sweep();
			if (_entries.Count >= capacity) return new Error<string>("Too many pending security proofs. Try again shortly.");
			var grant = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
			var expires = time.GetUtcNow() + Lifetime;
			_entries.Add(Digest(grant), new Entry(accountId, Digest(sessionToken), operation, authority, expires));
			return new RecentAccountProof(grant, expires);
		}
	}

	public Found<AccountProofAuthority> Consume(string? grant, string accountId, string sessionToken, AccountSecurityOperation operation)
	{
		if (string.IsNullOrEmpty(grant) || grant.Length > 128) return new NotFound();
		lock (_sync)
		{
			Sweep();
			var digest = Digest(grant);
			if (!_entries.TryGetValue(digest, out var entry) || entry.AccountId != accountId
				|| entry.SessionDigest != Digest(sessionToken) || entry.Operation != operation)
				return new NotFound();
			_entries.Remove(digest);
			return entry.Authority;
		}
	}

	public void RevokeAccount(string accountId)
	{
		lock (_sync)
			foreach (var key in _entries.Where(pair => pair.Value.AccountId == accountId).Select(pair => pair.Key).ToArray())
				_entries.Remove(key);
	}

	public void RevokeSession(string sessionToken)
	{
		var digest = Digest(sessionToken);
		lock (_sync)
			foreach (var key in _entries.Where(pair => pair.Value.SessionDigest == digest).Select(pair => pair.Key).ToArray())
				_entries.Remove(key);
	}

	public void RevokeCharacter(DBRef character) { }

	private void Sweep()
	{
		var now = time.GetUtcNow();
		foreach (var key in _entries.Where(pair => pair.Value.ExpiresAt <= now).Select(pair => pair.Key).ToArray())
			_entries.Remove(key);
	}

	private static string Digest(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
