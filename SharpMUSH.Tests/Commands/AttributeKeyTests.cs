using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// The attribute table's read and write keys (<c>@attribute/lock/read</c>, <c>/write</c>): staff set them, and a key grants
/// reading or setting that attribute on every object to whoever passes it, and never takes access away. Attribute flags
/// still apply on top.
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
		_xp = await Entry("KEYXP");
		await AsOwner($"&{_xp} {_thing}=1");
	}

	/// <summary>The attribute every test keys: its own table entry, since the table is shared by every test.</summary>
	private string _xp = null!;

	/// <summary>A new attribute table entry, named <paramref name="prefix"/> and a unique suffix, with no default flags.</summary>
	private async Task<string> Entry(string prefix)
	{
		var name = prefix + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
		await AsWizard($"@attribute/access {name}=none");
		return name;
	}

	private Task Key(string kind, string name, string key) => AsWizard($"@attribute/lock/{kind} {name}={key}");

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
		await Key("write", _xp, $"={Ref(_clerk)}");

		await AsClerk($"&{_xp} {_thing}=5");
		await AsClerk($"&OTHER {_thing}=sneaky");
		await AsStranger($"&{_xp} {_thing}=99");

		await Assert.That(await Get(_owner, _xp)).IsEqualTo("5");
		await Assert.That(await Get(_owner, "OTHER")).IsEqualTo(string.Empty);
	}

	[Test]
	public async Task AWriteKeyCoversTheAttributeOnEveryObjectAndCreatesIt()
	{
		await Key("write", _xp, $"={Ref(_clerk)}");
		var other = (await AsStranger($"@create {TestIsolationHelpers.GenerateUniqueName("KeyBox2")}")).Trim();

		await AsClerk($"&{_xp} {other}=3");

		await Assert.That(await EvalAs(_stranger.DbRef, $"get({other}/{_xp})")).IsEqualTo("3");
	}

	[Test]
	public async Task AWriteKeySetsThroughTheFunctionsButNeverClears()
	{
		await Key("write", _xp, $"={Ref(_clerk)}");

		await EvalAs(_clerk.DbRef, $"set({_thing},{_xp}:7)");
		await EvalAs(_clerk.DbRef, $"attrib_set({_thing}/{_xp},8)");
		await Assert.That(await Get(_owner, _xp)).IsEqualTo("8");

		var refused = await HeardBy(_clerk, () => AsClerk($"&{_xp} {_thing}"));
		await Assert.That(refused).Contains(line => line.StartsWith("Permission denied.")).Because(string.Join("|", refused));
		await EvalAs(_clerk.DbRef, $"attrib_set({_thing}/{_xp})");
		await Assert.That(await Get(_owner, _xp)).IsEqualTo("8");
	}

	[Test]
	public async Task AKeyCannotStripAnAttributesFlagsByClearingIt()
	{
		await Key("write", _xp, $"={Ref(_clerk)}");
		await AsOwner($"@set {_thing}/{_xp}=no_command");

		await AsClerk($"&{_xp} {_thing}");
		await AsClerk($"&{_xp} {_thing}=$hack:think hacked");

		await Assert.That(await EvalAs(_owner.DbRef, $"flags({_thing}/{_xp})")).Contains("$");
	}

	[Test]
	public async Task AWriteKeyGrantsNoFlagChangesAndNoWipe()
	{
		await Key("write", _xp, $"={Ref(_clerk)}");

		await AsClerk($"@set {_thing}/{_xp}=no_command");
		await AsClerk($"@wipe {_thing}");

		await Assert.That(await EvalAs(_owner.DbRef, $"flags({_thing}/{_xp})")).DoesNotContain("$");
		await Assert.That(await Get(_owner, _xp)).IsEqualTo("1");
	}

	[Test]
	public async Task TheLockedFlagStillRefusesAKeyHolder()
	{
		await Key("write", _xp, $"={Ref(_clerk)}");
		await AsOwner($"@atrlock {_thing}/{_xp}=on");

		await AsClerk($"&{_xp} {_thing}=5");

		await Assert.That(await Get(_owner, _xp)).IsEqualTo("1");
	}

	[Test]
	public async Task AReadKeyLetsWhoeverPassesItReadAHiddenAttribute()
	{
		var secret = await Entry("KEYSECRET");
		await AsOwner($"&{secret} {_thing}=the plan");
		await Assert.That(await Get(_clerk, secret)).StartsWith("#-1");

		await Key("read", secret, $"={Ref(_clerk)}");

		await Assert.That(await Get(_clerk, secret)).IsEqualTo("the plan");
		await Assert.That(await Get(_stranger, secret)).StartsWith("#-1");
		await Assert.That(await EvalAs(_clerk.DbRef, $"lattr({_thing})")).Contains(secret);
		await Assert.That(await EvalAs(_stranger.DbRef, $"lattr({_thing})")).DoesNotContain(secret);
	}

	[Test]
	public async Task DarkFlagsStillHideFromAKeyHolder()
	{
		var secret = await Entry("KEYDARK");
		await AsOwner($"&{secret} {_thing}=the plan");
		await Key("read", secret, $"={Ref(_clerk)}");
		await AsWizard($"@set {_thing}/{secret}=mortal_dark");

		await Assert.That(await Get(_clerk, secret)).StartsWith("#-1");
		await Assert.That(await Get(_wizard, secret)).IsEqualTo("the plan");
	}

	[Test]
	public async Task AReadKeyIsNotAWriteKey()
	{
		await Key("read", _xp, $"={Ref(_clerk)}");

		await AsClerk($"&{_xp} {_thing}=5");

		await Assert.That(await Get(_owner, _xp)).IsEqualTo("1");
	}

	[Test]
	public async Task OnlyStaffSetAKey()
	{
		var heard = await HeardBy(_owner, () => AsOwner($"@attribute/lock/write {_xp}=={Ref(_owner)}"));

		await Assert.That(heard).Contains(line => line.StartsWith("Permission denied.")).Because(string.Join("|", heard));
		await AsStranger($"&{_xp} {_thing}=5");
		await AsClerk($"&{_xp} {_thing}=6");
		await Assert.That(await Get(_owner, _xp)).IsEqualTo("1");
		await Assert.That(await HeardBy(_wizard, () => AsWizard($"@attribute {_xp}"))).DoesNotContain(line => line.Contains("Write key"));
	}

	[Test]
	public async Task TheCommandReportsWhatItDid()
	{
		await Assert.That(await HeardBy(_wizard, () => AsWizard($"@attribute/lock/write {_xp}=={Ref(_clerk)}")))
			.IsEquivalentTo([$"{_xp} - write key set."]);
		await Assert.That(await HeardBy(_wizard, () => AsWizard($"@attribute/lock/write {_xp}")))
			.IsEquivalentTo([$"{_xp} - write key: ={Ref(_clerk)}"]);
		await Assert.That(await HeardBy(_wizard, () => AsWizard($"@attribute/lock/read {_xp}")))
			.IsEquivalentTo([$"{_xp} has no read key."]);
		await Assert.That(await HeardBy(_wizard, () => AsWizard($"@attribute/lock/write {_xp}=")))
			.IsEquivalentTo([$"{_xp} - write key cleared."]);
		await Assert.That(await HeardBy(_wizard, () => AsWizard($"@attribute/lock/write {_xp}=)(")))
			.IsEquivalentTo(["I don't understand that key."]);
		await Assert.That(await HeardBy(_wizard, () => AsWizard($"@attribute/lock {_xp}=={Ref(_clerk)}")))
			.IsEquivalentTo(["Say which key: @attribute/lock/read or @attribute/lock/write."]);
	}

	[Test]
	public async Task ABranchKeyCoversItsLeaves()
	{
		var stats = await Entry("KEYSTATS");
		await AsOwner($"&{stats} {_thing}=");
		await AsOwner($"&{stats}`STR {_thing}=10");
		await Key("write", stats, $"={Ref(_clerk)}");

		await AsClerk($"&{stats}`STR {_thing}=12");
		await AsClerk($"&{stats}`DEX {_thing}=8");

		await Assert.That(await Get(_owner, $"{stats}`STR")).IsEqualTo("12");
		await Assert.That(await Get(_owner, $"{stats}`DEX")).IsEqualTo("8");
	}

	[Test]
	public async Task AKeyOnALeafLetsItBeCreatedUnderAnExistingBranch()
	{
		var branch = "KEYBR" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
		await AsWizard($"@attribute/access {branch}`DEX=none");
		await Key("write", $"{branch}`DEX", $"={Ref(_clerk)}");
		await AsOwner($"&{branch} {_thing}=");
		await AsOwner($"&{branch}`STR {_thing}=10");

		await AsClerk($"&{branch}`DEX {_thing}=8");
		await AsClerk($"&{branch}`STR {_thing}=1");

		await Assert.That(await Get(_owner, $"{branch}`DEX")).IsEqualTo("8");
		await Assert.That(await Get(_owner, $"{branch}`STR")).IsEqualTo("10");
	}

	[Test]
	public async Task AnAncestorsReadKeyLetsExamineShowALeaf()
	{
		var notes = await Entry("KEYNOTES");
		await AsOwner($"&{notes} {_thing}=top");
		await AsOwner($"&{notes}`PRIVATE {_thing}=leafvalue");
		await Key("read", notes, $"={Ref(_clerk)}");

		var clerk = await HeardBy(_clerk, () => AsClerk($"examine {_thing}/{notes}`PRIVATE"));
		await Assert.That(clerk).Contains(line => line.Contains("leafvalue")).Because(string.Join("|", clerk));
		var stranger = await HeardBy(_stranger, () => AsStranger($"examine {_thing}/{notes}`PRIVATE"));
		await Assert.That(stranger).DoesNotContain(line => line.Contains("leafvalue"));

		// The ancestor's key never reaches past the ancestor's own dark flag.
		await AsWizard($"@set {_thing}/{notes}=mortal_dark");
		var hidden = await HeardBy(_clerk, () => AsClerk($"examine {_thing}/{notes}`PRIVATE"));
		await Assert.That(hidden).DoesNotContain(line => line.Contains("leafvalue")).Because(string.Join("|", hidden));
	}

	[Test]
	public async Task GivenOnlyTheLeafAnAncestorsDarkFlagStillHidesIt()
	{
		var notes = await Entry("KEYLEAF");
		await AsOwner($"&{notes} {_thing}=top");
		await AsOwner($"&{notes}`PRIVATE {_thing}=leafvalue");
		await Key("read", notes, $"={Ref(_clerk)}");
		var permissions = WebAppFactoryArg.Services.GetRequiredService<IPermissionService>();
		var thing = (await Mediator.Send(new GetObjectNodeQuery(DBRef.Parse(_thing)))).Expect<AnySharpObject>();
		var clerk = (await Mediator.Send(new GetObjectNodeQuery(_clerk.DbRef))).Expect<AnySharpObject>();
		async Task<bool> ClerkSeesTheLeafAlone()
		{
			var leaf = (await Mediator.CreateStream(new GetAttributeQuery(thing.Object().DBRef, [notes, "PRIVATE"])).ToListAsync())[^1];
			return await permissions.CanViewAttribute(clerk, thing, leaf);
		}

		await Assert.That(await ClerkSeesTheLeafAlone()).IsTrue();

		await AsWizard($"@set {_thing}/{notes}=mortal_dark");
		await Assert.That(await ClerkSeesTheLeafAlone()).IsFalse();

		await AsWizard($"@set {_thing}/{notes}=!mortal_dark");
		await Cmd($"@set {_thing}/{notes}=wizard_dark");
		await Assert.That(await ClerkSeesTheLeafAlone()).IsFalse();
	}

	[Test]
	public async Task ExamineShowsNoKeyToTheHolder()
	{
		var secret = await Entry("KEYEXAM");
		await AsOwner($"&{secret} {_thing}=hidden");
		await Key("read", secret, $"={Ref(_clerk)}");

		var clerk = await HeardBy(_clerk, () => AsClerk($"examine {_thing}/{secret}"));

		await Assert.That(clerk).Contains(line => line.StartsWith($"{secret} [") && line.Contains("hidden")).Because(string.Join("|", clerk));
		await Assert.That(clerk).DoesNotContain(line => line.Contains(Ref(_clerk)));
	}

	[Test]
	public async Task ARoleKeyLetsARoleWriteThatAttributeOnEveryAccount()
	{
		var role = "xpclerk" + Guid.NewGuid().ToString("N")[..8];
		await Cmd($"@role/create {role}=Staff/XP clerk");
		await Cmd($"@role/assign {Ref(_clerk)}={role}");
		await Key("write", _xp, $"role^{role}");
		var account = (await Accounts.CreateAccountAsync(TestIsolationHelpers.GenerateUniqueName("keytab").ToLowerInvariant(), null,
			"correct horse battery")).Expect<SharpAccount>();
		await Accounts.AttachCharacterAsync(account.Id!, (await Mediator.Send(new GetObjectNodeQuery(_owner.DbRef))).Expect<SharpPlayer>());
		var holder = AccountHolders.ForAccount(account.Id)!.Value.ToString();
		await AsWizard($"&{_xp} {holder}=10");

		await AsClerk($"&{_xp} {holder}=15");
		await AsClerk($"&OTHER {holder}=no");
		await AsClerk($"@set {holder}/{_xp}=wizard");
		await AsStranger($"&{_xp} {holder}=99");

		await Assert.That(await EvalAs(_wizard.DbRef, $"get({holder}/{_xp})")).IsEqualTo("15");
		await Assert.That(await EvalAs(_wizard.DbRef, $"hasattr({holder},OTHER)")).IsEqualTo("0");
		await Assert.That(await EvalAs(_wizard.DbRef, $"flags({holder}/{_xp})")).DoesNotContain("w");

		// A role the account holds reaches every character on it.
		await Cmd($"@role/assign/account {Ref(_owner)}={role}");
		await AsOwner($"&{_xp} {holder}=20");
		await Assert.That(await EvalAs(_wizard.DbRef, $"get({holder}/{_xp})")).IsEqualTo("20");
	}

	[Test]
	public async Task AnEvaluationKeyNeverPassesOnAnAccount()
	{
		var pass = await Entry("KEYEVAL");
		await Key("write", pass, "CHECK/1");
		await AsOwner($"&CHECK {_thing}=1");
		await AsOwner($"&{pass} {_thing}=0");
		var account = (await Accounts.CreateAccountAsync(TestIsolationHelpers.GenerateUniqueName("keyeval").ToLowerInvariant(), null,
			"correct horse battery")).Expect<SharpAccount>();
		var holder = AccountHolders.ForAccount(account.Id)!.Value.ToString();
		await AsWizard($"&CHECK {holder}=1");
		await AsWizard($"&{pass} {holder}=0");

		await AsStranger($"&{pass} {_thing}=2");
		await AsStranger($"&{pass} {holder}=2");

		await Assert.That(await Get(_owner, pass)).IsEqualTo("2");
		await Assert.That(await EvalAs(_wizard.DbRef, $"get({holder}/{pass})")).IsEqualTo("0");
	}

	[Test]
	public async Task TheAttributeTableShowsAndDecompilesItsKeys()
	{
		await Key("read", _xp, $"={Ref(_clerk)}");
		await Key("write", _xp, $"={Ref(_stranger)}");

		var shown = await HeardBy(_wizard, () => AsWizard($"@attribute {_xp}"));
		await Assert.That(shown).Contains($"  Read key: ={Ref(_clerk)}");
		await Assert.That(shown).Contains($"  Write key: ={Ref(_stranger)}");

		var player = await HeardBy(_owner, () => AsOwner($"@attribute {_xp}"));
		await Assert.That(player).DoesNotContain(line => line.Contains(" key: ")).Because("a key is shown only to staff");

		var decompiled = await HeardBy(_wizard, () => AsWizard($"@attribute/decompile {_xp}"));
		await Assert.That(decompiled).Contains($"@attribute/lock/read {_xp}=={Ref(_clerk)}");
		await Assert.That(decompiled).Contains($"@attribute/lock/write {_xp}=={Ref(_stranger)}");
	}

	[Test]
	public async Task TheHelpExamplesHold()
	{
		var role = "xpclerk" + Guid.NewGuid().ToString("N")[..8];
		await Cmd($"@role/create {role}=Staff/XP clerk");
		await Cmd($"@role/assign {Ref(_clerk)}={role}");

		// help attribute keys: the role may set this attribute on any object, and nothing else.
		await Assert.That(await HeardBy(_wizard, () => AsWizard($"@attribute/lock/write {_xp}=role^{role}")))
			.IsEquivalentTo([$"{_xp} - write key set."]);
		await AsClerk($"&{_xp} {_thing}=11");
		await AsClerk($"&LEVEL {_thing}=2");
		await Assert.That(await Get(_owner, _xp)).IsEqualTo("11");
		await Assert.That(await Get(_owner, "LEVEL")).IsEqualTo(string.Empty);
		await Assert.That(await HeardBy(_wizard, () => AsWizard($"@attribute {_xp}"))).Contains($"  Write key: ROLE^{role}");
	}

	[Test]
	public async Task AccessNoneSaysNoPermissionsAreSet()
	{
		var name = "KEYACC" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

		await Assert.That(await HeardBy(_wizard, () => AsWizard($"@attribute/access {name}=none")))
			.IsEquivalentTo([$"{name} -- Attribute permissions now: none"]);
		await Assert.That(await HeardBy(_wizard, () => AsWizard($"@attribute/access {name}=wizard")))
			.IsEquivalentTo([$"{name} -- Attribute permissions now: wizard"]);
	}

	[Test]
	public async Task NoKeysChangesNothing()
	{
		var secret = await Entry("KEYNONE");
		await AsOwner($"&{secret} {_thing}=the plan");

		await Assert.That(await Get(_clerk, secret)).StartsWith("#-1");
		await AsClerk($"&{_xp} {_thing}=5");
		await Assert.That(await Get(_owner, _xp)).IsEqualTo("1");
	}
}
