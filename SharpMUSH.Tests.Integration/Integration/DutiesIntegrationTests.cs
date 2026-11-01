using Mediator;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.API;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Controllers;
using SharpMUSH.Server.Services;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SharpMUSH.Tests.Integration;

/// <summary>
/// The bundled duties package, installed from the catalogue and driven with typed commands against a sheet
/// system written for the test: a player writes a duty and submits it, a duty reviewer returns and approves
/// it and puts it on the roster, and a party plays it through challenges, rounds, rewards and its end. The
/// test system's roll is set per character (FATE) or by the trait, so every outcome is known. Not in
/// parallel: the package and its store are shared.
/// </summary>
[NotInParallel]
public class DutiesIntegrationTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private TestHelpers.NotificationRecorder Notifications => WebAppFactoryArg.Notifications;
	private IPackageInstallService Installer => WebAppFactoryArg.Services.GetRequiredService<IPackageInstallService>();
	private IPackageRegistryService Registry => WebAppFactoryArg.Services.GetRequiredService<IPackageRegistryService>();

	private async Task<string> God(string command)
	{
		var result = await Parser.CommandParse(1, ConnectionService, MarkupText.Plain(command));
		await WebAppFactoryArg.QueueBarrierAsync();
		return result.Message?.ToPlainText() ?? string.Empty;
	}

	/// <summary>What <paramref name="player"/> was told while <paramref name="command"/> ran.</summary>
	private async Task<string> As(TestIsolationHelpers.TestPlayer player, string command)
	{
		var before = Notifications.CountFor(player.DbRef);
		await Parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain(command));
		await WebAppFactoryArg.QueueBarrierAsync();
		return string.Join("\n", Notifications.For(player.DbRef).Skip(before));
	}

	/// <summary>What <paramref name="player"/> was told, as one line: box sides dropped and wrapped lines joined, so
	/// a phrase can be found whatever the width of the names around it.</summary>
	private async Task<string> Read(TestIsolationHelpers.TestPlayer player, string command) =>
		System.Text.RegularExpressions.Regex.Replace((await As(player, command)).Replace('|', ' '), @"\s+", " ");

	private async Task<DBRef> RoomAsync(string prefix)
	{
		var god = (await Mediator.Send(new GetObjectNodeQuery(new DBRef(1)))).Expect<AnySharpObject>().Expect<SharpPlayer>();
		return await Mediator.Send(new CreateRoomCommand(TestIsolationHelpers.GenerateUniqueName($"{prefix}Room"), god));
	}

	/// <summary>A connected player in a room of their own.</summary>
	private async Task<TestIsolationHelpers.TestPlayer> Player(string prefix) =>
		await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(WebAppFactoryArg.Services, Mediator, ConnectionService, prefix, await RoomAsync(prefix));

	private async Task ApplyAsync(string package)
	{
		var controller = new PackagesController(
			Registry,
			WebAppFactoryArg.Services.GetRequiredService<IPackageSourceService>(),
			WebAppFactoryArg.Services.GetRequiredService<IPackageManifestService>(),
			Installer,
			WebAppFactoryArg.Services.GetRequiredService<IPackageAuthoringService>(),
			WebAppFactoryArg.Services.GetRequiredService<IPackageOperationRunner>(),
			WebAppFactoryArg.Services.GetRequiredService<PluginUploadStore>(),
			WebAppFactoryArg.Services.GetRequiredService<SharpMUSH.Server.Services.PackageManifestResolver>(),
			WebAppFactoryArg.Services.GetRequiredService<IAuditLog>(),
			WebAppFactoryArg.Services.GetRequiredService<SharpMUSH.Library.Models.Packages.EngineVersion>(),
			WebAppFactoryArg.Services.GetRequiredService<SharpMUSH.Library.Services.Interfaces.IBundledPackageBootstrap>());
		var applied = await controller.Apply(
			new ApplyRequest(BundledPackages.RemoteName, package, null, null, null), CancellationToken.None);
		await Assert.That(applied.Result).IsTypeOf<OkObjectResult>().Because($"{package} must install from the catalogue");
		await WebAppFactoryArg.QueueBarrierAsync();
	}

	/// <summary>
	/// Installs duties and its help, and attaches a sheet system: traits win, lose, crit and part roll that
	/// tier for everyone, skill needs TRAINED on the roller, and a FATE attribute on a character overrides
	/// the trait. Coin is its one reward, which it reports to the character it is given to.
	/// </summary>
	private async Task InstallAsync()
	{
		await ApplyAsync("duties");
		await ApplyAsync("duties-help");
		var duties = (await Registry.GetPackageObjectsAsync("duties")).Single(o => o.Ref == "duties").Objid;
		var name = TestIsolationHelpers.GenerateUniqueName("DutyRoller");
		var created = await God($"@create {name}");
		var roller = created[(created.IndexOf('#'))..].TrimEnd('.');
		await God($"&FN`DUTIES`TRAITS {roller}=lit([{{\"key\":\"win\",\"label\":\"Winning\",\"always\":true}},{{\"key\":\"lose\",\"label\":\"Losing\",\"always\":true}},{{\"key\":\"crit\",\"label\":\"Critical\",\"always\":true}},{{\"key\":\"part\",\"label\":\"Partial\",\"always\":true}},{{\"key\":\"skill\",\"label\":\"Skill\"}}])");
		await God($"&FN`DUTIES`CHECK {roller}=json(object,tier,json(string,switch(firstof(get(%<roller>/FATE),%<trait>),win,success,lose,fail,crit,crit-success,part,partial,skill,success,fail)),text,json(string,rolled %<trait> at %<difficulty>))");
		await God($"&FN`DUTIES`TRAINED {roller}=t(get(%<roller>/TRAINED))");
		await God($"&FN`DUTIES`FALLBACK {roller}=firstof(get(%<roller>/IMPROVISE),win)");
		await God($"&FN`DUTIES`REWARDS {roller}=lit([{{\"kind\":\"coin\",\"label\":\"Coin\",\"fields\":[{{\"key\":\"amount\",\"type\":\"number\"}}]}}])");
		await God($"&ON`DUTIES`GRANT {roller}=@pemit %<roller>=GRANTED [json_query(%<reward>,extract,$.kind)] [json_query(%<reward>,extract,$.amount)] [json_query(%<reward>,extract,$.repeat)] at [json_query(%<context>,extract,$.node)]");
		// WIZARD, so it reads a character's FATE as a real system reads their sheet.
		await God($"@set {roller}=WIZARD");
		await God($"&PROVIDER`TEST {duties}={roller}");
	}

	private async Task UninstallAsync() => await WebAppFactoryArg.Services.UninstallWithBridgesAsync("duties");

	private static string NewId() => "d" + Guid.NewGuid().ToString("N")[..10];

	/// <summary>Writes a duty: a challenge (gate) with an option anyone can roll and a trained one, a
	/// narration (hall), and a successful and a failed end, then gets it approved and on the roster.</summary>
	private async Task WriteAndApproveAsync(TestIsolationHelpers.TestPlayer author, TestIsolationHelpers.TestPlayer reviewer, string id)
	{
		await Assert.That(await Read(author, $"+duty/create {id}=The Harbor Fire")).Contains($"Created {id}.");
		await As(author, $"+duty/set {id}/description=Smoke over the docks.");
		await Assert.That(await Read(author, $"+duty/node {id}/gate=challenge")).Contains($"{id} starts there.");
		await As(author, $"+duty/node {id}/hall=narration");
		await As(author, $"+duty/node {id}/loot=end");
		await As(author, $"+duty/node {id}/lost=end");
		await As(author, $"+duty/set {id}/gate/text=A burning gate.");
		await As(author, $"+duty/set {id}/hall/text=A quiet hall.");
		await As(author, $"+duty/set {id}/gate/success=You force it open.");
		await As(author, $"+duty/set {id}/loot/result=success");
		await As(author, $"+duty/set {id}/lost/result=fail");
		await Assert.That(await Read(author, $"+duty/option {id}/gate/force=win standard")).Contains("gate/force rolls win at standard.");
		await As(author, $"+duty/option {id}/gate/sneak=skill hard 1 1 trained");

		// Without links the check says what is missing.
		var check = await Read(author, $"+duty/check {id}");
		await Assert.That(check).Contains("gate needs a link on success with no conditions.")
			.And.Contains("Nothing leads to hall.");

		await Assert.That(await Read(author, $"+duty/link {id}/gate/success=hall")).Contains("gate on success leads to hall.");
		await As(author, $"+duty/link {id}/gate/fail=lost");
		await As(author, $"+duty/link {id}/hall/continue=loot");
		await Assert.That(await Read(author, $"+duty/reward {id}/gate=coin once 5")).Contains("Reward 1 on gate: coin, once.");
		await As(author, $"+duty/reward {id}/loot=info every The fire was set on purpose.");
		await Assert.That(await Read(author, $"+duty/reward {id}/loot=info every success=100 The harbourmaster paid them."))
			.Contains("Reward 3 on loot: info, every, at 100% success or more.");
		await Assert.That(await Read(author, $"+duty/reward {id}/lost=info every success=101 Too much.")).Contains("success is 0 to 100");
		await As(author, $"+duty/reward {id}/lost=info every success=50 The fire spread to the hall.");
		await As(author, $"+duty/reward {id}/lost=info every The gate held.");
		await Assert.That(await Read(author, $"+duty/check {id}")).Contains($"{id} is ready to submit");

		await Assert.That(await Read(author, $"+duty/submit {id}")).Contains("waiting for a duty reviewer");
		await Assert.That(await Read(author, $"+duty/set {id}/title=Other")).Contains("waiting for review");
		await Assert.That(await Read(author, $"+duty/approve {id}")).Contains("Permission denied.");
		await Assert.That(await Read(reviewer, "+duty/review")).Contains(id);
		await Assert.That(await Read(reviewer, $"+duty/return {id}=Name the gate.")).Contains("goes back to its author");
		await Assert.That(await Read(author, $"+duty {id}")).Contains("Name the gate.");
		await As(author, $"+duty/set {id}/gate/label=The Gate");
		await As(author, $"+duty/submit {id}");
		await Assert.That(await Read(reviewer, $"+duty/approve {id}")).Contains($"{id} is approved.");
		await Assert.That(await Read(reviewer, $"+duty/roster {id}=on")).Contains("is on the active roster");
	}

	[Test]
	public async Task APartyPlaysAnApprovedDutyToItsEnd()
	{
		try
		{
			await InstallAsync();
			var author = await Player("DuAut");
			var reviewer = await Player("DuRev");
			var ana = await Player("DuAna");
			var ben = await Player("DuBen");
			await God($"@role/assign #{reviewer.DbRef.Number}=duty-reviewer");
			var id = NewId();
			await WriteAndApproveAsync(author, reviewer, id);

			await Assert.That(await Read(ana, "+duty")).Contains(id).And.Contains("The Harbor Fire");
			await Assert.That(await Read(ana, "+duties")).Contains(id).And.Contains("The Harbor Fire").And.Contains("0%");
			await Assert.That(await Read(ana, "+duty/list")).Contains(id).And.Contains("Explored");
			await Assert.That(await Read(ana, $"+duty/start/nosuch {id}")).Contains("+duty/start has no /nosuch option.");
			await Assert.That(await Read(ana, $"+duty/start {id}")).Contains("You gather a party for The Harbor Fire.");
			var party = (await As(ben, "+duty")).Split('\n').First(line => line.Contains("+duty/join"));
			var number = party[(party.IndexOf("+duty/join ", StringComparison.Ordinal) + 11)..].Split(' ')[0];
			await Assert.That(await Read(ben, $"+duty/join {number}")).Contains("joins the party");
			await Assert.That(await Read(ben, "+duty")).Contains("The Harbor Fire").And.DoesNotContain("#-1");
			await Assert.That(await Read(ben, "+duty/go")).Contains("Only the party's leader");

			// Setting out shows the challenge: its text, the open options, Improvise and the way on.
			var screen = await Read(ana, "+duty/go");
			await Assert.That(screen).Contains("A burning gate.").And.Contains("force").And.Contains("improvise")
				.And.Contains("Winning").And.Contains("2 ways");
			await Assert.That(await Read(ben, "+duty/try sneak")).Contains("needs training in Skill");
			await Assert.That(await Read(ben, "+duty/try force")).Contains("rolled win at standard");
			await Assert.That(await Read(ben, "+duty/try force")).Contains("You have rolled this round.");
			// Improvise shows the trait the sheet system picks for the one looking.
			await God($"&IMPROVISE #{ben.DbRef.Number}=part");
			await Assert.That(await Read(ben, "+duty")).Contains("improvise Improvise Partial");

			// The last roll resolves the challenge: both succeed, both get the coin, and the party is in the hall.
			var resolved = await Read(ana, "+duty/try improvise");
			await Assert.That(resolved).Contains("You force it open.").And.Contains("2 of 2 succeeded.")
				.And.Contains("GRANTED coin 5 once at gate").And.Contains("A quiet hall.");
			await Assert.That(await Read(ben, "+duty")).Contains("Waiting for");

			var end = await Read(ana, "+duty/go");
			await Assert.That(end).Contains("You learn: The fire was set on purpose.").And.Contains("It ends in success.")
				.And.Contains("You learn: The harbourmaster paid them.").And.Contains("The party succeeded at 1 of 1 challenge (100%).");
			await Assert.That(await Read(ana, "+duty")).DoesNotContain("A quiet hall.");
			var progress = await Read(ana, $"+duty {id}");
			await Assert.That(progress).Contains("3 of 5").And.Contains("1 of 1").And.Contains("Smoke over the docks.");
			// gate, hall and loot of its four steps.
			await Assert.That(progress).Contains("75% of its steps");
			await Assert.That(await Read(ana, "+duties")).Contains("75%");
			await Assert.That(await Read(ana, $"+duty/info {id}")).Contains("The fire was set on purpose.");

			// Coin is once: a second run gives the information again but not the coin.
			await As(ana, $"+duty/start {id}");
			await As(ana, "+duty/go");
			var again = await Read(ana, "+duty/try force");
			await Assert.That(again).Contains("You force it open.").And.DoesNotContain("GRANTED");
			await Assert.That(await Read(ana, "+duty/go")).Contains("You learn:");

			// A failed challenge leaves the party at 0%: the lost end gives what needs no success and holds back the rest.
			await As(ana, $"+duty/start {id}");
			await As(ana, "+duty/go");
			await God($"&FATE #{ana.DbRef.Number}=lose");
			var lost = await Read(ana, "+duty/try force");
			await Assert.That(lost).Contains("You learn: The gate held.").And.DoesNotContain("The fire spread")
				.And.Contains("It ends in failure. The party succeeded at 0 of 1 challenge (0%).");
		}
		finally
		{
			await UninstallAsync();
		}
	}
	[Test]
	public async Task APartyPlaysADutyInAScene()
	{
		try
		{
			await InstallAsync();
			await ApplyAsync("duties-scene");
			var author = await Player("DuScA");
			var reviewer = await Player("DuScR");
			var ana = await Player("DuScAna");
			var ben = await Player("DuScBen");
			await God($"@role/assign #{reviewer.DbRef.Number}=duty-reviewer");
			// Approved characters may make and join scenes.
			await God($"@role/assign #{ana.DbRef.Number}=approved");
			await God($"@role/assign #{ben.DbRef.Number}=approved");
			var id = NewId();
			await WriteAndApproveAsync(author, reviewer, id);

			await Assert.That(await Read(ana, "+duty/scene")).Contains("You are not on a duty.");
			var started = await Read(ana, $"+duty/start/scene {id}");
			await Assert.That(started).Contains("You gather a party for The Harbor Fire.").And.Contains("goes into scene");
			var scene = (await God($"think [scenefocus(#{ana.DbRef.Number})]")).Trim();
			await Assert.That(scene).Matches(@"^\d+$");
			await Assert.That(await God($"think [scene({scene},title)]")).IsEqualTo("The Harbor Fire");
			await Assert.That(await Read(ana, "+duty/scene")).Contains($"goes into scene {scene}: The Harbor Fire.");

			// A member who joins the party joins the scene too.
			var party = (await As(ben, "+duty")).Split('\n').First(line => line.Contains("+duty/join"));
			var number = party[(party.IndexOf("+duty/join ", StringComparison.Ordinal) + 11)..].Split(' ')[0];
			await As(ben, $"+duty/join {number}");
			await Assert.That((await God($"think [scenefocus(#{ben.DbRef.Number})]")).Trim()).IsEqualTo(scene);
			await Assert.That(await Read(ben, $"+duty/scene {scene}")).Contains("Only the party's leader");

			await As(ana, "+duty/go");
			await As(ben, "+duty/try force");
			await As(ana, "+duty/try improvise");
			await As(ana, "+duty/go");

			var poses = (await God($"think [sceneposes({scene})]")).Split(' ', StringSplitOptions.RemoveEmptyEntries);
			var log = new List<string>();
			foreach (var pose in poses)
			{
				log.Add(await God($"think [scenepose({scene},{pose},content)]"));
			}
			var text = string.Join("\n", log);
			await Assert.That(text).Contains("set out on The Harbor Fire.").And.Contains("The Gate: A burning gate.")
				.And.Contains(" tries ").And.Contains("You force it open. 2 of 2 succeeded.")
				.And.Contains("A quiet hall.").And.Contains("It ends in success.");
			await Assert.That(await God($"think [scenepose({scene},{poses[^1]},type)]")).IsEqualTo("narration");
		}
		finally
		{
			await Installer.UninstallAsync("duties-scene", force: true, CancellationToken.None);
			await UninstallAsync();
		}
	}

	/// <summary>A climb needing two successes, two successful ends on success, a vault on a critical success
	/// and a failed end. Approved and on the roster.</summary>
	private async Task WriteClimbAsync(TestIsolationHelpers.TestPlayer author, TestIsolationHelpers.TestPlayer reviewer, string id)
	{
		foreach (var line in new[]
		{
			$"+duty/create {id}=The Climb", $"+duty/set {id}/description=Up the wall.",
			$"+duty/node {id}/gate=challenge", $"+duty/option {id}/gate/climb=win standard 2",
			$"+duty/node {id}/east=end", $"+duty/node {id}/west=end", $"+duty/node {id}/vault=end", $"+duty/node {id}/lost=end",
			$"+duty/set {id}/east/text=East end.", $"+duty/set {id}/west/text=West end.", $"+duty/set {id}/vault/text=Vault end.",
			$"+duty/set {id}/lost/text=Lost end.",
			$"+duty/set {id}/east/result=success", $"+duty/set {id}/west/result=success", $"+duty/set {id}/vault/result=success",
			$"+duty/set {id}/lost/result=fail",
			$"+duty/link {id}/gate/success=east", $"+duty/link {id}/gate/success=west", $"+duty/link {id}/gate/fail=lost",
			$"+duty/link {id}/gate/crit-success=vault", $"+duty/link {id}/gate/success=vault min=5",
		})
		{
			await As(author, line);
		}

		await Assert.That(await Read(author, $"+duty/submit {id}")).Contains("waiting for a duty reviewer");
		await As(reviewer, $"+duty/approve {id}");
		await As(reviewer, $"+duty/roster {id}=on");
	}

	[Test]
	public async Task RoundsUnseenStepsAndTheLeadersTools()
	{
		try
		{
			await InstallAsync();
			var author = await Player("DuCAut");
			var reviewer = await Player("DuCRev");
			await God($"@role/assign #{reviewer.DbRef.Number}=duty-reviewer");
			var id = NewId();
			await WriteClimbAsync(author, reviewer, id);

			// Two successes are needed, so one roll leaves the climb open for another round. The step nobody has
			// seen is taken: the second run goes the other way.
			var ana = await Player("DuCAna");
			await As(ana, $"+duty/start {id}");
			await As(ana, "+duty/go");
			await Assert.That(await Read(ana, "+duty/try climb")).Contains("Round 2: Winning needs more rolls.");
			var first = await Read(ana, "+duty/try climb");
			await Assert.That(first).Contains("It ends in success.").And.DoesNotContain("Vault end.");
			var firstEnd = first.Contains("East end.") ? "East end." : "West end.";
			var otherEnd = firstEnd == "East end." ? "West end." : "East end.";
			await Assert.That(first).Contains(firstEnd);
			await As(ana, $"+duty/start {id}");
			await As(ana, "+duty/go");
			await As(ana, "+duty/try climb");
			await Assert.That(await Read(ana, "+duty/try climb")).Contains(otherEnd).And.DoesNotContain(firstEnd);

			// A failed roll loses the climb at once: the failed end.
			var dee = await Player("DuCDee");
			await God($"&FATE #{dee.DbRef.Number}=lose");
			await As(dee, $"+duty/start {id}");
			await As(dee, "+duty/go");
			await Assert.That(await Read(dee, "+duty/try climb")).Contains("Lost end.").And.Contains("It ends in failure.");

			// A critical success takes the critical link.
			var eve = await Player("DuCEve");
			await God($"&FATE #{eve.DbRef.Number}=crit");
			await As(eve, $"+duty/start {id}");
			await As(eve, "+duty/go");
			await As(eve, "+duty/try climb");
			await Assert.That(await Read(eve, "+duty/try climb")).Contains("Vault end.").And.Contains("Critical success");

			// The leader rolls for someone and sends someone away; the challenge goes on without them.
			var lea = await Player("DuCLea");
			var ben = await Player("DuCBen");
			var cal = await Player("DuCCal");
			await As(lea, $"+duty/start {id}");
			var number = await LeaderPartyNumberAsync(ben, lea);
			await As(ben, $"+duty/join {number}");
			await As(cal, $"+duty/join {number}");
			await As(lea, "+duty/go");
			await Assert.That(await Read(ben, $"+duty/force {lea.Name}")).Contains("Only the party's leader");
			await As(lea, "+duty/try climb");
			await Assert.That(await Read(ben, $"+duty/force {ben.Name}")).Contains("Only the party's leader");
			await Assert.That(await Read(lea, $"+duty/force {ben.Name}")).Contains("rolls for").And.Contains("Improvise");
			await Assert.That(await Read(cal, $"+duty/kick {cal.Name}")).Contains("Only the party's leader");
			await Assert.That(await Read(lea, $"+duty/kick {cal.Name}")).Contains("sends").And.Contains("Round 2:");
			await Assert.That(await Read(cal, "+duty")).DoesNotContain("Round");
			await As(lea, "+duty/try climb");
			await Assert.That(await Read(lea, $"+duty/force {ben.Name}")).Contains("It ends in success.");

			// The leader leaving hands the party on; the last to leave ends it.
			await As(lea, $"+duty/start {id}");
			number = await LeaderPartyNumberAsync(ben, lea);
			await As(ben, $"+duty/join {number}");
			await Assert.That(await Read(ben, "+duty/leave")).Contains("leaves the party");
			await As(ben, $"+duty/join {number}");
			await Assert.That(await Read(lea, "+duty/leave")).Contains("You are no longer on");
			await Assert.That(await Read(ben, "+duty/go")).Contains("climb").Because("the leader left, so the longest-standing member leads");
			await Assert.That(await Read(ben, "+duty/leave")).Contains("You are no longer on");
			await Assert.That(await Read(ben, "+duty")).Contains("+duty/start <duty> to gather a party.");
		}
		finally
		{
			await UninstallAsync();
		}
	}

	/// <summary>The number of the party <paramref name="leader"/> is gathering, as <paramref name="viewer"/> sees it.</summary>
	private async Task<string> LeaderPartyNumberAsync(TestIsolationHelpers.TestPlayer viewer, TestIsolationHelpers.TestPlayer leader)
	{
		var line = (await As(viewer, "+duty")).Split('\n').First(l => l.Contains(leader.Name) && l.Contains("Party "));
		return line[(line.IndexOf("Party ", StringComparison.Ordinal) + 6)..].Split(':')[0];
	}

	[Test]
	public async Task TheRosterAndDraftsDecideWhatIsPlayed()
	{
		try
		{
			await InstallAsync();
			await ApplyAsync("duties-config");
			var author = await Player("DuRAut");
			var reviewer = await Player("DuRRev");
			var player = await Player("DuRPla");
			await God($"@role/assign #{reviewer.DbRef.Number}=duty-reviewer");
			var id = NewId();
			await WriteAndApproveAsync(author, reviewer, id);

			// A window in the past keeps it off; clearing it puts it back.
			await Assert.That(await Read(player, $"+duty/window {id}=2000-01-01,2000-01-02")).Contains("Permission denied.");
			await Assert.That(await Read(reviewer, $"+duty/window {id}=2000-01-01,2000-01-02")).Contains("From 2000-01-01 to 2000-01-02");
			await Assert.That(await Read(reviewer, $"+duty/window {id}=12-01,99")).Contains("Days are YYYY-MM-DD");
			await Assert.That(await Read(player, $"+duty/start {id}")).Contains("is not on the active roster now");
			await Assert.That(await Read(player, "+duty")).DoesNotContain(id);
			await Assert.That(await Read(reviewer, "+duty/roster")).Contains(id).And.Contains("From 2000-01-01");
			await As(reviewer, $"+duty/window {id}=01-01,12-31,yearly");
			await Assert.That(await Read(player, $"+duty/start {id}")).Contains("You gather a party");
			await Assert.That(await Read(player, "+duty/abandon")).Contains("abandons the duty");

			// Changing an approved duty makes a draft beside it: players still see the approved one.
			await Assert.That(await Read(player, $"+duty/set {id}/title=Mine")).Contains("Permission denied.");
			await As(author, $"+duty/set {id}/title=The Harbor Blaze");
			await Assert.That(await Read(player, $"+duty {id}")).Contains("The Harbor Fire").And.DoesNotContain("Blaze");
			await Assert.That(await Read(author, $"+duty {id}")).Contains("changes being made");
			await Assert.That(await Read(author, $"+duty/steps {id}")).Contains("The Harbor Blaze").And.Contains("gate (challenge) The Gate");
			await Assert.That(await Read(author, $"+duty/discard {id}")).Contains("are dropped");
			await Assert.That(await Read(author, $"+duty/steps {id}")).Contains("The Harbor Fire");
			await Assert.That(await Read(author, $"+duty/delete {id}")).Contains("has been approved");

			// Retired, it cannot be started; the settings are in +config and the topics in +help.
			await As(reviewer, $"+duty/retire {id}");
			await Assert.That(await Read(player, $"+duty/start {id}")).Contains("is not on the active roster now");
			await Assert.That(await Read(player, "+config duties")).Contains("Sheet system");
			await Assert.That(await Read(player, "+help duty")).Contains("short adventure for a party");
			await Assert.That(await Read(player, "+duty/bogus")).Contains("+duty has no /bogus switch.");
		}
		finally
		{
			await UninstallAsync();
		}
	}

	private sealed record PortalCharacter(int DbrefNumber, long CreationTime);
	private sealed record PortalAccount(string AccountSessionToken);

	/// <summary>A new account with one character, and an HTTP client signed in as it.</summary>
	private async Task<(HttpClient Http, int Dbref)> SignedInAsync(string prefix)
	{
		var http = WebAppFactoryArg.CreateHttpClient();
		http.BaseAddress = new Uri("https://localhost/");
		const string password = "Duties-Portal-Pw-1!";
		var register = await http.PostAsJsonAsync("api/auth/account-register",
			new { Username = $"{prefix}{Guid.NewGuid():N}"[..15], Email = (string?)null, Password = password });
		await Assert.That(register.StatusCode).IsEqualTo(HttpStatusCode.OK);
		var account = (await register.Content.ReadFromJsonAsync<PortalAccount>())!;
		http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", account.AccountSessionToken);
		var created = await http.PostAsJsonAsync("api/account/characters",
			new { Name = $"{prefix}{Guid.NewGuid():N}"[..15], Password = password });
		await Assert.That(created.StatusCode).IsEqualTo(HttpStatusCode.OK);
		return (http, (await created.Content.ReadFromJsonAsync<PortalCharacter>())!.DbrefNumber);
	}

	private static async Task<JsonNode> ActAsync(HttpClient http, string route, object body)
	{
		var response = await http.PostAsJsonAsync($"http/duties/{route}", body);
		var text = (await response.Content.ReadAsStringAsync()).Trim();
		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK).Because(text);
		await Assert.That(text).StartsWith("{").Because(text);
		return JsonNode.Parse(text)!;
	}

	private static async Task<JsonNode> GetAsync(HttpClient http, string route) =>
		JsonNode.Parse(await http.GetStringAsync($"http/duties/{route}"))!;

	/// <summary>The duty <paramref name="id"/> as the page posts it: a challenge with two ways to beat it, a narration
	/// and two ends, with a reward and a link that needs one of the rolls.</summary>
	private static object Draft(string op, string id, string title) => new
	{
		op,
		duty = id,
		title,
		description = "Follow the lantern through the bog.",
		min = 1,
		max = 4,
		repeat = "once",
		steps = new
		{
			start = "gate",
			start_x = 47,
			start_y = -66,
			nodes = new object[]
			{
				new { key = "gate", kind = "challenge", label = "The gate", x = 10, y = 10, fields = new
				{
					text = "A burning gate.",
					combine = "majority",
					success = "You force it open.",
					options = new object[]
					{
						new { id = "force", label = "Force it", trait = "win", difficulty = "standard", successes = 1, failures = 1, trained = false },
						new { id = "", label = "Sneak", trait = "skill", difficulty = "hard", successes = 1, failures = 1, trained = true },
					},
					rewards = new object[] { new { kind = "coin", repeat = "once", amount = 5 } },
				} },
				new { key = "hall", kind = "narration", label = "The hall", x = 10, y = 150, fields = new { text = "A quiet hall." } },
				new { key = "loot", kind = "end", label = "Loot", x = 10, y = 300, fields = new
				{
					result = "success",
					rewards = new object[] { new { kind = "info", repeat = "every", success = 60, text = "The harbourmaster paid them." } },
				} },
				new { key = "lost", kind = "end", label = "Lost", x = 200, y = 300, fields = new { result = "fail" } },
			},
			edges = new object[]
			{
				new { from = "gate", port = "success", to = "hall" },
				new { from = "gate", port = "success", to = "loot", weight = 2, requires = "force" },
				new { from = "gate", port = "fail", to = "lost" },
				new { from = "hall", port = "continue", to = "loot" },
			},
		},
	};

	[Test]
	public async Task ThePortalWritesReviewsAndApprovesADuty()
	{
		try
		{
			await InstallAsync();
			await ApplyAsync("duties-app");
			var (author, _) = await SignedInAsync("DuWeb");
			var (reviewer, reviewerDbref) = await SignedInAsync("DuWebRev");
			var (other, _) = await SignedInAsync("DuWebOth");
			await God($"@role/assign #{reviewerDbref}=duty-reviewer");
			// A reviewer at a terminal, to see the same duty in game.
			var staff = await Player("DuWebSta");
			await God($"@role/assign #{staff.DbRef.Number}=duty-reviewer");
			var id = NewId();

			// Every page and its data parse; the home page offers to write one.
			var home = await GetAsync(author, "schema?at=");
			await Assert.That(home.ToJsonString()).Contains("Write a new duty");
			await Assert.That((await ActAsync(author, "act", new { op = "create", new_title = "", new_id = id }))["ok"]!.GetValue<bool>()).IsFalse();
			var created = await ActAsync(author, "act", new { op = "create", new_title = "The Lantern Road", new_id = id });
			await Assert.That(created["redirect"]!.GetValue<string>()).IsEqualTo($"/apps/duties/{id}");

			// A save writes the graph; the data reads it back the way the page posted it.
			var saved = await ActAsync(author, "act", Draft("save", id, "The Lantern Road"));
			await Assert.That(saved["ok"]!.GetValue<bool>()).IsTrue().Because(saved.ToJsonString());
			var data = (await GetAsync(author, $"data?at={id}"))["fields"]!;
			var steps = data["steps"]!["value"]!;
			await Assert.That(steps["start"]!.GetValue<string>()).IsEqualTo("gate");
			await Assert.That(steps["start_x"]!.GetValue<int>()).IsEqualTo(47);
			await Assert.That(steps["start_y"]!.GetValue<int>()).IsEqualTo(-66);
			await Assert.That(steps["nodes"]!.AsArray().Count).IsEqualTo(4);
			await Assert.That(steps["edges"]!.AsArray().Count).IsEqualTo(4);
			var gate = steps["nodes"]!.AsArray().Single(n => n!["key"]!.GetValue<string>() == "gate")!;
			await Assert.That(gate["fields"]!["options"]!.AsArray().Select(o => o!["id"]!.GetValue<string>())).IsEquivalentTo(["force", "o2"]);
			await Assert.That(gate["fields"]!["rewards"]![0]!["amount"]!.GetValue<int>()).IsEqualTo(5);
			await Assert.That(gate["fields"]!["rewards"]![0]!["success"]!.GetValue<int>()).IsEqualTo(0);
			var loot = steps["nodes"]!.AsArray().Single(n => n!["key"]!.GetValue<string>() == "loot")!;
			await Assert.That(loot["fields"]!["rewards"]![0]!["success"]!.GetValue<int>()).IsEqualTo(60);
			await Assert.That(loot["fields"]!["rewards"]![0]!["text"]!.GetValue<string>()).IsEqualTo("The harbourmaster paid them.");
			await Assert.That(data["issues"]!["value"]!["issues"]!.AsArray().Count).IsEqualTo(0);
			// The same duty, in game.
			await Assert.That(await As(staff, $"+duty/steps {id}")).Contains("gate (challenge) The gate");

			// Someone else can neither read nor change it; a bad step is refused with the reason.
			await Assert.That((await GetAsync(other, $"schema?at={id}")).ToJsonString()).DoesNotContain("graph");
			var refused = await ActAsync(other, "act", Draft("save", id, "Mine"));
			await Assert.That(refused["errors"]!["_global"]!.GetValue<string>()).Contains("Permission denied.");
			var bad = await ActAsync(author, "act", Draft("save", id, new string('x', 81)));
			await Assert.That(bad["errors"]!["_global"]!.GetValue<string>()).Contains("80 characters");

			// Submitted, it waits for review: the reviewer notes a step and returns it, then approves it.
			await Assert.That((await ActAsync(author, "act", Draft("submit", id, "The Lantern Road")))["message"]!.GetValue<string>())
				.Contains("waiting for a duty reviewer");
			await Assert.That((await GetAsync(reviewer, "data?at="))["fields"]!["review"]!.ToJsonString()).Contains(id);
			await Assert.That((await ActAsync(reviewer, "note", new { duty = id, node = "hall", text = "Say what is in it." }))["ok"]!.GetValue<bool>()).IsTrue();
			await Assert.That((await GetAsync(author, $"data?at={id}"))["fields"]!["notes"]!.ToJsonString()).Contains("Say what is in it.");
			await ActAsync(reviewer, "act", new { op = "return", duty = id, return_note = "Fill in the hall." });
			await Assert.That(await As(staff, $"+duty {id}")).Contains("Fill in the hall.");
			await ActAsync(author, "act", Draft("submit", id, "The Lantern Road"));
			await Assert.That((await ActAsync(author, "act", new { op = "approve", duty = id }))["errors"]!["_global"]!.GetValue<string>()).Contains("Permission denied.");
			await Assert.That((await ActAsync(reviewer, "act", new { op = "approve", duty = id }))["message"]!.GetValue<string>()).Contains($"{id} is approved.");

			// A change to an approved duty is a draft beside it, listed step by step.
			await ActAsync(author, "act", Draft("save", id, "The Lantern Road, again"));
			var changed = (await GetAsync(author, $"data?at={id}"))["fields"]!;
			await Assert.That(changed["Status"]!["value"]!.GetValue<string>()).Contains("changes being made");
			await Assert.That(await As(staff, $"+duty {id}")).Contains("The Lantern Road").And.DoesNotContain("again");
			await Assert.That((await ActAsync(author, "act", new { op = "discard", duty = id }))["ok"]!.GetValue<bool>()).IsTrue();

			// On the active roster, every player sees it among the duties they can start, with their progress.
			await As(staff, $"+duty/roster {id}=on");
			var play = (await GetAsync(other, "data?at="))["fields"]!["play"]!["value"]!.AsArray()
				.Single(row => row!["id"]!.GetValue<string>() == id)!;
			await Assert.That(play["title"]!.GetValue<string>()).IsEqualTo("The Lantern Road");
			await Assert.That(play["about"]!.GetValue<string>()).IsEqualTo("Follow the lantern through the bog.");
			await Assert.That(play["explored"]!.GetValue<string>()).IsEqualTo("0%");
			await Assert.That((await GetAsync(other, "schema?at=")).ToJsonString()).Contains("Duties you can start");
		}
		finally
		{
			await Installer.UninstallAsync("duties-app", force: true, CancellationToken.None);
			await UninstallAsync();
		}
	}
}
