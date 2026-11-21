using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// An attribute's read and write keys (<c>@atrlock/read</c>, <c>@atrlock/write</c>, <c>@attribute/lock</c>,
/// <c>atrlock(&lt;obj&gt;/&lt;attr&gt;, read|write[, &lt;key&gt;])</c>): a key grants reading or setting that one attribute to
/// whoever passes it, and never takes access away. Attribute flags still apply on top.
/// </summary>
public class AttributeKeyTests : ServerTestBase
{
	private TestIsolationHelpers.TestPlayer _owner = null!;
	private TestIsolationHelpers.TestPlayer _clerk = null!;
	private TestIsolationHelpers.TestPlayer _stranger = null!;
	private TestIsolationHelpers.TestPlayer _wizard = null!;
	private string _thing = null!;

	private IAccountService Accounts => WebAppFactoryArg.Services.GetRequiredService<IAccountService>();

	[Before(Test)]
	public async Task CreateWorld()
	{
		var room = DBRef.Parse((await Cmd($"@dig {TestIsolationHelpers.GenerateUniqueName("KeyRoom")}")).Trim());
		_owner = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(WebAppFactoryArg.Services, Mediator, ConnectionService, "KeyOwner", room);
		_clerk = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(WebAppFactoryArg.Services, Mediator, ConnectionService, "KeyClerk", room);
		_stranger = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(WebAppFactoryArg.Services, Mediator, ConnectionService, "KeyStranger", room);
		_wizard = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(WebAppFactoryArg.Services, Mediator, ConnectionService, "KeyWiz", room);
		var wizard = (await Mediator.Send(new GetObjectNodeQuery(_wizard.DbRef))).Expect<SharpPlayer>();
		await Mediator.Send(new SetObjectFlagCommand(wizard, (await Mediator.Send(new GetObjectFlagQuery("WIZARD")))!));

		_thing = (await AsOwner($"@create {TestIsolationHelpers.GenerateUniqueName("KeyBox")}")).Trim();
		await AsOwner($"&XP {_thing}=1");
	}

	[After(Test)]
	public async Task Disconnect()
	{
		foreach (var player in new[] { _owner, _clerk, _stranger, _wizard })
			await ConnectionService.Disconnect(player.Handle);
	}

	private static string Ref(TestIsolationHelpers.TestPlayer player) => $"#{player.DbRef.Number}";

	private Task<string> AsOwner(string command) => CmdAs(_owner.DbRef, _owner.Handle, command);
	private Task<string> AsClerk(string command) => CmdAs(_clerk.DbRef, _clerk.Handle, command);
	private Task<string> AsStranger(string command) => CmdAs(_stranger.DbRef, _stranger.Handle, command);
	private Task<string> AsWizard(string command) => CmdAs(_wizard.DbRef, _wizard.Handle, command);

	/// <summary>What <paramref name="who"/> was told while <paramref name="act"/> ran.</summary>
	private async Task<IReadOnlyList<string>> HeardBy(TestIsolationHelpers.TestPlayer who, Func<Task> act)
	{
		var before = Notifications.For(who.DbRef).Count;
		await act();
		return [.. Notifications.For(who.DbRef).Skip(before)];
	}

	private Task<string> Get(TestIsolationHelpers.TestPlayer who, string attribute) => EvalAs(who.DbRef, $"get({_thing}/{attribute})");

	[Test]
	public async Task AWriteKeyLetsWhoeverPassesItSetThatAttributeOnly()
	{
		await AsOwner($"@atrlock/write {_thing}/XP=={Ref(_clerk)}");

		await AsClerk($"&XP {_thing}=5");
		await AsClerk($"&OTHER {_thing}=sneaky");
		await AsStranger($"&XP {_thing}=99");

		await Assert.That(await Get(_owner, "XP")).IsEqualTo("5");
		await Assert.That(await Get(_owner, "OTHER")).IsEqualTo(string.Empty);
	}

