using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Plugins.Storage.Lightning;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Services;
using TUnit.Assertions.Enums;

namespace SharpMUSH.Tests.Database.Lightning;

/// <summary>
/// The expiry-ordered session index (#1458): every create, renewal and delete moves it in the same write,
/// and the sweep reclaims expired sessions and all their secondary entries without the token being
/// presented again, reading only the expired head of the index.
/// </summary>
public class SessionExpiryIndexTests : LightningDatabaseFixture
{
	private static SharpSession Session(string token, long expiry, string account = "acct", string ip = "10.0.0.1") => new()
	{
		Token = token,
		AccountId = account,
		OriginIp = ip,
		ExpiryUnixMs = expiry,
		TtlMs = 60_000
	};

	private bool HasAnyTrace(string token)
	{
		var key = Keys.Str(token);
		return Db.Store.Read(tx => tx.TryGet(Tables.Session, key, out _)
			|| tx.Range(Tables.SessionAccount, []).Any(e => e.Value.AsSpan().SequenceEqual(key))
			|| tx.Range(Tables.SessionIp, []).Any(e => e.Value.AsSpan().SequenceEqual(key))
			|| tx.Range(Tables.SessionExpiry, []).Any(e => e.Key.AsSpan(8).SequenceEqual(key)));
	}

	[Test]
	public async Task SweepDeletesExpiredSessionsAndEverySecondaryEntry()
	{
		await Db.UpsertSessionAsync(Session("expired-a", 1_000, ip: "10.0.0.9"));
		await Db.UpsertSessionAsync(Session("expired-b", 2_000));
		await Db.UpsertSessionAsync(Session("live", 10_000));

		await Assert.That(await Db.CountExpiredSessionsAsync(5_000)).IsEqualTo(2L);
		var deleted = await Db.DeleteExpiredSessionsAsync(5_000, 100);

		await Assert.That(deleted).IsEqualTo(2);
		await Assert.That(HasAnyTrace("expired-a")).IsFalse();
		await Assert.That(HasAnyTrace("expired-b")).IsFalse();
		await Assert.That(await Db.GetSessionAsync("live")).IsNotNull();
		await Assert.That(await Db.CountExpiredSessionsAsync(5_000)).IsEqualTo(0L);
		// The origin address only the expired session held is no longer known to ban enforcement.
		await Assert.That(await Db.GetSessionOriginIpsAsync()).IsEquivalentTo(["10.0.0.1"]);
	}

	/// <summary>The boundary is the one validation uses: a session expires at its expiry instant.</summary>
	[Test]
	public async Task SweepTreatsTheExpiryInstantAsExpired()
	{
		await Db.UpsertSessionAsync(Session("at-instant", 5_000));
		await Db.UpsertSessionAsync(Session("one-after", 5_001));

		await Assert.That(await Db.DeleteExpiredSessionsAsync(5_000, 100)).IsEqualTo(1);
		await Assert.That(await Db.GetSessionAsync("at-instant")).IsNull();
		await Assert.That(await Db.GetSessionAsync("one-after")).IsNotNull();
	}

	[Test]
	public async Task SweepWorksInBatchesEarliestFirst()
	{
		for (var i = 0; i < 5; i++)
		{
			await Db.UpsertSessionAsync(Session($"batch-{i}", 1_000 + (5 - i)));
		}

		await Assert.That(await Db.DeleteExpiredSessionsAsync(9_000, 2)).IsEqualTo(2);
		await Assert.That(await Db.CountExpiredSessionsAsync(9_000)).IsEqualTo(3L);
		// batch-4 (1001) and batch-3 (1002) expired first, so they went first.
		await Assert.That(await Db.GetSessionAsync("batch-4")).IsNull();
		await Assert.That(await Db.GetSessionAsync("batch-3")).IsNull();
		await Assert.That(await Db.GetSessionAsync("batch-0")).IsNotNull();

		await Assert.That(await Db.DeleteExpiredSessionsAsync(9_000, 2)).IsEqualTo(2);
		await Assert.That(await Db.DeleteExpiredSessionsAsync(9_000, 2)).IsEqualTo(1);
		await Assert.That(await Db.DeleteExpiredSessionsAsync(9_000, 2)).IsEqualTo(0);
	}

