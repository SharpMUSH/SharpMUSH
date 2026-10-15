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
/// The bundled info package, installed from the catalogue and driven with typed commands: +info and +pinfo files
/// kept on the character, and +journal entries shared one at a time.
/// Each test installs the package and removes it again. Not in parallel: the commands are global.
/// </summary>
[NotInParallel]
public class InfoIntegrationTests
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

	/// <summary>A connected player in a room of its own.</summary>
	private async Task<TestIsolationHelpers.TestPlayer> Player(string prefix)
	{
		var god = (await Mediator.Send(new GetObjectNodeQuery(new DBRef(1)))).Expect<AnySharpObject>().Expect<SharpPlayer>();
		var home = await Mediator.Send(new CreateRoomCommand(TestIsolationHelpers.GenerateUniqueName($"{prefix}Room"), god));
		return await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, prefix, home);
	}

	private async Task InstallAsync()
	{
		var controller = new PackagesController(
			WebAppFactoryArg.Services.GetRequiredService<IPackageRegistryService>(),
			WebAppFactoryArg.Services.GetRequiredService<IPackageSourceService>(),
			WebAppFactoryArg.Services.GetRequiredService<IPackageManifestService>(),
			Installer,
			WebAppFactoryArg.Services.GetRequiredService<IPackageAuthoringService>(),
			WebAppFactoryArg.Services.GetRequiredService<IPackageOperationRunner>(),
			WebAppFactoryArg.Services.GetRequiredService<PluginUploadStore>(),
			WebAppFactoryArg.Services.GetRequiredService<IAuditLog>(),
			WebAppFactoryArg.Services.GetRequiredService<SharpMUSH.Library.Models.Packages.EngineVersion>());
		var applied = await controller.Apply(
			new ApplyRequest(BundledPackages.RemoteName, "info", null, null, null), CancellationToken.None);
		await Assert.That(applied.Result).IsTypeOf<OkObjectResult>().Because("info must install from the catalogue");
		// AINSTALL is queued after the apply: it adds the commands.
		await WebAppFactoryArg.QueueBarrierAsync();
	}

	private async Task UninstallAsync() => await Installer.UninstallAsync("info", force: true, CancellationToken.None);

	[Test]
	public async Task FilesArePublicOrPrivateAndStaffLockThem()
	{
		try
		{
			await InstallAsync();
			var ann = await Player("InAnn");
			var bo = await Player("InBo");
			var staff = await Player("InStaff");
			await God($"@permission/allow #{staff.DbRef.Number}=softcode.info.staff");

			// What a player types is kept as typed, never evaluated.
			await Assert.That(await As(ann, "+info Background=Raised on the docks. [add(1,2)] 50%"))
				.Contains("Your info file Background is saved.");
			await Assert.That(await God($"think get(#{ann.DbRef.Number}/INFOFILE`1`TEXT)"))
				.IsEqualTo("Raised on the docks. [add(1,2)] 50%");
			await Assert.That(await As(ann, "+pinfo Secrets=Owes the harbourmaster.")).Contains("Your private file Secrets is saved.");

			// The tree is the package's: the player cannot write it with &.
			await As(ann, "&INFOFILE`1`TEXT me=Rewritten");
			await Assert.That(await God($"think get(#{ann.DbRef.Number}/INFOFILE`1`TEXT)")).StartsWith("Raised on the docks.");

			// Anyone reads +info, by a name's start; only the player and staff read +pinfo.
			await Assert.That(await As(bo, $"+info {ann.Name}/back")).Contains("Raised on the docks.");
			await Assert.That(await As(bo, $"+info {ann.Name}")).Contains("Background");
			await Assert.That(await As(bo, $"+pinfo {ann.Name}/Secrets"))
				.Contains($"Only {ann.Name} and staff read {ann.Name}'s private files.");
			await Assert.That(await As(staff, $"+pinfo {ann.Name}/Secrets")).Contains("Owes the harbourmaster.");
			await Assert.That(await As(ann, "+info me/Background")).Contains("Raised on the docks.");

			await Assert.That(await As(bo, $"+info {ann.Name}/Background=Mine now"))
				.Contains("Only staff change someone else's files.");
			await Assert.That(await As(bo, $"+info/lock {ann.Name}/Background")).Contains("Only staff lock files.");

			// A locked file is staff's; the player is told what staff did.
			await Assert.That(await As(staff, $"+info/lock {ann.Name}/Background"))
				.Contains($"{ann.Name}'s info file Background is locked: only staff change it now.");
			await Assert.That(string.Join("\n", Notifications.For(ann.DbRef))).Contains($"{staff.Name} locked your info file Background.");
			await Assert.That(await As(ann, "+info Background=Changed")).Contains("Staff locked Background, so only staff change it.");
			await Assert.That(await As(ann, "+info/delete Background")).Contains("Staff locked Background, so only staff change it.");
			await Assert.That(await As(staff, $"+info/unlock {ann.Name}/Background")).Contains("is unlocked.");
			await Assert.That(await As(ann, "+info/rename Background=History")).Contains("Your info file Background is called History now.");
			await Assert.That(await As(ann, "+info/get History")).Contains("+info History=Raised on the docks. [add(1,2)] 50%");
			await Assert.That(await As(ann, "+info/delete History|Nothing"))
				.Contains("Your info file History is deleted.").And.Contains("There is no info file called 'Nothing'.");

			// Names keep to the characters the commands can parse.
			await Assert.That(await As(ann, "+info a,b=x")).Contains("A file's name cannot hold");
			await Assert.That(await As(ann, "+info/bogus")).Contains("+info has no /bogus switch.");
			await Assert.That(await As(ann, "+info/rename/2 x=y")).Contains("+info/rename takes no page number.");
		}
		finally
		{
			await UninstallAsync();
		}
	}

	[Test]
	public async Task ApprovalLeavesFilesToStaff()
	{
		try
		{
			await InstallAsync();
			var ann = await Player("ApAnn");
			await Assert.That(await As(ann, "+info Look=Tall.")).Contains("is saved.");
			await God($"@role/assign #{ann.DbRef.Number}=approved");

			// Off by default: an approved character still writes their own.
			await Assert.That(await As(ann, "+info Look=Taller.")).Contains("is updated.");
			await God("+config info/approval-lock=1");
			try
			{
				await Assert.That(await As(ann, "+info Look=Tallest.")).Contains("Staff keep your files now you are approved.");
				await Assert.That(await As(ann, "+info New=x")).Contains("Staff keep your files now you are approved.");
			}
			finally
			{
				await God("+config/reset info/approval-lock");
			}
		}
		finally
		{
			await UninstallAsync();
		}
	}

	[Test]
	public async Task JournalEntriesAreReadOnlyWhereShared()
	{
		try
		{
			await InstallAsync();
			var ann = await Player("JnAnn");
			var bo = await Player("JnBo");
			var cy = await Player("JnCy");

			await Assert.That(await As(ann, "+journal The night market=Nobody followed.")).Contains("Journal entry 1, The night market, is written.");
			await Assert.That(await As(ann, "+journal Second=Still nobody.")).Contains("Journal entry 2, Second, is written.");
			await Assert.That(await As(bo, $"+journal {ann.Name}")).Contains($"Nothing in {ann.Name}'s journal is shared with you.");
			await Assert.That(await As(bo, $"+journal {ann.Name}/1")).Contains("that you can read.");

			// Shared with one player: they are told, read it, and see only "you" as its readers.
			await Assert.That(await As(ann, $"+journal/share 1={bo.Name}")).Contains($"{bo.Name} reads journal entry 1, The night market, now.");
			await Assert.That(string.Join("\n", Notifications.For(bo.DbRef))).Contains($"{ann.Name} shared a journal entry");
			var seen = await As(bo, $"+journal {ann.Name}");
			await Assert.That(seen).Contains("The night market").And.DoesNotContain("Second");
			await Assert.That(await As(bo, $"+journal {ann.Name}/1")).Contains("Nobody followed.");
			await Assert.That(await As(cy, $"+journal {ann.Name}/1")).Contains("that you can read.");

			await Assert.That(await As(ann, "+journal/share 2=all")).Contains("Everyone reads journal entry 2, Second, now.");
			await Assert.That(await As(cy, $"+journal {ann.Name}/2")).Contains("Still nobody.");
			await Assert.That(await As(ann, "+journal/unshare 1")).Contains("Only you and staff read journal entry 1");
			await Assert.That(await As(bo, $"+journal {ann.Name}/1")).Contains("that you can read.");

			await Assert.That(await As(ann, "+journal/edit 1=Somebody followed.")).Contains("is updated.");
			await Assert.That(await As(ann, "+journal/get 1")).Contains("+journal/edit 1=Somebody followed.");
			await Assert.That(await As(ann, "+journal/share 1=nobody-by-that-name")).Contains("There is no player called 'nobody-by-that-name'.");
			await Assert.That(await As(ann, "+journal/delete 1")).Contains("Journal entry 1, The night market, is deleted.");
			await Assert.That(await As(ann, "+journal 2")).Contains("Still nobody.");
		}
		finally
		{
			await UninstallAsync();
		}
	}
}
