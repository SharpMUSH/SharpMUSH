using System.Text.Json;
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
/// Poses typed by approved characters reach activity(), and the bundled health and health-jobs packages draw
/// them for staff. The world is shared, so every count is read for this test's own room and characters, never
/// as a game-wide total. Not in parallel: +health is global.
/// </summary>
[NotInParallel]
public class HealthIntegrationTests
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

	/// <summary>A connected player in a room of its own, and that room's name.</summary>
	private async Task<(TestIsolationHelpers.TestPlayer Player, string Room)> Player(string prefix)
	{
		var god = (await Mediator.Send(new GetObjectNodeQuery(new DBRef(1)))).Expect<AnySharpObject>().Expect<SharpPlayer>();
		var roomName = TestIsolationHelpers.GenerateUniqueName($"{prefix}Room");
		var home = await Mediator.Send(new CreateRoomCommand(roomName, god));
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, prefix, home);
		return (player, roomName);
	}

	private async Task ApplyAsync(string package)
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
			new ApplyRequest(BundledPackages.RemoteName, package, null, null, null), CancellationToken.None);
		await Assert.That(applied.Result).IsTypeOf<OkObjectResult>().Because($"{package} must install from the catalogue");
		// AINSTALL is queued after the apply: it adds the commands.
		await WebAppFactoryArg.QueueBarrierAsync();
	}

	/// <summary>The rooms report's row for <paramref name="room"/>, or nothing.</summary>
	private async Task<JsonElement?> RoomRow(string room)
	{
		using var report = JsonDocument.Parse(await God("""think activity(rooms,{{"limit":1000}})"""));
		foreach (var row in report.RootElement.GetProperty("used").EnumerateArray())
		{
			if (row.GetProperty("name").GetString() == room) return row.Clone();
		}

		return null;
	}

	[Test]
	public async Task TypedPosesAreCountedOncePerLine()
	{
		var (ann, room) = await Player("ActAnn");
		await God($"@role/assign #{ann.DbRef.Number}=approved");

		await As(ann, "pose waves.");
		await As(ann, "say Hello.");
		await As(ann, "@emit The lamps flicker.");
		await As(ann, "think Not a pose.");
		// Refused before it runs: an unknown switch says nothing, so it is no pose.
		await As(ann, "say/bogus Not said.");

		var row = (await RoomRow(room)).GetValueOrDefault();
		await Assert.That(row.ValueKind).IsEqualTo(JsonValueKind.Object).Because("the room must have had poses");
		await Assert.That(row.GetProperty("poses").GetInt64()).IsEqualTo(3);
		await Assert.That(row.GetProperty("people").GetInt32()).IsEqualTo(1);
	}

	[Test]
	public async Task ASceneCountsOnceWhenItFirstGoesActive()
	{
		var (eve, room) = await Player("ActEve");
		await As(eve, "pose sets the stage.");
		var where = $"#{(await Mediator.Send(new GetLocationQuery(eve.DbRef))).Expect<AnySharpContainer>().Object().DBRef.Number}";

		var made = await God($"think scenecreate({where},#1,{TestIsolationHelpers.GenerateUniqueName("Act")})");
		await God($"@scene/set {made}/status=active");
		await God($"@scene/set {made}/status=paused");
		await God($"@scene/set {made}/status=active");
		var planned = await God($"think scenecreate({where},#1,{TestIsolationHelpers.GenerateUniqueName("Act")})");
		await God($"@scene/set {planned}/status=scheduled");
		await God($"think sceneset({planned},status,active)");

		var row = (await RoomRow(room)).GetValueOrDefault();
		await Assert.That(row.ValueKind).IsEqualTo(JsonValueKind.Object).Because("the room must have had a pose");
		await Assert.That(row.GetProperty("scenes").GetInt64()).IsEqualTo(2);
	}

	[Test]
	public async Task ActivityNeedsPlayersView()
	{
		var (bo, _) = await Player("ActBo");

		await Assert.That(await As(bo, "think activity(summary)")).StartsWith("#-1");
		await Assert.That(await God("think activity(nosuch)")).IsEqualTo("#-1 NO SUCH REPORT");
		await Assert.That(await God("""think activity(summary,{{"days":0}})""")).IsEqualTo("#-1 DAYS MUST BE FROM 1 TO 366");
		await Assert.That(await God("""think activity(summary,{{"weeks":2}})""")).IsEqualTo("#-1 UNKNOWN OPTION WEEKS");

		using var summary = JsonDocument.Parse(await God("""think activity(summary,{{"days":7,"zone":"Europe/London"}})"""));
		await Assert.That(summary.RootElement.GetProperty("days").GetInt32()).IsEqualTo(7);
		await Assert.That(summary.RootElement.GetProperty("zone").GetString()).IsEqualTo("Europe/London");
	}

	[Test]
	public async Task HealthDrawsTheReportsForStaffOnly()
	{
		try
		{
			await ApplyAsync("health");
			var (cy, room) = await Player("HealthCy");
			await God($"@role/assign #{cy.DbRef.Number}=approved");
			await As(cy, "pose looks around.");

			await Assert.That(await As(cy, "+health")).Contains("+health needs the players.view permission.");

			var (staff, _) = await Player("HealthStaff");
			await God($"@role/assign #{staff.DbRef.Number}=royalty");

			var summary = await As(staff, "+health");
			await Assert.That(summary).Contains("Health").And.Contains("Approved").And.Contains("Busiest hour")
				.And.Contains("+health /cohorts /drifting /rooms /when");
			await Assert.That(await As(staff, "+health/rooms 7")).Contains(room).And.Contains("Last 7 days");
			await Assert.That(await As(staff, "+health/when")).Contains("When people play").And.Contains("Mon ");
			await Assert.That(await As(staff, "+health/cohorts")).Contains("Month").And.Contains("180d");
			await Assert.That(await As(staff, "+health/drifting")).Contains("Drifting away");
			await Assert.That(await As(staff, "+health/bogus")).Contains("+health has no /bogus switch.");
			await Assert.That(await As(staff, "+health 400")).Contains("Give a number of days from 1 to 366");
		}
		finally
		{
			await Installer.UninstallAsync("health", force: true, CancellationToken.None);
		}
	}

	[Test]
	public async Task HealthJobsShowsStaffLoad()
	{
		try
		{
			await ApplyAsync("jobs");
			await ApplyAsync("health");
			await ApplyAsync("health-jobs");
			var (dee, _) = await Player("LoadDee");
			var title = TestIsolationHelpers.GenerateUniqueName("Lamp");
			await Assert.That(await As(dee, $"+request {title}=The lamp in my room is out.")).Contains("Filed job");

			var (staff, _) = await Player("LoadStaff");
			await God($"@role/assign #{staff.DbRef.Number}=royalty");

			await Assert.That(await As(staff, "+health")).Contains("Staff load").And.Contains("Waiting on staff");
			var load = await As(staff, "+health/staff");
			await Assert.That(load).Contains("Staff load").And.Contains("Nobody");
			await Assert.That(await As(staff, "+health/bogus")).Contains("/staff");
		}
		finally
		{
			await Installer.UninstallAsync("health-jobs", force: true, CancellationToken.None);
			await Installer.UninstallAsync("health", force: true, CancellationToken.None);
			await Installer.UninstallAsync("jobs", force: true, CancellationToken.None);
		}
	}
}
