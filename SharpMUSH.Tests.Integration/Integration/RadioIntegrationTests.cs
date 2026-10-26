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

namespace SharpMUSH.Tests.Integration;

/// <summary>
/// The bundled radio package, and radio-scene on top of it, installed from the catalogue and driven with typed commands: the worked example
/// of a system built on @feed. Each test installs the package, removes it again and undefines the radio feed
/// kind, so frequency ids start at 1. Not in parallel: +radio is global.
/// </summary>
[NotInParallel]
public class RadioIntegrationTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private TestHelpers.NotificationRecorder Notifications => WebAppFactoryArg.Notifications;
	private IPackageInstallService Installer => WebAppFactoryArg.Services.GetRequiredService<IPackageInstallService>();

	private async Task<string> God(string command) =>
		(await Parser.CommandParse(1, ConnectionService, MarkupText.Plain(command))).Message.ToPlainText()?.Trim() ?? string.Empty;

	/// <summary>What <paramref name="player"/> was told while <paramref name="command"/> ran.</summary>
	private async Task<string> As(TestIsolationHelpers.TestPlayer player, string command)
	{
		var before = Notifications.CountFor(player.DbRef);
		await Parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain(command));
		await WebAppFactoryArg.QueueBarrierAsync();
		return string.Join("\n", Notifications.For(player.DbRef).Skip(before));
	}

	/// <summary>The lines <paramref name="player"/> heard that contain <paramref name="text"/>.</summary>
	private List<string> Heard(TestIsolationHelpers.TestPlayer player, string text)
		=> Notifications.For(player.DbRef).Where(line => line.Contains(text, StringComparison.Ordinal)).ToList();

	/// <summary>A connected player in a room of its own, holding <paramref name="role"/> when one is named.</summary>
	private async Task<TestIsolationHelpers.TestPlayer> Player(string prefix, string? role = null)
	{
		var god = (await Mediator.Send(new GetObjectNodeQuery(new DBRef(1)))).Expect<AnySharpObject>().Expect<SharpPlayer>();
		var home = await Mediator.Send(new CreateRoomCommand(TestIsolationHelpers.GenerateUniqueName($"{prefix}Room"), god));
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, prefix, home);
		if (role is not null) await God($"@role/assign #{player.DbRef.Number}={role}");
		return player;
	}

	private async Task InstallAsync(string package = "radio")
	{
		var controller = new PackagesController(
			WebAppFactoryArg.Services.GetRequiredService<IPackageRegistryService>(),
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
		await Assert.That(applied.Result).IsTypeOf<OkObjectResult>()
			.Because($"{package} must install from the catalogue: {System.Text.Json.JsonSerializer.Serialize((applied.Result as ObjectResult)?.Value)}");
		// AINSTALL is queued after the apply: it defines the feed kind and adds +radio.
		await WebAppFactoryArg.QueueBarrierAsync();
	}

	private async Task UninstallAsync()
	{
		await WebAppFactoryArg.Services.UninstallWithBridgesAsync("radio");
		await God("@feed/undefine radio");
	}

	[Test]
	public async Task AFrequencyIsCreatedTunedInToSpokenOnAndRecalled()
	{
		try
		{
			await InstallAsync();
			var admin = await Player("RadA", "radio-admin");
			var ann = await Player("RadAnn");
			var bo = await Player("RadBo");

			await Assert.That(await As(ann, "+radio/create Police")).Contains("Only a radio admin can create frequencies.");
			await Assert.That(await As(admin, "+radio/create Police=Emergency")).Contains("Created Police.");
			await Assert.That(await As(admin, "+radio/create police")).Contains("There is a frequency called police already.");
			await Assert.That(await As(ann, "+radio")).Contains("Police").And.Contains("Emergency");

			await Assert.That(await As(ann, "+radio/join pol")).Contains("You tune in to Police.");
			await As(bo, "+radio/join Police");
			await Assert.That(await God($"think [feedwho(radio/1)]")).Contains($"#{ann.DbRef.Number}:");

			await As(ann, "+radio/title Police=Officer");
			var said = TestIsolationHelpers.GenerateUniqueName("RadSay");
			await As(ann, $"+radio pol={said}");
			await Assert.That(Heard(bo, said)).IsEquivalentTo(new[] { $"<Police> Officer {ann.Name} says, \"{said}\"" });

			var posed = TestIsolationHelpers.GenerateUniqueName("RadPose");
			await As(ann, $"+radio pol=:{posed}");
			await Assert.That(Heard(bo, posed)).IsEquivalentTo(new[] { $"<Police> Officer {ann.Name} {posed}" });

			// What a player types is never evaluated.
			var raw = TestIsolationHelpers.GenerateUniqueName("RadRaw");
			await As(ann, $"+radio pol={raw} [add(1,2)] 50% off");
			await Assert.That(Heard(bo, raw)).IsEquivalentTo(new[] { $"<Police> Officer {ann.Name} says, \"{raw} [add(1,2)] 50% off\"" });

			await As(ann, "+radio/alterego Police=Unit 4");
			var asUnit = TestIsolationHelpers.GenerateUniqueName("RadUnit");
			await As(ann, $"+radio pol={asUnit}");
			await Assert.That(Heard(bo, asUnit)).IsEquivalentTo(new[] { $"<Police> Officer Unit 4 says, \"{asUnit}\"" });

			var recall = await As(bo, "+radio/recall Police=2");
			await Assert.That(recall).Contains($"<Police> Officer {ann.Name} says, \"{raw} [add(1,2)] 50% off\"")
				.And.Contains($"<Police> Officer Unit 4 says, \"{asUnit}\"")
				.And.DoesNotContain(posed).Because("recall=2 shows the last two lines");

			// A rename keeps the frequency and everything said on it.
			await Assert.That(await As(admin, "+radio/rename Police=Dispatch")).Contains("Police is called Dispatch now.");
			await Assert.That(await As(bo, "+radio/recall dis=1")).Contains($"<Dispatch> Officer Unit 4 says, \"{asUnit}\"");

			await Assert.That(await As(bo, "+radio/gag all")).Contains("You hear none of your frequencies");
			var gagged = TestIsolationHelpers.GenerateUniqueName("RadGag");
			await As(ann, $"+radio dis={gagged}");
			await Assert.That(Heard(bo, gagged)).IsEmpty();
			await Assert.That(await As(bo, "+radio/ungag Dispatch")).Contains("You hear Dispatch again.");

			await Assert.That(await As(admin, "+radio/delete Dispatch")).Contains("Type +radio/delete Dispatch=Dispatch to do it.");
			await Assert.That(await As(admin, "+radio/delete Dispatch=Dispatch")).Contains("Deleted Dispatch.");
			await Assert.That(await God("think [feeds(radio)]")).IsEqualTo("");
			await Assert.That(await As(ann, "+radio")).Contains("There are no frequencies you can tune in to.");
		}
		finally
		{
			await UninstallAsync();
		}
	}

	/// <summary>
	/// A frequency's colour is a theme colour, which tone() paints in each listener's own theme, or ansi()
	/// codes; the default is the info theme colour.
	/// </summary>
	[Test]
	public async Task AFrequencysColourIsAThemeColourOrAnsiCodes()
	{
		try
		{
			await InstallAsync();
			var radio = (await WebAppFactoryArg.Services.GetRequiredService<IPackageRegistryService>().GetPackageObjectsAsync("radio"))
				.Single(o => o.Ref == "radio").Objid;
			async Task<string> Paint(string colour) =>
				(await WebAppFactoryArg.FunctionParser.FunctionParse(MarkupText.Plain($"u({radio}/FUN`PAINT,{colour},Police)")))!
					.Message.Render(MarkupFormat.Html);

			await Assert.That(await Paint("success")).Contains("tone-success").And.Contains("Police");
			await Assert.That(await Paint("hg")).DoesNotContain("tone-").And.Contains("Police");
			await Assert.That(await Paint("")).IsEqualTo("Police");
			await Assert.That(await God($"think [get({radio}/DATA`CONFIG`COLOR)]")).IsEqualTo("info");

			var admin = await Player("RadColA", "radio-admin");
			var ann = await Player("RadColAnn");
			await Assert.That(await As(admin, "+radio/create Tower")).Contains("Created Tower.");
			await As(ann, "+radio/join Tower");
			await Assert.That(await As(ann, "+radio/color Tower=success")).Contains("You see Tower like this now.");
			await Assert.That(await As(ann, "+radio/color Tower=hg")).Contains("You see Tower like this now.");
			await Assert.That(await As(ann, "+radio/color Tower=")).Contains("You see Tower like this now.");
		}
		finally
		{
			await UninstallAsync();
		}
	}

	/// <summary>A word that is neither a theme colour nor ansi() codes is refused, not stored.</summary>
	[Test]
	public async Task AColourThatIsNoColourIsRefused()
	{
		try
		{
			await InstallAsync();
			var admin = await Player("RadSpkA", "radio-admin");
			var ann = await Player("RadSpkAnn");
			await As(admin, "+radio/create Beacon");
			await As(ann, "+radio/join Beacon");

			await Assert.That(await As(ann, "+radio/color Beacon=sparkly")).Contains("'sparkly' is not a colour.");
			await Assert.That(await As(ann, "+radio/color Beacon=hz")).Contains("'hz' is not a colour.");
			await Assert.That(await As(ann, "+radio/color Beacon=+red")).Contains("You see Beacon like this now.");
			await Assert.That(await As(ann, "+radio/color Beacon=#ff8800")).Contains("You see Beacon like this now.");
			await Assert.That(await As(ann, "+radio/color Beacon=warning")).Contains("You see Beacon like this now.");
		}
		finally
		{
			await UninstallAsync();
		}
	}

	/// <summary>
	/// The radio's settings are +config's: +config radio lists them, a change is what +radio reads, and a colour
	/// is checked the way +radio/color checks one.
	/// </summary>
	[Test]
	public async Task TheRadiosSettingsAreChangedThroughConfig()
	{
		try
		{
			await InstallAsync();
			var staff = await Player("RadCfg");
			await God($"@permission/allow #{staff.DbRef.Number}=config.admin");

			await Assert.That(await As(staff, "+config radio")).Contains("recall-max").And.Contains("title-length").And.DoesNotContain("no such system");
			await Assert.That(await As(staff, "+config radio/color=sparkly")).Contains("'sparkly' is not a colour.");
			await As(staff, "+config radio/title-length=5");
			await As(staff, "+config radio/recall=1");

			var admin = await Player("RadCfgA", "radio-admin");
			await As(admin, "+radio/create Relay");
			await As(admin, "+radio/join Relay");
			await Assert.That(await As(admin, "+radio/title Relay=Lieutenant")).Contains("A title is up to 5 characters.");
			var first = TestIsolationHelpers.GenerateUniqueName("RadCfgOne");
			var second = TestIsolationHelpers.GenerateUniqueName("RadCfgTwo");
			await As(admin, $"+radio Relay={first}");
			await As(admin, $"+radio Relay={second}");
			await Assert.That(await As(admin, "+radio/recall Relay")).Contains(second).And.DoesNotContain(first)
				.Because("recall is 1 line when no number is given");
		}
		finally
		{
			await UninstallAsync();
		}
	}

	/// <summary>A long recall comes a screen at a time, the newest lines first; a page number goes only with a paged screen.</summary>
	[Test]
	public async Task ALongRecallIsPaged()
	{
		try
		{
			await InstallAsync();
			var admin = await Player("RadPgA", "radio-admin");
			await As(admin, "+radio/create Echo");
			await As(admin, "+radio/join Echo");
			var marker = TestIsolationHelpers.GenerateUniqueName("RadPg");
			for (var i = 1; i <= 40; i++) await As(admin, $"+radio Echo={marker}-{i:00}");

			var newest = await As(admin, "+radio/recall Echo=40");
			await Assert.That(newest).Contains($"{marker}-40").And.DoesNotContain($"{marker}-01")
				.And.Contains("page 1 of 2").And.Contains("+radio/recall/2 Echo=40 for the lines before");
			var older = await As(admin, "+radio/recall/2 Echo=40");
			await Assert.That(older).Contains($"{marker}-01").And.DoesNotContain($"{marker}-40").And.Contains("page 2 of 2");
			await Assert.That(await As(admin, "+radio/recall/3 Echo=40")).Contains("There are 2 pages - you asked for page 3.");
			await Assert.That(await As(admin, "+radio/recall/0 Echo")).Contains("+radio/recall <frequency>=<lines> shows what was said last");
			await Assert.That(await As(admin, "+radio/join/2 Echo")).Contains("+radio/join takes no page number.");
		}
		finally
		{
			await UninstallAsync();
		}
	}

	[Test]
	public async Task ModeratorsRestrictAndLocksAndListsDecideWhoMayTuneIn()
	{
		try
		{
			await InstallAsync();
			var admin = await Player("RadA", "radio-admin");
			var mod = await Player("RadMod");
			var ann = await Player("RadAnn");
			var cy = await Player("RadCy");

			await As(admin, "+radio/create Ops");
			await Assert.That(await As(mod, "+radio/restrict Ops=" + ann.Name)).Contains("Only a moderator of Ops can do that.");
			await Assert.That(await As(admin, $"+radio/modadd Ops={mod.Name}")).Contains($"{mod.Name} is one of the moderators of Ops now.");

			await As(ann, "+radio/join Ops");
			await Assert.That(await As(mod, $"+radio/restrict Ops={ann.Name}/1h")).Contains($"{ann.Name} may not speak on Ops until");
			await Assert.That(await As(ann, "+radio Ops=hello")).Contains("You may not speak on Ops.");
			await Assert.That(await As(mod, $"+radio/unrestrict Ops={ann.Name}")).Contains($"{ann.Name} may speak on Ops again.");
			var spoke = TestIsolationHelpers.GenerateUniqueName("RadOps");
			await As(ann, $"+radio Ops={spoke}");
			await Assert.That(Heard(ann, spoke)).IsEquivalentTo(new[] { $"<Ops> {ann.Name} says, \"{spoke}\"" });

			// A talk lock that lets nobody else in leaves the frequency to its lists.
			await Assert.That(await As(mod, "+radio/lock Ops/talk=#0")).Contains("Only an admin of Ops can do that.");
			await Assert.That(await As(admin, "+radio/lock Ops/talk=#0")).Contains("The talk lock of Ops is #0 now.");
			await Assert.That(await As(cy, "+radio/join Ops")).Contains("You may not tune in to Ops.");
			await Assert.That(await As(mod, $"+radio/memadd Ops={cy.Name}")).Contains($"{cy.Name} is one of the members of Ops now.");
			await Assert.That(await As(cy, "+radio/join Ops")).Contains("You tune in to Ops.");

			await Assert.That(await As(admin, "+radio/options Ops/keep=1")).Contains("Ops: keep is 1.");
			await As(cy, "+radio Ops=one");
			await As(cy, "+radio Ops=two");
			await Assert.That(await God("think [feedinfo(radio/1,messages)]")).IsEqualTo("1");

			await Assert.That(await As(cy, "+help radio moderators")).Contains("+radio/restrict");
		}
		finally
		{
			await UninstallAsync();
		}
	}

	[Test]
	public async Task WithRadioScene_WhatALoggingListenerHearsGoesIntoTheirSceneOnce()
	{
		try
		{
			await InstallAsync();
			var early = await Player("RadEarly");
			await Assert.That(await As(early, "+radio/log")).Contains("+radio has no /log switch");
			await Assert.That(await As(early, "+help radio")).Contains("Subtopics: admins · moderators");
			await InstallAsync("radio-scene");
			// Its topics join the radio's and the scene package's own help, as subtopics of each.
			await Assert.That(await As(early, "+help radio")).Contains("Subtopics: admins · log · moderators");
			await Assert.That(await As(early, "+help radio log")).Contains("+radio/stoplog <frequency>");
			await Assert.That(await As(early, "+help scene")).Contains("privacy · radio ·");
			await Assert.That(await As(early, "+help scene radio")).Contains("+help radio log");
			var admin = await Player("RadA", "radio-admin");
			var logger = await Player("RadLog", "approved");
			var ann = await Player("RadAnn");

			await As(admin, "+radio/create Ship");
			await As(logger, "+radio/join Ship");
			await As(ann, "+radio/join Ship");
			await Assert.That(await As(logger, "+radio/log Ship")).Contains("Join a scene first");

			await As(logger, $"+scene/create {TestIsolationHelpers.GenerateUniqueName("RadScene")}");
			var scene = await God($"think [scenefocus(#{logger.DbRef.Number})]");
			await Assert.That(await As(logger, "+radio/log Ship")).Contains("goes into your scene until +radio/stoplog");
			await Assert.That(await As(logger, "+radio/log")).Contains("Logged into your scene: Ship.");

			var said = TestIsolationHelpers.GenerateUniqueName("RadLogged");
			await As(ann, $"+radio Ship={said}");
			var poses = (await God($"think [sceneposes({scene})]")).Split(' ', StringSplitOptions.RemoveEmptyEntries);
			var logged = new List<string>();
			foreach (var pose in poses)
			{
				var content = await God($"think [scenepose({scene},{pose},content)]");
				if (content.Contains(said, StringComparison.Ordinal)) logged.Add(content);
			}

			await Assert.That(logged).IsEquivalentTo(new[] { $"<Ship> {ann.Name} says, \"{said}\"" });
			await Assert.That(await God($"think [scenepose({scene},{poses[^1]},type)]")).IsEqualTo("radio");
			await Assert.That(await God($"think [scenepose({scene},{poses[^1]},frequency)]")).IsEqualTo("Ship");
			await Assert.That(await God("think [scenetype(radio,presentation)]")).IsEqualTo("message")
				.Because("radio-scene adds the radio type to the Scene Logger");
			await Assert.That(await As(logger, "+scene/types")).Contains("Radio");
			await Assert.That(await As(logger, "+scene/hide radio")).Contains("Radio lines are hidden from your recall and log.");
			var recalled = await As(logger, "+scene/recall 1");
			await Assert.That(recalled).DoesNotContain(said);
			await Assert.That(recalled).Contains("1 line of radio hidden.");
			await Assert.That(await As(logger, "+scene/show radio")).Contains("Radio lines show in your recall and log.");
			await Assert.That(await As(logger, "+scene/recall 1")).IsEqualTo($"<RADIO · {scene}> <Ship> {ann.Name} says, \"{said}\"")
				.Because("a radio line is one line tagged with its scene, with no rule over it");

			await As(logger, "+radio/stoplog Ship");
			var unlogged = TestIsolationHelpers.GenerateUniqueName("RadUnlogged");
			await As(ann, $"+radio Ship={unlogged}");
			await Assert.That(await God($"think [words(sceneposes({scene}))]")).IsEqualTo(poses.Length.ToString());
		}
		finally
		{
			await Installer.UninstallAsync("radio-scene", force: true, CancellationToken.None);
			await UninstallAsync();
		}
	}

	/// <summary>
	/// +scene/pause leaves the player's focus on the scene, so a tap that only asks for a focus would keep writing
	/// radio lines into it. A paused scene records nothing, radio included, until +scene/start resumes it.
	/// </summary>
	[Test]
	public async Task WithRadioScene_APausedSceneLogsNoRadioUntilItResumes()
	{
		try
		{
			await InstallAsync();
			await InstallAsync("radio-scene");
			var admin = await Player("RadA", "radio-admin");
			var logger = await Player("RadPauser", "approved");
			var ann = await Player("RadAnn");

			await As(admin, "+radio/create Harbour");
			await As(logger, "+radio/join Harbour");
			await As(ann, "+radio/join Harbour");
			await As(logger, $"+scene/create {TestIsolationHelpers.GenerateUniqueName("RadPaused")}");
			var scene = await God($"think [scenefocus(#{logger.DbRef.Number})]");
			await As(logger, "+radio/log Harbour");

			await Assert.That(await As(logger, "+scene/pause")).Contains("paused");
			await Assert.That(await God($"think [scenefocus(#{logger.DbRef.Number})]")).IsEqualTo(scene)
				.Because("pausing keeps the player's focus on the scene");
			var before = await God($"think [words(sceneposes({scene}))]");
			var whilePaused = TestIsolationHelpers.GenerateUniqueName("RadWhilePaused");
			await As(ann, $"+radio Harbour={whilePaused}");
			await Assert.That(await God($"think [words(sceneposes({scene}))]")).IsEqualTo(before)
				.Because("a paused scene records nothing");

			await As(logger, $"+scene/start {scene}");
			var resumed = TestIsolationHelpers.GenerateUniqueName("RadResumed");
			await As(ann, $"+radio Harbour={resumed}");
			var last = await God($"think [last(sceneposes({scene}))]");
			await Assert.That(await God($"think [scenepose({scene},{last},content)]")).Contains(resumed);
		}
		finally
		{
			await Installer.UninstallAsync("radio-scene", force: true, CancellationToken.None);
			await UninstallAsync();
		}
	}
}
