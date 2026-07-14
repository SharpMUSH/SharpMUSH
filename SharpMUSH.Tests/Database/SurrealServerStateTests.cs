using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SharpMUSH.Database.SurrealDB;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;
using SurrealDb.Net;

namespace SharpMUSH.Tests.Database;

/// <summary>
/// ServerState round-trips (Task 2), plus the migration-time upgrade inference: a game that
/// already has a claimed account (non-empty passwordHash) when the server_state doc is first
/// created must come up with SetupCompleted already true, so the first-run wizard doesn't
/// re-open on an existing, already-set-up game. Pattern lifted from
/// <see cref="SurrealMigrationIdempotencyTests"/>: a fresh in-memory SurrealDatabase per test.
/// </summary>
public class SurrealServerStateTests
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

	/// <summary>A fresh, NOT yet migrated database — used to seed state before the first Migrate().</summary>
	private static async Task<SurrealDatabase> CreateFreshSurrealDatabaseAsync(string dbName)
	{
		// The embedded in-memory engine resolves through DI (same as Startup's AddSurreal +
		// AddInMemoryProvider); a bare `new SurrealDbClient(...)` cannot create mem:// engines.
		var services = new ServiceCollection();
		services.AddSurreal($"Endpoint=mem://;Namespace=sharpmush_serverstate;Database={dbName}")
			.AddInMemoryProvider();
		var client = services.BuildServiceProvider().GetRequiredService<ISurrealDbClient>();
		await client.Connect();

		return new SurrealDatabase(NullLogger<SurrealDatabase>.Instance, client, new NoopPasswordService());
	}

	private static async Task<SurrealDatabase> CreateFreshMigratedSurrealDatabaseAsync(string dbName)
	{
		var database = await CreateFreshSurrealDatabaseAsync(dbName);
		await database.Migrate();
		return database;
	}

	[Test]
	public async Task FreshGame_SetupNotCompleted_RoundTrips()
	{
		var db = await CreateFreshMigratedSurrealDatabaseAsync("fresh");

		var initial = await db.GetServerStateAsync();
		await Assert.That(initial.SetupCompleted).IsFalse();

		await db.SetServerSetupCompletedAsync(true);
		await Assert.That((await db.GetServerStateAsync()).SetupCompleted).IsTrue();
	}

	[Test]
	public async Task ClaimedGame_MigrationInfersSetupCompleted()
	{
		// Simulate a pre-upgrade deployment: an account is already claimed (non-empty password
		// hash) before the very first Migrate() ever runs against this database — the state doc
		// does not exist yet, so inference must run and see the claimed account.
		var db = await CreateFreshSurrealDatabaseAsync("claimed");
		await db.CreateAccountAsync("upgrade-admin", null, "some-real-hash");

		await db.Migrate();

		await Assert.That((await db.GetServerStateAsync()).SetupCompleted).IsTrue();
	}

	[Test]
	public async Task ClaimedGame_AfterStateExists_MigrationDoesNotRetroactivelyFlip()
	{
		// The state doc is created first (no accounts yet -> false), then an account is claimed,
		// then Migrate() runs again. Inference must only run when the state doc is missing, so a
		// re-run must never overwrite the existing record either way.
		var db = await CreateFreshMigratedSurrealDatabaseAsync("no-reinference");
		await db.CreateAccountAsync("late-admin", null, "some-real-hash");

		await db.Migrate();

		await Assert.That((await db.GetServerStateAsync()).SetupCompleted).IsFalse();
	}
}
