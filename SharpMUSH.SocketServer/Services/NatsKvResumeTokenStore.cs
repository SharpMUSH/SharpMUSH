using System.Security.Cryptography;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.KeyValueStore;

namespace SharpMUSH.ConnectionServer.Services;

/// <summary>Durable, atomically consumed resume tokens. KV keys contain hashes, never bearer credentials.</summary>
public sealed class NatsKvResumeTokenStore : IResumeTokenStore, IAsyncDisposable
{
	private const string Bucket = "terminal_resume";
	private static readonly TimeSpan DefaultTtl = TimeSpan.FromHours(24);
	private readonly NatsConnection _nats;
	private readonly INatsKVStore _store;

	private NatsKvResumeTokenStore(NatsConnection nats, INatsKVStore store)
	{
		_nats = nats;
		_store = store;
	}

	public static async Task<NatsKvResumeTokenStore> CreateAsync(
		string url, ILogger<NatsKvResumeTokenStore> logger, TimeSpan? ttl = null, CancellationToken ct = default)
	{
		var maxAge = ttl ?? DefaultTtl;
		var nats = new NatsConnection(new NatsOpts { Url = url });
		await nats.ConnectAsync();
		var kv = new NatsKVContext(new NatsJSContext(nats));
		var store = await kv.CreateOrUpdateStoreAsync(new NatsKVConfig(Bucket) { MaxAge = maxAge }, ct);
		logger.LogInformation("KV resume bucket '{Bucket}' ready (TTL {Ttl})", Bucket, maxAge);
		return new NatsKvResumeTokenStore(nats, store);
	}

	// v1 indexed plaintext tokens and lacked atomic consumption. Deliberately reject them at this
	// upgrade boundary rather than restoring an authenticated session using the weaker protocol.
	private const string TokenFormatVersion = "2";
	private static string Key(string token) => $"v2.{ResumeTokenService.TokenKey(token)}";

	public async ValueTask<string> MintAsync(long handle, string session, CancellationToken ct = default)
	{
		if (await IsRevokedAsync(session, ct)) throw new InvalidOperationException("Cannot mint a token for a revoked session.");
		var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
		await _store.CreateAsync(Key(token), $"{TokenFormatVersion}:{handle}:{session}", cancellationToken: ct);
		return token;
	}

	public async ValueTask<(bool Found, long Handle, string Session)> TryResolveAsync(string token, CancellationToken ct = default)
	{
		var entry = await _store.TryGetEntryAsync<string>(Key(token), cancellationToken: ct);
		if (!entry.Success)
		{
			if (entry.Error is NatsKVKeyNotFoundException or NatsKVKeyDeletedException) return (false, 0, string.Empty);
			throw entry.Error;
		}
		var binding = Parse(entry.Value.Value);
		return binding.Found && !await IsRevokedAsync(binding.Session, ct) ? binding : (false, 0, string.Empty);
	}

	public async ValueTask<(bool Found, long Handle, string Session)> TryConsumeAsync(string token, CancellationToken ct = default)
	{
		var key = Key(token);
		var entry = await _store.TryGetEntryAsync<string>(key, cancellationToken: ct);
		if (!entry.Success)
		{
			if (entry.Error is NatsKVKeyNotFoundException or NatsKVKeyDeletedException) return (false, 0, string.Empty);
			throw entry.Error;
		}
		var binding = Parse(entry.Value.Value);
		if (!binding.Found || await IsRevokedAsync(binding.Session, ct)) return (false, 0, string.Empty);
		// A consumed marker is deliberately not parseable as a binding. The revision precondition
		// ensures two processes racing on the same token cannot both restore it. ConnectionPump
		// separately CASes the connection incarnation against logout before exposing replay.
		var spent = await _store.TryUpdateAsync(key, "consumed", entry.Value.Revision, cancellationToken: ct);
		if (spent.Success)
			return !await IsRevokedAsync(binding.Session, ct) ? binding : (false, 0, string.Empty);
		if (spent.Error is NatsKVWrongLastRevisionException) return (false, 0, string.Empty);
		throw spent.Error;
	}

	public async ValueTask RevokeSessionAsync(string session, CancellationToken ct = default) =>
		await _store.PutAsync($"revoked.{ResumeTokenService.TokenKey(session)}", "revoked", cancellationToken: ct);

	private async ValueTask<bool> IsRevokedAsync(string session, CancellationToken ct)
	{
		var entry = await _store.TryGetEntryAsync<string>($"revoked.{ResumeTokenService.TokenKey(session)}", cancellationToken: ct);
		if (entry.Success) return true;
		if (entry.Error is NatsKVKeyNotFoundException or NatsKVKeyDeletedException) return false;
		throw entry.Error;
	}

	private static (bool Found, long Handle, string Session) Parse(string? value)
	{
		var parts = value?.Split(':', 3);
		return parts is { Length: 3 } && parts[0] == TokenFormatVersion
			&& long.TryParse(parts[1], out var handle) && parts[2].Length > 0
			? (true, handle, parts[2]) : (false, 0, string.Empty);
	}

	public async ValueTask InvalidateAsync(string token, CancellationToken ct = default) =>
		await _store.DeleteAsync(Key(token), cancellationToken: ct);

	public async ValueTask DisposeAsync() => await _nats.DisposeAsync();
}
