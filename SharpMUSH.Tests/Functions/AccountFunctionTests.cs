using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Functions;

/// <summary>
/// account() and accountchars(): which account holds a character, open to players.view and to the
/// characters on that account. The account-value functions and @accval: JSON kept on that account, read the
/// same way, written with players.moderate, and locked the way @atrlock locks an attribute.
/// </summary>
public class AccountFunctionTests : ServerTestBase
{
	private TestIsolationHelpers.TestPlayer _first = null!;
	private TestIsolationHelpers.TestPlayer _second = null!;
	private TestIsolationHelpers.TestPlayer _stranger = null!;
	private TestIsolationHelpers.TestPlayer _wizard = null!;
	private SharpAccount _account = null!;
	private readonly List<TestIsolationHelpers.TestPlayer> _extra = [];

	private IAccountService Accounts => WebAppFactoryArg.Services.GetRequiredService<IAccountService>();

	[Before(Test)]
	public async Task LinkTwoCharacters()
	{
		_first = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(WebAppFactoryArg.Services, Mediator, ConnectionService, "AcctFirst");
		_second = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(WebAppFactoryArg.Services, Mediator, ConnectionService, "AcctSecond");
		_stranger = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(WebAppFactoryArg.Services, Mediator, ConnectionService, "AcctStranger");
		_wizard = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(WebAppFactoryArg.Services, Mediator, ConnectionService, "AcctWiz");
		var wizard = (await Mediator.Send(new GetObjectNodeQuery(_wizard.DbRef))).Expect<SharpPlayer>();
		await Mediator.Send(new SetObjectFlagCommand(wizard, (await Mediator.Send(new GetObjectFlagQuery("WIZARD")))!));

		var username = TestIsolationHelpers.GenerateUniqueName("acct").ToLowerInvariant();
		_account = (await Accounts.CreateAccountAsync(username, null, "correct horse battery")).Expect<SharpAccount>();
		foreach (var player in new[] { _first, _second })
			await Accounts.AttachCharacterAsync(_account.Id!, (await Mediator.Send(new GetObjectNodeQuery(player.DbRef))).Expect<SharpPlayer>());
	}

	[After(Test)]
	public async Task Disconnect()
	{
		foreach (var player in new[] { _first, _second, _stranger, _wizard }.Concat(_extra))
			await ConnectionService.Disconnect(player.Handle);
	}

	private static string Ref(TestIsolationHelpers.TestPlayer player) => $"#{player.DbRef.Number}";

	[Test]
	public async Task StaffReadTheAccountIdAndName()
	{
		await Assert.That(await EvalAs(_wizard.DbRef, $"account({Ref(_first)})")).IsEqualTo(_account.Id);
		await Assert.That(await EvalAs(_wizard.DbRef, $"account({Ref(_first)},name)")).IsEqualTo(_account.Username);
		await Assert.That(await EvalAs(_wizard.DbRef, $"account({Ref(_stranger)})")).IsEqualTo(string.Empty);
	}

	[Test]
	public async Task ACharacterReadsItsOwnAccountIdButNotItsName()
	{
		await Assert.That(await EvalAs(_second.DbRef, $"account({Ref(_first)})")).IsEqualTo(_account.Id);
		await Assert.That(await EvalAs(_second.DbRef, $"account({Ref(_first)},name)")).IsEqualTo("#-1 PERMISSION DENIED: NEEDS PLAYERS.VIEW");
	}

	[Test]
	public async Task SomeoneElseIsRefused()
	{
		await Assert.That(await EvalAs(_stranger.DbRef, $"account({Ref(_first)})")).IsEqualTo("#-1 PERMISSION DENIED: NEEDS PLAYERS.VIEW");
		await Assert.That(await EvalAs(_stranger.DbRef, $"accountchars({Ref(_first)})")).IsEqualTo("#-1 PERMISSION DENIED: NEEDS PLAYERS.VIEW");
	}

	[Test]
	public async Task AccountCharsListsEveryCharacterOnTheAccount()
	{
		var expected = $"{await EvalAs(_wizard.DbRef, $"objid({Ref(_first)})")} {await EvalAs(_wizard.DbRef, $"objid({Ref(_second)})")}";

		await Assert.That(await EvalAs(_first.DbRef, $"accountchars({Ref(_second)})")).IsEqualTo(expected);
		await Assert.That(await EvalAs(_wizard.DbRef, $"accountchars({Ref(_first)})")).IsEqualTo(expected);
	}

	[Test]
	public async Task ACharacterOnNoAccountListsOnlyItself()
		=> await Assert.That(await EvalAs(_stranger.DbRef, $"accountchars({Ref(_stranger)})"))
			.IsEqualTo(await EvalAs(_stranger.DbRef, $"objid({Ref(_stranger)})"));

