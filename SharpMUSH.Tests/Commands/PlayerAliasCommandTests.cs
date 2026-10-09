using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// A player's alias set in game (#1499). In PennMUSH the ALIAS attribute is the alias list: every
/// write to it goes through <c>do_set_atr</c>'s ALIAS branch (<c>src/attrib.c:2268-2316</c>), which
/// validates it with <c>ok_player_alias</c> and rebuilds the player list, so <c>*alias</c>, page and
/// <c>alias()</c> all follow it. Each test makes its own players and aliases, since player names and
/// aliases are one namespace across the shared world.
/// </summary>
public class PlayerAliasCommandTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();

	private Task<TestIsolationHelpers.TestPlayer> PlayerAsync(string prefix)
		=> TestIsolationHelpers.CreateTestPlayerWithHandleAsync(WebAppFactoryArg.Services, Mediator, ConnectionService, prefix);

	/// <summary>A valid player name no other test holds: short enough for <c>player_name_len</c>.</summary>
	private static string Unique(string stem) => $"{stem}{Guid.NewGuid():N}"[..12];

	private async Task<string[]> RunAsync(TestIsolationHelpers.TestPlayer player, string command)
	{
		var before = NotificationsTo(player.DbRef).Length;
		await WebAppFactoryArg.CommandParserFor(player.DbRef, player.Handle)
			.CommandParse(player.Handle, ConnectionService, MarkupText.Plain(command));
		return [.. NotificationsTo(player.DbRef).Skip(before)];
	}

	private async Task<string> EvaluateAsync(TestIsolationHelpers.TestPlayer player, string expression)
		=> (await WebAppFactoryArg.CommandParserFor(player.DbRef, player.Handle)
			.EvaluateAsync(MarkupText.Plain(expression))).ToPlainText();

	private async Task<int[]> PlayersCalled(string name)
		=> [.. (await Mediator.CreateStream(new GetPlayerQuery(name)).ToArrayAsync()).Select(player => player.Object.DBRef.Number)];

	private string[] NotificationsTo(DBRef target) => [.. WebAppFactoryArg.Notifications.For(target)];

	[Test]
	public async ValueTask AtAliasMakesThePlayerFindableByIt()
	{
		var alice = await PlayerAsync("PaSet");
		var alias = Unique("Al");

		await Assert.That(await RunAsync(alice, $"@alias me={alias}")).Contains("Alias set.");

		await Assert.That(await PlayersCalled(alias.ToUpperInvariant())).IsEquivalentTo([alice.DbRef.Number]);
		await Assert.That(await EvaluateAsync(alice, $"num(*{alias})")).IsEqualTo($"#{alice.DbRef.Number}");
		await Assert.That(await EvaluateAsync(alice, "alias(me)")).IsEqualTo(alias);
		await Assert.That(await EvaluateAsync(alice, "fullalias(me)")).IsEqualTo(alias);
	}

	[Test]
	public async ValueTask PageReachesThePlayerByAlias()
	{
		var alice = await PlayerAsync("PaPageTo");
		var bob = await PlayerAsync("PaPageFrom");
		var alias = Unique("Pg");
		var message = Unique("Msg");
		await RunAsync(alice, $"@alias me={alias}");

		await RunAsync(bob, $"page {alias}={message}");

		await Assert.That(NotificationsTo(alice.DbRef)).Contains(line => line.Contains(message));
	}

	[Test]
	public async ValueTask AmpersandAliasSetsEveryAliasAndAliasFunctionsReadIt()
	{
		var alice = await PlayerAsync("PaMulti");
		var first = Unique("Fa");
		var second = Unique("Fb");

		await Assert.That(await RunAsync(alice, $"&ALIAS me={first};{second}")).Contains("Alias set.");

		await Assert.That(await PlayersCalled(second)).IsEquivalentTo([alice.DbRef.Number]);
		await Assert.That(await EvaluateAsync(alice, "alias(me)")).IsEqualTo(first);
		await Assert.That(await EvaluateAsync(alice, "fullalias(me)")).IsEqualTo($"{first};{second}");
	}

	[Test]
	public async ValueTask SetAndTheAliasFunctionWriteTheAliasToo()
	{
		var alice = await PlayerAsync("PaForms");
		var bySet = Unique("St");
		var byFunction = Unique("Fn");

		await Assert.That(await RunAsync(alice, $"@set me=ALIAS:{bySet}")).Contains("Alias set.");
		await Assert.That(await PlayersCalled(bySet)).IsEquivalentTo([alice.DbRef.Number]);

		await Assert.That(await RunAsync(alice, $"think [alias(me,{byFunction})]")).Contains("Alias set.");
		await Assert.That(await PlayersCalled(byFunction)).IsEquivalentTo([alice.DbRef.Number]);
		await Assert.That(await PlayersCalled(bySet)).IsEmpty();
	}

	[Test]
	public async ValueTask AnAliasAnotherPlayerAnswersToIsRefused()
	{
		var alice = await PlayerAsync("PaTaken");
		var bob = await PlayerAsync("PaTaker");
		var alias = Unique("Tk");
		await RunAsync(alice, $"@alias me={alias}");

		await Assert.That(await RunAsync(bob, $"@alias me={alice.Name}")).Contains($"'{alice.Name}' is not a valid alias.");
		var shouted = alias.ToUpperInvariant();
		await Assert.That(await RunAsync(bob, $"@alias me={shouted}")).Contains($"'{shouted}' is not a valid alias.");

		await Assert.That(await EvaluateAsync(bob, "hasattr(me,ALIAS)")).IsEqualTo("0");
		await Assert.That(await PlayersCalled(alias)).IsEquivalentTo([alice.DbRef.Number]);
	}

	[Test]
	public async ValueTask ANameAnotherPlayerHoldsAsAnAliasIsRefused()
	{
		var alice = await PlayerAsync("PaNameOwn");
		var bob = await PlayerAsync("PaNameTry");
		var alias = Unique("Nm");
		await RunAsync(alice, $"@alias me={alias}");

		await Assert.That(await RunAsync(bob, $"@name me={alias}")).Contains("You can't give a player that name or alias.");
		await Assert.That(await EvaluateAsync(bob, "name(me)")).IsEqualTo(bob.Name);
	}

	[Test]
	public async ValueTask TooManyOrNullAliasesAreRefused()
	{
		var alice = await PlayerAsync("PaLimits");
		var aliases = string.Join(';', Enumerable.Range(0, 4).Select(_ => Unique("Lm")));

		await Assert.That(await RunAsync(alice, $"&ALIAS me={aliases}")).Contains($"'{aliases}' contains too many aliases.");
		var withHole = $"{Unique("Nl")};;{Unique("Nl")}";
		await Assert.That(await RunAsync(alice, $"&ALIAS me={withHole}")).Contains("Null aliases are not valid.");
		await Assert.That(await EvaluateAsync(alice, "hasattr(me,ALIAS)")).IsEqualTo("0");
	}

	[Test]
	public async ValueTask ClearingTheAliasRemovesItFromLookups()
	{
		var alice = await PlayerAsync("PaClear");
		var alias = Unique("Cl");
		await RunAsync(alice, $"@alias me={alias}");

		await Assert.That(await RunAsync(alice, "&ALIAS me=")).Contains("'' is not a valid alias.");
		await Assert.That(await PlayersCalled(alias)).IsEquivalentTo([alice.DbRef.Number]);

		await Assert.That(await RunAsync(alice, "@alias me")).Contains("Alias removed.");
		await Assert.That(await PlayersCalled(alias)).IsEmpty();
		await Assert.That(await EvaluateAsync(alice, "alias(me)")).IsEqualTo(string.Empty);
	}

	[Test]
	public async ValueTask WipingTheAliasRemovesItFromLookups()
	{
		var alice = await PlayerAsync("PaWipe");
		var alias = Unique("Wp");
		await RunAsync(alice, $"@alias me={alias}");

		await RunAsync(alice, "@wipe me/ALIAS");

		await Assert.That(await PlayersCalled(alias)).IsEmpty();
	}

	[Test]
	public async ValueTask NameWithAliasesSetsThemAndATrailingSemicolonClearsThem()
	{
		var alice = await PlayerAsync("PaNameSet");
		var name = Unique("Nn");
		var first = Unique("Na");
		var second = Unique("Nb");

		var output = await RunAsync(alice, $"@name me={name};{first};{second}");
		await Assert.That(output).Contains("Alias set.");
		await Assert.That(output).Contains("Name set.");
		await Assert.That(await PlayersCalled(second)).IsEquivalentTo([alice.DbRef.Number]);
		await Assert.That(await EvaluateAsync(alice, "fullalias(me)")).IsEqualTo($"{first};{second}");

		await Assert.That(await RunAsync(alice, $"@name me={name};{Unique("Nc")};{Unique("Nd")};{Unique("Ne")}"))
			.Contains("Too many aliases.");

		output = await RunAsync(alice, $"@name me={name};");
		await Assert.That(output).Contains("Alias removed.");
		await Assert.That(await PlayersCalled(first)).IsEmpty();
		await Assert.That(await EvaluateAsync(alice, "hasattr(me,ALIAS)")).IsEqualTo("0");
	}

	[Test]
	public async ValueTask RenamingToTheAliasAndBackKeepsTheAlias()
	{
		var alice = await PlayerAsync("PaRename");
		var original = Unique("Or");
		var alias = Unique("Rn");
		await RunAsync(alice, $"@name me={original}");
		await RunAsync(alice, $"@alias me={alias}");

		await Assert.That(await RunAsync(alice, $"@name me={alias}")).Contains("Name set.");
		await Assert.That(await RunAsync(alice, $"@name me={original}")).Contains("Name set.");

		await Assert.That(await PlayersCalled(alias)).IsEquivalentTo([alice.DbRef.Number]);
		await Assert.That(await PlayersCalled(original)).IsEquivalentTo([alice.DbRef.Number]);
	}

	[Test]
	public async ValueTask DestroyingThePlayerFreesTheAlias()
	{
		// No connection for the doomed player: a handle bound to a deleted player outlives it.
		var aliceRef = await TestIsolationHelpers.CreateTestPlayerAsync(WebAppFactoryArg.Services, Mediator, "PaDestroy");
		var alice = (await Mediator.Send(new GetObjectNodeQuery(aliceRef))).Expect<AnySharpObject>();
		var bob = await PlayerAsync("PaInherit");
		var alias = Unique("Ds");
		(await WebAppFactoryArg.Services.GetRequiredService<IAttributeService>()
			.SetAttributeAsync(alice, alice, "ALIAS", MarkupText.Plain(alias))).Expect<Success>();
		await Assert.That(await PlayersCalled(alias)).IsEquivalentTo([aliceRef.Number]);

		await Mediator.Send(new DeleteObjectCommand(aliceRef));

		await Assert.That(await PlayersCalled(alias)).IsEmpty();
		await Assert.That(await RunAsync(bob, $"@alias me={alias}")).Contains("Alias set.");
	}
}
