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
/// The bundled alts package, installed from the catalogue and driven with typed commands: +alts lists the
/// characters on your account, private until the account makes the list public, with any one kept off it, and
/// staff with players.view see every one. Each test installs the package and removes it again. Not in parallel:
/// +alts is global.
/// </summary>
[NotInParallel]
public class AltsIntegrationTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private TestHelpers.NotificationRecorder Notifications => WebAppFactoryArg.Notifications;
	private IPackageInstallService Installer => WebAppFactoryArg.Services.GetRequiredService<IPackageInstallService>();
	private IAccountService Accounts => WebAppFactoryArg.Services.GetRequiredService<IAccountService>();

	private string Store { get; set; } = string.Empty;

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

	/// <summary>An account holding <paramref name="players"/>.</summary>
	private async Task<SharpAccount> Account(params TestIsolationHelpers.TestPlayer[] players)
	{
		var username = TestIsolationHelpers.GenerateUniqueName("alts").ToLowerInvariant();
		var account = (await Accounts.CreateAccountAsync(username, null, "correct horse battery")).Expect<SharpAccount>();
		foreach (var player in players)
			await Accounts.AttachCharacterAsync(account.Id!,
				(await Mediator.Send(new GetObjectNodeQuery(player.DbRef))).Expect<AnySharpObject>().Expect<SharpPlayer>());
		return account;
	}

	private static string Key(SharpAccount account) => account.Id!.Replace('/', '_').ToUpperInvariant();

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
			new ApplyRequest(BundledPackages.RemoteName, "alts", null, null, null), CancellationToken.None);
		await Assert.That(applied.Result).IsTypeOf<OkObjectResult>().Because("alts must install from the catalogue");
		// AINSTALL is queued after the apply: it adds the command.
		await WebAppFactoryArg.QueueBarrierAsync();
		var objects = await WebAppFactoryArg.Services.GetRequiredService<IPackageRegistryService>().GetPackageObjectsAsync("alts");
		Store = objects.First(o => o.Ref == "store").Objid;
	}

	private async Task UninstallAsync() => await Installer.UninstallAsync("alts", force: true, CancellationToken.None);

	[Test]
	public async Task AListIsPrivateUntilTheAccountMakesItPublic()
	{
		try
		{
			await InstallAsync();
			var ann = await Player("AlAnn");
			var annAlt = await Player("AlAnnAlt");
			var bo = await Player("AlBo");
			await Account(ann, annAlt);

			// Your own list is always yours, every character on it, and says it is private.
			var own = await As(ann, "+alts");
			await Assert.That(own).Contains("Your alts").And.Contains(ann.Name).And.Contains(annAlt.Name)
				.And.Contains("online").And.Contains("Your list is private");

			// Anyone else hears only that it is private, never how many there are.
			var refused = await As(bo, $"+alts {ann.Name}");
			await Assert.That(refused).Contains($"[ALTS] {ann.Name}'s alts are private.").And.DoesNotContain(annAlt.Name);

			// Made public from one character, it holds for the whole account.
			await Assert.That(await As(annAlt, "+alts/public on")).Contains("Your alts are public");
			await Assert.That(await As(ann, "+alts/public")).Contains("Your list is public.");
			var listed = await As(bo, $"+alts {ann.Name}");
			await Assert.That(listed).Contains($"Alts of {ann.Name}").And.Contains(annAlt.Name).And.Contains("online");
			await Assert.That(await As(bo, $"+alts {annAlt.Name}")).Contains(ann.Name);

			await Assert.That(await As(ann, "+alts/public off")).Contains("Your alts are private");
			await Assert.That(await As(bo, $"+alts {ann.Name}")).Contains($"{ann.Name}'s alts are private.").And.DoesNotContain(annAlt.Name);

			await Assert.That(await As(ann, "+alts/public maybe")).Contains("+alts/public on lets anyone list your alts");
			await Assert.That(await As(ann, "+alts/bogus")).Contains("+alts has no /bogus switch.");
		}
		finally
		{
			await UninstallAsync();
		}
	}

	[Test]
	public async Task AHiddenAltIsLeftOffThePublicListAndAnsweredAsPrivate()
	{
		try
		{
			await InstallAsync();
			var ann = await Player("HdAnn");
			var annAlt = await Player("HdAnnAlt");
			var annThird = await Player("HdAnnThird");
			var bo = await Player("HdBo");
			await Account(ann, annAlt, annThird);
			await As(ann, "+alts/public on");

			await Assert.That(await As(ann, $"+alts/hide {annAlt.Name}")).Contains($"{annAlt.Name} is off your public list.");
			await Assert.That(await As(ann, $"+alts/hide {annAlt.Name}")).Contains("is already off your public list.");
			await Assert.That(await As(ann, "+alts")).Contains(annAlt.Name).And.Contains("hidden");

			var listed = await As(bo, $"+alts {ann.Name}");
			await Assert.That(listed).Contains(annThird.Name).And.DoesNotContain(annAlt.Name);
			// Asked about the hidden one itself, the answer is the private one: it is never linked to the others.
			var hidden = await As(bo, $"+alts {annAlt.Name}");
			await Assert.That(hidden).Contains($"{annAlt.Name}'s alts are private.").And.DoesNotContain(ann.Name);

			// Only a character on your own account can be hidden.
			await Assert.That(await As(ann, $"+alts/hide {bo.Name}")).Contains($"{bo.Name} is not on your account.");

			await Assert.That(await As(ann, $"+alts/unhide {annAlt.Name}")).Contains($"{annAlt.Name} is back on your public list.");
			await Assert.That(await As(bo, $"+alts {ann.Name}")).Contains(annAlt.Name);
		}
		finally
		{
			await UninstallAsync();
		}
	}

	[Test]
	public async Task StaffSeeEveryAltAndAnUnlistedPlayerIsRefused()
	{
		try
		{
			await InstallAsync();
			var ann = await Player("StAnn");
			var annAlt = await Player("StAnnAlt");
			var staff = await Player("StStaff");
			var loner = await Player("StLoner");
			var bo = await Player("StBo");
			var account = await Account(ann, annAlt);
			await God($"@permission/allow #{staff.DbRef.Number}=players.view");
			await As(ann, $"+alts/hide {annAlt.Name}");

			// Private, and one hidden: staff still see both, the hidden one marked, and the account's name.
			var seen = await As(staff, $"+alts {ann.Name}");
			await Assert.That(seen).Contains(annAlt.Name).And.Contains("hidden").And.Contains(account.Username);
			await Assert.That(await As(staff, $"+alts {loner.Name}")).Contains($"{loner.Name} is on no account.");

			// A character on no account answers anyone else exactly as a private list does.
			await Assert.That(await As(bo, $"+alts {loner.Name}")).Contains($"{loner.Name}'s alts are private.");
			await Assert.That(await As(loner, "+alts")).Contains("This character is on no account");
			await Assert.That(await As(bo, "+alts nobody-by-that-name")).Contains("There is no player called 'nobody-by-that-name'.");
			// What a player types is never evaluated.
			await Assert.That(await As(bo, "+alts [add(1,2)]")).Contains("There is no player called '[add(1,2)]'.");
		}
		finally
		{
			await UninstallAsync();
		}
	}

	[Test]
	public async Task AnAltThatLeavesTheAccountIsForgotten()
	{
		try
		{
			await InstallAsync();
			var ann = await Player("UlAnn");
			var annAlt = await Player("UlAnnAlt");
			var account = await Account(ann, annAlt);
			var key = Key(account);
			var altKey = (await God($"think objid(#{annAlt.DbRef.Number})")).Replace("#", string.Empty).Replace(':', '_');

			await As(ann, $"+alts/hide {annAlt.Name}");
			await Assert.That(await God($"think hasattr({Store},ACCOUNT`{key}`HIDDEN`{altKey})")).IsEqualTo("1");

			await Accounts.UnlinkCharacterAsync(account.Id!, annAlt.DbRef);
			// PLAYER`UNLINK is queued, and its attribute on the event handler queues the Alts object's own.
			await WebAppFactoryArg.QueueBarrierAsync();
			await WebAppFactoryArg.QueueBarrierAsync();

			await Assert.That(await God($"think hasattr({Store},ACCOUNT`{key}`HIDDEN`{altKey})")).IsEqualTo("0");
			// Nothing else was kept for the account, so its branch is gone too.
			await Assert.That(await God($"think hasattr({Store},ACCOUNT`{key})")).IsEqualTo("0");
			await Assert.That(await As(ann, "+alts")).DoesNotContain(annAlt.Name);
		}
		finally
		{
			await UninstallAsync();
		}
	}
}