	[Test]
	public async Task AnUnknownFieldIsAnError()
		=> await Assert.That(await EvalAs(_wizard.DbRef, $"account({Ref(_first)},email)")).IsEqualTo("#-1 FIELD MUST BE ID OR NAME");

	[Test]
	public async Task StaffSetAValueThatTheAccountsCharactersRead()
	{
		await Assert.That(await EvalAs(_wizard.DbRef, $"setaccval({Ref(_first)}/Stats,json(object,xp,12))")).IsEqualTo(string.Empty);

		await Assert.That(await EvalAs(_second.DbRef, $"getaccval({Ref(_first)}/stats)")).IsEqualTo("{\"xp\":12}");
		await Assert.That(await EvalAs(_first.DbRef, "getaccval(me/STATS)")).IsEqualTo("{\"xp\":12}");
		await Assert.That(await EvalAs(_wizard.DbRef, $"laccval({Ref(_second)})")).IsEqualTo("stats");
	}

	[Test]
	public async Task ValuesListInAsciiOrderAndFilterByWildcard()
	{
		foreach (var name in new[] { "zeta", "alpha", "mid.dle", "almond" })
			await EvalAs(_wizard.DbRef, $"setaccval({Ref(_first)}/{name},1)");

		await Assert.That(await EvalAs(_first.DbRef, "laccval(me)")).IsEqualTo("almond alpha mid.dle zeta");
		await Assert.That(await EvalAs(_first.DbRef, "laccval(me/AL*)")).IsEqualTo("almond alpha");
	}

	[Test]
	public async Task AnEmptyValueRemovesIt()
	{
		await EvalAs(_wizard.DbRef, $"setaccval({Ref(_first)}/gone,json(array,1,2))");
		await Assert.That(await EvalAs(_wizard.DbRef, $"setaccval({Ref(_first)}/gone)")).IsEqualTo(string.Empty);

		await Assert.That(await EvalAs(_wizard.DbRef, $"getaccval({Ref(_first)}/gone)")).IsEqualTo(string.Empty);
		await Assert.That(await EvalAs(_wizard.DbRef, $"laccval({Ref(_first)})")).IsEqualTo(string.Empty);
		await Assert.That(await EvalAs(_wizard.DbRef, $"setaccval({Ref(_first)}/never)")).IsEqualTo(string.Empty);
	}

	[Test]
	public async Task OnlyPlayersModerateWritesValues()
	{
		await Assert.That(await EvalAs(_first.DbRef, "setaccval(me/mine,1)")).IsEqualTo("#-1 PERMISSION DENIED: NEEDS PLAYERS.MODERATE");
		await Assert.That(await EvalAs(_first.DbRef, "laccval(me)")).IsEqualTo(string.Empty);
	}

	[Test]
	public async Task SomeoneElseCannotReadTheValues()
	{
		await EvalAs(_wizard.DbRef, $"setaccval({Ref(_first)}/secret,true)");

		await Assert.That(await EvalAs(_stranger.DbRef, $"getaccval({Ref(_first)}/secret)")).IsEqualTo("#-1 PERMISSION DENIED: NEEDS PLAYERS.VIEW");
		await Assert.That(await EvalAs(_stranger.DbRef, $"laccval({Ref(_first)})")).IsEqualTo("#-1 PERMISSION DENIED: NEEDS PLAYERS.VIEW");
		await Assert.That(await EvalAs(_stranger.DbRef, $"accvallock({Ref(_first)}/secret)")).IsEqualTo("#-1 PERMISSION DENIED: NEEDS PLAYERS.VIEW");
	}

	[Test]
	public async Task AValueMustBeJson()
	{
		await Assert.That(await EvalAs(_wizard.DbRef, $"setaccval({Ref(_first)}/bad,xp: 12)")).IsEqualTo("#-1 INVALID JSON");
		await Assert.That(await EvalAs(_wizard.DbRef, $"laccval({Ref(_first)})")).IsEqualTo(string.Empty);
	}

	[Test]
	public async Task AValueNameIsPrintableAsciiWithoutSpacesOrSlashes()
	{
		const string refused = "#-1 ACCOUNT VALUE NAME MUST BE 1 TO 64 PRINTABLE ASCII CHARACTERS WITHOUT SPACES OR /";
		await Assert.That(await EvalAs(_wizard.DbRef, $"setaccval({Ref(_first)}/two words,1)")).IsEqualTo(refused);
		await Assert.That(await EvalAs(_wizard.DbRef, $"setaccval({Ref(_first)}/caf\u00e9,1)")).IsEqualTo(refused);
		await Assert.That(await EvalAs(_wizard.DbRef, $"setaccval({Ref(_first)}/a/b,1)")).IsEqualTo(refused);
		await Assert.That(await EvalAs(_wizard.DbRef, $"setaccval({Ref(_first)},1)")).IsEqualTo(refused);
		await Assert.That(await EvalAs(_wizard.DbRef, $"getaccval({Ref(_first)}/{new string('a', 65)})")).IsEqualTo(refused);
	}