	[Test]
	public async Task RenewalMovesTheSessionOutOfTheSweepsReach()
	{
		await Db.UpsertSessionAsync(Session("renewed", 1_000));
		await Assert.That(await Db.TouchSessionExpiryAsync("renewed", 50_000)).IsTrue();

		await Assert.That(await Db.CountExpiredSessionsAsync(10_000)).IsEqualTo(0L);
		await Assert.That(await Db.DeleteExpiredSessionsAsync(10_000, 100)).IsEqualTo(0);
		await Assert.That((await Db.GetSessionAsync("renewed"))!.ExpiryUnixMs).IsEqualTo(50_000L);
		// Exactly one index entry, at the new expiry.
		await Assert.That(Db.Store.Read(tx => tx.Range(Tables.SessionExpiry, []).Select(e => Keys.ReadDbref(e.Key)).ToList()))
			.IsEquivalentTo([50_000L]);
	}

	/// <summary>Replacing a session through the upsert moves its index entry too, as it does its account and IP ones.</summary>
	[Test]
	public async Task UpsertReplacingASessionLeavesOneIndexEntry()
	{
		await Db.UpsertSessionAsync(Session("replaced", 1_000));
		await Db.UpsertSessionAsync(Session("replaced", 70_000));

		await Assert.That(Db.Store.Read(tx => tx.Range(Tables.SessionExpiry, []).Select(e => Keys.ReadDbref(e.Key)).ToList()))
			.IsEquivalentTo([70_000L]);
	}

	/// <summary>
	/// A sweep never inserts, so it cannot reinstate a revoked session, and a renewal that races a sweep
	/// either moved the session out of reach first or finds it gone and fails.
	/// </summary>
	[Test]
	public async Task SweepNeverResurrectsAndRenewalAfterSweepFails()
	{
		await Db.UpsertSessionAsync(Session("revoked", 1_000));
		await Db.UpsertSessionAsync(Session("swept", 1_000));
		await Db.DeleteSessionAsync("revoked");

		await Assert.That(await Db.DeleteExpiredSessionsAsync(5_000, 100)).IsEqualTo(1);
		await Assert.That(HasAnyTrace("revoked")).IsFalse();
		await Assert.That(await Db.TouchSessionExpiryAsync("swept", 90_000)).IsFalse();
		await Assert.That(HasAnyTrace("swept")).IsFalse();
	}

	[Test]
	public async Task ConcurrentRenewalsAndSweepsKeepEveryRenewedSession()
	{
		for (var i = 0; i < 40; i++)
		{
			await Db.UpsertSessionAsync(Session($"race-{i}", 1_000));
		}

		var renewals = Enumerable.Range(0, 40).Where(i => i % 2 == 0)
			.Select(i => Db.TouchSessionExpiryAsync($"race-{i}", 100_000).AsTask());
		var sweeps = Enumerable.Range(0, 10).Select(_ => Db.DeleteExpiredSessionsAsync(5_000, 3).AsTask());
		var renewed = await Task.WhenAll(renewals);
		await Task.WhenAll(sweeps);
		int swept;
		do
		{
			swept = await Db.DeleteExpiredSessionsAsync(5_000, 100);
		} while (swept > 0);

		for (var i = 0; i < 40; i += 2)
		{
			// A renewal that won keeps its session; one that lost found it already swept.
			var present = await Db.GetSessionAsync($"race-{i}") is not null;
			await Assert.That(present).IsEqualTo(renewed[i / 2]);
		}

		for (var i = 1; i < 40; i += 2)
		{
			await Assert.That(HasAnyTrace($"race-{i}")).IsFalse();
		}

		await Assert.That(await Db.CountExpiredSessionsAsync(5_000)).IsEqualTo(0L);
	}

