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

	public async ValueTask<string> MintAsync(long handle, string session, CancellationToken ct = default)
	{
		var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
		// Value is "<handle>:<session>" — handle is numeric and the session id is a hex GUID, so ':' is an
		// unambiguous separator.
		await _store.PutAsync(token, $"{handle}:{session}", cancellationToken: ct);
		return token;
	}

	public async ValueTask<(bool Found, long Handle, string Session)> TryResolveAsync(string token, CancellationToken ct = default)
	{
		var result = await _store.TryGetEntryAsync<string>(token, cancellationToken: ct);
		if (!result.Success || result.Value.Value is null)
			return (false, 0L, string.Empty);

		var value = result.Value.Value;
		var sep = value.IndexOf(':');
		if (sep <= 0 || !long.TryParse(value.AsSpan(0, sep), out var handle))
			return (false, 0L, string.Empty);
		return (true, handle, value[(sep + 1)..]);
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