	[Test]
	public async Task APlayerOnNoAccountHasNoValues()
	{
		await Assert.That(await EvalAs(_wizard.DbRef, $"setaccval({Ref(_stranger)}/x,1)")).IsEqualTo("#-1 PLAYER IS ON NO ACCOUNT");
		await Assert.That(await EvalAs(_stranger.DbRef, "laccval(me)")).IsEqualTo(string.Empty);
		await Assert.That(await EvalAs(_stranger.DbRef, "getaccval(me/x)")).IsEqualTo(string.Empty);
	}

	[Test]
	public async Task ALockedValueChangesOnlyForItsOwnerAndOverride()
	{
		var moderator = await ModeratorAsync();
		await EvalAs(_wizard.DbRef, $"setaccval({Ref(_first)}/rank,1)");
		await Assert.That(await EvalAs(_wizard.DbRef, $"accvallock({Ref(_first)}/rank)")).IsEqualTo("0");

		await Assert.That(await EvalAs(moderator.DbRef, $"accvallock({Ref(_first)}/rank,on)")).IsEqualTo(string.Empty);
		await Assert.That(await EvalAs(_first.DbRef, "accvallock(me/rank)")).IsEqualTo("1");

		var other = await ModeratorAsync();
		const string locked = "#-1 PERMISSION DENIED: ACCOUNT VALUE IS LOCKED";
		await Assert.That(await EvalAs(other.DbRef, $"setaccval({Ref(_first)}/rank,2)")).IsEqualTo(locked);
		await Assert.That(await EvalAs(other.DbRef, $"setaccval({Ref(_first)}/rank)")).IsEqualTo(locked);
		await Assert.That(await EvalAs(other.DbRef, $"accvallock({Ref(_first)}/rank,off)")).IsEqualTo(locked);
		await Assert.That(await EvalAs(other.DbRef, $"getaccval({Ref(_first)}/rank)")).IsEqualTo("1");

		await Assert.That(await EvalAs(moderator.DbRef, $"setaccval({Ref(_first)}/rank,3)")).IsEqualTo(string.Empty);
		await Assert.That(await EvalAs(_wizard.DbRef, $"setaccval({Ref(_first)}/rank,4)")).IsEqualTo(string.Empty);
		await Assert.That(await EvalAs(_wizard.DbRef, $"accvallock({Ref(_first)}/rank,off)")).IsEqualTo(string.Empty);
		await Assert.That(await EvalAs(other.DbRef, $"setaccval({Ref(_first)}/rank,5)")).IsEqualTo(string.Empty);
	}

	[Test]
	public async Task LockingAnotherOwnersValueTakesItOver()
	{
		var moderator = await ModeratorAsync();
		await EvalAs(_wizard.DbRef, $"setaccval({Ref(_first)}/badge,1)");
		await EvalAs(_wizard.DbRef, $"accvallock({Ref(_first)}/badge,on)");
		await EvalAs(_wizard.DbRef, $"accvallock({Ref(_first)}/badge,off)");

		await Assert.That(await EvalAs(moderator.DbRef, $"accvallock({Ref(_first)}/badge,on)")).IsEqualTo(string.Empty);
		await Assert.That(await EvalAs(moderator.DbRef, $"setaccval({Ref(_first)}/badge,2)")).IsEqualTo(string.Empty);
	}

	[Test]
	public async Task LockingNeedsPlayersModerateAndAValue()
	{
		await EvalAs(_wizard.DbRef, $"setaccval({Ref(_first)}/held,1)");

		await Assert.That(await EvalAs(_first.DbRef, "accvallock(me/held,on)")).IsEqualTo("#-1 PERMISSION DENIED: NEEDS PLAYERS.MODERATE");
		await Assert.That(await EvalAs(_wizard.DbRef, $"accvallock({Ref(_first)}/none,on)")).IsEqualTo("#-1 NO SUCH ACCOUNT VALUE");
		await Assert.That(await EvalAs(_wizard.DbRef, $"accvallock({Ref(_first)}/none)")).IsEqualTo("#-1 NO SUCH ACCOUNT VALUE");
		await Assert.That(await EvalAs(_wizard.DbRef, $"accvallock({Ref(_first)}/held,maybe)")).IsEqualTo("#-1 INVALID VALUE");
	}

