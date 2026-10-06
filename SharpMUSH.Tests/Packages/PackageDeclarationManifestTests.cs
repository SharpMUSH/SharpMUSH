using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Tests.Packages;

/// <summary>The <c>categories:</c>, <c>permissions:</c>, <c>roles:</c> and <c>jobs:</c> blocks of a manifest (format 1.2).</summary>
public class PackageDeclarationManifestTests
{
	private readonly PackageManifestService _service = new();

	private const string Header = """
		format: 1.2
		package: requests
		version: "1.0"
		objects:
		  - ref: desk
		    type: thing
		    name: Request Desk
		    attributes:
		      JOB`SWEEP: "@pemit me=sweep"
		""";

	private PackageManifestResult<ParsedPackageManifest> Parse(string body) => _service.ParseManifest(Header + "\n" + body);

	private static IReadOnlyList<PackageManifestIssue> Errors(PackageManifestResult<ParsedPackageManifest> result)
		=> result.Expect<PackageManifestFailure>().Issues.Where(i => i.Severity == PackageManifestIssueSeverity.Error).ToList();

	[Test]
	public async Task EveryBlockParses()
	{
		var declared = Parse("""
			categories:
			  roles:
			    - name: Requests
			      description: Roles for working the request queue.
			  permissions:
			    - name: Requests
			      description: Permissions for the request queue.
			permissions:
			  - name: requests.handle
			    category: Requests
			    description: Work on any request.
			roles:
			  - slug: handler
			    name: Request Handler
			    category: Requests
			    color: "#4fc3c8"
			    priority: 11
			    permissions:
			      requests.handle: allow
			jobs:
			  - ref: sweep
			    target: "{{desk}}"
			    attribute: job`sweep
			    schedule: "0 4 * * *"
			    timezone: UTC
			    description: Closes stale requests.
			""").Expect<ParsedPackageManifest>().Manifest.Declared;

		await Assert.That(declared.RoleCategories.Single()).IsEqualTo(new PackageCategorySpec("Requests", "Roles for working the request queue."));
		await Assert.That(declared.PermissionCategories.Single().Name).IsEqualTo("Requests");
		await Assert.That(declared.Permissions.Single()).IsEqualTo(new PackagePermissionSpec("requests.handle", "Requests", "Work on any request."));
		var role = declared.Roles.Single();
		await Assert.That(role.Slug).IsEqualTo("handler");
		await Assert.That(role.Name).IsEqualTo("Request Handler");
		await Assert.That(role.Priority).IsEqualTo(11);
		await Assert.That(role.Permissions["requests.handle"]).IsEqualTo(PermissionState.Allow);
		var job = declared.Jobs.Single();
		await Assert.That(job.Target).IsEqualTo(new PackageRef(PackageRefKind.Internal, "desk"));
		await Assert.That(job.Attribute).IsEqualTo("JOB`SWEEP");
		await Assert.That(job.TimeZone).IsEqualTo("UTC");
	}

	[Test]
	public async Task APackageThatDeclaresNothingHasNoDeclarations()
	{
		var manifest = Parse("").Expect<ParsedPackageManifest>().Manifest;
		await Assert.That(manifest.Declarations).IsNull();
		await Assert.That(manifest.Declared.IsEmpty).IsTrue();
	}

	[Test]
	public async Task ARoleCannotSetABuiltInPermission()
	{
		var errors = Errors(Parse("""
			roles:
			  - slug: handler
			    category: Staff
			    permissions:
			      roles.admin: allow
			"""));
		await Assert.That(errors.Single().Path).IsEqualTo("roles[0].permissions.roles.admin");
		await Assert.That(errors.Single().Message).Contains("built-in permission");
	}

	[Test]
	public async Task ASystemRoleCannotBeDeclared()
	{
		var errors = Errors(Parse("""
			roles:
			  - slug: wizard
			    category: System
			"""));
		await Assert.That(errors.Single().Message).Contains("system role");
	}

	[Test]
	[Arguments("0")]
	[Arguments("30")]
	[Arguments("high")]
	public async Task ARolePriorityStaysBelowTheWizardRole(string priority)
	{
		var errors = Errors(Parse($"""
			roles:
			  - slug: handler
			    category: Staff
			    priority: {priority}
			"""));
		await Assert.That(errors.Single().Path).IsEqualTo("roles[0].priority");
	}

	[Test]
	public async Task APermissionMustBeACustomName()
	{
		var errors = Errors(Parse("""
			permissions:
			  - name: game.wizard
			    category: Staff
			"""));
		await Assert.That(errors.Single().Path).IsEqualTo("permissions[0].name");
	}

	[Test]
	[Arguments("0 4 * *", "UTC")]
	[Arguments("0 4 * * *", "Mars/Olympus")]
	public async Task AJobScheduleIsChecked(string schedule, string zone)
	{
		var errors = Errors(Parse($$$"""
			jobs:
			  - ref: sweep
			    target: "{{desk}}"
			    attribute: JOB`SWEEP
			    schedule: "{{{schedule}}}"
			    timezone: {{{zone}}}
			"""));
		await Assert.That(errors.Single().Path).IsEqualTo("jobs[0].schedule");
	}

	[Test]
	public async Task AJobTargetMustNameAnObject()
	{
		var errors = Errors(Parse("""
			jobs:
			  - ref: sweep
			    target: "{{nowhere}}"
			    attribute: JOB`SWEEP
			    schedule: "0 4 * * *"
			"""));
		await Assert.That(errors.Single().Path).IsEqualTo("jobs[0].target");
	}

	[Test]
	public async Task AnUndeclaredCategoryOrPermissionIsOnlyAWarning()
	{
		var parsed = Parse("""
			permissions:
			  - name: requests.handle
			    category: Elsewhere
			roles:
			  - slug: handler
			    category: Staff
			    permissions:
			      scene.close: allow
			""").Expect<ParsedPackageManifest>();
		await Assert.That(parsed.Warnings.Select(i => i.Path)).IsEquivalentTo(new[] { "permissions[0].category", "roles[0].permissions.scene.close" });
	}

	[Test]
	public async Task AManagedPackageCannotDeclareRoles()
	{
		var result = _service.ParseManifest("""
			package: plugin
			version: "1.0"
			kind: managed
			binaries:
			  min_server_version: ">=1.0"
			  files:
			    - file: plugin.dll
			      sha256: 0000000000000000000000000000000000000000000000000000000000000000
			roles:
			  - slug: handler
			    category: Staff
			""");
		await Assert.That(Errors(result).Select(e => e.Path)).Contains("roles");
	}
}
