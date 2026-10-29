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
/// The bundled staff package, installed from the catalogue and driven with typed commands: +staff lists the
/// characters holding a staff role and everyone holding a duty from the +config duties table, leaves God out,
/// names the duties nobody holds and shows who is on duty; staff-status adds a staff member's +status beside
/// that. Each test installs the packages and removes them again. Not in parallel: +staff is global and its
/// duties table is the package's.
/// </summary>
[NotInParallel]
public class StaffIntegrationTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private TestHelpers.NotificationRecorder Notifications => WebAppFactoryArg.Notifications;
	private IPackageInstallService Installer => WebAppFactoryArg.Services.GetRequiredService<IPackageInstallService>();
	private IPackageRegistryService Registry => WebAppFactoryArg.Services.GetRequiredService<IPackageRegistryService>();

	private async Task God(string command)
	{
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain(command));
		await WebAppFactoryArg.QueueBarrierAsync();
	}

	/// <summary>What <paramref name="player"/> was told while <paramref name="command"/> ran.</summary>
	private async Task<string> As(TestIsolationHelpers.TestPlayer player, string command)
	{
		var before = Notifications.CountFor(player.DbRef);
		await Parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain(command));
		await WebAppFactoryArg.QueueBarrierAsync();
		return string.Join("\n", Notifications.For(player.DbRef).Skip(before));
	}

	/// <summary>The line of <paramref name="screen"/> naming <paramref name="name"/>, or nothing.</summary>
	private static string RowOf(string screen, string name)
		=> screen.Split('\n').FirstOrDefault(line => line.Contains(name)) ?? string.Empty;

	private async Task<DBRef> RoomAsync(string prefix)
	{
		var god = (await Mediator.Send(new GetObjectNodeQuery(new DBRef(1)))).Expect<AnySharpObject>().Expect<SharpPlayer>();
		return await Mediator.Send(new CreateRoomCommand(TestIsolationHelpers.GenerateUniqueName($"{prefix}Room"), god));
	}

	/// <summary>A connected player in a room of their own.</summary>
	private async Task<TestIsolationHelpers.TestPlayer> Player(string prefix) =>
		await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(WebAppFactoryArg.Services, Mediator, ConnectionService, prefix, await RoomAsync(prefix));

	private Task InstallAsync() => ApplyAsync("staff");

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

	private async Task UninstallAsync() => await WebAppFactoryArg.Services.UninstallWithBridgesAsync("staff");

	[Test]
	public async Task StaffRolesAndDutiesAreListed()
	{
		try
		{
			await InstallAsync();
			var director = await Player("StDir");
			var royal = await Player("StRoy");
			var builder = await Player("StBld");
			var mortal = await Player("StMor");
			var manager = await Player("StMgr");
			await God($"@role/assign #{director.DbRef.Number}=wizard");
			await God($"@role/assign #{royal.DbRef.Number}=royalty");
			await God($"@permission/allow #{manager.DbRef.Number}=config.admin");

			// Before any duty: role holders are listed with their role, a mortal and God are not.
			var screen = await As(mortal, "+staff");
			await Assert.That(RowOf(screen, director.Name)).Contains("Wizard");
			await Assert.That(RowOf(screen, royal.Name)).Contains("Royalty");
			await Assert.That(screen).DoesNotContain(mortal.Name).And.DoesNotContain(builder.Name)
				.And.DoesNotContain("God").And.DoesNotContain("Package Manager");

			// Duties come from the +config table: a holder without a role is listed, a duty nobody holds is open.
			var key = TestIsolationHelpers.GenerateUniqueName("d").Replace("_", "");
			// Titles short enough to stay on one line beside a long test player's name; the table is the package's own.
			const string title = "Director";
			const string open = "Story";
			await Assert.That(await As(manager, $"+config/add staff/duties/{key}a={title}")).Contains($"Added staff/duties/{key}a");
			await Assert.That(await As(manager, $"+config staff/duties/{key}a/holder=#{director.DbRef.Number}")).Contains("holder is now");
			await As(manager, $"+config staff/duties/{key}a/order=0");
			await As(manager, $"+config/add staff/duties/{key}b=Builder");
			await As(manager, $"+config staff/duties/{key}b/holder=#{builder.DbRef.Number}");
			await As(manager, $"+config/add staff/duties/{key}c={open}");

			screen = await As(mortal, "+stafflist");
			await Assert.That(RowOf(screen, director.Name)).Contains(title).And.Contains("Wizard");
			await Assert.That(RowOf(screen, builder.Name)).Contains("Builder").And.DoesNotContain("Wizard");
			await Assert.That(RowOf(screen, "Open:")).Contains(open);
			await Assert.That(screen.IndexOf(director.Name, StringComparison.Ordinal))
				.IsLessThan(screen.IndexOf(builder.Name, StringComparison.Ordinal))
				.Because("a duty of order 0 comes before one of order 50");

			// God is listed once it holds a duty.
			await As(manager, $"+config staff/duties/{key}c/holder=#1");
			await Assert.That(RowOf(await As(mortal, "+staff"), "God")).Contains(open);

			// Staff go on and off duty; anyone else is refused.
			await Assert.That(RowOf(await As(mortal, "+staff"), royal.Name)).Contains("Off duty");
			await Assert.That(await As(royal, "+staff/on")).Contains("[STAFF] Done: You are on duty.");
			await Assert.That(RowOf(await As(mortal, "+staff"), royal.Name)).Contains("On duty");
			await Assert.That(await As(builder, "+staff/on")).Contains("You are on duty.")
				.Because("holding a duty puts someone on the list without a staff role");
			await Assert.That(await As(mortal, "+staff/on")).Contains("Only someone on the staff list can go on or off duty.");
			await Assert.That(await As(mortal, "+staff/all")).Contains("+staff has no /all switch.");

			// A hidden staff member is shown Offline, on duty or not.
			await God($"@set #{royal.DbRef.Number}=DARK");
			await Assert.That(RowOf(await As(mortal, "+staff"), royal.Name)).Contains("Offline").And.DoesNotContain("On duty");
			await Assert.That(await As(royal, "+staff/off")).Contains("You are off duty.");
			await Assert.That(await As(mortal, "+help staff")).Contains("+stafflist is the same list");
		}
		finally
		{
			await UninstallAsync();
		}
	}

	[Test]
	public async Task AStaffMembersStatusIsShownBesideTheirDuty()
	{
		try
		{
			await InstallAsync();
			await ApplyAsync("who-where");
			await ApplyAsync("status");
			await ApplyAsync("staff-status");

			var wizard = await Player("StsWiz");
			var mortal = await Player("StsMor");
			await God($"@role/assign #{wizard.DbRef.Number}=wizard");
			await As(wizard, "+staff/on");
			await Assert.That(RowOf(await As(mortal, "+staff"), wizard.Name)).Contains("On duty").And.DoesNotContain("LFRP");

			await As(wizard, "+status lfrp");
			await Assert.That(RowOf(await As(mortal, "+staff"), wizard.Name)).Contains("[On duty] · LFRP");
			await As(wizard, "+staff/off");
			await Assert.That(RowOf(await As(mortal, "+staff"), wizard.Name)).Contains("Off duty · LFRP");
		}
		finally
		{
			await WebAppFactoryArg.Services.UninstallWithBridgesAsync("staff-status");
			await WebAppFactoryArg.Services.UninstallWithBridgesAsync("status");
			await WebAppFactoryArg.Services.UninstallWithBridgesAsync("who-where");
			await UninstallAsync();
		}
	}
}
