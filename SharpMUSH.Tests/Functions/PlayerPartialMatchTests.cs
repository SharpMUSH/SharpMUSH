using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Functions;

/// <summary>
/// <c>*name</c> falls back on a unique prefix of a connected player the looker can see when no player
/// has that exact name or alias (<c>match_player</c>, <c>src/match.c:276-292</c>, calling
/// <c>visible_short_page</c>, <c>src/bsd.c:6416</c>), and <c>num()</c> answers a miss with a bare dbref
/// (<c>fun_num</c> is <c>safe_dbref(match_thing(...))</c>) (#1502).
/// </summary>
public class PlayerPartialMatchTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory Factory { get; init; }

	private IConnectionService Connections => Factory.Services.GetRequiredService<IConnectionService>();
	private IMediator Mediator => Factory.Services.GetRequiredService<IMediator>();

	/// <summary>A prefix no other player's name starts with.</summary>
	private static string UniquePrefix(string stem) => $"{stem}{Guid.NewGuid():N}"[..20];

	private Task<TestIsolationHelpers.TestPlayer> Connected(string prefix)
		=> TestIsolationHelpers.CreateTestPlayerWithHandleAsync(Factory.Services, Mediator, Connections, prefix);

	private async Task<string> Evaluate(DBRef executor, string expression)
		=> (await Factory.FunctionParserFor(executor).FunctionParse(MarkupText.Plain(expression)))!.Message!.ToPlainText();

	[Test]
	public async Task APrefixOfAConnectedPlayerFindsThem()
	{
		var prefix = UniquePrefix("SpFind");
		var target = await Connected(prefix);
		var looker = await Connected("SpLooker");

		await Assert.That(await Evaluate(looker.DbRef, $"num(*{prefix})")).IsEqualTo($"#{target.DbRef.Number}");
	}

	[Test]
	public async Task APrefixOfTwoConnectedPlayersIsAmbiguous()
	{
		var prefix = UniquePrefix("SpTwo");
		await Connected(prefix);
		await Connected(prefix);
		var looker = await Connected("SpLooker");

		await Assert.That(await Evaluate(looker.DbRef, $"num(*{prefix})")).IsEqualTo("#-2");
	}

	/// <summary>visible_short_page: a DARK player is not found by a mortal, but is by a wizard (Priv_Who).</summary>
	[Test]
	public async Task ADarkPlayerIsFoundOnlyByThePrivileged()
	{
		var prefix = UniquePrefix("SpDark");
		var target = await Connected(prefix);
		var looker = await Connected("SpLooker");
		await Factory.CommandParser.CommandParse(1, Connections, MarkupText.Plain($"@set #{target.DbRef.Number}=DARK"));

		await Assert.That(await Evaluate(looker.DbRef, $"num(*{prefix})")).IsEqualTo("#-1");
		await Assert.That(await Evaluate(new DBRef(1), $"num(*{prefix})")).IsEqualTo($"#{target.DbRef.Number}");
	}

	[Test]
	public async Task NumAnswersAMissWithABareDbref()
	{
		var looker = await Connected("SpLooker");

		await Assert.That(await Evaluate(looker.DbRef, $"num(*{UniquePrefix("SpNobody")})")).IsEqualTo("#-1");
	}
}
