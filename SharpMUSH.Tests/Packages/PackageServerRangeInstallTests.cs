using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Packages;

/// <summary>
/// The engine-version range planned and applied against the real world: a server below <c>requires_server</c> is
/// refused, and one above <c>maximum_server</c> applies only once the administrator confirms, which is recorded.
/// The test server is 2.x, so a minimum of 99 and a maximum of 1 stay out of range.
/// </summary>
public class PackageServerRangeInstallTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IPackageInstallService Installer => WebAppFactoryArg.Services.GetRequiredService<IPackageInstallService>();
	private IPackageRegistryService Registry => (IPackageRegistryService)WebAppFactoryArg.Services.GetRequiredService<ISharpDatabase>();

	private static string FreshId() => "range-" + Guid.NewGuid().ToString("N")[..8];

	private static PackageManifest Manifest(string id, string version, string range)
		=> new PackageManifestService().ParseManifest($$"""
			format: 1.6
			package: {{id}}
			version: "{{version}}"
			{{range}}
			objects:
			  - ref: box
			    type: thing
			    name: Range Box
			""").Expect<ParsedPackageManifest>().Manifest;

	private Task<Result<PackageApplyResult>> ApplyAsync(PackageManifest manifest, bool allowPastMaximum = false)
		=> Installer.ApplyAsync(manifest, new PackageApplyRequest(
			new PackageApplySource("https://example.com/range.git", null, "commit-" + manifest.Version, null),
			new Dictionary<string, string>(), [], AllowPastMaximum: allowPastMaximum));

	[Test]
	public async Task AServerBelowTheMinimum_BlocksThePlanAndTheApply()
	{
		var manifest = Manifest(FreshId(), "1.0.0", "requires_server: \">=99.0\"");

		var plan = await Installer.PlanAsync(manifest);
		await Assert.That(plan.Compatibility!.Fit).IsEqualTo(PackageServerFit.BelowMinimum);
		await Assert.That(plan.IsBlocked).IsTrue();

		var error = (await ApplyAsync(manifest, allowPastMaximum: true)).Expect<Error<string>>();
		await Assert.That(error.Value).Contains("requires SharpMUSH >=99.0.0");
		await Assert.That((await Registry.GetInstalledPackageAsync(manifest.Name)).Value).IsTypeOf<NotFound>();
	}

	[Test]
	public async Task AServerAboveTheMaximum_AppliesOnlyOnceConfirmed_AndTheChoiceIsRecorded()
	{
		var id = FreshId();
		var manifest = Manifest(id, "1.0.0", "maximum_server: \"1\"");

		var plan = await Installer.PlanAsync(manifest);
		await Assert.That(plan.Compatibility!.Fit).IsEqualTo(PackageServerFit.AboveMaximum);
		await Assert.That(plan.IsBlocked).IsFalse();

		var refused = (await ApplyAsync(manifest)).Expect<Error<string>>();
		await Assert.That(refused.Value).Contains("runs on SharpMUSH up to 1");

		var applied = (await ApplyAsync(manifest, allowPastMaximum: true)).Expect<PackageApplyResult>();
		await Assert.That(applied.Notes.Any(n => n.Contains("past maximum_server 1"))).IsTrue();
		var installed = (await Registry.GetInstalledPackageAsync(id)).Expect<InstalledPackageRecord>();
		await Assert.That(installed.PastMaximum!.Maximum).IsEqualTo("1");

		// A version without the bound is inside the range again, and the record says so.
		(await ApplyAsync(Manifest(id, "1.0.1", ""))).Expect<PackageApplyResult>();
		await Assert.That((await Registry.GetInstalledPackageAsync(id)).Expect<InstalledPackageRecord>().PastMaximum).IsNull();

		// A rollback restores the mark that revision carried, in either direction.
		(await Installer.RollbackAsync(id, 1)).Expect<PackageRollbackResult>();
		await Assert.That((await Registry.GetInstalledPackageAsync(id)).Expect<InstalledPackageRecord>().PastMaximum!.Maximum).IsEqualTo("1");
		(await Installer.RollbackAsync(id, 2)).Expect<PackageRollbackResult>();
		await Assert.That((await Registry.GetInstalledPackageAsync(id)).Expect<InstalledPackageRecord>().PastMaximum).IsNull();

		await Assert.That((await Installer.UninstallAsync(id)).Value).IsTypeOf<Success>();
	}

	[Test]
	public async Task AServerNewerThanVerified_OnlyWarns()
	{
		var id = FreshId();
		var manifest = Manifest(id, "1.0.0", "verified_server: \"1.0\"");

		var plan = await Installer.PlanAsync(manifest);
		await Assert.That(plan.Compatibility!.Fit).IsEqualTo(PackageServerFit.NewerThanVerified);

		(await ApplyAsync(manifest)).Expect<PackageApplyResult>();
		await Assert.That((await Registry.GetInstalledPackageAsync(id)).Expect<InstalledPackageRecord>().PastMaximum).IsNull();
		await Assert.That((await Installer.UninstallAsync(id)).Value).IsTypeOf<Success>();
	}
}
