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
		services.AddSurreal($"Endpoint=mem://;Namespace=sharpmush_sessionstore;Database={dbName}")
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
