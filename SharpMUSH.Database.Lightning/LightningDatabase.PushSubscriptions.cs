using SharpMUSH.Library.DiscriminatedUnions;
using System.Buffers.Text;
using SharpMUSH.Database.Lightning.Records;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Plugins.Storage.Lightning;

namespace SharpMUSH.Database.Lightning;

/// <summary>
/// <see cref="IPushSubscriptionStore"/>. A row is keyed by the SHA-256 of its endpoint (the subscription's id,
/// decoded); <see cref="Tables.PushSubscriptionByAccount"/> lists an account's.
/// </summary>
public partial class LightningDatabase
{
	private static byte[]? PushKey(string id)
	{
		try
		{
			var key = Base64Url.DecodeFromChars(id);
			return key.Length == 32 ? key : null;
		}
		catch (FormatException)
		{
			return null;
		}
	}

	private static PushSubscription MapToPushSubscription(byte[] key, PushSubscriptionRecord record) => new(
		Base64Url.EncodeToString(key),
		$"node_accounts/{record.AccountKey}",
		record.Endpoint,
		record.P256dh,
		record.Auth,
		record.Device,
		record.KeyId,
		DateTimeOffset.FromUnixTimeMilliseconds(record.CreatedAtMs),
		record.LastSuccessAtMs is { } success ? DateTimeOffset.FromUnixTimeMilliseconds(success) : null,
		record.LastFailureAtMs is { } failure ? DateTimeOffset.FromUnixTimeMilliseconds(failure) : null,
		record.LastFailure);

	public async ValueTask<PushSubscriptionSaveOutcome> SavePushSubscriptionAsync(PushSubscription subscription,
		int maxPerAccount, CancellationToken cancellationToken = default)
	{
		var accountKey = ParseAccountId(subscription.AccountId);
		var key = PushKey(subscription.Id) ?? throw new ArgumentException("Not a push subscription id.", nameof(subscription));
		var record = new PushSubscriptionRecord
		{
			AccountKey = accountKey,
			Endpoint = subscription.Endpoint,
			P256dh = subscription.P256dh,
			Auth = subscription.Auth,
			Device = subscription.Device,
			KeyId = subscription.KeyId,
			CreatedAtMs = subscription.CreatedAt.ToUnixTimeMilliseconds()
		};

		return await Store.WriteAsync(tx =>
		{
			var outcome = PushSubscriptionSaveOutcome.Added;
			if (tx.TryGet(Tables.PushSubscription, key, out var bytes))
			{
				var previous = Codec.Deserialize<PushSubscriptionRecord>(bytes);
				tx.Delete(Tables.PushSubscriptionByAccount, Keys.Str(previous.AccountKey), key);
				outcome = PushSubscriptionSaveOutcome.Replaced;
			}
			else if (tx.Dups(Tables.PushSubscriptionByAccount, Keys.Str(accountKey)).Count() >= maxPerAccount)
			{
				return PushSubscriptionSaveOutcome.AccountFull;
			}

			tx.Put(Tables.PushSubscription, key, Codec.Serialize(record));
			tx.Put(Tables.PushSubscriptionByAccount, Keys.Str(accountKey), key);
			return outcome;
		}, cancellationToken);
	}

	public ValueTask<Found<PushSubscription>> GetPushSubscriptionAsync(string id, CancellationToken cancellationToken = default)
	{
		if (PushKey(id) is not { } key)
		{
			return ValueTask.FromResult<Found<PushSubscription>>(new NotFound());
		}

		return ValueTask.FromResult(Store.Read<Found<PushSubscription>>(tx => tx.TryGet(Tables.PushSubscription, key, out var bytes)
			? MapToPushSubscription(key, Codec.Deserialize<PushSubscriptionRecord>(bytes))
			: new NotFound()));
	}

	public ValueTask<IReadOnlyList<PushSubscription>> GetPushSubscriptionsAsync(string accountId,
		CancellationToken cancellationToken = default)
	{
		var accountKey = ParseAccountId(accountId);
		var result = Store.Read(tx => tx.Dups(Tables.PushSubscriptionByAccount, Keys.Str(accountKey))
			.Select(key => tx.TryGet(Tables.PushSubscription, key, out var bytes)
				&& Codec.Deserialize<PushSubscriptionRecord>(bytes) is { } record
				&& record.AccountKey == accountKey
					? MapToPushSubscription(key, record)
					: null)
			.OfType<PushSubscription>()
			.OrderBy(subscription => subscription.CreatedAt)
			.ToList());
		return ValueTask.FromResult<IReadOnlyList<PushSubscription>>(result);
	}

	public async ValueTask<bool> RemovePushSubscriptionAsync(string id, string? accountId = null,
		CancellationToken cancellationToken = default)
	{
		if (PushKey(id) is not { } key)
		{
			return false;
		}

		var accountKey = accountId is null ? null : ParseAccountId(accountId);
		return await Store.WriteAsync(tx =>
		{
			if (!tx.TryGet(Tables.PushSubscription, key, out var bytes)) return false;
			var record = Codec.Deserialize<PushSubscriptionRecord>(bytes);
			if (accountKey is not null && record.AccountKey != accountKey) return false;
			tx.Delete(Tables.PushSubscription, key);
			tx.Delete(Tables.PushSubscriptionByAccount, Keys.Str(record.AccountKey), key);
			return true;
		}, cancellationToken);
	}

	public async ValueTask RecordPushResultAsync(string id, DateTimeOffset at, string? failure,
		CancellationToken cancellationToken = default)
	{
		if (PushKey(id) is not { } key)
		{
			return;
		}

		await Store.WriteAsync(tx =>
		{
			if (!tx.TryGet(Tables.PushSubscription, key, out var bytes)) return false;
			var record = Codec.Deserialize<PushSubscriptionRecord>(bytes);
			tx.Put(Tables.PushSubscription, key, Codec.Serialize(failure is null
				? record with { LastSuccessAtMs = at.ToUnixTimeMilliseconds() }
				: record with { LastFailureAtMs = at.ToUnixTimeMilliseconds(), LastFailure = failure }));
			return true;
		}, cancellationToken);
	}
}
