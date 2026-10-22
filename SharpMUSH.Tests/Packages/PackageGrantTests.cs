using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Tests.Packages;

/// <summary>The <c>grants:</c> block (format 1.15): the package's own permissions allowed on the game's roles.</summary>
public class PackageGrantTests
{
	private static readonly PackageCategorySpec Requests = new("Requests", "The request queue.");
	private static readonly PackagePermissionSpec Handle = new("requests.handle", "Requests", "Work on any request.");
	private static readonly PackageGrantSpec WizardHandles = new("wizard", "requests.handle");

	private static PackageDeclarations Declared(params PackageGrantSpec[] grants)
		=> new([], [Requests], [Handle], [], Grants: grants.Length == 0 ? null : grants);

	private static SharpRole Wizard(PermissionState? state = null) => new()
	{
		Slug = "wizard", Name = "Wizard", Category = "Staff", Priority = 30, IsSystem = true,
		Permissions = state is { } s
			? new Dictionary<string, PermissionState> { ["game.wizard"] = PermissionState.Allow, ["requests.handle"] = s }
			: new Dictionary<string, PermissionState> { ["game.wizard"] = PermissionState.Allow }
	};

	private static PackageDeclarationLiveState Live(SharpRole wizard, bool permissionExists = false)
		=> new(
			[.. Categories.RoleSeeds],
			[.. Categories.PermissionSeeds, new RoleCategory("Requests", "The request queue.", 1)],
			permissionExists ? [new CustomPermission("requests.handle", "Requests", "Work on any request.", 1)] : [],
			[wizard],
			new HashSet<string>(),
			new Dictionary<(PackageDeclarationKind, string), string>());

	private static PackageDeclarationChange GrantChange(PackageDeclarationPlan plan)
		=> plan.Changes.Single(c => c.Kind == PackageDeclarationKind.Grant);

	[Test]
	public async Task InstallAllowsThePermissionOnTheRole()
	{
		var plan = PackageDeclarationPlanner.Plan(Declared(WizardHandles), null, Live(Wizard()), 5);

		await Assert.That(plan.IsBlocked).IsFalse();
		await Assert.That(GrantChange(plan).Action).IsEqualTo(PackageDeclarationAction.Create);
		await Assert.That(GrantChange(plan).Name).IsEqualTo("wizard: requests.handle");
		var wizard = plan.RoleWrites.Single();
		await Assert.That(wizard.IsSystem).IsTrue();
		await Assert.That(wizard.Priority).IsEqualTo(30);
		await Assert.That(wizard.Permissions["requests.handle"]).IsEqualTo(PermissionState.Allow);
		await Assert.That(wizard.Permissions["game.wizard"]).IsEqualTo(PermissionState.Allow);
		await Assert.That(plan.Owned.RoleGrants).IsEquivalentTo([WizardHandles]);
	}

	[Test]
	[Arguments(PermissionState.Allow)]
	[Arguments(PermissionState.Deny)]
	public async Task ARoleThatAlreadySetsItIsLeftAlone(PermissionState state)
	{
		var plan = PackageDeclarationPlanner.Plan(Declared(WizardHandles), null, Live(Wizard(state), permissionExists: true), 5);

		await Assert.That(GrantChange(plan).Action).IsEqualTo(PackageDeclarationAction.Existing);
		await Assert.That(plan.RoleWrites).IsEmpty();
		await Assert.That(plan.Owned.RoleGrants).IsEmpty();
	}

	[Test]
	public async Task AGrantTheGameClearedStaysClearedOnUpgrade()
	{
		var plan = PackageDeclarationPlanner.Plan(Declared(WizardHandles), Declared(WizardHandles) with { Permissions = [Handle] },
			Live(Wizard(), permissionExists: true), 5);

		await Assert.That(GrantChange(plan).Action).IsEqualTo(PackageDeclarationAction.NoChange);
		await Assert.That(plan.RoleWrites).IsEmpty();
		await Assert.That(plan.Owned.RoleGrants).IsEquivalentTo([WizardHandles]);
	}