	[Test]
	public async Task RevocationByAccountAndIpRemovesIndexEntries()
	{
		await Db.UpsertSessionAsync(Session("by-account", 1_000, account: "acct-x"));
		await Db.UpsertSessionAsync(Session("by-ip", 1_000, ip: "192.0.2.7"));
		await Db.DeleteSessionsForAccountAsync("acct-x");
		await Db.DeleteSessionsForIpAsync("192.0.2.7");

		await Assert.That(Dump(Tables.SessionExpiry)).IsEmpty();
	}

	[Test]
	public async Task IndexSurvivesRestart()
	{
		await Db.UpsertSessionAsync(Session("persisted", 1_000));
		await ReopenAsync();

		await Assert.That(await Db.DeleteExpiredSessionsAsync(5_000, 100)).IsEqualTo(1);
		await Assert.That(HasAnyTrace("persisted")).IsFalse();
	}

	[Test]
	public async Task OriginIpsAreDistinctIgnoringCase()
	{
		await Db.UpsertSessionAsync(Session("ip-1", 90_000, ip: "10.0.0.1"));
		await Db.UpsertSessionAsync(Session("ip-2", 90_000, ip: "10.0.0.1"));
		await Db.UpsertSessionAsync(Session("ip-3", 90_000, ip: "fe80::A"));
		await Db.UpsertSessionAsync(Session("ip-4", 90_000, ip: "fe80::a"));
		await Db.UpsertSessionAsync(Session("ip-5", 90_000, ip: "10.0.0.10"));

		var ips = await Db.GetSessionOriginIpsAsync();

		await Assert.That(ips.Length).IsEqualTo(3);
		await Assert.That(ips).Contains("10.0.0.1");
		await Assert.That(ips).Contains("10.0.0.10");
		await Assert.That(ips.Count(ip => ip.Equals("fe80::a", StringComparison.OrdinalIgnoreCase))).IsEqualTo(1);
	}

	/// <summary>The service sweep uses the wall clock and the validation boundary, and reports the backlog left.</summary>
	[Test]
	public async Task ServiceSweepReportsDeletedAndRemaining()
	{
		var store = new DatabaseAccountSessionStore(Db);
		var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
		await Db.UpsertSessionAsync(Session("svc-old-1", now - 60_000));
		await Db.UpsertSessionAsync(Session("svc-old-2", now - 50_000));
		await Db.UpsertSessionAsync(Session("svc-old-3", now - 40_000));
		var live = await store.CreateTokenAsync("acct", TimeSpan.FromHours(1), "10.0.0.1");

		var first = await store.SweepExpiredAsync(2);
		await Assert.That(first.Deleted).IsEqualTo(2);
		await Assert.That(first.Remaining).IsEqualTo(1L);

		var second = await store.SweepExpiredAsync(2);
		await Assert.That(second.Deleted).IsEqualTo(1);
		await Assert.That(second.Remaining).IsEqualTo(0L);
		await Assert.That(await store.ValidateAsync(live)).IsNotNull();
	}
}

/// <summary>The hosted sweep keeps taking batches while they come back full, and stops at the first short one.</summary>
public class ExpiredSessionSweepServiceTests
{
	[Test]
	public async Task PassRunsBatchesUntilOneComesBackShort()
	{
		var sessions = Substitute.For<IAccountSessionStore>();
		const int full = ExpiredSessionSweepService.BatchSize;
		sessions.SweepExpiredAsync(full, Arg.Any<CancellationToken>()).Returns(
			new IAccountSessionStore.SessionSweep(full, 7),
			new IAccountSessionStore.SessionSweep(full, 3),
			new IAccountSessionStore.SessionSweep(3, 0));
		var service = new ExpiredSessionSweepService(sessions, NullLogger<ExpiredSessionSweepService>.Instance);

		var deleted = await service.SweepAsync(CancellationToken.None);

		await Assert.That(deleted).IsEqualTo(full * 2 + 3);
		await sessions.Received(3).SweepExpiredAsync(full, Arg.Any<CancellationToken>());
	}
}
