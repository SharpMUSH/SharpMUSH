using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SharpMUSH.Database.SurrealDB;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;
using SurrealDb.Net;

namespace SharpMUSH.Tests.Database;

/// <summary>
/// A server restart against a persistent store re-runs <see cref="SurrealDatabase.Migrate"/> on a
/// database that already holds data. Restarts are simulated here by constructing a fresh
/// <see cref="SurrealDatabase"/> over the same client: applied migrations are recorded in the
/// database itself, so the second instance must re-apply nothing — no duplicated seed edges
/// (production showed one extra copy of every room-contents row and flag letter per boot) and no
/// dbref allocator reset below keys that already exist.
/// </summary>
public class SurrealMigrationIdempotencyTests
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

	private static async Task<(SurrealDatabase Database, ISurrealDbClient Client)> CreateMigratedAsync(string dbName)
	{
		// The embedded in-memory engine resolves through DI (same as Startup's AddSurreal +
		// AddInMemoryProvider); a bare `new SurrealDbClient(...)` cannot create mem:// engines.
		var services = new ServiceCollection();
		services.AddSurreal($"Endpoint=mem://;Namespace=sharpmush_idempotency;Database={dbName}")
			.AddInMemoryProvider();
		var client = services.BuildServiceProvider().GetRequiredService<ISurrealDbClient>();
		await client.Connect();

		var database = NewDatabase(client);
		await database.Migrate();
		return (database, client);
	}

	/// <summary>A fresh instance over the same client — what a server restart effectively is.</summary>
	private static SurrealDatabase NewDatabase(ISurrealDbClient client) =>
		new(NullLogger<SurrealDatabase>.Instance, client, new NoopPasswordService());

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
	public async Task Migrate_AfterRestart_DoesNotReapplyTheSeed()
	{
		var (_, client) = await CreateMigratedAsync("restart");

		await NewDatabase(client).Migrate();

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
	public async Task UniqueEdgeIndexes_RejectDuplicateAndSecondLocationRelates()
	{
		var (_, client) = await CreateMigratedAsync("uniqueness");

		// Exact duplicates (what every pre-fix boot appended) are rejected by the indexes...
		await client.RawQuery("RELATE player:1->at_location->room:0");
		await client.RawQuery("RELATE object:1->has_flags->object_flag:WIZARD");
		// ...and so is a SECOND location for the same object (UNIQUE on `in`): an object cannot be
		// in two rooms at once, whichever room the extra edge points at.
		await client.RawQuery("RELATE player:1->at_location->room:2");

		await Assert.That(await CountAsync(client,
			"SELECT count() AS cnt FROM at_location WHERE in = player:1 GROUP ALL")).IsEqualTo(1L);
		await Assert.That(await CountAsync(client,
			"SELECT count() AS cnt FROM has_flags WHERE in = object:1 AND out.name = 'WIZARD' GROUP ALL")).IsEqualTo(1L);
	}

	[Test]
	public async Task Migrate_AfterRestart_DoesNotMoveARelocatedObjectBack()
	{
		var (_, client) = await CreateMigratedAsync("relocated");

		// God walks to the Master Room (the same delete-then-RELATE the runtime performs)...
		await client.RawQuery("DELETE at_location WHERE in = player:1");
		await client.RawQuery("RELATE player:1->at_location->room:2");

		// ...and a restart re-applies nothing: the seed migration is already recorded.
		await NewDatabase(client).Migrate();

		await Assert.That(await CountAsync(client,
			"SELECT count() AS cnt FROM at_location WHERE in = player:1 GROUP ALL")).IsEqualTo(1L);
		await Assert.That(await CountAsync(client,
			"SELECT count() AS cnt FROM at_location WHERE in = player:1 AND out.key = 2 GROUP ALL")).IsEqualTo(1L);
	}

	[Test]
	public async Task Migrate_AfterRestart_AllocatesKeysAboveExistingObjects()
	{
		var (_, client) = await CreateMigratedAsync("keycounter");

		// A world that grew past the seed: highest allocated dbref is #42.
		await client.RawQuery(
			"UPSERT object:42 SET name = 'Latest Creation', type = 'THING', creationTime = 0, modifiedTime = 0, locks = '{}', warnings = 0, key = 42");

		// A restart used to reset the allocator to 9, so the next @create overwrote object #10.
		var restarted = NewDatabase(client);
		await restarted.Migrate();
		var created = await restarted.CreatePlayerAsync(
			"Newcomer", "password", new DBRef(0), new DBRef(0), quota: 1);

		await Assert.That(created.Number).IsEqualTo(43);
	}
}
