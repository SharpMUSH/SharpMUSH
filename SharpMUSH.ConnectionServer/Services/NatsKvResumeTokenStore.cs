using System.Security.Cryptography;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.KeyValueStore;

namespace SharpMUSH.ConnectionServer.Services;

/// <summary>
/// NATS KV-backed <see cref="IResumeTokenStore"/>. Token → handle mappings live in a KV bucket with a
/// short <see cref="NatsKVConfig.MaxAge"/> TTL, so resume works after a ConnectionServer restart /
/// instance change (paired with <see cref="JetStreamTerminalReplayStore"/> for the durable buffer).
/// </summary>
public sealed class NatsKvResumeTokenStore : IResumeTokenStore, IAsyncDisposable
{
	private const string Bucket = "terminal_resume";
	private static readonly TimeSpan DefaultTtl = TimeSpan.FromHours(24);

	private readonly NatsConnection _nats;
	private readonly INatsKVStore _store;
	private readonly ILogger<NatsKvResumeTokenStore> _logger;

	private NatsKvResumeTokenStore(NatsConnection nats, INatsKVStore store, ILogger<NatsKvResumeTokenStore> logger)
	{
		_nats = nats;
		_store = store;
		_logger = logger;
	}

	public static async Task<NatsKvResumeTokenStore> CreateAsync(
		string url, ILogger<NatsKvResumeTokenStore> logger, TimeSpan? ttl = null, CancellationToken ct = default)
	{
		var maxAge = ttl ?? DefaultTtl;
		var nats = new NatsConnection(new NatsOpts { Url = url });
		await nats.ConnectAsync();
		var js = new NatsJSContext(nats);
		var kv = new NatsKVContext(js);
		var store = await kv.CreateOrUpdateStoreAsync(new NatsKVConfig(Bucket) { MaxAge = maxAge }, ct);
		logger.LogInformation("KV resume bucket '{Bucket}' ready (TTL {Ttl})", Bucket, maxAge);
		return new NatsKvResumeTokenStore(nats, store, logger);
	}

	// Serialized resume-token value format, versioned so the layout can evolve deliberately. A token
	// stored under a different version — or the pre-versioned legacy layout, or a corrupt entry — fails
	// to resolve, and the caller falls back to a fresh session (which still replays via the durable
	// store). That is safe because resume tokens are single-use and short-lived (re-minted on every
	// connect/reattach), so a format change only costs a brief, self-healing loss of seamless reattach
	// across the one deploy that introduces it — never a correctness or security problem.
	private const string TokenFormatVersion = "1";

	public async ValueTask<string> MintAsync(long handle, string session, CancellationToken ct = default)
	{
		var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
		// "<version>:<handle>:<session>" — handle is numeric and the session id is a hex GUID (no ':'),
		// so ':' is an unambiguous separator.
		await _store.PutAsync(token, $"{TokenFormatVersion}:{handle}:{session}", cancellationToken: ct);
		return token;
	}

	public async ValueTask<(bool Found, long Handle, string Session)> TryResolveAsync(string token, CancellationToken ct = default)
	{
		var result = await _store.TryGetEntryAsync<string>(token, cancellationToken: ct);
		if (!result.Success || result.Value.Value is null)
			return (false, 0L, string.Empty);

		// Expect exactly "<version>:<handle>:<session>". Require the current version, a parseable handle, and
		// a non-empty session: the session is part of the security boundary (it scopes replay to one
		// incarnation), so anything else is rejected rather than resolved to a guessed or empty session.
		var parts = result.Value.Value.Split(':', 3);
		if (parts.Length != 3
		    || parts[0] != TokenFormatVersion
		    || !long.TryParse(parts[1], out var handle)
		    || parts[2].Length == 0)
			return (false, 0L, string.Empty);
		return (true, handle, parts[2]);
	}

	public async ValueTask InvalidateAsync(string token, CancellationToken ct = default)
	{
		// Best-effort: invalidation is an optimisation (the KV TTL expires the token anyway), so any
		// failure — missing key, transient NATS error — is intentionally swallowed rather than surfaced.
		try { await _store.DeleteAsync(token, cancellationToken: ct); }
		catch (Exception ex) { _logger.LogTrace(ex, "Resume token invalidate no-op for {Token}", token); }
	}

	public async ValueTask DisposeAsync() => await _nats.DisposeAsync();
}
