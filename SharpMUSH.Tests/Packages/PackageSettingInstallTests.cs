using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Packages;

/// <summary>
/// A package's <c>settings:</c>, planned, installed, upgraded and uninstalled against the real world. Each test
/// sets <c>portal_port</c>, which nothing reads, and they take turns since the option is the world's.
/// </summary>
[NotInParallel(nameof(PackageSettingInstallTests))]
public class PackageSettingInstallTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IServiceProvider Services => WebAppFactoryArg.Services;
	private IPackageInstallService Installer => Services.GetRequiredService<IPackageInstallService>();
	private IConfigOptionWriter Config => Services.GetRequiredService<IConfigOptionWriter>();
	private IPackageRegistryService Registry => (IPackageRegistryService)Services.GetRequiredService<ISharpDatabase>();

	private const string Option = "portal_port";
	private const string Property = nameof(NetOptions.PortalPort);

	private static string FreshId() => "set-" + Guid.NewGuid().ToString("N")[..8];

	private static PackageManifest Manifest(string id, string version, string settings)
		=> new PackageManifestService().ParseManifest($$"""
			format: 1.3
			package: {{id}}
			version: "{{version}}"
			objects:
			  - ref: desk
			    type: thing
			    name: Settings Desk
			{{settings}}
			""").Expect<ParsedPackageManifest>().Manifest;

	private static PackageManifest Port(string id, string version, string port)
		=> Manifest(id, version, $"settings:\n  {Option}: \"{port}\"");

	private Task<Result<PackageApplyResult>> ApplyAsync(PackageManifest manifest)
		=> Installer.ApplyAsync(manifest, new PackageApplyRequest(
			new PackageApplySource("https://github.com/SharpMUSH/SharpMUSH-Packages", $"{manifest.Name}/", "commit-1", "main"),
			new Dictionary<string, string>(), []));

	private async Task SetPortAsync(uint port) => (await Config.SetAsync(Property, port)).Expect<SharpMUSHOptions>();

	private ValueTask<string?> PortAsync() => Config.CurrentTextAsync(Property);

	[Test]
	public async Task InstallSetsTheOptionAndUninstallPutsItBack()
	{
		await SetPortAsync(4100);
		var id = FreshId();

		var plan = (await Installer.PlanAsync(Port(id, "1.0", "4301"))).Settings!.Single();
		await Assert.That(plan).IsEqualTo(new PackageSettingChange(Option, PackageSettingAction.Set, "4100", "4301"));

		(await ApplyAsync(Port(id, "1.0", "4301"))).Expect<PackageApplyResult>();
		await Assert.That(await PortAsync()).IsEqualTo("4301");
		var installed = (await Registry.GetInstalledPackageAsync(id)).Expect<InstalledPackageRecord>();
		await Assert.That(installed.Settings!.Single()).IsEqualTo(new PackageSettingRecord(Option, "4301", "4100"));

		await Assert.That((await Installer.UninstallAsync(id)).Value).IsTypeOf<Success>();
		await Assert.That(await PortAsync()).IsEqualTo("4100");
	}

	[Test]
	public async Task AnUpgradeKeepsAnAdministratorsChangeAndUninstallLeavesIt()
	{
		await SetPortAsync(4100);
		var id = FreshId();
		(await ApplyAsync(Port(id, "1.0", "4302"))).Expect<PackageApplyResult>();
		await SetPortAsync(4999);

		try
		{
			var plan = (await Installer.PlanAsync(Port(id, "1.1", "4302"))).Settings!.Single();
			await Assert.That(plan.Action).IsEqualTo(PackageSettingAction.Keep);
			(await ApplyAsync(Port(id, "1.1", "4302"))).Expect<PackageApplyResult>();
			await Assert.That(await PortAsync()).IsEqualTo("4999");

			var removal = (await Installer.PlanAsync(Manifest(id, "1.2", ""))).Settings!.Single();
			await Assert.That(removal.Action).IsEqualTo(PackageSettingAction.Release);
		}
		finally
		{
			await Assert.That((await Installer.UninstallAsync(id)).Value).IsTypeOf<Success>();
		}

		await Assert.That(await PortAsync()).IsEqualTo("4999");
	}

	[Test]
	public async Task AnUpgradeThatChangesTheValueSetsItAndKeepsTheOriginalToRestore()
	{
		await SetPortAsync(4100);
		var id = FreshId();
		(await ApplyAsync(Port(id, "1.0", "4303"))).Expect<PackageApplyResult>();
		(await ApplyAsync(Port(id, "1.1", "4304"))).Expect<PackageApplyResult>();
		await Assert.That(await PortAsync()).IsEqualTo("4304");

		await Assert.That((await Installer.UninstallAsync(id)).Value).IsTypeOf<Success>();
		await Assert.That(await PortAsync()).IsEqualTo("4100");
	}

	[Test]
	public async Task TwoPackagesCannotSetTheSameOption()
	{
		await SetPortAsync(4100);
		var first = FreshId();
		(await ApplyAsync(Port(first, "1.0", "4305"))).Expect<PackageApplyResult>();

		try
		{
			var second = FreshId();
			var plan = await Installer.PlanAsync(Port(second, "1.0", "4306"));
			await Assert.That(plan.IsBlocked).IsTrue();
			var setting = plan.Settings!.Single();
			await Assert.That(setting.Action).IsEqualTo(PackageSettingAction.Blocked);
			await Assert.That(setting.Detail!).Contains(first);

			await Assert.That((await ApplyAsync(Port(second, "1.0", "4306"))).Value).IsTypeOf<Error<string>>();
			await Assert.That(await PortAsync()).IsEqualTo("4305");
		}
		finally
		{
			await Assert.That((await Installer.UninstallAsync(first)).Value).IsTypeOf<Success>();
		}
	}

	/// <summary>A value the parser takes but the option's range does not is refused, not clamped.</summary>
	[Test]
	public async Task AValueOutsideTheOptionsRangeBlocksThePlan()
	{
		await SetPortAsync(4100);
		var id = FreshId();

		var plan = await Installer.PlanAsync(Port(id, "1.0", "70000"));
		await Assert.That(plan.IsBlocked).IsTrue();
		await Assert.That(plan.Settings!.Single().Action).IsEqualTo(PackageSettingAction.Blocked);
		await Assert.That((await ApplyAsync(Port(id, "1.0", "70000"))).Value).IsTypeOf<Error<string>>();
		await Assert.That(await PortAsync()).IsEqualTo("4100");
	}

	/// <summary>An option set to an object the install creates is shown as that object until it exists.</summary>
	[Test]
	public async Task ARefToANewObjectIsPlannedAsThatObject()
	{
		var plan = await Installer.PlanAsync(Manifest(FreshId(), "1.0", "settings:\n  messages_object: \"{{desk}}\""));

		var setting = plan.Settings!.Single();
		await Assert.That(plan.IsBlocked).IsFalse();
		await Assert.That(setting.Action).IsEqualTo(PackageSettingAction.Set);
		await Assert.That(setting.Value).IsEqualTo("{{desk}}");
	}
}
