using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using System.Collections.Concurrent;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// The behaviour the Commands/Functions partials sweep (#964, #965) moved onto shared code: the one
/// q-register scope, the one privilege reset on a change of owner or zone, @wizmotd and @rejectmotd
/// running @motd's body, @verb reporting a failed match once, and the one too-few-arguments guard
/// that still words each command's own usage.
/// </summary>
public class PartialsNormalisationTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser CommandParser => WebAppFactoryArg.CommandParser;
	private IMUSHCodeParser FunctionParser => WebAppFactoryArg.FunctionParser;
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();

	private Task AsGod(string command) =>
		CommandParser.CommandParse(1, ConnectionService, MarkupText.Plain(command)).AsTask();

	private async Task<string> Eval(string expression) =>
		(await FunctionParser.FunctionParse(MarkupText.Plain(expression)))!.Message.ToPlainText();

	private async Task<TestIsolationHelpers.TestPlayer> PlayerAsync(string prefix) =>
		await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(WebAppFactoryArg.Services, Mediator,
			ConnectionService, prefix);

	private async Task RunAs(TestIsolationHelpers.TestPlayer player, string command) =>
		await WebAppFactoryArg.CommandParserFor(player.DbRef, player.Handle)
			.CommandParse(player.Handle, ConnectionService, MarkupText.Plain(command));

	private static ConcurrentStack<Dictionary<string, MString>> Registers(string name, string value)
	{
		var registers = new ConcurrentStack<Dictionary<string, MString>>();
		registers.Push(new Dictionary<string, MString> { [name] = MarkupText.Plain(value) });
		return registers;
	}

	[Test]
	public async ValueTask RegisterScopeLocalizeRestoresTheCallersRegisters()
	{
		var registers = Registers("A", "before");

		using (RegisterScope.Enter(registers, localize: true, clear: false))
		{
			registers.TryPeek(out var inner);
			inner!["A"] = MarkupText.Plain("inside");
			inner["B"] = MarkupText.Plain("added");
		}

		registers.TryPeek(out var after);
		await Assert.That(after!.Keys.ToArray()).IsEquivalentTo(["A"]);
		await Assert.That(after["A"].ToPlainText()).IsEqualTo("before");
	}

	[Test]
	public async ValueTask RegisterScopeClearWithoutLocalizeKeepsTheChanges()
	{
		var registers = Registers("A", "before");

		using (RegisterScope.Enter(registers, localize: false, clear: true))
		{
			registers.TryPeek(out var inner);
			await Assert.That(inner!.Count).IsEqualTo(0);
			inner["B"] = MarkupText.Plain("added");
		}

		registers.TryPeek(out var after);
		await Assert.That(after!.Keys.ToArray()).IsEquivalentTo(["B"]);
	}

	[Test]
	public async ValueTask RegisterScopeClearAndLocalizeStartsEmptyAndRestores()
	{
		var registers = Registers("A", "before");

		using (RegisterScope.Enter(registers, localize: true, clear: true))
		{
			registers.TryPeek(out var inner);
			await Assert.That(inner!.Count).IsEqualTo(0);
			inner["B"] = MarkupText.Plain("added");
		}

		registers.TryPeek(out var after);
		await Assert.That(after!.Keys.ToArray()).IsEquivalentTo(["A"]);
		await Assert.That(after["A"].ToPlainText()).IsEqualTo("before");
	}

	/// <summary>
	/// PennMUSH's <c>chown_object</c> (src/set.c:333-340) clears WIZARD, ROYALTY and TRUST and the whole
	/// power set, and sets HALT. @chown cleared only WIZARD and ROYALTY.
	/// </summary>
	[Test]
	public async ValueTask ChownStripsTrustAndPowers()
	{
		var newOwner = await PlayerAsync("ChownTo");
		var thing = await Eval($"create({TestIsolationHelpers.GenerateUniqueName("ChownStrip")})");
		await AsGod($"@set {thing}=TRUST");
		await AsGod($"@power {thing}=No_Quota");
		await Assert.That(await Eval($"hasflag({thing},TRUST)")).IsEqualTo("1");
		await Assert.That(await Eval($"haspower({thing},No_Quota)")).IsEqualTo("1");

		await AsGod($"@chown {thing}={newOwner.DbRef}");

		await Assert.That(await Eval($"owner({thing})")).IsEqualTo($"#{newOwner.DbRef.Number}");
		await Assert.That(await Eval($"hasflag({thing},TRUST)")).IsEqualTo("0");
		await Assert.That(await Eval($"haspower({thing},No_Quota)")).IsEqualTo("0");
		await Assert.That(await Eval($"hasflag({thing},HALT)")).IsEqualTo("1");
	}

	/// <summary>
	/// PennMUSH's <c>do_chown</c> ends every successful transfer with <c>Owner changed.</c> to the enactor
	/// (<c>src/set.c:238</c>), and <c>chown_object</c>'s <c>do_halt</c> (<c>src/set.c:340</c>) has already
	/// told the new owner <c>Halted: &lt;name&gt;(#&lt;dbref&gt;)</c>. SharpMUSH's @chown said nothing.
	/// </summary>
	[Test]
	public async ValueTask ChownReportsOwnerChangedAndHaltsForTheNewOwner()
	{
		var wizard = await PlayerAsync("ChownWizard");
		var newOwner = await PlayerAsync("ChownReceiver");
		await AsGod($"@set {wizard.DbRef}=WIZARD");
		var name = TestIsolationHelpers.GenerateUniqueName("ChownReported");
		var thing = await Eval($"create({name})");
		await AsGod($"@chown/preserve {thing}={wizard.DbRef}");
		var recorder = WebAppFactoryArg.Notifications;
		var wizardBefore = recorder.CountFor(wizard.DbRef);
		var ownerBefore = recorder.CountFor(newOwner.DbRef);

		await RunAs(wizard, $"@chown {thing}={newOwner.DbRef}");

		await Assert.That(recorder.For(wizard.DbRef).Skip(wizardBefore).ToList()).Contains("Owner changed.");
		await Assert.That(recorder.For(newOwner.DbRef).Skip(ownerBefore).Where(message => message.Contains(name)))
			.IsEquivalentTo([$"Halted: {name}({thing})"]);
		await Assert.That(await Eval($"owner({thing})")).IsEqualTo($"#{newOwner.DbRef.Number}");
	}

	/// <summary>
	/// <c>/preserve</c> skips <c>chown_object</c>'s halt (<c>src/set.c:332</c>), so only <c>Owner changed.</c> is said.
	/// </summary>
	[Test]
	public async ValueTask ChownPreserveReportsOwnerChangedWithoutHalting()
	{
		var wizard = await PlayerAsync("ChownPreserveWizard");
		var newOwner = await PlayerAsync("ChownPreserveReceiver");
		await AsGod($"@set {wizard.DbRef}=WIZARD");
		var name = TestIsolationHelpers.GenerateUniqueName("ChownPreserved");
		var thing = await Eval($"create({name})");
		await AsGod($"@chown/preserve {thing}={wizard.DbRef}");
		var recorder = WebAppFactoryArg.Notifications;
		var wizardBefore = recorder.CountFor(wizard.DbRef);
		var ownerBefore = recorder.CountFor(newOwner.DbRef);

		await RunAs(wizard, $"@chown/preserve {thing}={newOwner.DbRef}");

		await Assert.That(recorder.For(wizard.DbRef).Skip(wizardBefore).ToList()).Contains("Owner changed.");
		await Assert.That(recorder.For(newOwner.DbRef).Skip(ownerBefore).Where(message => message.Contains(name))).IsEmpty();
		await Assert.That(await Eval($"hasflag({thing},HALT)")).IsEqualTo("0");
	}

	/// <summary>
	/// A refused @chown changes nothing: <c>do_chown</c> runs <c>chown_object</c> only after the transfer
	/// is allowed (src/set.c:237), so the object keeps its flags and powers.
	/// </summary>
	[Test]
	public async ValueTask RefusedChownKeepsTrustAndPowers()
	{
		var holder = await PlayerAsync("ChownHolder");
		var stranger = await PlayerAsync("ChownStranger");
		var thing = await Eval($"create({TestIsolationHelpers.GenerateUniqueName("ChownRefused")})");
		await AsGod($"@chown {thing}={holder.DbRef}");
		await AsGod($"@set {thing}=!HALT");
		await AsGod($"@set {thing}=TRUST");
		await AsGod($"@power {thing}=No_Quota");

		var before = WebAppFactoryArg.Notifications.CountFor(holder.DbRef);
		await RunAs(holder, $"@chown {thing}={stranger.DbRef}");

		await Assert.That(WebAppFactoryArg.Notifications.For(holder.DbRef).Skip(before)).DoesNotContain("Owner changed.");
		await Assert.That(await Eval($"owner({thing})")).IsEqualTo($"#{holder.DbRef.Number}");
		await Assert.That(await Eval($"hasflag({thing},TRUST)")).IsEqualTo("1");
		await Assert.That(await Eval($"haspower({thing},No_Quota)")).IsEqualTo("1");
		await Assert.That(await Eval($"hasflag({thing},HALT)")).IsEqualTo("0");
	}

	/// <summary>
	/// PennMUSH's <c>do_cpattr</c> skips a destination that is the source attribute itself
	/// (src/set.c:751-753), so @mvattr onto itself copies nothing and leaves the attribute in place.
	/// </summary>
	[Test]
	public async ValueTask MvattrOntoItselfKeepsTheAttribute()
	{
		var thing = await Eval($"create({TestIsolationHelpers.GenerateUniqueName("MvattrSelf")})");
		await AsGod($"&KEEPME {thing}=still here");

		await AsGod($"@mvattr {thing}/KEEPME={thing}/keepme");

		await Assert.That(await Eval($"get({thing}/KEEPME)")).IsEqualTo("still here");
	}

	/// <summary>
	/// <c>zone(obj, zone)</c> is PennMUSH's <c>do_chzone</c> with no /preserve (src/fundb.c:1604), which
	/// clears the power set along with WIZARD, ROYALTY and TRUST (src/set.c:477-481).
	/// </summary>
	[Test]
	public async ValueTask ZoneFunctionStripsPowers()
	{
		var thing = await Eval($"create({TestIsolationHelpers.GenerateUniqueName("ZoneStrip")})");
		var zone = await Eval($"create({TestIsolationHelpers.GenerateUniqueName("ZoneStripZmo")})");
		await AsGod($"@set {thing}=TRUST");
		await AsGod($"@power {thing}=No_Quota");

		await Eval($"zone({thing},{zone})");

		await Assert.That(await Eval($"num(zone({thing}))")).IsEqualTo(zone);
		await Assert.That(await Eval($"hasflag({thing},TRUST)")).IsEqualTo("0");
		await Assert.That(await Eval($"haspower({thing},No_Quota)")).IsEqualTo("0");
	}

	[Test]
	public async ValueTask WizmotdWithNoMessageGivesItsOwnUsage()
	{
		var wizard = await PlayerAsync("WizmotdUsage");
		await AsGod($"@set {wizard.DbRef}=WIZARD");

		await RunAs(wizard, "@wizmotd");

		await Assert.That(WebAppFactoryArg.Notifications.For(wizard.DbRef)).Contains("Usage: @wizmotd <message>");
	}

	[Test]
	public async ValueTask RejectmotdWithNoMessageGivesItsOwnUsage()
	{
		var wizard = await PlayerAsync("RejectmotdUsage");
		await AsGod($"@set {wizard.DbRef}=WIZARD");

		await RunAs(wizard, "@rejectmotd");

		await Assert.That(WebAppFactoryArg.Notifications.For(wizard.DbRef)).Contains("Usage: @rejectmotd <message>");
	}

	/// <summary>
	/// The notifying locate already says why a name did not match. @verb used to repeat the failure
	/// as a second line, the bare <c>#-1</c> return; PennMUSH's <c>do_verb</c> says one line.
	/// </summary>
	[Test]
	public async ValueTask VerbWithAnUnknownVictimSaysSoOnce()
	{
		var player = await PlayerAsync("VerbNoVictim");
		var missing = TestIsolationHelpers.GenerateUniqueName("NoSuchVictim");

		await RunAs(player, $"@verb {missing}=me,,VerbNoVictim_What,,,");

		var messages = WebAppFactoryArg.Notifications.For(player.DbRef);
		await Assert.That(messages.Count(message => message == "I can't see that here.")).IsEqualTo(1);
		await Assert.That(messages.Any(message => message.StartsWith("#-1"))).IsFalse();
		await Assert.That(messages).DoesNotContain("VerbNoVictim_What");
	}

	/// <summary>
	/// PennMUSH's <c>do_dolist</c> tells the executor (<c>src/game.c:2025</c>), not the enactor, that the
	/// list has no command. SharpMUSH told the enactor, so a forced object's mistake went to the forcer.
	/// </summary>
	[Test]
	public async ValueTask DolistWithoutACommandTellsTheExecutor()
	{
		var player = await PlayerAsync("DolistForcer");
		var thing = await Eval($"create({TestIsolationHelpers.GenerateUniqueName("DolistForced")})");
		await AsGod($"@chown/preserve {thing}={player.DbRef}");
		var thingRef = DBRef.Parse(await Eval($"objid({thing})"));
		var recorder = WebAppFactoryArg.Notifications;
		var thingBefore = recorder.CountFor(thingRef);
		var playerBefore = recorder.CountFor(player.DbRef);

		await RunAs(player, $"@force {thing}=@dolist a b");

		await recorder.WaitForAsync(thingRef, "What do you want to do with the list?", startIndex: thingBefore);
		await Assert.That(recorder.For(player.DbRef).Skip(playerBefore))
			.DoesNotContain("What do you want to do with the list?");
	}

	/// <summary>
	/// A guard that belongs to one switch keeps that switch's own usage line rather than the generic
	/// arity message the declared-MinArgs guard gives.
	/// </summary>
	[Test]
	[Arguments("@suggest/add DemoCategory", "Usage: @suggest/add <category>=<word>")]
	[Arguments("@quota/set me", "Usage: @quota/set <player>=<amount>")]
	[Arguments("@attribute/access GUARDATTR", "You must specify attribute flags.")]
	public async ValueTask SwitchGuardsKeepTheirOwnUsage(string command, string usage)
	{
		var wizard = await PlayerAsync("GuardUsage");
		await AsGod($"@set {wizard.DbRef}=WIZARD");
		var before = WebAppFactoryArg.Notifications.CountFor(wizard.DbRef);

		await RunAs(wizard, command);

		await Assert.That(WebAppFactoryArg.Notifications.For(wizard.DbRef).Skip(before).ToList()).Contains(usage);
	}
}
