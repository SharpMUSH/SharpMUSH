using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Tests.Packages;

/// <summary>
/// Unit tests for the application package kind (decision 20.22): a
/// <c>kind: application</c> manifest registers a portal application, owns no
/// objects, and may carry <c>{{?configure}}</c> refs in its application fields.
/// </summary>
public class ApplicationPackageManifestTests
{
	private readonly PackageManifestService _service = new();

	private const string ValidApplication =
		"""
		format: 1.1
		package: chargen-app
		version: 1.0.0
		kind: application
		description: "Registers the chargen form as a portal page."
		depends:
		  - chargen: ">=1.0 <2.0"
		configure:
		  access:
		    label: "Minimum role"
		    type: string
		    default: player
		application:
		  slug: chargen
		  display_name: Character Application
		  icon: assignment_ind
		  type: page
		  schema_url: http/chargen/schema
		  submit_route: http/chargen
		  minimum_role: "{{?access}}"
		  nav_placement: main
		  order: 50
		""";

	[Test]
	public async Task ValidApplicationPackage_Parses()
	{
		var result = _service.ParseManifest(ValidApplication);

		await Assert.That(result.IsT0).IsTrue();
		var (manifest, warnings) = result.AsT0;

		await Assert.That(manifest.Kind).IsEqualTo(PackageKind.Application);
		await Assert.That(manifest.Objects.Count).IsEqualTo(0);
		await Assert.That(warnings.Count).IsEqualTo(0);

		var app = manifest.Application!;
		await Assert.That(app.Slug).IsEqualTo("chargen");
		await Assert.That(app.DisplayName).IsEqualTo("Character Application");
		await Assert.That(app.Kind).IsEqualTo(PackageApplicationDisplay.Page);
		await Assert.That(app.SchemaUrl).IsEqualTo("http/chargen/schema");
		await Assert.That(app.SubmitRoute).IsEqualTo("http/chargen");
		await Assert.That(app.MinimumRole).IsEqualTo("{{?access}}");
		await Assert.That(app.NavPlacement).IsEqualTo("main");
		await Assert.That(app.Order).IsEqualTo(50);
	}

	[Test]
	public async Task SoftcodeIsTheDefaultKind()
	{
		var result = _service.ParseManifest(
			"""
			package: plain
			version: 1.0.0
			objects:
			  - ref: room
			    type: room
			    name: A Room
			""");

		await Assert.That(result.IsT0).IsTrue();
		await Assert.That(result.AsT0.Manifest.Kind).IsEqualTo(PackageKind.Softcode);
		await Assert.That(result.AsT0.Manifest.Application).IsNull();
	}

	[Test]
	public async Task ApplicationPackage_WithObjects_IsRejected()
	{
		var result = _service.ParseManifest(
			"""
			package: bad-app
			version: 1.0.0
			kind: application
			application:
			  slug: bad-app
			  display_name: Bad
			  schema_url: http/bad/schema
			objects:
			  - ref: room
			    type: room
			    name: A Room
			""");

		await Assert.That(result.IsT1).IsTrue();
		await Assert.That(result.AsT1.Errors.Any(e => e.Path == "objects")).IsTrue();
	}

	[Test]
	public async Task ApplicationPackage_MissingApplicationBlock_IsRejected()
	{
		var result = _service.ParseManifest(
			"""
			package: no-block
			version: 1.0.0
			kind: application
			""");

		await Assert.That(result.IsT1).IsTrue();
		await Assert.That(result.AsT1.Errors.Any(e => e.Path == "application")).IsTrue();
	}

	[Test]
	public async Task SoftcodePackage_WithApplicationBlock_IsRejected()
	{
		var result = _service.ParseManifest(
			"""
			package: misplaced
			version: 1.0.0
			application:
			  slug: misplaced
			  display_name: Nope
			  schema_url: http/x/schema
			objects:
			  - ref: room
			    type: room
			    name: A Room
			""");

		await Assert.That(result.IsT1).IsTrue();
		await Assert.That(result.AsT1.Errors.Any(e =>
			e.Path == "application" && e.Message.Contains("kind: application"))).IsTrue();
	}

	[Test]
	public async Task ApplicationPackage_UndeclaredConfigureRef_IsRejected()
	{
		var result = _service.ParseManifest(
			"""
			package: undeclared
			version: 1.0.0
			kind: application
			application:
			  slug: undeclared
			  display_name: Undeclared
			  schema_url: http/x/schema
			  minimum_role: "{{?mystery}}"
			""");

		await Assert.That(result.IsT1).IsTrue();
		await Assert.That(result.AsT1.Errors.Any(e => e.Message.Contains("{{?mystery}}"))).IsTrue();
	}

	[Test]
	public async Task ApplicationPackage_InvalidLiteralRole_IsRejected()
	{
		var result = _service.ParseManifest(
			"""
			package: badrole
			version: 1.0.0
			kind: application
			application:
			  slug: badrole
			  display_name: Bad Role
			  schema_url: http/x/schema
			  minimum_role: superuser
			""");

		await Assert.That(result.IsT1).IsTrue();
		await Assert.That(result.AsT1.Errors.Any(e => e.Path == "application.minimum_role")).IsTrue();
	}

	[Test]
	public async Task ApplicationPackage_InvalidType_IsRejected()
	{
		var result = _service.ParseManifest(
			"""
			package: badtype
			version: 1.0.0
			kind: application
			application:
			  slug: badtype
			  display_name: Bad Type
			  type: dashboard
			  schema_url: http/x/schema
			""");

		await Assert.That(result.IsT1).IsTrue();
		await Assert.That(result.AsT1.Errors.Any(e => e.Path == "application.type")).IsTrue();
	}

	[Test]
	public async Task ApplicationSlug_DefaultsToPackageId()
	{
		var result = _service.ParseManifest(
			"""
			package: my-widget
			version: 1.0.0
			kind: application
			application:
			  display_name: My Widget
			  type: widget
			  schema_url: http/widget/schema
			  zones: [MainContent, RightSidebar]
			""");

		await Assert.That(result.IsT0).IsTrue();
		var app = result.AsT0.Manifest.Application!;
		await Assert.That(app.Slug).IsEqualTo("my-widget");
		await Assert.That(app.Kind).IsEqualTo(PackageApplicationDisplay.Widget);
		await Assert.That(app.Zones.Count).IsEqualTo(2);
	}

	[Test]
	public async Task InvalidKind_IsRejected()
	{
		var result = _service.ParseManifest(
			"""
			package: weird
			version: 1.0.0
			kind: plugin
			objects:
			  - ref: room
			    type: room
			    name: A Room
			""");

		await Assert.That(result.IsT1).IsTrue();
		await Assert.That(result.AsT1.Errors.Any(e => e.Path == "kind")).IsTrue();
	}
}
