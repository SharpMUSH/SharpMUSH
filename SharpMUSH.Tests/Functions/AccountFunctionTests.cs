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
/// characters on that account.
/// </summary>
public class AccountFunctionTests : ServerTestBase
{
	private TestIsolationHelpers.TestPlayer _first = null!;
	private TestIsolationHelpers.TestPlayer _second = null!;
	private TestIsolationHelpers.TestPlayer _stranger = null!;
	private TestIsolationHelpers.TestPlayer _wizard = null!;
	private SharpAccount _account = null!;

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
		foreach (var player in new[] { _first, _second, _stranger, _wizard })
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
	public async Task StaffSetABucketThatTheAccountsCharactersRead()
	{
		await Assert.That(await EvalAs(_wizard.DbRef, $"setbucket({Ref(_first)},Stats,json(object,xp,12))")).IsEqualTo(string.Empty);

		await Assert.That(await EvalAs(_second.DbRef, $"bucket({Ref(_first)},stats)")).IsEqualTo("{\"xp\":12}");
		await Assert.That(await EvalAs(_first.DbRef, "bucket(me,STATS)")).IsEqualTo("{\"xp\":12}");
		await Assert.That(await EvalAs(_wizard.DbRef, $"buckets({Ref(_second)})")).IsEqualTo("stats");
	}

	[Test]
	public async Task BucketsListInAsciiOrder()
	{
		foreach (var name in new[] { "zeta", "alpha", "mid.dle" })
			await EvalAs(_wizard.DbRef, $"setbucket({Ref(_first)},{name},1)");

		await Assert.That(await EvalAs(_first.DbRef, "buckets(me)")).IsEqualTo("alpha mid.dle zeta");
	}

	[Test]
	public async Task AnEmptyValueRemovesTheBucket()
	{
		await EvalAs(_wizard.DbRef, $"setbucket({Ref(_first)},gone,json(array,1,2))");
		await Assert.That(await EvalAs(_wizard.DbRef, $"setbucket({Ref(_first)},gone)")).IsEqualTo(string.Empty);

		await Assert.That(await EvalAs(_wizard.DbRef, $"bucket({Ref(_first)},gone)")).IsEqualTo(string.Empty);
		await Assert.That(await EvalAs(_wizard.DbRef, $"buckets({Ref(_first)})")).IsEqualTo(string.Empty);
	}

	[Test]
	public async Task OnlyPlayersModerateWritesBuckets()
	{
		await Assert.That(await EvalAs(_first.DbRef, "setbucket(me,mine,1)")).IsEqualTo("#-1 PERMISSION DENIED: NEEDS PLAYERS.MODERATE");
		await Assert.That(await EvalAs(_first.DbRef, "buckets(me)")).IsEqualTo(string.Empty);
	}

	[Test]
	public async Task SomeoneElseCannotReadTheBuckets()
	{
		await EvalAs(_wizard.DbRef, $"setbucket({Ref(_first)},secret,true)");

		await Assert.That(await EvalAs(_stranger.DbRef, $"bucket({Ref(_first)},secret)")).IsEqualTo("#-1 PERMISSION DENIED: NEEDS PLAYERS.VIEW");
		await Assert.That(await EvalAs(_stranger.DbRef, $"buckets({Ref(_first)})")).IsEqualTo("#-1 PERMISSION DENIED: NEEDS PLAYERS.VIEW");
	}

	[Test]
	public async Task ABucketValueMustBeJson()
	{
		await Assert.That(await EvalAs(_wizard.DbRef, $"setbucket({Ref(_first)},bad,xp: 12)")).IsEqualTo("#-1 INVALID JSON");
		await Assert.That(await EvalAs(_wizard.DbRef, $"buckets({Ref(_first)})")).IsEqualTo(string.Empty);
	}

	[Test]
	public async Task ABucketNameIsPrintableAsciiWithoutSpaces()
	{
		const string refused = "#-1 BUCKET NAME MUST BE 1 TO 64 PRINTABLE ASCII CHARACTERS WITHOUT SPACES";
		await Assert.That(await EvalAs(_wizard.DbRef, $"setbucket({Ref(_first)},two words,1)")).IsEqualTo(refused);
		await Assert.That(await EvalAs(_wizard.DbRef, $"setbucket({Ref(_first)},caf\u00e9,1)")).IsEqualTo(refused);
		await Assert.That(await EvalAs(_wizard.DbRef, $"bucket({Ref(_first)},{new string('a', 65)})")).IsEqualTo(refused);
	}

	[Test]
	public async Task APlayerOnNoAccountHasNoBuckets()
	{
		await Assert.That(await EvalAs(_wizard.DbRef, $"setbucket({Ref(_stranger)},x,1)")).IsEqualTo("#-1 PLAYER IS ON NO ACCOUNT");
		await Assert.That(await EvalAs(_stranger.DbRef, "buckets(me)")).IsEqualTo(string.Empty);
		await Assert.That(await EvalAs(_stranger.DbRef, "bucket(me,x)")).IsEqualTo(string.Empty);
	}

	[Test]
	public async Task TheStoreRefusesANewBucketPastTheLimitButStillReplacesOne()
	{
		var store = WebAppFactoryArg.Services.GetRequiredService<IAccountBucketStore>();
		await store.SetAccountBucketAsync(_account.Id!, "one", "1", 2);
		await store.SetAccountBucketAsync(_account.Id!, "two", "2", 2);

		await Assert.That(await store.SetAccountBucketAsync(_account.Id!, "three", "3", 2) is AccountBucketsFull).IsTrue();
		await Assert.That(await store.SetAccountBucketAsync(_account.Id!, "two", "22", 2) is Success).IsTrue();
		await Assert.That(await store.GetAccountBucketAsync(_account.Id!, "two") is string json && json == "22").IsTrue();
		await Assert.That(await store.SetAccountBucketAsync("node_accounts/no-such-account", "one", "1", 2) is NotFound).IsTrue();
	}
}
