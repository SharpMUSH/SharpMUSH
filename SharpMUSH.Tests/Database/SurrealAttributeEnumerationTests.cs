using System.Threading;
using DotNext.Threading;
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
/// LongName is an invariant: an attribute is addressed by its fully-qualified backtick path, so
/// every attribute — leaf or auto-created branch parent — carries a non-empty LongName on the
/// production (SurrealDB) provider. (The separate <c>examine</c>-truncation bug, where a branch
/// parent's null owner aborted the listing, is covered by <c>ExamineNullOwnerTests</c>.)
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

	private static async Task<(SurrealDatabase Db, ISurrealDbClient Client)> CreateFreshMigratedAsync(string dbName)
	{
		var services = new ServiceCollection();
		services.AddSurreal($"Endpoint=mem://;Namespace=sharpmush_attrenum;Database={dbName}")
			.AddInMemoryProvider();
		var client = services.BuildServiceProvider().GetRequiredService<ISurrealDbClient>();
		await client.Connect();
		var db = new SurrealDatabase(NullLogger<SurrealDatabase>.Instance, client, new NoopPasswordService());
		await db.Migrate();
		return (db, client);
	}


	[Test]
	public async Task AutoCreatedBranchParent_HasAnOwner()
	{
		var (db, _) = await CreateFreshMigratedAsync("branchowner");
		var god = (await db.GetObjectNodeAsync(new DBRef(1))).AsPlayer;
		var godRef = new DBRef(god.Object.Key);

		// Setting only the child auto-creates BRANCH as a parent node; it must be owned, like the leaf.
		await db.SetAttributeAsync(godRef, ["BRANCH", "CHILD"], MModule.single("child"), god);

		var obj = await db.GetObjectNodeAsync(godRef);
		var branch = (await obj.Object()!.Attributes.Value.ToListAsync()).Single(a => a.Name == "BRANCH");
		var owner = await branch.Owner.WithCancellation(CancellationToken.None);

		await Assert.That(owner).IsNotNull()
			.Because("every attribute, including an auto-created branch parent, must have an owner");
	}

	[Test]
	public async Task EveryAttribute_LeafBranchParentAndChild_HasItsFullyQualifiedLongName()
	{
		var (db, _) = await CreateFreshMigratedAsync("longnameinvariant");
		var god = (await db.GetObjectNodeAsync(new DBRef(1))).AsPlayer;
		var godRef = new DBRef(god.Object.Key);

		await db.SetAttributeAsync(godRef, ["LEAFONE"], MModule.single("one"), god);
		await db.SetAttributeAsync(godRef, ["BRANCH", "CHILD"], MModule.single("child"), god);

		var obj = await db.GetObjectNodeAsync(godRef);
		var all = await db.GetAttributeAsync(godRef, ["BRANCH", "CHILD"])!.ToListAsync();
		var topLevel = await obj.Object()!.Attributes.Value.ToListAsync();

		await Assert.That(all.Select(a => a.LongName)).IsEquivalentTo(new[] { "BRANCH", "BRANCH`CHILD" });

		var branch = topLevel.Single(a => a.Name == "BRANCH");
		await Assert.That(branch.LongName).IsEqualTo("BRANCH")
			.Because("an auto-created branch parent must carry its own longName, never empty");
	}
}
