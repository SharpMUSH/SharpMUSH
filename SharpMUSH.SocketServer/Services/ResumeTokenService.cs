using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace SharpMUSH.ConnectionServer.Services;

/// <summary>
/// In-memory <see cref="IResumeTokenStore"/>. Does NOT survive a ConnectionServer restart — use
/// <see cref="NatsKvResumeTokenStore"/> for durable, restart-survivable resume.
/// </summary>
public sealed class ResumeTokenService : IResumeTokenStore
{
	private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(30);

	private readonly ConcurrentDictionary<string, Entry> _tokens = new();
	private readonly ConcurrentDictionary<string, DateTimeOffset> _revoked = new();
	private readonly Func<DateTimeOffset> _now;
	internal int RevocationCount => _revoked.Count;

	public ResumeTokenService() : this(() => DateTimeOffset.UtcNow) { }

	// Test seam: inject a clock so TTL expiry is deterministic.
	public ResumeTokenService(Func<DateTimeOffset> now) => _now = now;

	public ValueTask<string> MintAsync(long handle, string session, CancellationToken ct = default)
	{
		if (IsRevoked(session)) throw new InvalidOperationException("Cannot mint a token for a revoked session.");
		var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
		_tokens[TokenKey(token)] = new Entry(handle, session, _now() + Ttl);
		return ValueTask.FromResult(token);
	}

	public ValueTask<(bool Found, long Handle, string Session)> TryResolveAsync(string token, CancellationToken ct = default)
	{
		if (!_tokens.TryGetValue(TokenKey(token), out var entry))
			return ValueTask.FromResult((false, 0L, string.Empty));
		if (_now() >= entry.Expires || IsRevoked(entry.Session))
		{
			_tokens.TryRemove(TokenKey(token), out _);
			return ValueTask.FromResult((false, 0L, string.Empty));
		}

		return ValueTask.FromResult((true, entry.Handle, entry.Session));
	}

	public ValueTask<(bool Found, long Handle, string Session)> TryConsumeAsync(string token, CancellationToken ct = default)
	{
		ct.ThrowIfCancellationRequested();
		return ValueTask.FromResult(_tokens.TryRemove(TokenKey(token), out var entry) && _now() < entry.Expires && !IsRevoked(entry.Session)
			? (true, entry.Handle, entry.Session)
			: (false, 0L, string.Empty));
	}

	public ValueTask RevokeSessionAsync(string session, CancellationToken ct = default)
	{
		ct.ThrowIfCancellationRequested();
		foreach (var entry in _revoked)
			if (_now() >= entry.Value) _revoked.TryRemove(entry);
		_revoked[session] = _now() + Ttl;
		return ValueTask.CompletedTask;
	}

	private bool IsRevoked(string session)
	{
		if (!_revoked.TryGetValue(session, out var expires)) return false;
		if (_now() < expires) return true;
		_revoked.TryRemove(new KeyValuePair<string, DateTimeOffset>(session, expires));
		return false;
	}

	internal static string TokenKey(string token)
	{
		var utf8 = System.Text.Encoding.UTF8;
		var byteCount = utf8.GetByteCount(token);
		Span<byte> bytes = byteCount <= 256 ? stackalloc byte[256] : new byte[byteCount];
		bytes = bytes[..utf8.GetBytes(token, bytes)];
		Span<byte> hash = stackalloc byte[SHA256.HashSizeInBytes];
		SHA256.HashData(bytes, hash);
		return Convert.ToHexString(hash);
	}

	public ValueTask InvalidateAsync(string token, CancellationToken ct = default)
	{
		_tokens.TryRemove(TokenKey(token), out _);
		return ValueTask.CompletedTask;
	}

	private sealed record Entry(long Handle, string Session, DateTimeOffset Expires);
}
