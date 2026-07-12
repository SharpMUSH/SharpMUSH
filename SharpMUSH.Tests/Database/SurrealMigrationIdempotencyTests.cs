using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SharpMUSH.Database.SurrealDB;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;
using SurrealDb.Net;

namespace SharpMUSH.Tests.Database;

/// <summary>
/// Re-running <see cref="SurrealDatabase.Migrate"/> against a database that already holds data —
/// which is exactly what every server restart does against a persistent RocksDB store — must not
/// duplicate seed edges (production showed one extra copy of every room-contents row and one extra
/// flag letter per boot) and must not reset the dbref allocator below keys that already exist.
/// </summary>
/// <remarks>
/// NotInParallel: these tests flip the provider's static migration gate and dbref allocator, which
/// other SurrealDB-backed tests in the same process share.
/// </remarks>
[NotInParallel]
public class SurrealMigrationIdempotencyTests
{
	// The migration gate and dbref allocator are process-wide statics shared with the suite's
	// shared factory database. Snapshot and restore them around each test here, or the allocator
	// is left pointing at THIS test's tiny private database and the shared database starts
	// re-handing-out keys that already exist (which broke ~27 object-creating tests).
	private (bool Migrated, int NextObjectKey) _stateSnapshot;

	[Before(Test)]
	public void SnapshotSharedStaticState()
		=> _stateSnapshot = SurrealDatabase.SnapshotMigrationStateForTests();

	[After(Test)]
	public void RestoreSharedStaticState()
		=> SurrealDatabase.RestoreMigrationStateForTests(_stateSnapshot.Migrated, _stateSnapshot.NextObjectKey);

	private sealed class NoopPasswordService : IPasswordService
	{
		public string HashPassword(string user, string pw) => pw;
		public bool PasswordIsValid(string user, string pw, string hash) => pw == hash;
		public ValueTask SetPassword(SharpPlayer user, string hashedPassword) => ValueTask.CompletedTask;
		public string GenerateRandomPassword() => "password";
		public bool NeedsRehash(string hash) => false;
		public ValueTask RehashPasswordAsync(SharpPlayer player, string plaintext) => ValueTask.CompletedTask;
	}

	private static async Task<(SurrealDatabase Database, ISurrealDbClient Client)> CreateMigratedAsync(string dbName)
	{
		// The embedded in-memory engine resolves through DI (same as Startup's AddSurreal +
		// AddInMemoryProvider); a bare `new SurrealDbClient(...)` cannot create mem:// engines.
		var services = new ServiceCollection();
		services.AddSurreal($"Endpoint=mem://;Namespace=sharpmush_idempotency;Database={dbName}")
			.AddInMemoryProvider();
		var client = services.BuildServiceProvider().GetRequiredService<ISurrealDbClient>();
		await client.Connect();

		var database = new SurrealDatabase(
			NullLogger<SurrealDatabase>.Instance, client, new NoopPasswordService());
		SurrealDatabase.ResetMigrationGateForTests();
		await database.Migrate();
		return (database, client);
	}

	private static async Task RemigrateAsync(SurrealDatabase database)
	{
		SurrealDatabase.ResetMigrationGateForTests();
		await database.Migrate();
	}

	private sealed record CountRow
	{
		public long cnt { get; set; }
	}

	private static async Task<long> CountAsync(ISurrealDbClient client, string query)
	{
		var response = await client.RawQuery(query);
		var rows = response.GetValue<List<CountRow>>(0);
		return rows is { Count: > 0 } ? rows[0].cnt : 0;
	}

	[Test]
	public async Task Migrate_RunTwice_DoesNotDuplicateSeedEdges()
	{
		var (database, client) = await CreateMigratedAsync("twice");

		await RemigrateAsync(database);

		await Assert.That(await CountAsync(client,
			"SELECT count() AS cnt FROM at_location WHERE in = player:1 GROUP ALL")).IsEqualTo(1L);
		await Assert.That(await CountAsync(client,
			"SELECT count() AS cnt FROM has_home WHERE in = player:7 GROUP ALL")).IsEqualTo(1L);
		await Assert.That(await CountAsync(client,
			"SELECT count() AS cnt FROM is_object WHERE in = room:0 GROUP ALL")).IsEqualTo(1L);
		await Assert.That(await CountAsync(client,
			"SELECT count() AS cnt FROM has_owner WHERE in = object:0 GROUP ALL")).IsEqualTo(1L);
		await Assert.That(await CountAsync(client,
			"SELECT count() AS cnt FROM has_flags WHERE in = object:1 AND out.name = 'WIZARD' GROUP ALL")).IsEqualTo(1L);
		// The visible production symptom: every object in Room Zero listed once per boot.
		await Assert.That(await CountAsync(client,
			"SELECT count() AS cnt FROM at_location WHERE out.key = 0 GROUP ALL")).IsEqualTo(2L);
	}

	[Test]
	public async Task UniqueEdgeIndexes_RejectDuplicateRelates()
	{
		var (database, client) = await CreateMigratedAsync("collapse");

		// The (in, out) UNIQUE indexes make the duplicate-edge bug class impossible at the source:
		// these RELATEs (what every pre-fix boot effectively did) must be rejected, not appended.
		for (var boot = 0; boot < 3; boot++)
		{
			await client.RawQuery("RELATE player:1->at_location->room:0");
			await client.RawQuery("RELATE object:1->has_flags->object_flag:WIZARD");
		}

		await Assert.That(await CountAsync(client,
			"SELECT count() AS cnt FROM at_location WHERE in = player:1 GROUP ALL")).IsEqualTo(1L);
		await Assert.That(await CountAsync(client,
			"SELECT count() AS cnt FROM has_flags WHERE in = object:1 AND out.name = 'WIZARD' GROUP ALL")).IsEqualTo(1L);

		// And a re-migration on top stays clean too.
		await RemigrateAsync(database);

		await Assert.That(await CountAsync(client,
			"SELECT count() AS cnt FROM at_location WHERE in = player:1 GROUP ALL")).IsEqualTo(1L);
	}

	[Test]
	public async Task Migrate_DoesNotMoveARelocatedObjectBack()
	{
		var (database, client) = await CreateMigratedAsync("relocated");

		// God walks to the Master Room (same delete-then-RELATE the runtime performs)...
		await client.RawQuery("DELETE at_location WHERE in = player:1");
		await client.RawQuery("RELATE player:1->at_location->room:2");
		// ...and a pre-fix boot re-added the seed edge on top of it.
		await client.RawQuery("RELATE player:1->at_location->room:0");

		await RemigrateAsync(database);

		await Assert.That(await CountAsync(client,
			"SELECT count() AS cnt FROM at_location WHERE in = player:1 GROUP ALL")).IsEqualTo(1L);
		await Assert.That(await CountAsync(client,
			"SELECT count() AS cnt FROM at_location WHERE in = player:1 AND out.key = 2 GROUP ALL")).IsEqualTo(1L);
	}

	[Test]
	public async Task Migrate_RestoresKeyCounterAboveExistingKeys()
	{
		var (database, client) = await CreateMigratedAsync("keycounter");

		// A world that grew past the seed: highest allocated dbref is #42.
		await client.RawQuery(
			"UPSERT object:42 SET name = 'Latest Creation', type = 'THING', creationTime = 0, modifiedTime = 0, locks = '{}', warnings = 0, key = 42");

		// The restart used to reset the allocator to 9, so the next @create collided with #10.
		await RemigrateAsync(database);

		await Assert.That(SurrealDatabase.PeekNextObjectKeyForTests).IsEqualTo(42);
	}
}