	[Test]
	public async Task ARoleThatDoesNotExistBlocks()
	{
		var plan = PackageDeclarationPlanner.Plan(Declared(new PackageGrantSpec("helper", "requests.handle")), null, Live(Wizard()), 5);

		await Assert.That(plan.IsBlocked).IsTrue();
		await Assert.That(GrantChange(plan).Detail!).Contains("helper");
	}

	[Test]
	public async Task UninstallClearsTheGrantAndThenThePermission()
	{
		var plan = PackageDeclarationPlanner.Plan(PackageDeclarations.None, Declared(WizardHandles),
			Live(Wizard(PermissionState.Allow), permissionExists: true), 5);

		await Assert.That(GrantChange(plan).Action).IsEqualTo(PackageDeclarationAction.Remove);
		await Assert.That(plan.RoleWrites.Single().Permissions.ContainsKey("requests.handle")).IsFalse();
		await Assert.That(plan.PermissionRemovals).IsEquivalentTo(["requests.handle"]);
	}

	[Test]
	public async Task UninstallLeavesARoleTheGameChanged()
	{
		var plan = PackageDeclarationPlanner.Plan(PackageDeclarations.None, Declared(WizardHandles),
			Live(Wizard(PermissionState.Deny), permissionExists: true), 5);

		await Assert.That(plan.Changes.Any(c => c.Kind == PackageDeclarationKind.Grant)).IsFalse();
		await Assert.That(plan.RoleWrites).IsEmpty();
		await Assert.That(plan.PermissionRemovals).IsEmpty();
	}

	private const string Manifest = """
		format: "1.15"
		package: requests
		version: "1.0"
		objects:
		  - ref: desk
		    type: thing
		    name: Request Desk
		categories:
		  permissions:
		    - name: Requests
		      description: Permissions for the request queue.
		permissions:
		  - name: requests.handle
		    category: Requests
		roles:
		  - slug: handler
		    category: Requests
		""";

	private static PackageManifestResult<ParsedPackageManifest> Parse(string grants, string format = "\"1.15\"")
		=> new PackageManifestService().ParseManifest(Manifest.Replace("\"1.15\"", format) + "\n" + grants);

	private static IEnumerable<string> Errors(PackageManifestResult<ParsedPackageManifest> result)
		=> result.Expect<PackageManifestFailure>().Issues.Where(i => i.Severity == PackageManifestIssueSeverity.Error).Select(i => i.Message);

	[Test]
	public async Task TheBlockParses()
	{
		var declared = Parse("""
			grants:
			  wizard: [requests.handle]
			  royalty: [requests.handle]
			""").Expect<ParsedPackageManifest>().Manifest.Declared;

		await Assert.That(declared.RoleGrants).IsEquivalentTo([WizardHandles, new PackageGrantSpec("royalty", "requests.handle")]);
	}

	[Test]
	public async Task OnlyThePackagesOwnPermissionsCanBeGranted()
	{
		await Assert.That(Errors(Parse("grants:\n  wizard: [wiki.admin]")).Single()).Contains("a package grants only its own permissions");
	}

	[Test]
	public async Task ARoleThePackageDeclaresIsRefused()
	{
		await Assert.That(Errors(Parse("grants:\n  handler: [requests.handle]")).Single()).Contains("under roles");
	}

	[Test]
	public async Task AnOlderFormatIgnoresTheBlock()
	{
		var parsed = Parse("grants:\n  wizard: [requests.handle]", "\"1.14\"").Expect<ParsedPackageManifest>();

		await Assert.That(parsed.Manifest.Declared.RoleGrants).IsEmpty();
		await Assert.That(parsed.Warnings.Any(i => i.Path == "grants")).IsTrue();
	}

	[Test]
	public async Task TheWriterWritesWhatTheReaderReads()
	{
		var manifest = Parse("grants:\n  wizard: [requests.handle]").Expect<ParsedPackageManifest>().Manifest;
		var again = new PackageManifestService().ParseManifest(PackageManifestWriter.Write(manifest)).Expect<ParsedPackageManifest>();

		await Assert.That(again.Manifest.Declared.RoleGrants).IsEquivalentTo([WizardHandles]);
	}
}
