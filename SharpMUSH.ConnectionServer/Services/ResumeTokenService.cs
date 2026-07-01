using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace SharpMUSH.ConnectionServer.Services;

/// <summary>
/// Mints and resolves opaque resume tokens that bind a fresh reconnect back to a surviving handle
/// within a short grace window. In-memory for this spike; a durable store could back it later.
/// </summary>
public sealed class ResumeTokenService
{
	private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(30);

	private readonly ConcurrentDictionary<string, Entry> _tokens = new();
	private readonly Func<DateTimeOffset> _now;

	public ResumeTokenService() : this(() => DateTimeOffset.UtcNow) { }

	// Test seam: inject a clock so TTL expiry is deterministic.
	public ResumeTokenService(Func<DateTimeOffset> now) => _now = now;

	public string Mint(long handle)
	{
		var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
		_tokens[token] = new Entry(handle, _now() + Ttl);
		return token;
	}

	public bool TryResolve(string token, out long handle)
	{
		handle = 0;
		if (!_tokens.TryGetValue(token, out var entry)) return false;
		if (_now() > entry.Expires)
		{
			_tokens.TryRemove(token, out _);
			return false;
		}

		handle = entry.Handle;
		return true;
	}

	public void Invalidate(string token) => _tokens.TryRemove(token, out _);

	private sealed record Entry(long Handle, DateTimeOffset Expires);
}