	[Test]
	public async Task AWriteKeySetsThroughTheFunctionsButNeverClears()
	{
		await AsOwner($"@atrlock/write {_thing}/XP=={Ref(_clerk)}");

		await EvalAs(_clerk.DbRef, $"set({_thing},XP:7)");
		await EvalAs(_clerk.DbRef, $"attrib_set({_thing}/XP,8)");
		await Assert.That(await Get(_owner, "XP")).IsEqualTo("8");

		await AsClerk($"&XP {_thing}");
		await EvalAs(_clerk.DbRef, $"attrib_set({_thing}/XP)");
		await Assert.That(await Get(_owner, "XP")).IsEqualTo("8");
		await Assert.That(await EvalAs(_owner.DbRef, $"atrlock({_thing}/XP,write)")).IsEqualTo($"={Ref(_clerk)}");
	}

	[Test]
	public async Task ATableKeyCannotStripAnAttributesFlagsByClearingIt()
	{
		var name = "KEYNC" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
		await AsWizard($"@attribute/access {name}=none");
		await AsWizard($"@attribute/lock/write {name}=={Ref(_clerk)}");
		await AsOwner($"&{name} {_thing}=notes");
		await AsOwner($"@set {_thing}/{name}=no_command");

		await AsClerk($"&{name} {_thing}");
		await AsClerk($"&{name} {_thing}=$hack:think hacked");

		await Assert.That(await EvalAs(_owner.DbRef, $"flags({_thing}/{name})")).Contains("$");
	}

	[Test]
	public async Task CopyingCarriesKeysOnlyForSomeoneWhoMayExamineTheSource()
	{
		await AsOwner($"&SHOWN {_thing}=public");
		await AsOwner($"@set {_thing}/SHOWN=visual");
		await AsOwner($"@atrlock/write {_thing}/SHOWN=={Ref(_clerk)}");
		var mine = (await AsStranger($"@create {TestIsolationHelpers.GenerateUniqueName("KeyMine")}")).Trim();

		await AsStranger($"@cpattr {_thing}/SHOWN={mine}/SHOWN");

		await Assert.That(await EvalAs(_stranger.DbRef, $"get({mine}/SHOWN)")).IsEqualTo("public");
		await Assert.That(await EvalAs(_stranger.DbRef, $"atrlock({mine}/SHOWN,write)")).IsEqualTo(string.Empty);
	}

	[Test]
	public async Task AWriteKeyGrantsNoFlagChangesAndNoWipe()
	{
		await AsOwner($"@atrlock/write {_thing}/XP=={Ref(_clerk)}");

		await AsClerk($"@set {_thing}/XP=no_command");
		await AsClerk($"@wipe {_thing}");
		await AsClerk($"@atrlock/write {_thing}/XP=");

		await Assert.That(await EvalAs(_owner.DbRef, $"flags({_thing}/XP)")).DoesNotContain("$");
		await Assert.That(await Get(_owner, "XP")).IsEqualTo("1");
		await Assert.That(await EvalAs(_owner.DbRef, $"atrlock({_thing}/XP,write)")).IsNotEqualTo(string.Empty);
	}

	[Test]
	public async Task TheLockedFlagStillRefusesAKeyHolder()
	{
		await AsOwner($"@atrlock/write {_thing}/XP=={Ref(_clerk)}");
		await AsOwner($"@atrlock {_thing}/XP=on");

		await AsClerk($"&XP {_thing}=5");

		await Assert.That(await Get(_owner, "XP")).IsEqualTo("1");
	}

	[Test]
	public async Task AReadKeyLetsWhoeverPassesItReadAHiddenAttribute()
	{
		await AsOwner($"&SECRET {_thing}=the plan");
		await Assert.That(await Get(_clerk, "SECRET")).StartsWith("#-1");

		await AsOwner($"@atrlock/read {_thing}/SECRET=={Ref(_clerk)}");

		await Assert.That(await Get(_clerk, "SECRET")).IsEqualTo("the plan");
		await Assert.That(await Get(_stranger, "SECRET")).StartsWith("#-1");
		await Assert.That(await EvalAs(_clerk.DbRef, $"lattr({_thing})")).Contains("SECRET");
		await Assert.That(await EvalAs(_stranger.DbRef, $"lattr({_thing})")).DoesNotContain("SECRET");
	}