	[Test]
	public async Task TheCommandListsShowsSetsLocksAndClears()
	{
		var name = TestIsolationHelpers.GenerateUniqueName("v").ToLowerInvariant();
		await Assert.That(await CmdAs(_wizard.DbRef, _wizard.Handle, $"@accval {Ref(_first)}/{name}=[json(object,theme,json(string,dark))]")).IsEqualTo("Set.");
		await Assert.That(await CmdAs(_wizard.DbRef, _wizard.Handle, $"@accval {Ref(_first)}/{name}")).IsEqualTo("{\"theme\":\"dark\"}");
		await Assert.That(await CmdAs(_wizard.DbRef, _wizard.Handle, $"@accval/lock {Ref(_first)}/{name}")).IsEqualTo("Locked.");

		var listing = await CmdAs(_second.DbRef, _second.Handle, $"@accval {Ref(_first)}");
		await Assert.That(listing).Contains(name);
		await Assert.That(listing).Contains("yes");

		await Assert.That(await CmdAs(_first.DbRef, _first.Handle, $"@accval me/{name}=2")).IsEqualTo("#-1 PERMISSION DENIED: NEEDS PLAYERS.MODERATE");
		await Assert.That(await CmdAs(_wizard.DbRef, _wizard.Handle, $"@accval/unlock {Ref(_first)}/{name}")).IsEqualTo("Unlocked.");
		await Assert.That(await CmdAs(_wizard.DbRef, _wizard.Handle, $"@accval {Ref(_first)}/{name}=")).IsEqualTo("Cleared.");
		await Assert.That(await EvalAs(_wizard.DbRef, $"getaccval({Ref(_first)}/{name})")).IsEqualTo(string.Empty);
	}

	[Test]
	public async Task TheStoreRefusesANewValuePastTheLimitButStillReplacesOne()
	{
		var store = WebAppFactoryArg.Services.GetRequiredService<IAccountValueStore>();
		await store.SetAccountValueAsync(_account.Id!, "one", "1", "#1", false, 2);
		await store.SetAccountValueAsync(_account.Id!, "two", "2", "#1", false, 2);

		await Assert.That(await store.SetAccountValueAsync(_account.Id!, "three", "3", "#1", false, 2) is AccountValuesFull).IsTrue();
		await Assert.That(await store.SetAccountValueAsync(_account.Id!, "two", "22", "#1", false, 2) is Success).IsTrue();
		await Assert.That(await store.GetAccountValueAsync(_account.Id!, "two") is AccountValue { Json: "22", Owner: "#1", Locked: false }).IsTrue();
		await Assert.That(await store.SetAccountValueAsync("node_accounts/no-such-account", "one", "1", "#1", false, 2) is NotFound).IsTrue();
	}

	[Test]
	public async Task TheHelpExamplesHold()
	{
		var who = Ref(_first);
		await EvalAs(_wizard.DbRef, $"setaccval({who}/stats, json(object, xp, 12))");
		await Assert.That(await EvalAs(_wizard.DbRef, $"getaccval({who}/stats)")).IsEqualTo("{\"xp\":12}");
		await Assert.That(await EvalAs(_wizard.DbRef, $"json_query(getaccval({who}/stats), get, xp)")).IsEqualTo("12");

		await EvalAs(_wizard.DbRef, $"setaccval({who}/settings, json(object, theme, json(string, dark)))");
		await EvalAs(_wizard.DbRef, $"setaccval({who}/unlocks, json(array))");
		await Assert.That(await EvalAs(_wizard.DbRef, $"getaccval({who}/settings)")).IsEqualTo("{\"theme\":\"dark\"}");
		await Assert.That(await EvalAs(_first.DbRef, "json_query(getaccval(%#/settings), get, theme)")).IsEqualTo("\"dark\"");
		await Assert.That(await EvalAs(_first.DbRef, "laccval(me)")).IsEqualTo("settings stats unlocks");
		await Assert.That(await EvalAs(_first.DbRef, "laccval(me/s*)")).IsEqualTo("settings stats");

		await EvalAs(_wizard.DbRef, $"setaccval({who}/rank, 1)");
		await Assert.That(await EvalAs(_wizard.DbRef, $"accvallock({who}/rank, on)")).IsEqualTo(string.Empty);
		await Assert.That(await EvalAs(_wizard.DbRef, $"accvallock({who}/rank)")).IsEqualTo("1");

		await Assert.That(await EvalAs(_wizard.DbRef, $"setaccval({who}/settings)")).IsEqualTo(string.Empty);
		await Assert.That(await EvalAs(_wizard.DbRef, $"laccval({who})")).IsEqualTo("rank stats unlocks");
	}

	/// <summary>A player holding <c>players.moderate</c> and nothing that overrides a lock.</summary>
	private async Task<TestIsolationHelpers.TestPlayer> ModeratorAsync()
	{
		var moderator = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(WebAppFactoryArg.Services, Mediator, ConnectionService, "AcctMod");
		_extra.Add(moderator);
		await Cmd($"@permission/allow {Ref(moderator)}=players.moderate");
		return moderator;
	}
}
