using SharpMUSH.Library.Models;

namespace SharpMUSH.Database.Lightning;

/// <summary>
/// <see cref="ISessionRecordStore"/>: persisted account-session records keyed by token. Not ported yet
/// — every member throws <see cref="NotImplementedException"/> until a later task.
/// </summary>
public sealed partial class LightningDatabase
{
	public ValueTask UpsertSessionAsync(SharpSession session, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public ValueTask<SharpSession?> GetSessionAsync(string token, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public ValueTask<bool> TouchSessionExpiryAsync(string token, long expiryUnixMs, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public ValueTask DeleteSessionAsync(string token, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public ValueTask DeleteSessionsForAccountAsync(string accountId, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public ValueTask DeleteSessionsForIpAsync(string originIp, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public ValueTask<string[]> GetSessionOriginIpsAsync(CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();
}