	[Test]
	public async Task DarkFlagsStillHideFromAKeyHolder()
	{
		await AsOwner($"&SECRET {_thing}=the plan");
		await AsOwner($"@atrlock/read {_thing}/SECRET=={Ref(_clerk)}");
		await AsWizard($"@set {_thing}/SECRET=mortal_dark");

		await Assert.That(await Get(_clerk, "SECRET")).StartsWith("#-1");
		await Assert.That(await Get(_wizard, "SECRET")).IsEqualTo("the plan");
	}

	[Test]
	public async Task AReadKeyIsNotAWriteKey()
	{
		await AsOwner($"@atrlock/read {_thing}/XP=={Ref(_clerk)}");

		await AsClerk($"&XP {_thing}=5");

		await Assert.That(await Get(_owner, "XP")).IsEqualTo("1");
	}

	[Test]
	public async Task OnlySomeoneWhoMaySetTheAttributeChangesItsKeys()
	{
		await AsOwner($"@set {_thing}/XP=visual");
		var heard = await HeardBy(_stranger, () => AsStranger($"@atrlock/write {_thing}/XP=={Ref(_stranger)}"));

		await Assert.That(heard).Contains("XP - you need to be able to set the attribute to change its keys.");
		await Assert.That(await EvalAs(_owner.DbRef, $"atrlock({_thing}/XP,write)")).IsEqualTo(string.Empty);
	}

	[Test]
	public async Task TheCommandReportsWhatItDid()
	{
		await Assert.That(await HeardBy(_owner, () => AsOwner($"@atrlock/write {_thing}/XP=={Ref(_clerk)}")))
			.IsEquivalentTo(["XP - write key set."]);
		await Assert.That(await HeardBy(_owner, () => AsOwner($"@atrlock/write {_thing}/XP")))
			.IsEquivalentTo([$"XP - write key: ={Ref(_clerk)}"]);
		await Assert.That(await HeardBy(_owner, () => AsOwner($"@atrlock/read {_thing}/XP")))
			.IsEquivalentTo(["XP has no read key."]);
		await Assert.That(await HeardBy(_owner, () => AsOwner($"@atrlock/write {_thing}/XP=")))
			.IsEquivalentTo(["XP - write key cleared."]);
		await Assert.That(await HeardBy(_owner, () => AsOwner($"@atrlock/write {_thing}/XP=)(")))
			.IsEquivalentTo(["I don't understand that key."]);
	}

	[Test]
	public async Task OnlySomeoneWhoMayExamineSeesAKey()
	{
		await AsOwner($"@atrlock/read {_thing}/XP=={Ref(_clerk)}");

		await Assert.That(await EvalAs(_owner.DbRef, $"atrlock({_thing}/XP,read)")).IsEqualTo($"={Ref(_clerk)}");
		await Assert.That(await EvalAs(_clerk.DbRef, $"get({_thing}/XP)")).IsEqualTo("1");
		await Assert.That(await EvalAs(_clerk.DbRef, $"atrlock({_thing}/XP,read)")).StartsWith("#-1");
	}

	[Test]
	public async Task ReadingAKeyWorksWhereSideEffectsAreOff()
	{
		await AsOwner($"@atrlock/write {_thing}/XP=={Ref(_clerk)}");
		await AsOwner($"&SHOW {_thing}=[atrlock(%!/XP,write)]");

		string read, set, oldForm;
		using (SharpMUSH.Library.ParserInterfaces.SideEffectFreeEvaluation.Enter())
		{
			read = await EvalAs(_owner.DbRef, $"atrlock({_thing}/XP, Write )");
			set = await EvalAs(_owner.DbRef, $"atrlock({_thing}/XP,read,={Ref(_stranger)})");
			oldForm = await EvalAs(_owner.DbRef, $"atrlock({_thing}/XP,on)");
		}

		await Assert.That(read).IsEqualTo($"={Ref(_clerk)}");
		await Assert.That(set).IsEqualTo("#-1 FUNCTION DISABLED");
		await Assert.That(oldForm).IsEqualTo("#-1 FUNCTION DISABLED");
		await Assert.That(await EvalAs(_owner.DbRef, $"atrlock({_thing}/XP,read)")).IsEqualTo(string.Empty);
		await Assert.That(await EvalAs(_owner.DbRef, $"memo({_thing}/SHOW)")).IsEqualTo($"={Ref(_clerk)}");
	}

