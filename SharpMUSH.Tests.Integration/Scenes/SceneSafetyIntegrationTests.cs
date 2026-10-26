using Mediator;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
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

namespace SharpMUSH.Tests.Integration.Scenes;

/// <summary>
/// <c>@scene/notice</c>, the hook that tells one member of a scene and nobody else, and the scene-safety package
/// built on it: lines and veils a player keeps to themselves, the warning a joining player gets, the owner's
/// anonymous summary, and the pause signal that reaches the owner without the sender's name. Not in parallel:
/// scene-verse's details and the logger's filters are the game's, one for every test.
/// </summary>
[NotInParallel]
public class SceneSafetyIntegrationTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IMUSHCodeParser FunctionParser => WebAppFactoryArg.FunctionParser;
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private TestHelpers.NotificationRecorder Notifications => WebAppFactoryArg.Notifications;
	private IPackageInstallService Installer => WebAppFactoryArg.Services.GetRequiredService<IPackageInstallService>();

	private async Task<string> Eval(string expression) =>
		(await FunctionParser.EvaluateAsync(MarkupText.Plain(expression))).ToPlainText().Trim();

	private async Task<string> God(string command)
	{
		var god = DBRef.Parse(await Eval("objid(#1)"));
		var before = Notifications.CountFor(god);
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain(command));
		await WebAppFactoryArg.QueueBarrierAsync();
		return string.Join("\n", Notifications.For(god).Skip(before));
	}

	/// <summary>What <paramref name="player"/> was told while <paramref name="command"/> ran.</summary>
	private async Task<string> As(TestIsolationHelpers.TestPlayer player, string command)
	{
		var before = Notifications.CountFor(player.DbRef);
		await Parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain(command));
		await WebAppFactoryArg.QueueBarrierAsync();
		return Heard(player, before);
	}

	private string Heard(TestIsolationHelpers.TestPlayer player, int from) =>
		string.Join("\n", Notifications.For(player.DbRef).Skip(from));

	private async Task LoggerAsync()
	{
		var registry = (IPackageRegistryService)WebAppFactoryArg.Services.GetRequiredService<ISharpDatabase>();
		var logger = DBRef.Parse((await registry.GetPackageObjectsAsync("scene")).Single(o => o.Ref == "logger").Objid);
		await God($"@teleport {logger}=#2");
	}

	private async Task<TestIsolationHelpers.TestPlayer> PlayerAsync(string prefix, DBRef home)
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, prefix, home);
		await God($"@role/assign #{player.DbRef.Number}=approved");
		return player;
	}

	private async Task<DBRef> RoomAsync(string prefix)
	{
		var god = (await Mediator.Send(new GetObjectNodeQuery(new DBRef(1)))).Expect<AnySharpObject>().Expect<SharpPlayer>();
		return await Mediator.Send(new CreateRoomCommand(TestIsolationHelpers.GenerateUniqueName(prefix), god));
	}

	private async Task<string> SceneAsync(TestIsolationHelpers.TestPlayer owner)
	{
		await As(owner, $"+scene/create {TestIsolationHelpers.GenerateUniqueName("SafetyScene")}");
		return await Eval($"scenefocus(#{owner.DbRef.Number})");
	}

	private async Task InstallAsync(string package)
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
		await Assert.That(applied.Result).IsTypeOf<OkObjectResult>().Because($"{package} must install from the catalogue");
		await WebAppFactoryArg.QueueBarrierAsync();
	}

	[Test]
	public async Task A_notice_reaches_the_member_named_and_nobody_else()
	{
		await LoggerAsync();
		var room = await RoomAsync("NoticeRoom");
		var owner = await PlayerAsync("NotOwner", room);
		var member = await PlayerAsync("NotMember", room);
		var scene = await SceneAsync(owner);
		await As(member, $"+scene/join {scene}");
		var text = TestIsolationHelpers.GenerateUniqueName("NoticeText");
		var ownerBefore = Notifications.CountFor(owner.DbRef);
		var memberBefore = Notifications.CountFor(member.DbRef);

		await God($"@scene/notice {scene}/#{owner.DbRef.Number}={text}");

		await Assert.That(Heard(owner, ownerBefore)).Contains(text);
		await Assert.That(Heard(member, memberBefore)).DoesNotContain(text);
		await Assert.That(await God($"@scene/notice {scene}/#{room.Number}={text}")).Contains("No such scene or member.");
	}

	[Test]
	public async Task Scene_safety_warns_a_joiner_summarises_for_the_owner_and_signals_without_a_name()
	{
		await LoggerAsync();
		await InstallAsync("scene-verse");
		await InstallAsync("scene-safety");
		try
		{
			var room = await RoomAsync("SafetyRoom");
			var owner = await PlayerAsync("SafOwner", room);
			var lined = await PlayerAsync("SafLine", room);
			var veiled = await PlayerAsync("SafVeil", room);
			var scene = await SceneAsync(owner);
			await As(owner, "+scene/set warning=Spiders and snakes|Gore");

			await Assert.That(await As(lined, "+safety/line Spiders")).Contains("Your lines: Spiders. Nobody else sees them.");
			await Assert.That(await As(veiled, "+safety/veil Gore|Drowning")).Contains("Your veils: Gore · Drowning.");
			await Assert.That(await As(lined, "+safety")).Contains("Spiders").And.DoesNotContain("Gore");

			await As(owner, $"+scene/invite {scene}=#{lined.DbRef.Number}");
			var registry = WebAppFactoryArg.Services.GetRequiredService<IPackageRegistryService>();
			var safety = (await registry.GetPackageObjectsAsync("scene-safety")).Single(o => o.Ref == "safety").Objid;
			await Assert.That(await Eval($"u({safety}/FUN`SCENE`KEYS,{scene})")).IsEmpty();

			await Assert.That(await As(lined, $"+scene/join {scene}"))
				.Contains("This scene's content warnings touch your lines: Spiders and snakes.");
			await Assert.That(await As(veiled, $"+scene/join {scene}")).Contains("They touch your veils: Gore.");

			await Assert.That(await As(lined, $"+safety/scene {scene}")).Contains($"Only scene {scene}'s owner sees its summary.");
			var summary = await As(owner, $"+safety/scene {scene}");
			await Assert.That(summary).Contains("Spiders and snakes").And.Contains("Gore")
				.And.DoesNotContain(lined.Name).And.DoesNotContain(veiled.Name).And.DoesNotContain("Drowning");
			await Assert.That(await As(owner, $"+scene {scene}")).Contains("Spiders and snakes").And.Contains("Safety");
			await Assert.That(await As(lined, $"+scene {scene}")).DoesNotContain("Names, and the words they used");

			var ownerBefore = Notifications.CountFor(owner.DbRef);
			var veiledBefore = Notifications.CountFor(veiled.DbRef);
			await Assert.That(await As(lined, "+safety/pause Can we fade this out?"))
				.Contains($"Scene {scene}'s owner was asked to pause, without your name.");
			var told = Heard(owner, ownerBefore);
			await Assert.That(told).Contains($"Someone in scene {scene} asks to pause. They add: Can we fade this out? Nobody else was told.")
				.And.DoesNotContain(lined.Name);
			await Assert.That(Heard(veiled, veiledBefore)).DoesNotContain("asks to pause");

			await Assert.That(await As(lined, "+safety/skip")).Contains("You signalled a moment ago.");
			await Assert.That(await As(owner, "+safety/pause")).Contains($"You run scene {scene}, so the call is yours.");
		}
		finally
		{
			await WebAppFactoryArg.Services.UninstallWithBridgesAsync("scene-safety");
			await Installer.UninstallAsync("scene-verse", force: true, CancellationToken.None);
		}
	}
}
