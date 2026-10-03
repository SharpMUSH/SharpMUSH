namespace SharpMUSH.Tests.Functions;

/// <summary>
/// <c>*name</c> falls back to a unique prefix of a connected player's name when no player holds the
/// name or alias: PennMUSH's <c>match_player</c> (<c>src/match.c:275-292</c>) calling
/// <c>visible_short_page</c> (<c>src/bsd.c:6416</c>). #1502.
/// </summary>
public class PlayerPartialMatchTests : ServerTestBase
{
	/// <summary>A name stem no other connected player's name starts with, this run or a retry of it.</summary>
	private static string Stem() => $"Pm{Guid.NewGuid():N}"[..12];

	private Task<TestIsolationHelpers.TestPlayer> Connected(string prefix)
		=> TestIsolationHelpers.CreateTestPlayerWithHandleAsync(WebAppFactoryArg.Services, Mediator, ConnectionService, prefix);

	[Test]
	public async Task AUniquePrefixOfAConnectedPlayerMatches()
	{
		var stem = Stem();
		var target = await Connected(stem);
		var looker = await Connected("PmLook");

		await Assert.That(await EvalAs(looker.DbRef, $"num(*{stem})")).IsEqualTo($"#{target.DbRef.Number}");
		await Assert.That(await EvalAs(looker.DbRef, $"num(*{stem.ToUpperInvariant()})")).IsEqualTo($"#{target.DbRef.Number}")
			.Because("short_page compares with string_prefix, which ignores case");
		await Assert.That(await EvalAs(looker.DbRef, $"num(*{target.Name})")).IsEqualTo($"#{target.DbRef.Number}");
	}

	[Test]
	public async Task APrefixTwoConnectedPlayersShareIsNotAMatch()
	{
		var stem = Stem();
		var first = await Connected($"{stem}A");
		var second = await Connected($"{stem}B");
		var looker = await Connected("PmLook");

		var found = await EvalAs(looker.DbRef, $"num(*{stem})");

		await Assert.That(found).IsNotEqualTo($"#{first.DbRef.Number}");
		await Assert.That(found).IsNotEqualTo($"#{second.DbRef.Number}");
		await Assert.That(found).StartsWith("#-");
	}

	[Test]
	public async Task APlayerWhoIsNotConnectedIsOnlyFoundByTheWholeName()
	{
		var stem = Stem();
		var offline = await TestIsolationHelpers.CreateTestPlayerAsync(WebAppFactoryArg.Services, Mediator, stem);
		var looker = await Connected("PmLook");

		await Assert.That(await EvalAs(looker.DbRef, $"num(*{stem})")).StartsWith("#-1");
		await Assert.That(await EvalAs(looker.DbRef, $"num(*#{offline.Number})")).IsEqualTo($"#{offline.Number}")
			.Because("match_player hands what follows the * to lookup_player, which takes a dbref");
	}
}
