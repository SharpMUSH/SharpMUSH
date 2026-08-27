using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SharpMUSH.Database.SurrealDB;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;
using SurrealDb.Net;

namespace SharpMUSH.Tests.Database;

/// <summary>
/// Session-store round-trips (Task 3) against a fresh, migrated, in-memory SurrealDatabase.
/// Pattern lifted from <see cref="SurrealServerStateTests"/>: a fresh in-memory SurrealDatabase
/// per test, same two round-trip assertions as <see cref="SessionStoreDbTests"/>.
/// </summary>
public class SurrealSessionStoreTests
{
	private sealed class NoopPasswordService : IPasswordService
	{
		public string HashPassword(string user, string pw) => pw;
		public bool PasswordIsValid(string user, string pw, string hash) => pw == hash;
		public ValueTask SetPassword(SharpPlayer user, string hashedPassword) => ValueTask.CompletedTask;
		public string GenerateRandomPassword() => "password";
		public bool NeedsRehash(string hash) => false;
		public ValueTask RehashPasswordAsync(SharpPlayer player, string plaintext) => ValueTask.CompletedTask;
	}

	private static async Task<SurrealDatabase> CreateFreshMigratedSurrealDatabaseAsync(string dbName)
	{
		// The embedded in-memory engine resolves through DI (same as Startup's AddSurreal +
		// AddInMemoryProvider); a bare `new SurrealDbClient(...)` cannot create mem:// engines.
		var services = new ServiceCollection();
		// ServiceLifetime.Singleton: AddSurreal defaults to Scoped, which registers ISurrealDbSession
		// instead of ISurrealDbClient (SurrealDb.Net 1.0+) - this test resolves ISurrealDbClient directly.
		services.AddSurreal($"Endpoint=mem://;Namespace=sharpmush_sessionstore;Database={dbName}", ServiceLifetime.Singleton)
			.AddInMemoryProvider();
		var client = services.BuildServiceProvider().GetRequiredService<ISurrealDbClient>();
		await client.Connect();

		var database = new SurrealDatabase(NullLogger<SurrealDatabase>.Instance, client, new NoopPasswordService());
		await database.Migrate();
		return database;
	}

	private static SharpSession Make(string token, string acct, string ip) => new()
	{
		Token = token, AccountId = acct, OriginIp = ip,
		ExpiryUnixMs = DateTimeOffset.UtcNow.AddMinutes(15).ToUnixTimeMilliseconds(),
		TtlMs = (long)TimeSpan.FromMinutes(15).TotalMilliseconds
	};

	[Test]
	public async Task Upsert_Get_Delete_RoundTrip()
	{
		var db = await CreateFreshMigratedSurrealDatabaseAsync("roundtrip");
		var s = Make("tok-rt-1", "node_accounts/1", "203.0.113.9");

		await db.UpsertSessionAsync(s);
		var got = await db.GetSessionAsync("tok-rt-1");
		await Assert.That(got).IsNotNull();
		await Assert.That(got!.AccountId).IsEqualTo("node_accounts/1");
		await Assert.That(got.OriginIp).IsEqualTo("203.0.113.9");

		await db.DeleteSessionAsync("tok-rt-1");
		await Assert.That(await db.GetSessionAsync("tok-rt-1")).IsNull();
	}

	/// <summary>
	/// The ArangoDB provider needed an exclusive transaction because concurrent upserts of one session
	/// document lost the race with a 409 that surfaced as an HTTP 500. This pins the answer to the same
	/// question for SurrealDB: the embedded engine serialises the statement itself, so the same fan-out
	/// that broke ArangoDB completes here without conflict and no provider-side change is warranted.
	/// </summary>
	[Test]
	public async Task ConcurrentUpsertsOfOneSession_DoNotConflict()
	{
		var db = await CreateFreshMigratedSurrealDatabaseAsync("concurrent");
		const int writers = 32;

		var gate = new SemaphoreSlim(0, writers);
		var writes = Enumerable.Range(0, writers).Select(async i =>
		{
			await gate.WaitAsync();
			var s = Make("tok-conc", "acctZ", "10.0.0.9");
			s.ExpiryUnixMs += i;
			await db.UpsertSessionAsync(s);
		}).ToArray();

		gate.Release(writers);
		await Task.WhenAll(writes);

		await Assert.That(await db.GetSessionAsync("tok-conc")).IsNotNull();
	}

	/// <summary>
	/// Same contract as <see cref="SessionStoreDbTests.TouchSessionExpiry_SlidesALiveSession_AndNeverInsertsARevokedOne"/>,
	/// pinned here because the ArangoDB/Memgraph test host has no SurrealDB leg. It also pins the
	/// SurrealDB-specific premise: since 2.0, UPDATE and UPSERT are distinct statements and UPDATE on a
	/// record that does not exist creates nothing.
	/// </summary>
	[Test]
	public async Task TouchSessionExpiry_SlidesALiveSession_AndNeverInsertsARevokedOne()
	{
		var db = await CreateFreshMigratedSurrealDatabaseAsync("touch");
		var s = Make("tok-touch-1", "acctTouch", "203.0.113.40");
		await db.UpsertSessionAsync(s);

		var slid = s.ExpiryUnixMs + 60_000;
		await Assert.That(await db.TouchSessionExpiryAsync("tok-touch-1", slid)).IsTrue();

		var touched = await db.GetSessionAsync("tok-touch-1");
		await Assert.That(touched!.ExpiryUnixMs).IsEqualTo(slid);
		await Assert.That(touched.AccountId).IsEqualTo("acctTouch")
			.Because("a touch slides the expiry and leaves the rest of the document alone");
		await Assert.That(touched.OriginIp).IsEqualTo("203.0.113.40");

		await db.DeleteSessionAsync("tok-touch-1");

		await Assert.That(await db.TouchSessionExpiryAsync("tok-touch-1", slid + 60_000)).IsFalse();
		await Assert.That(await db.GetSessionAsync("tok-touch-1")).IsNull()
			.Because("renewal must never insert — that is exactly what resurrects a revoked session");
	}

	[Test]
	public async Task DeleteForAccount_And_ForIp()
	{
		var db = await CreateFreshMigratedSurrealDatabaseAsync("deletefor");

		await db.UpsertSessionAsync(Make("tok-a1", "acctX", "10.0.0.1"));
		await db.UpsertSessionAsync(Make("tok-a2", "acctX", "10.0.0.2"));
		await db.UpsertSessionAsync(Make("tok-b1", "acctY", "10.0.0.1"));

		await db.DeleteSessionsForAccountAsync("acctX");
		await Assert.That(await db.GetSessionAsync("tok-a1")).IsNull();
		await Assert.That(await db.GetSessionAsync("tok-a2")).IsNull();
		await Assert.That(await db.GetSessionAsync("tok-b1")).IsNotNull();

		await db.DeleteSessionsForIpAsync("10.0.0.1");
		await Assert.That(await db.GetSessionAsync("tok-b1")).IsNull();
	}
}
