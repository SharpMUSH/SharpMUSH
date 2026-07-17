using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SharpMUSH.Database.SurrealDB;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;
using SurrealDb.Net;

namespace SharpMUSH.Tests.Database;

/// <summary>
/// LongName is an invariant: a <see cref="SharpAttribute"/> is addressed by its fully-qualified
/// backtick path, so every attribute — leaf, auto-created branch parent, or child — carries a
/// non-empty LongName. The type is non-nullable and no read-time fallback masks a missing value;
/// the write path must populate it. These tests pin that on the production (SurrealDB) provider.
/// </summary>
public class SurrealAttributeEnumerationTests
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

	private static async Task<SurrealDatabase> CreateFreshMigratedAsync(string dbName)
	{
		var services = new ServiceCollection();
		services.AddSurreal($"Endpoint=mem://;Namespace=sharpmush_attrenum;Database={dbName}")
			.AddInMemoryProvider();
		var client = services.BuildServiceProvider().GetRequiredService<ISurrealDbClient>();
		await client.Connect();
		var db = new SurrealDatabase(NullLogger<SurrealDatabase>.Instance, client, new NoopPasswordService());
		await db.Migrate();
		return db;
	}

	[Test]
	public async Task EveryAttribute_LeafBranchParentAndChild_HasItsFullyQualifiedLongName()
	{
		var db = await CreateFreshMigratedAsync("longnameinvariant");
		var god = (await db.GetObjectNodeAsync(new DBRef(1))).AsPlayer;
		var godRef = new DBRef(god.Object.Key);

		await db.SetAttributeAsync(godRef, ["LEAFONE"], MModule.single("one"), god);
		// Setting only the child auto-creates BRANCH as a parent node — it too must carry a longName.
		await db.SetAttributeAsync(godRef, ["BRANCH", "CHILD"], MModule.single("child"), god);

		var obj = await db.GetObjectNodeAsync(godRef);
		var all = await db.GetAttributeAsync(godRef, ["BRANCH", "CHILD"])!.ToListAsync();
		var topLevel = await obj.Object()!.Attributes.Value.ToListAsync();

		// The full backtick path down to the child resolves, each hop non-empty and correct.
		await Assert.That(all.Select(a => a.LongName)).IsEquivalentTo(new[] { "BRANCH", "BRANCH`CHILD" });

		var leaf = topLevel.Single(a => a.Name == "LEAFONE");
		var branch = topLevel.Single(a => a.Name == "BRANCH");
		await Assert.That(leaf.LongName).IsEqualTo("LEAFONE");
		await Assert.That(branch.LongName).IsEqualTo("BRANCH")
			.Because("an auto-created branch parent must carry its own longName, never empty");
	}
}
