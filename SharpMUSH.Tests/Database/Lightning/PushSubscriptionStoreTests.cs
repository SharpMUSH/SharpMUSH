using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Plugins.Storage.Lightning;
using SharpMUSH.Library.Push;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Database.Lightning;

/// <summary>The Lightning provider's web push subscriptions: one per endpoint, listed per account.</summary>
public class PushSubscriptionStoreTests
{
	private string _path = null!;
	private LightningDatabase _db = null!;
	private string _alice = null!;
	private string _bob = null!;

	[Before(Test)]
	public async Task Setup()
	{
		_path = Path.Combine(Path.GetTempPath(), "sharpmush-lmdb-" + Guid.NewGuid().ToString("N"));
		_db = new LightningDatabase(NullLogger<LightningDatabase>.Instance,
			new LightningStoreOptions { Path = _path, MapSize = 256L << 20 }, Substitute.For<IPasswordService>(), relations: null);
		await _db.Migrate();
		_alice = (await _db.CreateAccountAsync("alice", null, "hash")).Id!;
		_bob = (await _db.CreateAccountAsync("bob", null, "hash")).Id!;
	}

	[After(Test)]
	public async Task Cleanup()
	{
		await _db.DisposeAsync();
		try
		{
			Directory.Delete(_path, recursive: true);
		}
		catch (IOException)
		{
			// Best-effort, as AccountsAndSessionsTests: the lock file can outlive the writer thread briefly.
		}
	}

	private static PushSubscription Subscription(string accountId, string endpoint, string device = "Firefox on Linux")
		=> new(PushEndpoints.IdOf(endpoint), accountId, endpoint, [4, 1, 2], [9, 8, 7], device, "key-1",
			DateTimeOffset.FromUnixTimeMilliseconds(1_760_000_000_000), null, null, null);

	[Test]
	public async Task ASavedSubscriptionIsFoundByIdAndListedForItsAccount()
	{
		var saved = Subscription(_alice, "https://fcm.googleapis.com/fcm/send/a1");

		await Assert.That(await _db.SavePushSubscriptionAsync(saved, 5)).IsEqualTo(PushSubscriptionSaveOutcome.Added);

		var found = (await _db.GetPushSubscriptionAsync(saved.Id)).Expect<PushSubscription>();
		await Assert.That(found.Endpoint).IsEqualTo(saved.Endpoint);
		await Assert.That(found.AccountId).IsEqualTo(_alice);
		await Assert.That(found.P256dh).IsEquivalentTo(saved.P256dh);
		await Assert.That(found.Auth).IsEquivalentTo(saved.Auth);
		await Assert.That(found.CreatedAt).IsEqualTo(saved.CreatedAt);
		await Assert.That((await _db.GetPushSubscriptionsAsync(_alice)).Select(s => s.Id)).IsEquivalentTo([saved.Id]);
		await Assert.That(await _db.GetPushSubscriptionsAsync(_bob)).IsEmpty();
	}

	/// <summary>A browser signed in to another account takes its endpoint with it.</summary>
	[Test]
	public async Task SavingAnEndpointForAnotherAccountMovesIt()
	{
		const string endpoint = "https://fcm.googleapis.com/fcm/send/shared";
		await _db.SavePushSubscriptionAsync(Subscription(_alice, endpoint), 5);

		var moved = await _db.SavePushSubscriptionAsync(Subscription(_bob, endpoint), 5);

		await Assert.That(moved).IsEqualTo(PushSubscriptionSaveOutcome.Replaced);
		await Assert.That(await _db.GetPushSubscriptionsAsync(_alice)).IsEmpty();
		await Assert.That((await _db.GetPushSubscriptionsAsync(_bob)).Count).IsEqualTo(1);
	}

	[Test]
	public async Task AFullAccountTakesNoMoreButReplacesItsOwn()
	{
		await _db.SavePushSubscriptionAsync(Subscription(_alice, "https://fcm.googleapis.com/fcm/send/1"), 2);
		await _db.SavePushSubscriptionAsync(Subscription(_alice, "https://fcm.googleapis.com/fcm/send/2"), 2);

		var third = await _db.SavePushSubscriptionAsync(Subscription(_alice, "https://fcm.googleapis.com/fcm/send/3"), 2);
		var again = await _db.SavePushSubscriptionAsync(Subscription(_alice, "https://fcm.googleapis.com/fcm/send/2", "Renamed"), 2);

		await Assert.That(third).IsEqualTo(PushSubscriptionSaveOutcome.AccountFull);
		await Assert.That(again).IsEqualTo(PushSubscriptionSaveOutcome.Replaced);
		await Assert.That((await _db.GetPushSubscriptionsAsync(_alice)).Count).IsEqualTo(2);
	}

	/// <summary>An endpoint moving in from another account counts against the limit like a new one.</summary>
	[Test]
	public async Task AFullAccountTakesNoEndpointFromAnotherAccount()
	{
		await _db.SavePushSubscriptionAsync(Subscription(_alice, "https://fcm.googleapis.com/fcm/send/a"), 1);
		await _db.SavePushSubscriptionAsync(Subscription(_bob, "https://fcm.googleapis.com/fcm/send/b"), 1);

		var moved = await _db.SavePushSubscriptionAsync(Subscription(_alice, "https://fcm.googleapis.com/fcm/send/b"), 1);

		await Assert.That(moved).IsEqualTo(PushSubscriptionSaveOutcome.AccountFull);
		await Assert.That((await _db.GetPushSubscriptionsAsync(_bob)).Count).IsEqualTo(1);
		await Assert.That((await _db.GetPushSubscriptionsAsync(_alice)).Count).IsEqualTo(1);
	}

	[Test]
	public async Task ResultsAreRecordedAndASubscriptionIsRemovedOnlyByItsAccount()
	{
		var saved = Subscription(_alice, "https://fcm.googleapis.com/fcm/send/r");
		await _db.SavePushSubscriptionAsync(saved, 5);
		var failedAt = DateTimeOffset.FromUnixTimeMilliseconds(1_760_000_100_000);

		await _db.RecordPushResultAsync(saved.Id, failedAt, "HTTP 500");
		var failed = (await _db.GetPushSubscriptionAsync(saved.Id)).Expect<PushSubscription>();
		var refused = await _db.RemovePushSubscriptionAsync(saved.Id, _bob);
		var removed = await _db.RemovePushSubscriptionAsync(saved.Id, _alice);

		await Assert.That(failed.LastFailureAt).IsEqualTo(failedAt);
		await Assert.That(failed.LastFailure).IsEqualTo("HTTP 500");
		await Assert.That(refused).IsFalse();
		await Assert.That(removed).IsTrue();
		await Assert.That((await _db.GetPushSubscriptionAsync(saved.Id)).Value).IsTypeOf<NotFound>();
		await Assert.That(await _db.GetPushSubscriptionsAsync(_alice)).IsEmpty();
	}
}
