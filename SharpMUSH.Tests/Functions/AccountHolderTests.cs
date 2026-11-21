using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Functions;

/// <summary>
/// An account's attributes, on its holder <c>#acct:&lt;key&gt;</c>: written by <c>players.moderate</c>, read by
/// <c>players.view</c> and the account's own characters, and found only by the attribute commands and functions.
/// </summary>
public class AccountHolderTests : ServerTestBase
{
	private TestIsolationHelpers.TestPlayer _first = null!;
	private TestIsolationHelpers.TestPlayer _second = null!;
	private TestIsolationHelpers.TestPlayer _stranger = null!;
	private TestIsolationHelpers.TestPlayer _wizard = null!;
	private readonly List<TestIsolationHelpers.TestPlayer> _extra = [];
	private SharpAccount _account = null!;
	private string _holder = null!;

	private IAccountService Accounts => WebAppFactoryArg.Services.GetRequiredService<IAccountService>();

	[Before(Test)]
	public async Task LinkTwoCharacters()
	{
		_first = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(WebAppFactoryArg.Services, Mediator, ConnectionService, "HoldFirst");
		_second = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(WebAppFactoryArg.Services, Mediator, ConnectionService, "HoldSecond");
		_stranger = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(WebAppFactoryArg.Services, Mediator, ConnectionService, "HoldStranger");
		_wizard = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(WebAppFactoryArg.Services, Mediator, ConnectionService, "HoldWiz");
		var wizard = (await Mediator.Send(new GetObjectNodeQuery(_wizard.DbRef))).Expect<SharpPlayer>();
		await Mediator.Send(new SetObjectFlagCommand(wizard, (await Mediator.Send(new GetObjectFlagQuery("WIZARD")))!));

		var username = TestIsolationHelpers.GenerateUniqueName("hold").ToLowerInvariant();
		_account = (await Accounts.CreateAccountAsync(username, null, "correct horse battery")).Expect<SharpAccount>();
		foreach (var player in new[] { _first, _second })
			await Accounts.AttachCharacterAsync(_account.Id!, (await Mediator.Send(new GetObjectNodeQuery(player.DbRef))).Expect<SharpPlayer>());
		_holder = AccountHolders.ForAccount(_account.Id)!.Value.ToString();
	}

	[After(Test)]
	public async Task Disconnect()
	{
		foreach (var player in new[] { _first, _second, _stranger, _wizard }.Concat(_extra))
			await ConnectionService.Disconnect(player.Handle);
	}

	private static string Ref(TestIsolationHelpers.TestPlayer player) => $"#{player.DbRef.Number}";

	private Task<string> AsWizard(string command) => CmdAs(_wizard.DbRef, _wizard.Handle, command);

	[Test]
	public async Task AccountGivesTheHolderToStaffAndTheAccountsCharacters()
	{
		await Assert.That(await EvalAs(_wizard.DbRef, $"account({Ref(_first)},ref)")).IsEqualTo(_holder);
		await Assert.That(await EvalAs(_second.DbRef, $"account({Ref(_first)},ref)")).IsEqualTo(_holder);
		await Assert.That(await EvalAs(_stranger.DbRef, $"account({Ref(_first)},ref)")).IsEqualTo("#-1 PERMISSION DENIED: NEEDS PLAYERS.VIEW");
		await Assert.That(await EvalAs(_wizard.DbRef, $"account({Ref(_stranger)},ref)")).IsEqualTo(string.Empty);
	}

	[Test]
	public async Task StaffWriteAndTheAccountsCharactersRead()
	{
		await AsWizard($"&XP {_holder}=10");

		await Assert.That(await EvalAs(_first.DbRef, $"get({_holder}/xp)")).IsEqualTo("10");
		await Assert.That(await EvalAs(_second.DbRef, $"get(account(me,ref)/xp)")).IsEqualTo("10");
		await Assert.That(await EvalAs(_wizard.DbRef, $"xget({_holder},XP)")).IsEqualTo("10");
		await Assert.That(await EvalAs(_first.DbRef, $"lattr({_holder})")).IsEqualTo("XP");
		await Assert.That(await EvalAs(_first.DbRef, $"hasattr({_holder},xp)")).IsEqualTo("1");
		await Assert.That(await EvalAs(_stranger.DbRef, $"get({_holder}/xp)")).StartsWith("#-1");
	}

	[Test]
	public async Task TheAccountsCharactersCannotWrite()
	{
		await AsWizard($"&XP {_holder}=10");

		await CmdAs(_first.DbRef, _first.Handle, $"&XP {_holder}=99");
		await EvalAs(_first.DbRef, $"set({_holder},xp:98)");
		await EvalAs(_first.DbRef, $"wipe({_holder})");

		await Assert.That(await EvalAs(_wizard.DbRef, $"get({_holder}/xp)")).IsEqualTo("10");
	}