	[Test]
	public async Task TheFunctionSetsAKey()
	{
		await Assert.That(await EvalAs(_owner.DbRef, $"atrlock({_thing}/XP,write,={Ref(_clerk)})")).IsEqualTo(string.Empty);

		await AsClerk($"&XP {_thing}=5");

		await Assert.That(await Get(_owner, "XP")).IsEqualTo("5");
	}

	[Test]
	public async Task ATableKeyCoversTheAttributeOnEveryObject()
	{
		var name = "KEYXP" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
		await Cmd($"@attribute/access {name}=no_command");
		await Cmd($"@attribute/lock/write {name}=={Ref(_clerk)}");

		await AsOwner($"&{name} {_thing}=1");
		await AsClerk($"&{name} {_thing}=2");
		await Assert.That(await Get(_owner, name)).IsEqualTo("2");

		// Not yet on the object: the table's key still lets the clerk create it.
		var other = (await AsOwner($"@create {TestIsolationHelpers.GenerateUniqueName("KeyBox2")}")).Trim();
		await AsClerk($"&{name} {other}=3");
		await Assert.That(await EvalAs(_owner.DbRef, $"get({other}/{name})")).IsEqualTo("3");
	}

	[Test]
	public async Task AnAttributesOwnKeyOverridesTheTables()
	{
		var name = "KEYOV" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
		await Cmd($"@attribute/access {name}=no_command");
		await Cmd($"@attribute/lock/write {name}=={Ref(_clerk)}");
		await AsOwner($"&{name} {_thing}=1");
		await AsOwner($"@atrlock/write {_thing}/{name}=={Ref(_stranger)}");

		await AsClerk($"&{name} {_thing}=2");
		await Assert.That(await Get(_owner, name)).IsEqualTo("1");

		await AsStranger($"&{name} {_thing}=3");
		await Assert.That(await Get(_owner, name)).IsEqualTo("3");
	}

	[Test]
	public async Task ABranchKeyCoversItsLeaves()
	{
		await AsOwner($"&STATS {_thing}=");
		await AsOwner($"&STATS`STR {_thing}=10");
		await AsOwner($"@atrlock/write {_thing}/STATS=={Ref(_clerk)}");

		await AsClerk($"&STATS`STR {_thing}=12");
		await AsClerk($"&STATS`DEX {_thing}=8");

		await Assert.That(await Get(_owner, "STATS`STR")).IsEqualTo("12");
		await Assert.That(await Get(_owner, "STATS`DEX")).IsEqualTo("8");
	}

	[Test]
	public async Task ATableKeyOnALeafLetsItBeCreatedUnderAnExistingBranch()
	{
		var branch = "KEYBR" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
		await AsWizard($"@attribute/access {branch}`DEX=no_command");
		await AsWizard($"@attribute/lock/write {branch}`DEX=={Ref(_clerk)}");
		await AsOwner($"&{branch} {_thing}=");
		await AsOwner($"&{branch}`STR {_thing}=10");

		await AsClerk($"&{branch}`DEX {_thing}=8");
		await AsClerk($"&{branch}`STR {_thing}=1");

		await Assert.That(await Get(_owner, $"{branch}`DEX")).IsEqualTo("8");
		await Assert.That(await Get(_owner, $"{branch}`STR")).IsEqualTo("10");
	}

