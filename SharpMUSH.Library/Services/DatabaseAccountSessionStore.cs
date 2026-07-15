using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Services;

/// <summary>
/// DB-backed account session store. Sessions persist across restarts and are revoked
/// (deleted) by token, account, or origin IP for immediate ban enforcement.
/// </summary>
public sealed class DatabaseAccountSessionStore(ISharpDatabase database) : IAccountSessionStore
{
	public async Task<string> CreateTokenAsync(string accountId, TimeSpan ttl, string originIp, CancellationToken ct = default)
	{
		var token = Guid.NewGuid().ToString("N");
		await database.UpsertSessionAsync(new SharpSession
		{
			Token = token,
			AccountId = accountId,
			OriginIp = originIp,
			ExpiryUnixMs = DateTimeOffset.UtcNow.Add(ttl).ToUnixTimeMilliseconds(),
			TtlMs = (long)ttl.TotalMilliseconds
		}, ct);
		return token;
	}

	public async Task<string?> ValidateAsync(string token, CancellationToken ct = default)
	{
		var s = await database.GetSessionAsync(token, ct);
		if (s is null) return null;

		if (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() > s.ExpiryUnixMs)
		{
			await database.DeleteSessionAsync(token, ct);
			return null;
		}

		// Slide the rolling window.
		s.ExpiryUnixMs = DateTimeOffset.UtcNow.AddMilliseconds(s.TtlMs).ToUnixTimeMilliseconds();
		await database.UpsertSessionAsync(s, ct);
		return s.AccountId;
	}

	public Task RevokeAsync(string token, CancellationToken ct = default)
		=> database.DeleteSessionAsync(token, ct).AsTask();

	public Task RevokeAllForAccountAsync(string accountId, CancellationToken ct = default)
		=> database.DeleteSessionsForAccountAsync(accountId, ct).AsTask();

	public Task RevokeAllForIpAsync(string originIp, CancellationToken ct = default)
		=> database.DeleteSessionsForIpAsync(originIp, ct).AsTask();
}
