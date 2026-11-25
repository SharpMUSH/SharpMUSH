using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Functions;

/// <summary>
/// An account's attributes, on its holder <c>#acct:&lt;key&gt;</c>: written by <c>players.moderate</c> and the
/// account's own characters, read by <c>players.view</c> and the account's characters, and found only by the attribute commands and functions.
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
	public async Task TheAccountsCharactersWriteItsAttributes()
	{
		await CmdAs(_first.DbRef, _first.Handle, $"&XP {_holder}=99");
		await Assert.That(await EvalAs(_second.DbRef, $"get(account(me,ref)/xp)")).IsEqualTo("99");

		await EvalAs(_second.DbRef, $"set(account(me,ref),xp:98)");
		await Assert.That(await EvalAs(_first.DbRef, $"get({_holder}/xp)")).IsEqualTo("98");

		await CmdAs(_stranger.DbRef, _stranger.Handle, $"&XP {_holder}=1");
		await Assert.That(await EvalAs(_first.DbRef, $"get({_holder}/xp)")).IsEqualTo("98").Because("a stranger writes nothing");
	}

	[Test]
	public async Task AnAttributeStaffLockInTheAttributeTableStaysTheirs()
	{
		var name = TestIsolationHelpers.GenerateUniqueName("HOLDLOCK").ToUpperInvariant();
		await AsWizard($"@attribute/access {name}=wizard");
		await AsWizard($"&{name} {_holder}=staff");

		await CmdAs(_first.DbRef, _first.Handle, $"&{name} {_holder}=mine");
		await EvalAs(_first.DbRef, $"wipe({_holder}/{name})");

		await Assert.That(await EvalAs(_wizard.DbRef, $"get({_holder}/{name})")).IsEqualTo("staff");
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
		await Assert.That(await EvalAs(_wizard.DbRef, $"flags({_holder})")).StartsWith("#-1");
	}

	[Test]
	public async Task TheAttributeFormsOfObjectFunctionsReachAHolder()
	{
		await AsWizard($"&SEEN {_holder}=yes");
		await AsWizard($"@set {_holder}/SEEN=no_command");

		await Assert.That(await EvalAs(_wizard.DbRef, $"flags({_holder}/seen)")).Contains("$");
		await Assert.That(await EvalAs(_wizard.DbRef, $"owner({_holder}/seen)")).StartsWith("#");
		await Assert.That(await EvalAs(_wizard.DbRef, $"owner({_holder}/seen)")).DoesNotStartWith("#-1");
		await Assert.That(await EvalAs(_first.DbRef, $"visible(me,{_holder}/seen)")).IsEqualTo("1");
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

	private async Task<string> ThingOf(TestIsolationHelpers.TestPlayer player, string prefix)
		=> (await CmdAs(player.DbRef, player.Handle, $"@create {TestIsolationHelpers.GenerateUniqueName(prefix)}")).Trim();

	[Test]
	public async Task CpattrCopiesBetweenAHolderAndAnObject()
	{
		var thing = await ThingOf(_wizard, "HoldCopy");
		await AsWizard($"&XP {_holder}=5");

		await AsWizard($"@cpattr {_holder}/XP={thing}/XP");
		await AsWizard($"&LEVEL {thing}=7");
		await AsWizard($"@cpattr {thing}/LEVEL={_holder}/LEVEL");

		await Assert.That(await EvalAs(_wizard.DbRef, $"get({thing}/xp)")).IsEqualTo("5");
		await Assert.That(await EvalAs(_wizard.DbRef, $"get({_holder}/level)")).IsEqualTo("7");
		await Assert.That(await EvalAs(_wizard.DbRef, $"get({_holder}/xp)")).IsEqualTo("5");
	}

	[Test]
	public async Task MvattrMovesWithinAHolder()
	{
		await AsWizard($"&OLDNAME {_holder}=kept");

		await AsWizard($"@mvattr {_holder}/OLDNAME={_holder}/NEWNAME");

		await Assert.That(await EvalAs(_wizard.DbRef, $"hasattr({_holder},OLDNAME)")).IsEqualTo("0");
		await Assert.That(await EvalAs(_wizard.DbRef, $"get({_holder}/newname)")).IsEqualTo("kept");
	}

	[Test]
	public async Task EditRewritesAHoldersValue()
	{
		await AsWizard($"&MOTTO {_holder}=slow and steady");

		await AsWizard($"@edit {_holder}/MOTTO=slow,fast");

		await Assert.That(await EvalAs(_wizard.DbRef, $"get({_holder}/motto)")).IsEqualTo("fast and steady");
	}

	[Test]
	public async Task WipeAndWipeFunctionClearAHolder()
	{
		await AsWizard($"&KEEP {_holder}=1");
		await AsWizard($"&DROP_A {_holder}=1");
		await AsWizard($"&DROP_B {_holder}=1");

		await EvalAs(_wizard.DbRef, $"wipe({_holder}/DROP_*)");
		await Assert.That(await EvalAs(_wizard.DbRef, $"lattr({_holder})")).IsEqualTo("KEEP");

		await AsWizard($"@wipe {_holder}");
		await Assert.That(await EvalAs(_wizard.DbRef, $"nattr({_holder})")).IsEqualTo("0");
	}

	[Test]
	public async Task PatternsCountsAndTreesWorkOnAHolder()
	{
		await AsWizard($"&STAT_STR {_holder}=3");
		await AsWizard($"&STAT_DEX {_holder}=4");
		await AsWizard($"&BIO {_holder}=hello");
		await AsWizard($"&BIO`SHORT {_holder}=hi");

		await Assert.That(await EvalAs(_first.DbRef, $"lattr({_holder}/STAT_*)")).IsEqualTo("STAT_DEX STAT_STR");
		await Assert.That(await EvalAs(_first.DbRef, $"nattr({_holder}/STAT_*)")).IsEqualTo("2");
		await Assert.That(await EvalAs(_first.DbRef, $"get({_holder}/BIO`SHORT)")).IsEqualTo("hi");
		await Assert.That(await EvalAs(_first.DbRef, $"hasattr({_holder},MISSING)")).IsEqualTo("0");
	}

	[Test]
	public async Task AttribSetAndSetWriteForStaff()
	{
		await EvalAs(_wizard.DbRef, $"attrib_set({_holder}/GOLD,250)");
		await EvalAs(_wizard.DbRef, $"set({_holder},SILVER:12)");

		await Assert.That(await EvalAs(_first.DbRef, $"get({_holder}/gold)")).IsEqualTo("250");
		await Assert.That(await EvalAs(_first.DbRef, $"get({_holder}/silver)")).IsEqualTo("12");
	}

	[Test]
	public async Task AReferenceIsReadWhateverItsCase()
	{
		await AsWizard($"&XP {_holder}=8");

		await Assert.That(await EvalAs(_first.DbRef, $"get({_holder.ToUpperInvariant()}/xp)")).IsEqualTo("8");
	}

	[Test]
	[Arguments("#acct:")]
	[Arguments("#acct:abc")]
	[Arguments("#acct:-1")]
	[Arguments("#acct:+1")]
	[Arguments("#acct: 1")]
	[Arguments("#acct:99999999999")]
	public async Task AMalformedReferenceFindsNothing(string reference)
		=> await Assert.That(await EvalAs(_wizard.DbRef, $"get({reference}/xp)")).StartsWith("#-1");

	[Test]
	public async Task EachAccountKeepsItsOwnAttributes()
	{
		var other = (await Accounts.CreateAccountAsync(TestIsolationHelpers.GenerateUniqueName("holdother").ToLowerInvariant(), null,
			"correct horse battery")).Expect<SharpAccount>();
		var otherHolder = AccountHolders.ForAccount(other.Id)!.Value.ToString();
		await AsWizard($"&XP {_holder}=1");
		await AsWizard($"&XP {otherHolder}=2");

		await Assert.That(await EvalAs(_wizard.DbRef, $"get({_holder}/xp)")).IsEqualTo("1");
		await Assert.That(await EvalAs(_wizard.DbRef, $"get({otherHolder}/xp)")).IsEqualTo("2");
		await Assert.That(await EvalAs(_first.DbRef, $"get({otherHolder}/xp)")).StartsWith("#-1");
	}

	[Test]
	public async Task ObjectsOwnedByTheAccountsCharactersRead()
	{
		await AsWizard($"&XP {_holder}=6");
		var own = await ThingOf(_first, "HoldOwnThing");
		var foreign = await ThingOf(_stranger, "HoldForeignThing");

		await Assert.That(await EvalAs(DBRef.Parse(own), $"get({_holder}/xp)")).IsEqualTo("6");
		await Assert.That(await EvalAs(DBRef.Parse(foreign), $"get({_holder}/xp)")).StartsWith("#-1");
	}

	[Test]
	public async Task PlayersViewReadsButDoesNotWrite()
	{
		var viewer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(WebAppFactoryArg.Services, Mediator, ConnectionService, "HoldView");
		_extra.Add(viewer);
		await Cmd($"@permission/allow {Ref(viewer)}=players.view");
		await AsWizard($"&XP {_holder}=4");

		await Assert.That(await EvalAs(viewer.DbRef, $"get({_holder}/xp)")).IsEqualTo("4");
		await CmdAs(viewer.DbRef, viewer.Handle, $"&XP {_holder}=400");
		await EvalAs(viewer.DbRef, $"set({_holder},xp:401)");

		await Assert.That(await EvalAs(_wizard.DbRef, $"get({_holder}/xp)")).IsEqualTo("4");
	}

	[Test]
	public async Task AStrangerCannotExamineOrListAHolder()
	{
		await AsWizard($"&SECRET {_holder}=hidden value");

		await CmdAs(_stranger.DbRef, _stranger.Handle, $"examine {_holder}");

		await Assert.That(Notifications.For(_stranger.DbRef)).DoesNotContain(line => line.Contains("hidden value"));
		await Assert.That(await EvalAs(_stranger.DbRef, $"lattr({_holder})")).DoesNotContain("SECRET");
		await Assert.That(await EvalAs(_stranger.DbRef, $"xget({_holder},SECRET)")).StartsWith("#-1");
	}

	[Test]
	public async Task ExamineListsAHoldersAttributesForItsCharacters()
	{
		await AsWizard($"&DESCRIBE {_holder}=an account description");
		await AsWizard($"&BADGE {_holder}=founder");

		await CmdAs(_first.DbRef, _first.Handle, $"examine {_holder}");

		await Assert.That(Notifications.For(_first.DbRef)).Contains(line => line.Contains("founder"));
		await Assert.That(Notifications.For(_first.DbRef)).Contains(line => line.Contains("an account description"));
	}

	[Test]
	public async Task AnEnumOnTheAttributeTableAppliesToAHolder()
	{
		var name = TestIsolationHelpers.GenerateUniqueName("TIER").ToUpperInvariant();
		await Cmd($"@attribute/access {name}=");
		await Cmd($"@attribute/enum {name}=Bronze Silver Gold");

		await AsWizard($"&{name} {_holder}=gold");
		await AsWizard($"&{name} {_holder}=platinum");

		await Assert.That(await EvalAs(_wizard.DbRef, $"get({_holder}/{name})")).IsEqualTo("Gold");
	}

	[Test]
	public async Task ATriggerOrAPemitNeverReachesAHolder()
	{
		var token = TestIsolationHelpers.GenerateUniqueName("holdran");
		await AsWizard($"&CODE {_holder}=@pemit {Ref(_wizard)}={token}");

		await AsWizard($"@trigger {_holder}/CODE");
		await AsWizard($"@pemit {_holder}={token}");
		await AsWizard($"@force {_holder}=@pemit {Ref(_wizard)}={token}");

		await Assert.That(Notifications.For(_wizard.DbRef)).DoesNotContain(line => line.Contains(token));
	}

	[Test]
	public async Task AHolderCannotBeRenamedMovedOrDestroyed()
	{
		await AsWizard($"&XP {_holder}=3");
		var thing = await ThingOf(_wizard, "HoldDest");

		await AsWizard($"@name {_holder}=Renamed");
		await AsWizard($"@tel {_holder}={thing}");
		await AsWizard($"@destroy/instant {_holder}");
		await AsWizard($"@set {_holder}=!NO_COMMAND");

		await Assert.That(await EvalAs(_wizard.DbRef, $"get({_holder}/xp)")).IsEqualTo("3");
		await Assert.That(await EvalAs(_wizard.DbRef, $"lcon({thing})")).IsEqualTo(string.Empty);
	}

	[Test]
	public async Task SettingListenOnAHolderTellsNoRoom()
	{
		var listener = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(WebAppFactoryArg.Services, Mediator, ConnectionService, "HoldEars");
		_extra.Add(listener);
		await Cmd($"@tel {Ref(listener)}=#0");
		var name = $"Account {AccountHolders.Parse(_holder)!.Value.AccountNumber()}";

		await AsWizard($"&LISTEN {_holder}=*");
		await AsWizard($"&LISTEN {_holder}=");

		await Assert.That(Notifications.For(listener.DbRef)).DoesNotContain(line => line.StartsWith(name));
	}

	[Test]
	public async Task TheHelpExampleWorks()
	{
		await AsWizard($"&XP account({Ref(_first)}, ref)=10");

		await Assert.That(await EvalAs(_wizard.DbRef, $"get(account({Ref(_first)}, ref)/XP)")).IsEqualTo("10");
	}

	private string AccountName => $"Account {AccountHolders.Parse(_holder)!.Value.AccountNumber()}";

	[Test]
	public async Task ExamineShowsTheAccountItsUsernameCharactersAndAttributes()
	{
		await AsWizard($"&XP {_holder}=5");

		await AsWizard($"examine {_holder}");

		var heard = string.Join("\n", Notifications.For(_wizard.DbRef));
		await Assert.That(heard).Contains($"{AccountName}({_holder})");
		await Assert.That(heard).Contains($"Username: {_account.Username}");
		await Assert.That(heard).Contains("Characters: ");
		await Assert.That(heard).Contains($"({Ref(_first)}");
		await Assert.That(heard).Contains($"({Ref(_second)}");
		await Assert.That(heard).Contains("XP");
		await Assert.That(heard).DoesNotContain("Owner:");
		await Assert.That(heard).DoesNotContain("Location:");
	}

	[Test]
	public async Task TheAccountsCharacterIsNotShownTheUsername()
	{
		await CmdAs(_first.DbRef, _first.Handle, $"examine {_holder}");

		var heard = string.Join("\n", Notifications.For(_first.DbRef));
		await Assert.That(heard).Contains($"{AccountName}({_holder})");
		await Assert.That(heard).Contains("Characters: ");
		await Assert.That(heard).DoesNotContain("Username:");
	}

	[Test]
	public async Task ExamineBriefLeavesOutTheAttributes()
	{
		var token = TestIsolationHelpers.GenerateUniqueName("briefval");
		await AsWizard($"&NOTE {_holder}={token}");

		await AsWizard($"examine/brief {_holder}");

		var heard = string.Join("\n", Notifications.For(_wizard.DbRef));
		await Assert.That(heard).Contains($"{AccountName}({_holder})");
		await Assert.That(heard).DoesNotContain(token);
	}

	[Test]
	public async Task ExamineOfOneAttributeShowsThatAttributeAlone()
	{
		var shown = TestIsolationHelpers.GenerateUniqueName("shownval");
		var hidden = TestIsolationHelpers.GenerateUniqueName("otherval");
		await AsWizard($"&XP {_holder}={shown}");
		await AsWizard($"&BADGE {_holder}={hidden}");

		await CmdAs(_first.DbRef, _first.Handle, $"examine {_holder}/XP");

		var heard = string.Join("\n", Notifications.For(_first.DbRef));
		await Assert.That(heard).Contains(shown);
		await Assert.That(heard).DoesNotContain(hidden);
		await Assert.That(heard).DoesNotContain($"{AccountName}({_holder})");
	}

	[Test]
	public async Task AStrangerIsToldWhyExamineIsRefused()
	{
		await CmdAs(_stranger.DbRef, _stranger.Handle, $"examine {_holder}");

		await Assert.That(Notifications.For(_stranger.DbRef))
			.Contains(line => line.Contains($"Permission denied. {_holder} is not your account"));
	}

	[Test]
	public async Task DecompileWritesTheAttributesByTheHoldersReference()
	{
		var value = TestIsolationHelpers.GenerateUniqueName("decval");
		await AsWizard($"&XP {_holder}={value}");
		await AsWizard($"@atrlock {_holder}/XP=on");

		await AsWizard($"@decompile {_holder}");

		var heard = Notifications.For(_wizard.DbRef).SelectMany(line => line.Split('\n')).ToList();
		await Assert.That(heard).Contains($"&XP {_holder}={value}");
		await Assert.That(heard).Contains(line => line.StartsWith($"@set {_holder}/XP=") && line.Contains("locked"));
		await Assert.That(heard).DoesNotContain(line => line.StartsWith("@create") || line.StartsWith("@lock") || line.StartsWith("@power"));
	}

	[Test]
	public async Task DecompileOfOneAttributeWritesOnlyIt()
	{
		var value = TestIsolationHelpers.GenerateUniqueName("oneval");
		var other = TestIsolationHelpers.GenerateUniqueName("skipval");
		await AsWizard($"&XP {_holder}={value}");
		await AsWizard($"&BADGE {_holder}={other}");

		await CmdAs(_first.DbRef, _first.Handle, $"@decompile {_holder}/XP");

		var heard = string.Join("\n", Notifications.For(_first.DbRef));
		await Assert.That(heard).Contains($"&XP {_holder}={value}");
		await Assert.That(heard).DoesNotContain(other);
	}

	[Test]
	public async Task AStrangerCannotDecompileAHolder()
	{
		var value = TestIsolationHelpers.GenerateUniqueName("secretdec");
		await AsWizard($"&XP {_holder}={value}");

		await CmdAs(_stranger.DbRef, _stranger.Handle, $"@decompile {_holder}");

		await Assert.That(Notifications.For(_stranger.DbRef)).DoesNotContain(line => line.Contains(value));
		await Assert.That(Notifications.For(_stranger.DbRef))
			.Contains(line => line.Contains($"Permission denied. {_holder} is not your account"));
	}

	[Test]
	public async Task DecompiledLinesRebuildTheAttributesOnAnotherAccount()
	{
		var other = (await Accounts.CreateAccountAsync(TestIsolationHelpers.GenerateUniqueName("holdcopy").ToLowerInvariant(), null,
			"correct horse battery")).Expect<SharpAccount>();
		var target = AccountHolders.ForAccount(other.Id)!.Value.ToString();
		await AsWizard($"&XP {_holder}=12");
		await EvalAs(_wizard.DbRef, $"attrib_set({_holder}/TITLE,ansi(h,Founder))");
		await AsWizard($"@set {_holder}/XP=mortal_dark");

		await AsWizard($"@decompile {_holder}");
		var lines = Notifications.For(_wizard.DbRef).SelectMany(line => line.Split('\n'))
			.Where(line => line.Contains($" {_holder}") || line.Contains($" {_holder}/"))
			.Distinct().ToList();
		foreach (var line in lines)
			await AsWizard(line.Replace(_holder, target));

		await Assert.That(await EvalAs(_wizard.DbRef, $"get({target}/xp)")).IsEqualTo("12");
		await Assert.That(await EvalAs(_wizard.DbRef, $"lflags({target}/xp)")).Contains("mortal_dark");
		await Assert.That(lines).Contains(line => line.StartsWith($"@set {_holder}=TITLE:"));
		await Assert.That(await EvalAs(_wizard.DbRef, $"get({target}/title)")).IsEqualTo("Founder");
	}

	private async Task<TestIsolationHelpers.TestPlayer> ExtraPlayer(string prefix)
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(WebAppFactoryArg.Services, Mediator, ConnectionService, prefix);
		_extra.Add(player);
		return player;
	}

	[Test]
	public async Task AWizardAttributeIsKeptFromAModerator()
	{
		var moderator = await ExtraPlayer("HoldModWiz");
		await Cmd($"@permission/allow {Ref(moderator)}=players.moderate");
		await AsWizard($"&LEVEL {_holder}=1");
		await AsWizard($"@set {_holder}/LEVEL=wizard");

		await EvalAs(moderator.DbRef, $"set({_holder},level:2)");
		await EvalAs(moderator.DbRef, $"set({_holder},other:ok)");

		await Assert.That(await EvalAs(_wizard.DbRef, $"get({_holder}/level)")).IsEqualTo("1");
		await Assert.That(await EvalAs(_wizard.DbRef, $"get({_holder}/other)")).IsEqualTo("ok");
	}

	[Test]
	public async Task VisualLetsAnyoneReadButNearbyMeansNobodyIsNear()
	{
		await AsWizard($"&BIO {_holder}=public words");
		await AsWizard($"@set {_holder}/BIO=visual");
		await AsWizard($"&NEAR {_holder}=near words");
		await AsWizard($"@set {_holder}/NEAR=visual");
		await AsWizard($"@set {_holder}/NEAR=nearby");

		await Assert.That(await EvalAs(_stranger.DbRef, $"get({_holder}/bio)")).IsEqualTo("public words");
		await Assert.That(await EvalAs(_stranger.DbRef, $"get({_holder}/near)")).StartsWith("#-1");
		await Assert.That(await EvalAs(_first.DbRef, $"get({_holder}/near)")).IsEqualTo("near words");
	}

	[Test]
	public async Task TheAttributeTablesDefaultFlagsApply()
	{
		var name = TestIsolationHelpers.GenerateUniqueName("STAFFNOTE").ToUpperInvariant();
		await Cmd($"@attribute/access {name}=mortal_dark");

		await AsWizard($"&{name} {_holder}=for staff");

		await Assert.That(await EvalAs(_wizard.DbRef, $"lflags({_holder}/{name})")).Contains("mortal_dark");
		await Assert.That(await EvalAs(_first.DbRef, $"get({_holder}/{name})")).StartsWith("#-1");
		await Assert.That(await EvalAs(_wizard.DbRef, $"get({_holder}/{name})")).IsEqualTo("for staff");
	}

	[Test]
	public async Task ARoleOnTheAccountPassesARoleLockForEachOfItsCharacters()
	{
		var role = "acctrole" + Guid.NewGuid().ToString("N")[..10];
		await Cmd($"@role/create {role}=Staff/Account role");
		try
		{
			await Cmd($"@role/assign/account {Ref(_first)}={role}");

			await Assert.That(await Eval($"testlock(role^{role},{Ref(_first)})")).IsEqualTo("1");
			await Assert.That(await Eval($"testlock(role^{role},{Ref(_second)})")).IsEqualTo("1");
			await Assert.That(await Eval($"testlock(role^{role},{Ref(_stranger)})")).IsEqualTo("0");
		}
		finally
		{
			await Cmd($"@role/delete {role}");
		}
	}

	[Test]
	public async Task ARoleThatAllowsPlayersViewReadsEveryAccountButCannotWrite()
	{
		var role = "acctview" + Guid.NewGuid().ToString("N")[..10];
		var viewer = await ExtraPlayer("HoldRoleView");
		await Cmd($"@role/create {role}=Staff/Account reader");
		try
		{
			await AsWizard($"&XP {_holder}=8");
			await Assert.That(await EvalAs(viewer.DbRef, $"get({_holder}/xp)")).StartsWith("#-1");

			await Cmd($"@role/allow {role}=players.view");
			await Cmd($"@role/assign {Ref(viewer)}={role}");

			await Assert.That(await EvalAs(viewer.DbRef, $"get({_holder}/xp)")).IsEqualTo("8");
			await Assert.That(await EvalAs(viewer.DbRef, $"testlock(perm^players.view,{Ref(viewer)})")).IsEqualTo("1");
			await EvalAs(viewer.DbRef, $"set({_holder},xp:80)");
			await Assert.That(await EvalAs(_wizard.DbRef, $"get({_holder}/xp)")).IsEqualTo("8");
		}
		finally
		{
			await Cmd($"@role/delete {role}");
		}
	}

	[Test]
	public async Task AnEvalLockReadsAnAccountOnceItsObjectMayView()
	{
		var gate = await ThingOf(_stranger, "HoldGate");
		await AsWizard($"&XP {_holder}=7");
		await Cmd($"&CANPASS {gate}=[gte(get(account(%#,ref)/XP),5)]");
		await Cmd($"@lock {gate}=CANPASS/1");

		await Assert.That(await Eval($"elock({gate},{Ref(_first)})")).IsEqualTo("0");

		await Cmd($"@permission/allow {gate}=players.view");

		await Assert.That(await Eval($"elock({gate},{Ref(_first)})")).IsEqualTo("1");
		await Assert.That(await Eval($"elock({gate},{Ref(_second)})")).IsEqualTo("1");
		await Assert.That(await Eval($"elock({gate},{Ref(_stranger)})")).IsEqualTo("0");
	}

	[Test]
	public async Task TheAccountsHelpExamplesWork()
	{
		var name = _first.Name;
		await AsWizard($"&BIO account(*{name}, ref)=Plays since 2019.");
		await AsWizard($"@set account(*{name}, ref)/BIO=visual");
		await Assert.That(await EvalAs(_stranger.DbRef, $"get({_holder}/bio)")).IsEqualTo("Plays since 2019.");
		await Assert.That(await EvalAs(_first.DbRef, "lattr(account(me, ref))")).Contains("BIO");
		await Assert.That(await EvalAs(_wizard.DbRef, $"attrib_set(account(*{name}, ref)/XP, 12)[get(account(*{name}, ref)/XP)]"))
			.IsEqualTo("12");

		var role = "xpclerk" + Guid.NewGuid().ToString("N")[..10];
		var clerk = await ExtraPlayer("HoldClerk");
		await Cmd($"@role/create {role}=Staff/XP clerk");
		try
		{
			await Cmd($"@role/allow {role}=players.moderate");
			await Cmd($"@role/assign {Ref(clerk)}={role}");
			await CmdAs(clerk.DbRef, clerk.Handle, $"&XP account(*{name}, ref)=15");
			await Assert.That(await EvalAs(_second.DbRef, $"get(account(*{_second.Name}, ref)/XP)")).IsEqualTo("15");
		}
		finally
		{
			await Cmd($"@role/delete {role}");
		}
	}

	[Test]
	public async Task AWriteKeyClerkNamesTheAccountThroughPlayersView()
	{
		var xp = "KEYXP" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
		var role = "xpclerk" + Guid.NewGuid().ToString("N")[..10];
		var clerk = await ExtraPlayer("HoldKeyClerk");
		await Cmd($"@role/create {role}=Staff/XP clerk");
		try
		{
			await Cmd($"@role/assign {Ref(clerk)}={role}");
			await Cmd($"@attribute/access {xp}=none");
			await Cmd($"@attribute/lock/write {xp}=role^{role}");

			// The key alone covers the write, not naming another character's account.
			await Assert.That(await EvalAs(clerk.DbRef, $"account(*{_first.Name}, ref)")).IsEqualTo("#-1 PERMISSION DENIED: NEEDS PLAYERS.VIEW");

			// help accounts: the role also gets players.view.
			await Cmd($"@role/allow {role}=players.view");
			await Assert.That(await EvalAs(clerk.DbRef, $"account(*{_first.Name})")).IsEqualTo(_holder);
			await Assert.That(await EvalAs(clerk.DbRef, $"account(*{_first.Name}, id)")).IsEqualTo("#-1 FIELD MUST BE REF OR NAME");
			await CmdAs(clerk.DbRef, clerk.Handle, $"&{xp} account(*{_first.Name}, ref)=15");
			await Assert.That(await EvalAs(_wizard.DbRef, $"get({_holder}/{xp})")).IsEqualTo("15");

			// The key locks the attribute to the account's own characters: they neither set nor clear it.
			await CmdAs(_first.DbRef, _first.Handle, $"&{xp} account(me)=99");
			await EvalAs(_second.DbRef, $"set(account(me),{xp}:98)");
			await EvalAs(_first.DbRef, $"wipe(account(me)/{xp})");
			await Assert.That(await EvalAs(_wizard.DbRef, $"get({_holder}/{xp})")).IsEqualTo("15");
		}
		finally
		{
			await Cmd($"@role/delete {role}");
			await Cmd($"@attribute/delete {xp}");
		}
	}

	[Test]
	public async Task AStrangerIsRefusedExamineOfOneAttribute()
	{
		var value = TestIsolationHelpers.GenerateUniqueName("visualval");
		await AsWizard($"&BIO {_holder}={value}");
		await AsWizard($"@set {_holder}/BIO=visual");

		await CmdAs(_stranger.DbRef, _stranger.Handle, $"examine {_holder}/BIO");

		await Assert.That(Notifications.For(_stranger.DbRef)).DoesNotContain(line => line.Contains(value));
		await Assert.That(Notifications.For(_stranger.DbRef))
			.Contains(line => line.Contains($"Permission denied. {_holder} is not your account"));
	}

	[Test]
	public async Task ExamineMortalLeavesOutTheUsername()
	{
		await AsWizard($"examine/mortal {_holder}");

		var heard = string.Join("\n", Notifications.For(_wizard.DbRef));
		await Assert.That(heard).Contains($"{AccountName}({_holder})");
		await Assert.That(heard).DoesNotContain("Username:");
	}
}
