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
	private readonly Func<DateTimeOffset> _now;

	public ResumeTokenService() : this(() => DateTimeOffset.UtcNow) { }

	// Test seam: inject a clock so TTL expiry is deterministic.
	public ResumeTokenService(Func<DateTimeOffset> now) => _now = now;

	public ValueTask<string> MintAsync(long handle, string session, CancellationToken ct = default)
	{
		var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
		_tokens[token] = new Entry(handle, session, _now() + Ttl);
		return ValueTask.FromResult(token);
	}

	public ValueTask<(bool Found, long Handle, string Session)> TryResolveAsync(string token, CancellationToken ct = default)
	{
		if (!_tokens.TryGetValue(token, out var entry))
			return ValueTask.FromResult((false, 0L, string.Empty));
		if (_now() > entry.Expires)
		{
			_tokens.TryRemove(token, out _);
			return ValueTask.FromResult((false, 0L, string.Empty));
		}

		return ValueTask.FromResult((true, entry.Handle, entry.Session));
	}

	public ValueTask InvalidateAsync(string token, CancellationToken ct = default)
	{
		_tokens.TryRemove(token, out _);
		return ValueTask.CompletedTask;
	}

	private sealed record Entry(long Handle, string Session, DateTimeOffset Expires);
}