	[Test]
	public async Task ExamineShowsTheKeysToWhoeverMayExamine()
	{
		await AsOwner($"@atrlock/write {_thing}/XP=={Ref(_clerk)}");
		await AsOwner($"&SECRET {_thing}=hidden");
		await AsOwner($"@atrlock/read {_thing}/SECRET=={Ref(_clerk)}");

		var owner = await HeardBy(_owner, () => AsOwner($"examine {_thing}/XP"));
		await Assert.That(owner).Contains(line => line.StartsWith($"XP [#{_owner.DbRef.Number} write:={_clerk.Name}]: 1"));

		var clerk = await HeardBy(_clerk, () => AsClerk($"examine {_thing}/SECRET"));
		await Assert.That(clerk).Contains(line => line.StartsWith("SECRET [") && line.Contains("hidden"));
		await Assert.That(clerk).DoesNotContain(line => line.Contains("read:"));
	}

	[Test]
	public async Task DecompileWritesTheKeysAndTheLinesReplay()
	{
		await AsOwner($"@atrlock/write {_thing}/XP=={Ref(_clerk)}");
		await AsOwner($"@atrlock/read {_thing}/XP=={Ref(_stranger)}");

		var lines = await HeardBy(_owner, () => AsOwner($"@decompile/db {_thing}/XP"));
		var number = _thing.Split(':')[0];
		await Assert.That(lines).Contains($"@atrlock/read {number}/XP=={Ref(_stranger)}");
		await Assert.That(lines).Contains($"@atrlock/write {number}/XP=={Ref(_clerk)}");

		await AsOwner($"@atrlock/read {_thing}/XP=");
		await AsOwner($"@atrlock/write {_thing}/XP=");
		foreach (var line in lines.Where(line => line.StartsWith("@atrlock/")))
			await AsOwner(line);

		await Assert.That(await EvalAs(_owner.DbRef, $"atrlock({_thing}/XP,write)")).IsEqualTo($"={Ref(_clerk)}");
		await Assert.That(await EvalAs(_owner.DbRef, $"atrlock({_thing}/XP,read)")).IsEqualTo($"={Ref(_stranger)}");
	}

	[Test]
	public async Task DecompileOfASafeAttributeReplaysItsKeys()
	{
		await AsOwner($"@atrlock/write {_thing}/XP=={Ref(_clerk)}");
		await AsOwner($"@set {_thing}/XP=safe");

		var lines = (await HeardBy(_owner, () => AsOwner($"@decompile/db {_thing}/XP"))).ToList();
		var number = _thing.Split(':')[0];
		var key = lines.FindIndex(line => line.StartsWith("@atrlock/write "));
		var flags = lines.FindIndex(line => line.StartsWith($"@set {number}/XP="));
		await Assert.That(key).IsGreaterThanOrEqualTo(0).Because(string.Join("|", lines));
		await Assert.That(flags).IsGreaterThan(key).Because("SAFE would refuse the key line if it came first");

		var copy = (await AsOwner($"@create {TestIsolationHelpers.GenerateUniqueName("KeyCopy")}")).Trim();
		var copyNumber = copy.Split(':')[0];
		foreach (var line in lines.Where(line => line.Contains($"{number}/XP") || line.StartsWith("&XP ")))
			await AsOwner(System.Text.RegularExpressions.Regex.Replace(line, System.Text.RegularExpressions.Regex.Escape(number) + @"(?!\d)", copyNumber));

		await Assert.That(await EvalAs(_owner.DbRef, $"atrlock({copy}/XP,write)")).IsEqualTo($"={Ref(_clerk)}");
		await Assert.That(await EvalAs(_owner.DbRef, $"hasflag({copy}/XP,safe)")).IsEqualTo("1");
	}

	[Test]
	public async Task AnAncestorsReadKeyLetsExamineShowALeaf()
	{
		await AsOwner($"&NOTES {_thing}=top");
		await AsOwner($"&NOTES`PRIVATE {_thing}=leafvalue");
		await AsOwner($"@atrlock/read {_thing}/NOTES=={Ref(_clerk)}");

		var clerk = await HeardBy(_clerk, () => AsClerk($"examine {_thing}/NOTES`PRIVATE"));
		await Assert.That(clerk).Contains(line => line.Contains("leafvalue")).Because(string.Join("|", clerk));
		var stranger = await HeardBy(_stranger, () => AsStranger($"examine {_thing}/NOTES`PRIVATE"));
		await Assert.That(stranger).DoesNotContain(line => line.Contains("leafvalue"));

		// The ancestor's key never reaches past the ancestor's own dark flag.
		await AsWizard($"@set {_thing}/NOTES=mortal_dark");
		var hidden = await HeardBy(_clerk, () => AsClerk($"examine {_thing}/NOTES`PRIVATE"));
		await Assert.That(hidden).DoesNotContain(line => line.Contains("leafvalue")).Because(string.Join("|", hidden));
	}

