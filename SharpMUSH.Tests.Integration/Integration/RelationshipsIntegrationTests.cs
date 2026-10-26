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
/// The bundled relationships package, installed from the catalogue (with finger, which its card reads) and driven
/// with typed commands: one player asks, the other confirms, either ends it; a hidden one is seen only by the two
/// of them and staff; a note is private. Each test removes what it installed. Not in parallel: +rel is global.
/// </summary>
[NotInParallel]
public class RelationshipsIntegrationTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private TestHelpers.NotificationRecorder Notifications => WebAppFactoryArg.Notifications;
	private IPackageInstallService Installer => WebAppFactoryArg.Services.GetRequiredService<IPackageInstallService>();
	private IPackageRegistryService Registry => WebAppFactoryArg.Services.GetRequiredService<IPackageRegistryService>();

	/// <summary>The packages this test's install added, the last first, removed again afterwards in that order.</summary>
	private List<string> _added = [];

	private string Relationships { get; set; } = string.Empty;

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

	/// <summary>What <paramref name="player"/> was told since <paramref name="before"/>.</summary>
	private string Heard(TestIsolationHelpers.TestPlayer player, int before) =>
		string.Join("\n", Notifications.For(player.DbRef).Skip(before));

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
		var before = (await Registry.GetInstalledPackagesAsync()).Select(p => p.Id).ToHashSet(StringComparer.Ordinal);
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
		// The card reads finger's fields, so finger comes first when the game does not have it.
		foreach (var id in new[] { "finger", "relationships" }.Where(id => !before.Contains(id)))
		{
			var applied = await controller.Apply(new ApplyRequest(BundledPackages.RemoteName, id, null, null, null), CancellationToken.None);
			await Assert.That(applied.Result).IsTypeOf<OkObjectResult>().Because($"{id} must install from the catalogue");
			_added.Insert(0, id);
		}

		// AINSTALL is queued after the apply: it adds the commands.
		await WebAppFactoryArg.QueueBarrierAsync();
		var objects = await Registry.GetPackageObjectsAsync("relationships");
		Relationships = objects.First(o => o.Ref == "relationships").Objid;
	}

	private async Task UninstallAsync()
	{
		foreach (var id in _added)
		{
			await WebAppFactoryArg.Services.UninstallWithBridgesAsync(id);
		}

		_added = [];
	}

	[Test]
	public async Task OneAsksTheOtherConfirmsAndEitherEndsIt()
	{
		try
		{
			await InstallAsync();
			var ann = await Player("RlAnn");
			var bo = await Player("RlBo");

			var told = Notifications.CountFor(bo.DbRef);
			await Assert.That(await As(ann, $"+rel/set {bo.Name}=parent")).Contains($"You asked {bo.Name}. They show as your Parent once they confirm it.");
			await Assert.That(Heard(bo, told)).Contains($"{ann.Name} asked to be your Child.");
			await Assert.That(await As(bo, "+rel")).Contains("Asking you").And.Contains($"{ann.Name}: Child");

			// Confirming takes the pair of the type: Bo is Ann's Parent, so Ann is Bo's Child.
			await Assert.That(await As(bo, $"+rel/confirm {ann.Name}")).Contains("Child");
			await Assert.That(await As(ann, $"+rel {bo.Name}")).Contains("With you:").And.Contains("Parent");
			await Assert.That(await As(bo, $"+rel {ann.Name}")).Contains("With you:").And.Contains("Child");

			// What a player types is kept as typed.
			await Assert.That(await As(ann, $"+rel/text {bo.Name}=[add(1,2)] 50%")).Contains("[add(1,2)] 50%");

			told = Notifications.CountFor(ann.DbRef);
			await Assert.That(await As(bo, $"+rel/end {ann.Name}")).Contains($"You and {ann.Name} have no relationship now.");
			await Assert.That(Heard(ann, told)).Contains($"{bo.Name} ended your relationship.");
			await Assert.That(await God($"think lattr(#{ann.DbRef.Number}/RELATION`*)")).IsEqualTo(string.Empty);
			await Assert.That(await As(ann, $"+rel/end {bo.Name}")).Contains($"There is nothing between you and {bo.Name}.");
		}
		finally
		{
			await UninstallAsync();
		}
	}

	[Test]
	public async Task AnAskCanBeDeclinedAndNobodyConfirmsWhatWasNotAsked()
	{
		try
		{
			await InstallAsync();
			var ann = await Player("DcAnn");
			var bo = await Player("DcBo");

			await Assert.That(await As(bo, $"+rel/confirm {ann.Name}")).Contains($"{ann.Name} has not asked you anything.");
			await As(ann, $"+rel/set {bo.Name}=rival");
			await As(bo, $"+rel/decline {ann.Name}");
			await Assert.That(await God($"think lattr(#{ann.DbRef.Number}/RELATION`*)")).IsEqualTo(string.Empty);
			await Assert.That(await As(ann, $"+rel/set {ann.Name}=friend")).Contains("That is you.");
			await Assert.That(await As(ann, "+rel/bogus")).Contains("/bogus");
		}
		finally
		{
			await UninstallAsync();
		}
	}

	[Test]
	public async Task AHiddenRelationshipIsSeenOnlyByTheTwoAndStaff()
	{
		try
		{
			await InstallAsync();
			var ann = await Player("HrAnn");
			var bo = await Player("HrBo");
			var cy = await Player("HrCy");
			var staff = await Player("HrStaff");
			await God($"@permission/allow #{staff.DbRef.Number}=softcode.relationships.see");

			await As(ann, $"+rel/set {bo.Name}=friend");
			await As(bo, $"+rel/confirm {ann.Name}");
			await Assert.That(await As(cy, $"+rel {ann.Name}")).Contains(bo.Name);

			await As(bo, $"+rel/hide {ann.Name}");
			await Assert.That(await As(cy, $"+rel {ann.Name}")).DoesNotContain(bo.Name);
			await Assert.That(await As(ann, $"+rel {ann.Name}")).Contains(bo.Name).And.Contains("(hidden)");
			await Assert.That(await As(staff, $"+rel {ann.Name}")).Contains(bo.Name).And.Contains("(hidden)");

			await As(bo, $"+rel/unhide {ann.Name}");
			await Assert.That(await As(cy, $"+rel {ann.Name}")).Contains(bo.Name);
		}
		finally
		{
			await UninstallAsync();
		}
	}

	[Test]
	public async Task ANoteIsPrivate()
	{
		try
		{
			await InstallAsync();
			var ann = await Player("NtAnn");
			var bo = await Player("NtBo");
			var note = TestIsolationHelpers.GenerateUniqueName("Note");

			await Assert.That(await As(ann, $"+rel/note {bo.Name}={note}")).Contains(note);
			await Assert.That(await As(ann, $"+rel {bo.Name}")).Contains($"Your note: {note}");
			await Assert.That(await As(bo, $"+rel {ann.Name}")).DoesNotContain(note);
			await Assert.That(await As(bo, $"+rel {bo.Name}")).DoesNotContain(note);
			await Assert.That(await As(ann, $"+rel/note {bo.Name}=")).Contains(bo.Name);
			await Assert.That(await As(ann, $"+rel/note {bo.Name}=")).Contains($"You have no note about {bo.Name}.");
		}
		finally
		{
			await UninstallAsync();
		}
	}

	[Test]
	public async Task TheWebRouteSendsWhatTheViewerMaySee()
	{
		try
		{
			await InstallAsync();
			var ann = await Player("WbAnn");
			var bo = await Player("WbBo");
			await As(ann, $"+rel/set {bo.Name}=sibling");
			await As(bo, $"+rel/confirm {ann.Name}");
			var annObjid = await God($"think objid(#{ann.DbRef.Number})");

			var web = await God($"think uargs({Relationships}/WEB`GRAPH`DATA,viewer,,target,{annObjid})");
			await Assert.That(web).Contains("\"label\":\"Sibling\"").And.Contains($"\"label\":\"{bo.Name}\"").And.Contains("\"focus\":true");

			await As(bo, $"+rel/hide {ann.Name}");
			var hidden = await God($"think uargs({Relationships}/WEB`GRAPH`DATA,viewer,,target,{annObjid})");
			await Assert.That(hidden).DoesNotContain(bo.Name).And.Contains("\"edges\":[]");
		}
		finally
		{
			await UninstallAsync();
		}
	}

	[Test]
	public async Task StaffKeepTheTypesWithConfigAndTheCardShowsFingerFields()
	{
		try
		{
			await InstallAsync();
			var ann = await Player("TyAnn");
			var bo = await Player("TyBo");
			var staff = await Player("TyStaff");
			var name = TestIsolationHelpers.GenerateUniqueName("TyName");
			await God($"@permission/allow #{staff.DbRef.Number}=config.admin");

			// The types are a +config table on the Relationships object, under DATA`TYPE`.
			await As(staff, "+config/add relationships/types/cousin=Cousin");
			await Assert.That(await God($"think get({Relationships}/DATA`TYPE`COUSIN)")).IsEqualTo("Cousin");
			await As(staff, "+config relationships/types/cousin/tone=purple");
			await Assert.That(await God($"think get({Relationships}/DATA`TYPE`COUSIN`TONE)")).IsEqualTo("purple");
			await Assert.That(await As(staff, "+config relationships/types/cousin/pair=nephew")).Contains("There is no type called nephew.");
			await Assert.That(await As(ann, "+rel/types")).Contains("Cousin");
			await Assert.That(await As(ann, $"+rel/set {bo.Name}=cousin")).Contains($"You asked {bo.Name}. They show as your Cousin once they confirm it.");
			await As(staff, "+config/remove relationships/types/cousin");
			await Assert.That(await God($"think hasattr({Relationships},DATA`TYPE`COUSIN)")).IsEqualTo("0");

			// The card's top lines are finger fields, read through finger's own functions.
			await As(ann, $"+finger/set full name={name}");
			await Assert.That(await As(bo, $"+rel {ann.Name}")).Contains($"Full Name: {name}");
		}
		finally
		{
			await UninstallAsync();
		}
	}
}
