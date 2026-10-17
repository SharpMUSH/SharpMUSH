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
/// The bundled finger package, installed from the catalogue and driven with typed commands: +finger and
/// +oocfinger drawn from the FIELD` table (+config finger/fields) on the Finger object, values kept in each player's PROFILE`&lt;key&gt;,
/// hidden per field with the visual flag. Each test installs the package and removes it again. Not in
/// parallel: +finger is global.
/// </summary>
[NotInParallel]
public class FingerIntegrationTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private TestHelpers.NotificationRecorder Notifications => WebAppFactoryArg.Notifications;
	private IPackageInstallService Installer => WebAppFactoryArg.Services.GetRequiredService<IPackageInstallService>();

	private string Finger { get; set; } = string.Empty;

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
			WebAppFactoryArg.Services.GetRequiredService<SharpMUSH.Server.Services.PackageManifestResolver>(),
			WebAppFactoryArg.Services.GetRequiredService<IAuditLog>(),
			WebAppFactoryArg.Services.GetRequiredService<SharpMUSH.Library.Models.Packages.EngineVersion>());
		var applied = await controller.Apply(
			new ApplyRequest(BundledPackages.RemoteName, "finger", null, null, null), CancellationToken.None);
		await Assert.That(applied.Result).IsTypeOf<OkObjectResult>().Because("finger must install from the catalogue");
		// AINSTALL is queued after the apply: it adds the commands.
		await WebAppFactoryArg.QueueBarrierAsync();
		var objects = await WebAppFactoryArg.Services.GetRequiredService<IPackageRegistryService>().GetPackageObjectsAsync("finger");
		Finger = objects.First(o => o.Ref == "finger").Objid;
	}

	private async Task UninstallAsync() => await Installer.UninstallAsync("finger", force: true, CancellationToken.None);

	[Test]
	public async Task FieldsAreSetShownAndCleared()
	{
		try
		{
			await InstallAsync();
			var ann = await Player("FgAnn");
			var bo = await Player("FgBo");
			var name = TestIsolationHelpers.GenerateUniqueName("FgName");

			// Your own profile shows every field, - where you set nothing.
			var own = await As(ann, "+finger");
			await Assert.That(own).Contains(ann.Name).And.Contains("Full Name: -").And.Contains("+finger/set <field>=<text>");

			// A field is named by its key or its label; what a player types is never evaluated.
			await Assert.That(await As(ann, $"+finger/set full name={name}")).Contains($"[FINGER] Done: Your Full Name: {name}");
			await Assert.That(await As(ann, "+finger/set age=[add(1,2)] 50%")).Contains("Your Age: [add(1,2)] 50%");
			await Assert.That(await God($"think get(#{ann.DbRef.Number}/PROFILE`FULLNAME)")).IsEqualTo(name);
			await Assert.That(await As(ann, "+oocfinger/set timezone=US Eastern")).Contains("Your Time zone: US Eastern");

			var seen = await As(bo, $"+finger {ann.Name}");
			await Assert.That(seen).Contains($"Full Name: {name}").And.Contains("[add(1,2)] 50%")
				.And.DoesNotContain("Species").And.Contains($"+oocfinger {ann.Name}");
			await Assert.That(await As(bo, $"+oocfinger {ann.Name}")).Contains("US Eastern").And.Contains($"{ann.Name} · OOC")
				.And.Contains("Last on:");

			// A list separator a player stores with softcode is taken off before the card splits its fields on one.
			await As(ann, "think attrib_set(me/PROFILE`FULLNAME,Fg%*<|>Split)");
			await Assert.That(await God($"think strlen(get(#{ann.DbRef.Number}/PROFILE`FULLNAME))")).IsEqualTo("8");
			await Assert.That(await As(bo, $"+finger {ann.Name}")).Contains("Full Name: Fg|Split");
			await As(ann, $"+finger/set full name={name}");

			await Assert.That(await As(ann, "+finger/set age=")).Contains("Your Age is cleared.");
			await Assert.That(await As(ann, "+finger/set age=")).Contains("Your Age is not set.");
			await Assert.That(await As(bo, $"+finger {ann.Name}")).DoesNotContain("Age");

			await Assert.That(await As(ann, "+finger/set alias=x")).Contains("Alias is worked out by the game: nobody sets it.");
			await Assert.That(await As(ann, "+finger/set bogus=x")).Contains("There is no field called 'bogus'.");
			await Assert.That(await As(ann, "+finger/set age")).Contains("+finger/set <field>=<text> sets a field.");
			await Assert.That(await As(ann, $"+finger/set age={new string('x', 1001)}")).Contains("Keep Age to 1000 characters.");
			await Assert.That(await As(ann, "+finger nobody-by-that-name")).Contains("There is no player called 'nobody-by-that-name'.");
			await Assert.That(await As(ann, "+finger/bogus")).Contains("+finger has no /bogus switch. +help finger lists them.");

			// Someone with nothing set on a view says so.
			await Assert.That(await As(ann, $"+finger {bo.Name}")).Contains($"{bo.Name} has nothing on their +finger.");
		}
		finally
		{
			await UninstallAsync();
		}
	}

	[Test]
	public async Task AHiddenFieldIsSeenOnlyByItsPlayerAndStaff()
	{
		try
		{
			await InstallAsync();
			var ann = await Player("HdAnn");
			var bo = await Player("HdBo");
			var staff = await Player("HdStaff");
			await God($"@permission/allow #{staff.DbRef.Number}=softcode.finger.edit");

			await As(ann, "+finger/set age=32");
			await Assert.That(await As(ann, "+finger/hide age")).Contains("Only you and staff see your Age now.");
			await Assert.That(await As(bo, $"+finger {ann.Name}")).DoesNotContain("32");
			await Assert.That(await As(ann, "+finger")).Contains("32 (hidden)");
			await Assert.That(await As(staff, $"+finger {ann.Name}")).Contains("32 (hidden)");

			// A hidden field stays hidden when it changes.
			await As(ann, "+finger/set age=33");
			await Assert.That(await As(bo, $"+finger {ann.Name}")).DoesNotContain("33");
			await Assert.That(await As(ann, "+finger/fields")).Contains("set · hidden");

			await Assert.That(await As(ann, "+finger/unhide age")).Contains("Everyone sees your Age again.");
			await Assert.That(await As(bo, $"+finger {ann.Name}")).Contains("Age:").And.Contains("33");
		}
		finally
		{
			await UninstallAsync();
		}
	}

	[Test]
	public async Task StaffSetAnyoneAndTheFieldsOnlyTheySet()
	{
		try
		{
			await InstallAsync();
			var ann = await Player("StAnn");
			var staff = await Player("StStaff");
			await God($"@permission/allow #{staff.DbRef.Number}=softcode.finger.edit");

			await Assert.That(await As(ann, $"+finger/set {staff.Name}/age=5")).Contains("Only staff change someone else's fields.");
			await Assert.That(await As(staff, $"+finger/set {ann.Name}/occupation=Tinker")).Contains($"{ann.Name}'s Occupation: Tinker");
			await Assert.That(string.Join("\n", Notifications.For(ann.DbRef))).Contains($"{staff.Name} changed your Occupation.");

			// The fields only staff set are a +config list, checked against the field list.
			await God($"@permission/allow #{staff.DbRef.Number}=config.admin");
			await Assert.That(await As(staff, "+config/add finger/locked=occupation bogus")).Contains("bogus is not a field.");
			await Assert.That(await As(staff, "+config/add finger/locked=occupation")).Contains("finger/locked is now occupation, was none.");
			try
			{
				await Assert.That(await God($"think get({Finger}/DATA`CONFIG`LOCKED)")).IsEqualTo("occupation");
				await Assert.That(await As(ann, "+finger/set occupation=Pirate")).Contains("Only staff set Occupation.");
				await Assert.That(await As(ann, "+finger/fields")).Contains("set");
			}
			finally
			{
				await As(staff, "+config/reset finger/locked");
			}
		}
		finally
		{
			await UninstallAsync();
		}
	}

	[Test]
	public async Task StaffAddAFieldWithConfig()
	{
		try
		{
			await InstallAsync();
			var ann = await Player("AdAnn");
			var bo = await Player("AdBo");
			var staff = await Player("AdStaff");
			await God($"@permission/allow #{staff.DbRef.Number}=config.admin");

			// The fields are a +config table: a new one is +finger's, and its order puts it after Age.
			await As(staff, "+config/add finger/fields/height=Height");
			await Assert.That(await God($"think get({Finger}/FIELD`HEIGHT`VIEW)")).IsEqualTo("IC");
			await As(staff, "+config finger/fields/height/order=35");
			await Assert.That(await God($"think iter(u({Finger}/FUN`FIELD`IN,IC),last(%i0,`))"))
				.IsEqualTo("FULLNAME ALIAS AGE HEIGHT SPECIES OCCUPATION QUOTE");
			await Assert.That(await As(ann, "+finger/set height=Tall")).Contains("Your Height: Tall");
			await Assert.That(await As(bo, $"+finger {ann.Name}")).Contains("Height: Tall");

			// A worked-out field is a SHOW`<key> beside it, evaluated with the player and the reader.
			await As(staff, "+config/add finger/fields/reader=Read by");
			await As(staff, "+config finger/fields/reader/view=ooc");
			await God($"&SHOW`READER {Finger}=name(%<viewer>)");
			await Assert.That(await As(bo, $"+oocfinger {ann.Name}")).Contains($"Read by: {bo.Name}");
			await Assert.That(await As(ann, "+finger/set reader=x")).Contains("Read by is worked out by the game");

			await As(staff, "+config/remove finger/fields/height");
			await Assert.That(await As(ann, "+finger/set height=Short")).Contains("There is no field called 'height'.");
		}
		finally
		{
			await UninstallAsync();
		}
	}
}