	[Test]
	public async Task PlayersModerateWritesWithoutBeingAWizard()
	{
		var moderator = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(WebAppFactoryArg.Services, Mediator, ConnectionService, "HoldMod");
		_extra.Add(moderator);
		await Cmd($"@permission/allow {Ref(moderator)}=players.moderate");

		await EvalAs(moderator.DbRef, $"set({_holder},rank:3)");

		await Assert.That(await EvalAs(_first.DbRef, $"get({_holder}/rank)")).IsEqualTo("3");
	}

	[Test]
	public async Task AttributeFlagsHideAValueFromTheAccountsCharacters()
	{
		await AsWizard($"&NOTE {_holder}=watch this one");
		await AsWizard($"@set {_holder}/NOTE=mortal_dark");

		await Assert.That(await EvalAs(_first.DbRef, $"get({_holder}/note)")).StartsWith("#-1");
		await Assert.That(await EvalAs(_wizard.DbRef, $"get({_holder}/note)")).IsEqualTo("watch this one");
	}

	[Test]
	public async Task AnAccountHasNoFlags()
		=> await Assert.That(await AsWizard($"@set {_holder}=WIZARD")).IsEqualTo("#-1 AN ACCOUNT HAS NO FLAGS");

	[Test]
	public async Task NothingButTheAttributeCommandsAndFunctionsFindsAHolder()
	{
		await AsWizard($"&CODE {_holder}=ran");

		await Assert.That(await EvalAs(_wizard.DbRef, $"u({_holder}/code)")).IsNotEqualTo("ran");
		await Assert.That(await EvalAs(_wizard.DbRef, $"name({_holder})")).StartsWith("#-1");
		await Assert.That(await EvalAs(_wizard.DbRef, $"loc({_holder})")).StartsWith("#-1");
	}

	[Test]
	public async Task AHolderWithoutAnAccountIsNotThere()
		=> await Assert.That(await EvalAs(_wizard.DbRef, $"get(#acct:{AccountHolders.MaxKey}/xp)")).StartsWith("#-1");

	[Test]
	public async Task AHolderIsWrittenAndReadAsAcctAndItsKey()
	{
		var holder = AccountHolders.For(42);

		await Assert.That(holder.ToString()).IsEqualTo("#acct:42");
		await Assert.That(DBRef.Parse("#ACCT:42")).IsEqualTo(holder);
		await Assert.That(holder.IsAccountHolder()).IsTrue();
		await Assert.That(new DBRef(42).IsAccountHolder()).IsFalse();
		await Assert.That(DBRef.TryParse($"#acct:{(long)AccountHolders.MaxKey + 1}", out _)).IsFalse();
		await Assert.That(ObjectNames.IsLegal("#acct:42")).IsFalse();
	}

	[Test]
	public async Task AtrlockAndExamineWorkOnAHolder()
	{
		await AsWizard($"&LEVEL {_holder}=2");
		await AsWizard($"@atrlock {_holder}/LEVEL=on");

		await Assert.That(await EvalAs(_wizard.DbRef, $"lflags({_holder}/level)")).Contains("locked");
		await Assert.That(await AsWizard($"examine {_holder}")).IsEqualTo(_holder);
	}

	[Test]
	public async Task LockAndUnlockOfAnAttributeReachAHolder()
	{
		await AsWizard($"&RANK {_holder}=1");
		await AsWizard($"@lock {_holder}/RANK");
		await Assert.That(await EvalAs(_wizard.DbRef, $"lflags({_holder}/rank)")).Contains("locked");
		await AsWizard($"@unlock {_holder}/RANK");
		await Assert.That(await EvalAs(_wizard.DbRef, $"lflags({_holder}/rank)")).DoesNotContain("locked");
	}

	[Test]
	public async Task ThePortalSearchListsAHolderByItsKey()
	{
		var key = _holder["#acct:".Length..];
		var listed = await EvalAs(_wizard.DbRef,
			$"iter({_holder} #1,[if(strmatch(%i0,#acct:*),SHARP_ACCT:[after(%i0,:)],SHARP_OBJ:[before(%i0,:)]:[type(%i0)]:[name(%i0)])],,|)");

		await Assert.That(listed).IsEqualTo($"SHARP_ACCT:{key}|SHARP_OBJ:#1:PLAYER:{await EvalAs(_wizard.DbRef, "name(#1)")}");
	}

	[Test]
	public async Task TheHelpExampleWorks()
	{
		await AsWizard($"&XP account({Ref(_first)}, ref)=10");

		await Assert.That(await EvalAs(_wizard.DbRef, $"get(account({Ref(_first)}, ref)/XP)")).IsEqualTo("10");
	}
}
