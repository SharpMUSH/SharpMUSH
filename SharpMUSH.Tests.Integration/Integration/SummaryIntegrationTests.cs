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
/// The bundled summary package, installed from the catalogue and driven with typed commands: +summary tells
/// the player's location one line, refuses an empty or too long one, and is never recorded by a scene running
/// there. Each test installs the package and removes it again. Not in parallel: +summary is global, and the
/// scene test moves the Scene Logger.
/// </summary>
[NotInParallel]
public class SummaryIntegrationTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IMUSHCodeParser FunctionParser => WebAppFactoryArg.FunctionParser;
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private TestHelpers.NotificationRecorder Notifications => WebAppFactoryArg.Notifications;
	private IPackageInstallService Installer => WebAppFactoryArg.Services.GetRequiredService<IPackageInstallService>();
	private IPackageRegistryService Registry => WebAppFactoryArg.Services.GetRequiredService<IPackageRegistryService>();

	private async Task<string> God(string command) =>
		(await Parser.CommandParse(1, ConnectionService, MarkupText.Plain(command))).Message.ToPlainText()?.Trim() ?? string.Empty;

	private async Task<string> Eval(string expression) =>
		(await FunctionParser.EvaluateAsync(MarkupText.Plain(expression))).ToPlainText().Trim();

	/// <summary>What <paramref name="player"/> was told while <paramref name="command"/> ran.</summary>
	private async Task<string> As(TestIsolationHelpers.TestPlayer player, string command)
	{
		var before = Notifications.CountFor(player.DbRef);
		await Parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain(command));
		await WebAppFactoryArg.QueueBarrierAsync();
		return string.Join("\n", Notifications.For(player.DbRef).Skip(before));
	}

	/// <summary>Everything <paramref name="player"/> has been told so far.</summary>
	private string Heard(TestIsolationHelpers.TestPlayer player) => string.Join("\n", Notifications.For(player.DbRef));

	private async Task<DBRef> RoomAsync(string prefix)
	{
		var god = (await Mediator.Send(new GetObjectNodeQuery(new DBRef(1)))).Expect<AnySharpObject>().Expect<SharpPlayer>();
		return await Mediator.Send(new CreateRoomCommand(TestIsolationHelpers.GenerateUniqueName($"{prefix}Room"), god));
	}

	/// <summary>A connected player in <paramref name="room"/>.</summary>
	private Task<TestIsolationHelpers.TestPlayer> Player(string prefix, DBRef room) =>
		TestIsolationHelpers.CreateTestPlayerWithHandleAsync(WebAppFactoryArg.Services, Mediator, ConnectionService, prefix, room);

	private async Task InstallAsync()
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
			WebAppFactoryArg.Services.GetRequiredService<SharpMUSH.Library.Models.Packages.EngineVersion>());
		var applied = await controller.Apply(
			new ApplyRequest(BundledPackages.RemoteName, "summary", null, null, null), CancellationToken.None);
		await Assert.That(applied.Result).IsTypeOf<OkObjectResult>().Because("summary must install from the catalogue");
		// AINSTALL is queued after the apply: it adds the command.
		await WebAppFactoryArg.QueueBarrierAsync();
	}

	private async Task UninstallAsync() => await Installer.UninstallAsync("summary", force: true, CancellationToken.None);

	[Test]
	public async Task TheRoomHearsItAndNobodyElse()
	{
		try
		{
			await InstallAsync();
			var room = await RoomAsync("SmHere");
			var ann = await Player("SmAnn", room);
			var bo = await Player("SmBo", room);
			var cy = await Player("SmCy", await RoomAsync("SmThere"));
			var words = TestIsolationHelpers.GenerateUniqueName("SmWords");

			// What is typed is shown as typed: brackets, percent signs and commas included, none evaluated.
			var typed = $"{words} bars the door, [add(1,2)] 50% %r";
			var line = $"[SUMMARY] {ann.Name}: {typed}";
			await Assert.That(await As(ann, $"+summary {typed}")).Contains(line);
			await Assert.That(Heard(bo)).Contains(line);
			await Assert.That(Heard(cy)).DoesNotContain(words);

			// Nothing is kept: no attribute anywhere names the summary.
			await Assert.That(await God($"think lattr(#{ann.DbRef.Number}/**)")).DoesNotContain("SUMMARY");

			// +help carries the topics.
			await Assert.That(await As(cy, "+help summary")).Contains("sums up your last pose")
				.And.Contains("[SUMMARY] Mira: Bars the door and will not let anyone leave.");
			await Assert.That(await As(cy, "+help summary staff")).Contains("+config summary/max-length=300");
		}
		finally
		{
			await UninstallAsync();
		}
	}

	[Test]
	public async Task AnEmptyOrTooLongSummaryIsRefused()
	{
		try
		{
			await InstallAsync();
			var room = await RoomAsync("SmLimit");
			var ann = await Player("SmLAnn", room);
			var bo = await Player("SmLBo", room);

			await Assert.That(await As(ann, "+summary")).Contains("[SUMMARY] Warning: What happened? +summary <text>");
			await Assert.That(await As(ann, "+summary    ")).Contains("What happened?");
			await Assert.That(await As(ann, "+summary/bogus Hello")).Contains("+summary takes no switches.");

			var words = TestIsolationHelpers.GenerateUniqueName("SmLong");
			var tooLong = words + new string('x', 201 - words.Length);
			await Assert.That(await As(ann, $"+summary {tooLong}"))
				.Contains("Keep a summary to 200 characters. That one is 201.");
			await Assert.That(Heard(bo)).DoesNotContain(words);
			var fits = words + new string('y', 200 - words.Length);
			await Assert.That(await As(ann, $"+summary {fits}")).Contains($"{ann.Name}: {fits}");

			// The limit is a +config setting, read where it lives.
			var staff = await Player("SmLStaff", room);
			await God($"@permission/allow #{staff.DbRef.Number}=config.admin");
			await Assert.That(await As(staff, "+config summary/max-length=30")).Contains("summary/max-length is now 30, was 200.");
			await Assert.That(await As(staff, "+config summary/max-length=5")).Contains("It must be at least 20.");
			await Assert.That(await As(ann, $"+summary {new string('z', 31)}")).Contains("Keep a summary to 30 characters. That one is 31.");
			await Assert.That(await As(staff, "+config/reset summary/max-length")).Contains("summary/max-length is now 200, was 30.");
		}
		finally
		{
			await UninstallAsync();
		}
	}

	[Test]
	public async Task AScenePlayingInTheRoomDoesNotRecordIt()
	{
		try
		{
			await InstallAsync();
			// The Logger's capture hooks and $-commands answer from the master room; other suites move it.
			var logger = (await Registry.GetPackageObjectsAsync("scene")).Single(o => o.Ref == "logger").Objid;
			await God($"@teleport {DBRef.Parse(logger)}=#2");

			var room = await RoomAsync("SmScene");
			var ann = await Player("SmSAnn", room);
			var bo = await Player("SmSBo", room);
			await God($"@role/assign #{ann.DbRef.Number}=approved");

			await As(ann, $"+scene/create Summary Scene {TestIsolationHelpers.GenerateUniqueName("SmS")}");
			var sceneId = await Eval($"scenefocus(#{ann.DbRef.Number})");
			await Assert.That(sceneId).DoesNotStartWith("#-1");

			// A pose in the room is recorded, so the scene is capturing.
			var posed = TestIsolationHelpers.GenerateUniqueName("SmPose");
			await As(ann, $":{posed}");
			await Assert.That(await Eval($"words(sceneposes({sceneId}))")).IsEqualTo("1");

			var summed = TestIsolationHelpers.GenerateUniqueName("SmSum");
			await Assert.That(await As(ann, $"+summary {summed}")).Contains($"[SUMMARY] {ann.Name}: {summed}");
			await Assert.That(Heard(bo)).Contains($"[SUMMARY] {ann.Name}: {summed}");
			await Assert.That(await Eval($"words(sceneposes({sceneId}))")).IsEqualTo("1")
				.Because("a summary is local and kept nowhere, a running scene included");
			await Assert.That(await Eval($"scenepose({sceneId},last(sceneposes({sceneId})),content)")).DoesNotContain(summed);
		}
		finally
		{
			await UninstallAsync();
		}
	}
}