	[Test]
	public async Task GivenOnlyTheLeafAnAncestorsDarkFlagStillHidesIt()
	{
		await AsOwner($"&NOTES {_thing}=top");
		await AsOwner($"&NOTES`PRIVATE {_thing}=leafvalue");
		await AsOwner($"@atrlock/read {_thing}/NOTES=={Ref(_clerk)}");
		var permissions = WebAppFactoryArg.Services.GetRequiredService<IPermissionService>();
		var thing = (await Mediator.Send(new GetObjectNodeQuery(DBRef.Parse(_thing)))).Expect<AnySharpObject>();
		var clerk = (await Mediator.Send(new GetObjectNodeQuery(_clerk.DbRef))).Expect<AnySharpObject>();
		async Task<bool> ClerkSeesTheLeafAlone()
		{
			var leaf = (await Mediator.CreateStream(new GetAttributeQuery(thing.Object().DBRef, ["NOTES", "PRIVATE"])).ToListAsync())[^1];
			return await permissions.CanViewAttribute(clerk, thing, leaf);
		}

		await Assert.That(await ClerkSeesTheLeafAlone()).IsTrue();

		await AsWizard($"@set {_thing}/NOTES=mortal_dark");
		await Assert.That(await ClerkSeesTheLeafAlone()).IsFalse();

		await AsWizard($"@set {_thing}/NOTES=!mortal_dark");
		await Cmd($"@set {_thing}/NOTES=wizard_dark");
		await Assert.That(await ClerkSeesTheLeafAlone()).IsFalse();
	}

	[Test]
	public async Task AMemoOfAKeySeesTheKeyChange()
	{
		await AsOwner($"@atrlock/write {_thing}/XP=={Ref(_clerk)}");
		await AsOwner($"&SHOW {_thing}=[atrlock(%!/XP,write)]");
		await Assert.That(await EvalAs(_owner.DbRef, $"memo({_thing}/SHOW)")).IsEqualTo($"={Ref(_clerk)}");

		await AsOwner($"@atrlock/write {_thing}/XP=={Ref(_stranger)}");

		await Assert.That(await EvalAs(_owner.DbRef, $"memo({_thing}/SHOW)")).IsEqualTo($"={Ref(_stranger)}");
	}

	[Test]
	public async Task AValueNeverFingerprintsAsAKey()
	{
		var plain = SharpMUSH.Library.ParserInterfaces.AttributeReadLog.Fingerprint(MarkupString.MarkupText.Plain("A\u0003#1\u0003"), null, null);
		var keyed = SharpMUSH.Library.ParserInterfaces.AttributeReadLog.Fingerprint(MarkupString.MarkupText.Plain("A"), "#1", null);
		var empty = SharpMUSH.Library.ParserInterfaces.AttributeReadLog.Fingerprint(MarkupString.MarkupText.Plain("A"), "#1", string.Empty);

		await Assert.That(keyed).IsNotEqualTo(plain);
		await Assert.That(keyed).IsNotEqualTo(empty);
	}

	[Test]
	public async Task CopyCarriesTheKeysAndMoveKeepsThem()
	{
		await AsOwner($"@atrlock/write {_thing}/XP=={Ref(_clerk)}");
		var other = (await AsOwner($"@create {TestIsolationHelpers.GenerateUniqueName("KeyCopy")}")).Trim();

		await AsOwner($"@cpattr {_thing}/XP={other}/XP");
		await AsOwner($"@mvattr {_thing}/XP={other}/MOVED");
		await AsOwner($"@cpattr/noflagcopy {other}/XP={other}/BARE");

		await Assert.That(await EvalAs(_owner.DbRef, $"atrlock({other}/XP,write)")).IsEqualTo($"={Ref(_clerk)}");
		await Assert.That(await EvalAs(_owner.DbRef, $"atrlock({other}/MOVED,write)")).IsEqualTo($"={Ref(_clerk)}");
		await Assert.That(await EvalAs(_owner.DbRef, $"atrlock({other}/BARE,write)")).IsEqualTo(string.Empty);
	}

