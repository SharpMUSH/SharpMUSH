using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.DatabaseConversion;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// A player imported with any password shape PennMUSH accepts can log in with that password, and
/// the login moves the password onto SharpMUSH's own hash.
/// </summary>
/// <remarks>The vectors, all for <c>hunter2</c>, are the ones <see cref="PennMUSHLegacyPasswordTests"/> documents.</remarks>
public class PennMUSHPasswordImportTests
{
	private const string Password = "hunter2";

	[Test]
	[Arguments("Formatted", "2:sha512:9K276ed5a8de5d5b3333cac32f33cd10b8136792c98af63dc8ab0369740e349491d6e345b20a35c2e89d5ccdaa85dfb52fea87b2288659756c9b1be860eb477e8c:1789141638")]
	[Arguments("Unsalted", "1:sha1:f3bbbd66a63d4bf1747940578ec3d0103530e21d:0")]
	[Arguments("Sha0", "XX25278519322861399308")]
	[Arguments("Crypt", "XXuOowDRD5rnE")]
	[Arguments("Mux", "$SHA1$b3JhY2xlISE=$ZpP9asZ4IHXYH2Xear7C9UcerSw=")]
	[Arguments("Plain", Password)]
	public async Task AnImportedLegacyPasswordLogsInAndIsRehashed(string kind, string stored)
	{
		await using var world = await IsolatedImportWorld.CreateAsync();
		var name = $"Legacy{kind}";

		var result = await world.Converter.ConvertDatabaseAsync(new PennMUSHDatabase
		{
			Version = "Password Fixture",
			Objects = [new PennMUSHObject { DBRef = 20, Name = name, Type = PennMUSHObjectType.Player, Password = stored }]
		});
		await Assert.That(result.Errors).IsEmpty();

		var player = await FindPlayerAsync(world, name);
		var key = $"#{player.Object.Key}:{player.Object.CreationTime}";

		await Assert.That(player.PasswordHash).IsEqualTo(stored)
			.Because("the importer stores the source value verbatim");
		await Assert.That(world.Passwords.PasswordIsValid(key, Password, player.PasswordHash)).IsTrue();
		await Assert.That(world.Passwords.PasswordIsValid(key, "wrongpass", player.PasswordHash)).IsFalse();
		await Assert.That(world.Passwords.NeedsRehash(player.PasswordHash)).IsTrue();

		await world.Passwords.RehashPasswordAsync(player, Password);

		var rehashed = await FindPlayerAsync(world, name);
		await Assert.That(rehashed.PasswordHash).IsNotEqualTo(stored);
		await Assert.That(world.Passwords.PasswordIsValid(key, Password, rehashed.PasswordHash)).IsTrue();
		await Assert.That(world.Passwords.NeedsRehash(rehashed.PasswordHash)).IsFalse();
	}

	/// <summary>
	/// A source player with no password is imported locked, and stays locked: nothing it can type,
	/// the importer's own marker included, matches.
	/// </summary>
	[Test]
	public async Task AnImportedPlayerWithoutAPasswordCannotLogIn()
	{
		await using var world = await IsolatedImportWorld.CreateAsync();

		await world.Converter.ConvertDatabaseAsync(new PennMUSHDatabase
		{
			Version = "Password Fixture",
			Objects = [new PennMUSHObject { DBRef = 21, Name = "Passwordless", Type = PennMUSHObjectType.Player }]
		});

		var player = await FindPlayerAsync(world, "Passwordless");
		var key = $"#{player.Object.Key}:{player.Object.CreationTime}";

		await Assert.That(player.PasswordHash).IsNotEmpty()
			.Because("an empty hash is a passwordless character, which logs in with anything");
		foreach (var attempt in (string[])[Password, player.PasswordHash, "NEEDS_RESET", ""])
		{
			await Assert.That(world.Passwords.PasswordIsValid(key, attempt, player.PasswordHash)).IsFalse()
				.Because($"'{attempt}' logged in to a player imported without a password");
		}
	}

	private static async Task<SharpPlayer> FindPlayerAsync(IsolatedImportWorld world, string name)
	{
		var obj = await world.Database.GetAllObjectsAsync().SingleAsync(o => o.Name == name);
		return (await world.Database.GetObjectNodeAsync(new DBRef(obj.Key))).Expect<SharpPlayer>();
	}
}
