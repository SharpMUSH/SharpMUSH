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
/// The bundled census package, installed from the catalogue and driven with typed commands: staff add a
/// dimension with +config, and +census lists and counts the characters by it, joins filters and breakdowns,
/// keeps staff-only dimensions from players, starts from the scope setting and counts in parts. The world is
/// shared, so every query is narrowed to an attribute and values only these tests set. Not in parallel: the
/// dimensions table and the scope are the package's.
/// </summary>
[NotInParallel]
public class CensusIntegrationTests
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
			WebAppFactoryArg.Services.GetRequiredService<SharpMUSH.Library.Models.Packages.EngineVersion>(),
			WebAppFactoryArg.Services.GetRequiredService<SharpMUSH.Library.Services.Interfaces.IBundledPackageBootstrap>());
		var applied = await controller.Apply(
			new ApplyRequest(BundledPackages.RemoteName, "census", null, null, null), CancellationToken.None);
		await Assert.That(applied.Result).IsTypeOf<OkObjectResult>().Because("census must install from the catalogue");
		await WebAppFactoryArg.QueueBarrierAsync();
	}

	private async Task UninstallAsync() => await WebAppFactoryArg.Services.UninstallWithBridgesAsync("census");

	/// <summary>
	/// Three characters with a theme in an attribute only this test sets, and a manager who adds the theme
	/// dimension. Returns the players and the two theme values.
	/// </summary>
	private async Task<(TestIsolationHelpers.TestPlayer Manager, TestIsolationHelpers.TestPlayer Viewer,
		TestIsolationHelpers.TestPlayer[] Cast, string Marvel, string Wars)> CastAsync(string prefix)
	{
		var manager = await Player($"{prefix}Mgr");
		var viewer = await Player($"{prefix}View");
		var cast = new[] { await Player($"{prefix}A"), await Player($"{prefix}B"), await Player($"{prefix}C") };
		var attribute = TestIsolationHelpers.GenerateUniqueName("CENSUS").ToUpperInvariant();
		var marvel = TestIsolationHelpers.GenerateUniqueName("Marvel");
		var wars = TestIsolationHelpers.GenerateUniqueName("Wars");
		await God($"@permission/allow #{manager.DbRef.Number}=config.admin");
		await God($"&{attribute} #{cast[0].DbRef.Number}={marvel}");
		await God($"&{attribute} #{cast[1].DbRef.Number}={marvel.ToLowerInvariant()}");
		await God($"&{attribute} #{cast[2].DbRef.Number}={wars}");
		await God($"@sex #{cast[0].DbRef.Number}=Female");
		await God($"@sex #{cast[1].DbRef.Number}=Male");
		await God($"@sex #{cast[2].DbRef.Number}=Female");

		await Assert.That(await As(manager, "+config/add census/dimensions/theme=Theme")).Contains("Added census/dimensions/theme");
		await Assert.That(await As(manager, $"+config census/dimensions/theme/path={attribute}")).Contains("path is now");
		await Assert.That(await As(manager, "+config census/dimensions/theme/kind=bogus")).Contains("There is no kind called 'bogus'");
		return (manager, viewer, cast, marvel, wars);
	}

	[Test]
	public async Task FiltersListAndDimensionsCount()
	{
		try
		{
			await InstallAsync();
			var (manager, viewer, cast, marvel, wars) = await CastAsync("Cen");

			// A new dimension is staff only until its access is everyone.
			await Assert.That(await As(viewer, $"+census theme={marvel}")).Contains("There is no census dimension called 'theme'.");
			await As(manager, "+config census/dimensions/theme/access=everyone");
			await Assert.That(RowOf(await As(viewer, "+census"), "theme")).Contains("Theme");

			// Filters alone list who matches, without case, with a column per dimension filtered by.
			var screen = await As(viewer, $"+census theme={marvel}");
			await Assert.That(RowOf(screen, cast[0].Name)).Contains(marvel);
			await Assert.That(RowOf(screen, cast[1].Name)).Contains(marvel.ToLowerInvariant());
			await Assert.That(screen).DoesNotContain(cast[2].Name).And.Contains("2 characters.").And.Contains($"Filtered by theme={marvel}.");

			// Filters join with /, and every one must match; a wildcard matches both themes.
			var both = marvel.Split('_')[1];
			screen = await As(viewer, $"+census theme=*{both}*/gender=female");
			await Assert.That(screen).Contains(cast[0].Name).And.Contains(cast[2].Name).And.DoesNotContain(cast[1].Name);
			await Assert.That(RowOf(screen, cast[0].Name)).Contains("Female");

			// A dimension counts: a value written two ways is counted once, as first written, with its share.
			screen = await As(viewer, $"+census theme=*{both}*/theme");
			await Assert.That(RowOf(screen, marvel)).Contains(" 2 ").And.Contains("67%");
			await Assert.That(RowOf(screen, wars)).Contains(" 1 ").And.Contains("33%");
			await Assert.That(screen.IndexOf(marvel, StringComparison.Ordinal)).IsLessThan(screen.IndexOf(wars, StringComparison.Ordinal))
				.Because("the value most hold comes first");

			// Two dimensions count each pair, in the order typed.
			screen = await As(viewer, $"+census theme=*{both}*/theme/gender");
			await Assert.That(RowOf(screen, marvel)).Contains("Female");
			await Assert.That(RowOf(screen, wars)).Contains("Female");
			await Assert.That(screen).Contains("3 characters in 3 groups.");

			// Connected characters were last on now.
			await Assert.That(RowOf(await As(viewer, $"+census theme={wars}/offline<1h"), cast[2].Name)).Contains("now");

			// Mistakes are named.
			await Assert.That(await As(viewer, "+census gender>3")).Contains("gender counts words, so it takes only = or !=.");
			await Assert.That(await As(viewer, "+census offline>soon")).Contains("offline counts time");
			await Assert.That(await As(viewer, "+census a/b/c")).Contains("There is no census dimension called 'a'.");
			await Assert.That(await As(viewer, "+census/sideways")).Contains("+census has no /sideways switch.");
			await Assert.That(await As(viewer, $"+census/5 theme={marvel}")).Contains("There is one page.");
			await Assert.That(await As(viewer, "+help census")).Contains("+census counts the game's characters");
		}
		finally
		{
			await UninstallAsync();
		}
	}

	[Test]
	public async Task StaffDimensionsScopeAndParts()
	{
		try
		{
			await InstallAsync();
			var (manager, viewer, cast, marvel, wars) = await CastAsync("Cns");
			var wizard = await Player("CnsWiz");
			await God($"@role/assign #{wizard.DbRef.Number}=wizard");

			// Staff see staff-only dimensions and count every character; players do not.
			await Assert.That(await As(viewer, $"+census theme={marvel}/alts")).Contains("There is no census dimension called 'theme'.");
			await Assert.That(RowOf(await As(wizard, $"+census theme={marvel}/alts"), " 1 ")).Contains("100%");
			await Assert.That(await As(viewer, "+census/all")).Contains("Only staff count every character with +census/all.");

			// The scope narrows every census, and its footer says so; /all ignores it.
			await Assert.That(await As(manager, "+config census/scope=gender")).Contains("The scope can only hold filters");
			await Assert.That(await As(manager, "+config census/scope=gender=female")).Contains("scope is now gender=female");
			var screen = await As(wizard, $"+census theme={marvel}");
			await Assert.That(screen).Contains(cast[0].Name).And.DoesNotContain(cast[1].Name).And.Contains("Counting only gender=female.");
			screen = await As(wizard, $"+census/all theme={marvel}");
			await Assert.That(screen).Contains(cast[1].Name).And.Contains("Counting every character.");
			await As(manager, "+config census/scope=");

			// Counted in parts of one character, each its own queue entry, the answer is the same and the parts
			// are deleted once drawn.
			var census = await PackageObject("census");
			await Assert.That(await As(wizard, $"&DATA`PART`BUDGET {census}=60")).Contains("Set.");
			screen = await As(wizard, $"+census/all theme=*{marvel.Split('_')[1]}*/theme");
			await Assert.That(RowOf(screen, marvel)).Contains(" 2 ");
			await Assert.That(RowOf(screen, wars)).Contains(" 1 ");
			await Assert.That((await As(wizard, $"think nattr({await PackageObject("store")}/RUN`**)")).Trim()).IsEqualTo("0");
			await Assert.That(await As(wizard, $"+census theme={wars}")).Contains(cast[2].Name);
		}
		finally
		{
			await UninstallAsync();
		}
	}

	/// <summary>The objid of the census object the package calls <paramref name="reference"/>.</summary>
	private async Task<string> PackageObject(string reference)
		=> (await Registry.GetPackageObjectsAsync("census")).Single(o => o.Ref == reference).Objid;
}