	[Test]
	public async Task ClearingTheAttributeDropsItsKeys()
	{
		await AsOwner($"@atrlock/write {_thing}/XP=={Ref(_clerk)}");
		await AsOwner($"&XP {_thing}");
		await AsOwner($"&XP {_thing}=1");

		await Assert.That(await EvalAs(_owner.DbRef, $"atrlock({_thing}/XP,write)")).IsEqualTo(string.Empty);
	}

	[Test]
	public async Task SettingTheValueKeepsTheKeys()
	{
		await AsOwner($"@atrlock/write {_thing}/XP=={Ref(_clerk)}");
		await AsOwner($"&XP {_thing}=2");

		await Assert.That(await EvalAs(_owner.DbRef, $"atrlock({_thing}/XP,write)")).IsEqualTo($"={Ref(_clerk)}");
	}

	[Test]
	public async Task ARoleKeyOnAnAccountLetsItsHoldersWrite()
	{
		var account = (await Accounts.CreateAccountAsync(TestIsolationHelpers.GenerateUniqueName("keyacct").ToLowerInvariant(), null,
			"correct horse battery")).Expect<SharpAccount>();
		await Accounts.AttachCharacterAsync(account.Id!, (await Mediator.Send(new GetObjectNodeQuery(_owner.DbRef))).Expect<SharpPlayer>());
		var holder = AccountHolders.ForAccount(account.Id)!.Value.ToString();
		var role = "xpclerk" + Guid.NewGuid().ToString("N")[..8];
		await Cmd($"@role/create {role}=Staff/XP clerk");
		await Cmd($"@role/assign {Ref(_clerk)}={role}");

		await AsWizard($"&XP {holder}=10");
		await AsWizard($"@atrlock/write account({Ref(_owner)}, ref)/XP=role^{role}");

		await AsClerk($"&XP {holder}=15");
		await AsStranger($"&XP {holder}=99");
		await AsOwner($"&XP {holder}=98");

		await Assert.That(await EvalAs(_wizard.DbRef, $"get({holder}/XP)")).IsEqualTo("15");

		// A role the account holds reaches every character on it.
		await Cmd($"@role/assign/account {Ref(_owner)}={role}");
		await AsOwner($"&XP {holder}=20");
		await Assert.That(await EvalAs(_wizard.DbRef, $"get({holder}/XP)")).IsEqualTo("20");
	}

	[Test]
	public async Task AnAccountRefusesAnEvaluationKey()
	{
		var account = (await Accounts.CreateAccountAsync(TestIsolationHelpers.GenerateUniqueName("keyeval").ToLowerInvariant(), null,
			"correct horse battery")).Expect<SharpAccount>();
		var holder = AccountHolders.ForAccount(account.Id)!.Value.ToString();
		await AsWizard($"&XP {holder}=10");

		var heard = await HeardBy(_wizard, () => AsWizard($"@atrlock/write {holder}/XP=CHECK/1"));

		await Assert.That(heard).Contains("An account runs no code, so its keys cannot hold an evaluation lock.");
		await Assert.That(await EvalAs(_wizard.DbRef, $"atrlock({holder}/XP,write)")).IsEqualTo(string.Empty);
	}

	[Test]
	public async Task TheAttributeTableShowsAndDecompilesItsKeys()
	{
		var name = "KEYTAB" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
		await AsWizard($"@attribute/access {name}=no_command");
		await AsWizard($"@attribute/lock/read {name}=={Ref(_clerk)}");

		var shown = await HeardBy(_wizard, () => AsWizard($"@attribute {name}"));
		await Assert.That(shown).Contains($"  Read key: ={Ref(_clerk)}");

		var decompiled = await HeardBy(_wizard, () => AsWizard($"@attribute/decompile {name}"));
		await Assert.That(decompiled).Contains($"@attribute/lock/read {name}=={Ref(_clerk)}");
	}

	[Test]
	public async Task TheHelpExamplesHold()
	{
		var role = "xpclerk" + Guid.NewGuid().ToString("N")[..8];
		await Cmd($"@role/create {role}=Staff/XP clerk");
		await Cmd($"@role/assign {Ref(_clerk)}={role}");

		// help attribute keys: the role may set this XP and nothing else.
		await Assert.That(await HeardBy(_owner, () => AsOwner($"@atrlock/write {_thing}/XP=role^{role}")))
			.IsEquivalentTo(["XP - write key set."]);
		await AsClerk($"&XP {_thing}=11");
		await AsClerk($"&LEVEL {_thing}=2");
		await Assert.That(await Get(_owner, "XP")).IsEqualTo("11");
		await Assert.That(await Get(_owner, "LEVEL")).IsEqualTo(string.Empty);

		// help attribute keys showing: examine prints the key after the flags.
		var examined = await HeardBy(_owner, () => AsOwner($"examine {_thing}/XP"));
		await Assert.That(examined).Contains($"XP [#{_clerk.DbRef.Number} write:ROLE^{role}]: 11").Because(string.Join("|", examined));

		// help atrlock(): sets, then reads back.
		await AsOwner($"&LEVEL {_thing}=1");
		var told = await HeardBy(_owner, async () => await Assert.That(
			await EvalAs(_owner.DbRef, $"atrlock({_thing}/LEVEL, write, role^{role})[atrlock({_thing}/LEVEL, write)]")).IsEqualTo($"ROLE^{role}"));
		await Assert.That(told).Contains("LEVEL - write key set.");

		// help @attribute lock: the table's key and how @attribute shows it.
		var name = "XPHELP" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
		await AsWizard($"@attribute/access {name}=no_command");
		await Assert.That(await HeardBy(_wizard, () => AsWizard($"@attribute/lock/write {name}=role^{role}")))
			.Contains($"{name} - write key set.");
		await Assert.That(await HeardBy(_wizard, () => AsWizard($"@attribute {name}"))).Contains($"  Write key: ROLE^{role}");
	}

	[Test]
	public async Task ATableKeyLetsARoleWriteThatAttributeOnEveryAccount()
	{
		var role = "xpclerk" + Guid.NewGuid().ToString("N")[..8];
		var name = "XPACCT" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
		await Cmd($"@role/create {role}=Staff/XP clerk");
		await Cmd($"@role/assign {Ref(_clerk)}={role}");
		await AsWizard($"@attribute/access {name}=no_command");
		await AsWizard($"@attribute/lock/write {name}=role^{role}");
		var account = (await Accounts.CreateAccountAsync(TestIsolationHelpers.GenerateUniqueName("keytab").ToLowerInvariant(), null,
			"correct horse battery")).Expect<SharpAccount>();
		var holder = AccountHolders.ForAccount(account.Id)!.Value.ToString();

		await AsClerk($"&{name} {holder}=4");
		await AsClerk($"&OTHER {holder}=no");
		await AsClerk($"@set {holder}/{name}=wizard");

		await Assert.That(await EvalAs(_wizard.DbRef, $"get({holder}/{name})")).IsEqualTo("4");
		await Assert.That(await EvalAs(_wizard.DbRef, $"hasattr({holder},OTHER)")).IsEqualTo("0");
		await Assert.That(await EvalAs(_wizard.DbRef, $"flags({holder}/{name})")).DoesNotContain("w");
	}

	[Test]
	public async Task NoKeysChangesNothing()
	{
		await AsOwner($"&SECRET {_thing}=the plan");

		await Assert.That(await Get(_clerk, "SECRET")).StartsWith("#-1");
		await AsClerk($"&XP {_thing}=5");
		await Assert.That(await Get(_owner, "XP")).IsEqualTo("1");
	}
}
