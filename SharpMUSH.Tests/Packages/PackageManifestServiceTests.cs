using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Tests.Packages;

/// <summary>
/// Unit tests for package.yaml / index.yaml parsing and validation
/// (format v2, decisions 20.11–20.20). Pure text-in, model-out — no
/// database or server required.
/// </summary>
public class PackageManifestServiceTests
{
	private readonly PackageManifestService _service = new();

	private const string ValidManifest =
		"""
		format: 1
		package: myrddins-bbs
		version: 2.4.1
		authors: [Myrddin]
		description: "Bulletin Board System"
		license: MIT
		homepage: https://example.com/bbs
		keywords: [bbs, boards]
		convention_prefix: BBS_
		requires_server: ">=0.1"

		depends:
		  - volund-core: ">=1.0"

		objects:
		  - ref: bbs_global
		    type: thing
		    name: BBS Global Object
		    parent: "{{bbs_parent}}"
		    flags: [no_command]
		    attributes:
		      CMD_+BBREAD:
		        value: |-
		          $+bbread *:@pemit %#=[u({{bbs_parent}}/FN_READ,%0)]
		        flags: []
		      FN_READ:
		        value: |-
		          shorthand not used here

		  - ref: bbs_parent
		    type: thing
		    name: BBS Parent Object
		    attributes:
		      FN_FORMAT: "plain string shorthand"
		""";

	[Test]
	public async Task ValidManifest_Parses()
	{
		var result = _service.ParseManifest(ValidManifest);

		var parsed = await Assert.That(result.Value).IsTypeOf<ParsedPackageManifest>();
		var (manifest, warnings) = parsed!;

		await Assert.That(manifest.Format).IsEqualTo(new PackageFormatVersion(1, 0));
		await Assert.That(manifest.Name).IsEqualTo("myrddins-bbs");
		await Assert.That(manifest.Version).IsEqualTo(new PackageVersion(2, 4, 1));
		await Assert.That(manifest.Authors).Contains("Myrddin");
		await Assert.That(manifest.License).IsEqualTo("MIT");
		await Assert.That(manifest.Homepage).IsEqualTo("https://example.com/bbs");
		await Assert.That(manifest.Keywords.Count).IsEqualTo(2);
		await Assert.That(manifest.ConventionPrefix).IsEqualTo("BBS_");
		await Assert.That(manifest.RequiresServer!.IsSatisfiedBy(new PackageVersion(0, 2, 0))).IsTrue();
		await Assert.That(manifest.Objects.Count).IsEqualTo(2);
		await Assert.That(warnings.Count).IsEqualTo(0);

		var global = manifest.Objects[0];
		await Assert.That(global.Ref).IsEqualTo("bbs_global");
		await Assert.That(global.Type).IsEqualTo(PackageObjectType.Thing);
		await Assert.That(global.Parent).IsEqualTo(new PackageRef(PackageRefKind.Internal, "bbs_parent"));
		await Assert.That(global.Flags).Contains("no_command");
		await Assert.That(global.Attributes["CMD_+BBREAD"].Value).Contains("{{bbs_parent}}");

		await Assert.That(manifest.Objects[1].Attributes["FN_FORMAT"].Value)
			.IsEqualTo("plain string shorthand");
	}

	[Test]
	public async Task MissingFormat_DefaultsTo1()
	{
		var parsed = await Assert.That(_service.ParseManifest(MinimalManifest()).Value).IsTypeOf<ParsedPackageManifest>();
		await Assert.That(parsed!.Manifest.Format).IsEqualTo(new PackageFormatVersion(1, 0));
	}

	[Test]
	public async Task NewerFormatMinor_Warns_NewerMajor_Rejects()
	{
		var minor = await Assert.That(_service.ParseManifest(MinimalManifest("format: 1.5")).Value).IsTypeOf<ParsedPackageManifest>();
		await Assert.That(minor!.Warnings.Any(w => w.Path == "format")).IsTrue();

		var major = await Assert.That(_service.ParseManifest(MinimalManifest("format: 2")).Value).IsTypeOf<PackageManifestFailure>();
		await Assert.That(major!.Errors.Any(e => e.Path == "format")).IsTrue();
	}

	#region Versions & constraints

	[Test]
	[Arguments("2.4.1", 2, 4, 1, null)]
	[Arguments("1.0", 1, 0, 0, null)]
	[Arguments("3", 3, 0, 0, null)]
	[Arguments("3.0.0-beta.1", 3, 0, 0, "beta.1")]
	public async Task PackageVersion_ParsesValidForms(string input, int major, int minor, int patch, string? prerelease)
	{
		await Assert.That(PackageVersion.TryParse(input, out var version)).IsTrue();
		await Assert.That(version).IsEqualTo(new PackageVersion(major, minor, patch, prerelease));
	}

	[Test]
	[Arguments("")]
	[Arguments("abc")]
	[Arguments("1.2.3.4")]
	[Arguments("1.-2")]
	[Arguments("1.0-")]
	[Arguments("1.0.0+build5")]
	[Arguments("1..2")]
	[Arguments(".1")]
	[Arguments("1.")]
	public async Task PackageVersion_RejectsInvalidForms(string input)
	{
		await Assert.That(PackageVersion.TryParse(input, out _)).IsFalse();
	}

	[Test]
	[Arguments("1.0.0-beta.1", "1.0.0-beta.1")]
	[Arguments("1.0.0-rc", "1.0.0-rc")]
	[Arguments(" 1.0.0 ", "1.0.0")]
	public async Task PackageVersion_ComparesEqualIdentifiersAsEqual(string left, string right)
	{
		PackageVersion.TryParse(left, out var a);
		PackageVersion.TryParse(right, out var b);

		await Assert.That(a.CompareTo(b)).IsEqualTo(0);
		await Assert.That(b.CompareTo(a)).IsEqualTo(0);
	}

	[Test]
	public async Task PackageVersion_SemVerPrereleaseChain()
	{
		// The exact precedence chain from SemVer 2.0.0 item 11.
		string[] chain =
		[
			"1.0.0-alpha", "1.0.0-alpha.1", "1.0.0-alpha.beta", "1.0.0-beta",
			"1.0.0-beta.2", "1.0.0-beta.11", "1.0.0-rc.1", "1.0.0"
		];

		for (var i = 0; i < chain.Length - 1; i++)
		{
			PackageVersion.TryParse(chain[i], out var lower);
			PackageVersion.TryParse(chain[i + 1], out var higher);
			await Assert.That(lower.CompareTo(higher)).IsLessThan(0);
			await Assert.That(higher.CompareTo(lower)).IsGreaterThan(0);
		}
	}

	[Test]
	public async Task VersionConstraint_CompoundRange()
	{
		await Assert.That(VersionConstraint.TryParse(">=1.0 <2.0", out var constraint)).IsTrue();
		await Assert.That(constraint.IsSatisfiedBy(new PackageVersion(1, 5, 0))).IsTrue();
		await Assert.That(constraint.IsSatisfiedBy(new PackageVersion(2, 0, 0))).IsFalse();
		await Assert.That(constraint.IsSatisfiedBy(new PackageVersion(0, 9, 0))).IsFalse();
	}

	[Test]
	public async Task VersionConstraint_BareVersionMeansExact()
	{
		await Assert.That(VersionConstraint.TryParse("1.2.3", out var constraint)).IsTrue();
		await Assert.That(constraint.IsSatisfiedBy(new PackageVersion(1, 2, 3))).IsTrue();
		await Assert.That(constraint.IsSatisfiedBy(new PackageVersion(1, 2, 4))).IsFalse();
	}

	[Test]
	public async Task VersionConstraint_PrereleasesNeverMatchImplicitly()
	{
		VersionConstraint.TryParse(">=1.0", out var release);
		await Assert.That(release.IsSatisfiedBy(new PackageVersion(2, 0, 0, "beta"))).IsFalse();
		await Assert.That(release.IsSatisfiedBy(new PackageVersion(2, 0, 0))).IsTrue();

		// Opting into a prerelease on a tuple opts into that tuple's prereleases only.
		VersionConstraint.TryParse(">=1.2.3-alpha", out var opted);
		await Assert.That(opted.IsSatisfiedBy(new PackageVersion(1, 2, 3, "beta"))).IsTrue();
		await Assert.That(opted.IsSatisfiedBy(new PackageVersion(1, 3, 0, "beta"))).IsFalse();
		await Assert.That(opted.IsSatisfiedBy(new PackageVersion(1, 3, 0))).IsTrue();

		// Bare dependencies (Any) never match prereleases.
		await Assert.That(VersionConstraint.Any.IsSatisfiedBy(new PackageVersion(1, 0, 0, "beta"))).IsFalse();
		await Assert.That(VersionConstraint.Any.IsSatisfiedBy(new PackageVersion(1, 0, 0))).IsTrue();
	}

	[Test]
	public async Task VersionConstraint_RejectsGarbage()
	{
		await Assert.That(VersionConstraint.TryParse(">=banana", out _)).IsFalse();
		await Assert.That(VersionConstraint.TryParse("", out _)).IsFalse();
	}

	[Test]
	public async Task CaretConstraint_GetsTargetedError()
	{
		var result = _service.ParseManifest(MinimalManifest(extraTop:
			"""
			depends:
			  - other-pkg: "^1.2"
			"""));

		var failure = await Assert.That(result.Value).IsTypeOf<PackageManifestFailure>();
		await Assert.That(failure!.Errors.Any(e => e.Message.Contains("caret/tilde"))).IsTrue();
	}

	#endregion

	#region Refs (decision 20.11)

	[Test]
	public async Task ProseTildesAndCommandPatterns_AreNotRefs()
	{
		var result = _service.ParseManifest(MinimalManifest(attributes:
			"""
			      DESCRIBE: "A sign reads: type ~help, or ~~~wave~~~ hello."
			      CMD_GOD: "$god *:@pemit %#=You are not god."
			      FN_GLOB: "switch(%0, ?board, yes, no) and %?board"
			"""));

		var parsed = await Assert.That(result.Value).IsTypeOf<ParsedPackageManifest>();
		await Assert.That(parsed!.Warnings.Count).IsEqualTo(0);
	}

	[Test]
	public async Task MushcodeBraceGroups_AreNotRefs()
	{
		var result = _service.ParseManifest(MinimalManifest(attributes:
			"""
			      FN_SW: "@switch %0={{a},{b}}"
			"""));

		await Assert.That(result.Value).IsTypeOf<ParsedPackageManifest>();
	}

	[Test]
	public async Task UnresolvedInternalRef_IsError()
	{
		var result = _service.ParseManifest(MinimalManifest(attributes:
			"""
			      FN_X: "[u({{missing_object}}/FN_Y)]"
			"""));

		var failure = await Assert.That(result.Value).IsTypeOf<PackageManifestFailure>();
		await Assert.That(failure!.Errors.Any(e => e.Message.Contains("{{missing_object}}"))).IsTrue();
	}

	[Test]
	public async Task MalformedRefBody_IsError()
	{
		var result = _service.ParseManifest(MinimalManifest(attributes:
			"""
			      FN_X: "[u({{not a ref}}/FN)]"
			"""));

		var failure = await Assert.That(result.Value).IsTypeOf<PackageManifestFailure>();
		await Assert.That(failure!.Errors.Any(e => e.Message.Contains("not a valid ref"))).IsTrue();
	}

	[Test]
	public async Task EscapedBraces_AreLiteral()
	{
		var result = _service.ParseManifest(MinimalManifest(attributes:
			"""
			      FN_X: "literal {{{{mustache}}}} stays text"
			"""));

		var parsed = await Assert.That(result.Value).IsTypeOf<ParsedPackageManifest>();
		await Assert.That(parsed!.Warnings.Count).IsEqualTo(0);
	}

	[Test]
	public async Task Refs_AreCaseInsensitive()
	{
		var result = _service.ParseManifest(
			"""
			package: cased
			version: "1.0"
			objects:
			  - ref: bbs_parent
			    type: thing
			    name: Parent
			  - ref: a
			    type: thing
			    name: A
			    parent: "{{BBS_PARENT}}"
			    attributes:
			      FN_X: "U({{Bbs_Parent}}/FN_Y, %0)"
			""");

		var parsed = await Assert.That(result.Value).IsTypeOf<ParsedPackageManifest>();
		await Assert.That(parsed!.Manifest.Objects[1].Parent)
			.IsEqualTo(new PackageRef(PackageRefKind.Internal, "bbs_parent"));
	}

	[Test]
	public async Task WellKnownParent_Resolves_UnknownName_Errors()
	{
		var ok = await Assert.That(_service.ParseManifest(MinimalManifest(objectExtras: "    parent: \"{{$room_zero}}\"")).Value).IsTypeOf<ParsedPackageManifest>();
		await Assert.That(ok!.Manifest.Objects[0].Parent)
			.IsEqualTo(new PackageRef(PackageRefKind.WellKnown, "room_zero"));

		var bad = await Assert.That(_service.ParseManifest(MinimalManifest(objectExtras: "    parent: \"{{$nonsense_ref}}\"")).Value).IsTypeOf<PackageManifestFailure>();
		await Assert.That(bad!.Errors.Any(e => e.Message.Contains("$nonsense_ref"))).IsTrue();
	}

	[Test]
	public async Task AdditionalWellKnownRefs_AreHonored()
	{
		var extended = new PackageManifestService(["chargen_room"]);
		var result = extended.ParseManifest(MinimalManifest(objectExtras: "    parent: \"{{$chargen_room}}\""));

		await Assert.That(result.Value).IsTypeOf<ParsedPackageManifest>();
	}

	[Test]
	public async Task CrossPackageRef_RequiresDeclaredDependency()
	{
		var undeclared = _service.ParseManifest(MinimalManifest(attributes:
			"""
			      FN_X: "[u({{who-where/ww_functions}}/FN)]"
			"""));
		var undeclaredFailure = await Assert.That(undeclared.Value).IsTypeOf<PackageManifestFailure>();
		await Assert.That(undeclaredFailure!.Errors.Any(e => e.Message.Contains("not listed under 'depends'"))).IsTrue();

		var declared = _service.ParseManifest(MinimalManifest(
			extraTop:
			"""
			depends:
			  - who-where: ">=1.0"
			""",
			attributes:
			"""
			      FN_X: "[u({{who-where/ww_functions}}/FN)]"
			"""));
		await Assert.That(declared.Value).IsTypeOf<ParsedPackageManifest>();
	}

	#endregion

	#region Objects: exits, rooms, renames

	[Test]
	public async Task Exit_RequiresLocationAndDestination()
	{
		var missing = _service.ParseManifest(
			"""
			package: area
			version: "1.0"
			objects:
			  - ref: door
			    type: exit
			    name: Door;door
			""");

		var missingFailure = await Assert.That(missing.Value).IsTypeOf<PackageManifestFailure>();
		var paths = missingFailure!.Errors.Select(e => e.Path).ToList();
		await Assert.That(paths).Contains("objects[0].location");
		await Assert.That(paths).Contains("objects[0].destination");

		var valid = _service.ParseManifest(
			"""
			package: area
			version: "1.0"
			objects:
			  - ref: square
			    type: room
			    name: Town Square
			  - ref: tavern
			    type: room
			    name: Tavern
			  - ref: door
			    type: exit
			    name: Tavern;tavern;t
			    location: "{{square}}"
			    destination: "{{tavern}}"
			""");

		var validParsed = await Assert.That(valid.Value).IsTypeOf<ParsedPackageManifest>();
		var door = validParsed!.Manifest.Objects[2];
		await Assert.That(door.Location).IsEqualTo(new PackageRef(PackageRefKind.Internal, "square"));
		await Assert.That(door.Destination).IsEqualTo(new PackageRef(PackageRefKind.Internal, "tavern"));
	}

	[Test]
	public async Task Room_CannotHaveLocation_Thing_CannotHaveDestination()
	{
		var room = _service.ParseManifest(
			"""
			package: area
			version: "1.0"
			objects:
			  - ref: square
			    type: room
			    name: Square
			    location: "{{$room_zero}}"
			""");
		var roomFailure = await Assert.That(room.Value).IsTypeOf<PackageManifestFailure>();
		await Assert.That(roomFailure!.Errors.Any(e => e.Path == "objects[0].location")).IsTrue();

		var thing = await Assert.That(_service.ParseManifest(MinimalManifest(objectExtras: "    destination: \"{{$room_zero}}\"")).Value).IsTypeOf<PackageManifestFailure>();
		await Assert.That(thing!.Errors.Any(e => e.Path == "objects[0].destination")).IsTrue();
	}

	[Test]
	public async Task PreviousRefs_Parse_AndMustBeRetiredNames()
	{
		var ok = await Assert.That(_service.ParseManifest(MinimalManifest(objectExtras: "    previous_refs: [old_thing]")).Value).IsTypeOf<ParsedPackageManifest>();
		await Assert.That(ok!.Manifest.Objects[0].PreviousRefs).Contains("old_thing");

		var colliding = await Assert.That(_service.ParseManifest(MinimalManifest(objectExtras: "    previous_refs: [a]")).Value).IsTypeOf<PackageManifestFailure>();
		await Assert.That(colliding!.Errors.Any(e => e.Path == "objects[0].previous_refs")).IsTrue();
	}

	#endregion

	#region Configure (decision 20.19)

	[Test]
	public async Task UndeclaredConfigureRef_IsError()
	{
		var result = _service.ParseManifest(MinimalManifest(attributes:
			"""
			      FN_X: "get({{?mystery}}/SETTING)"
			"""));

		var failure = await Assert.That(result.Value).IsTypeOf<PackageManifestFailure>();
		await Assert.That(failure!.Errors.Any(e => e.Message.Contains("{{?mystery}}"))).IsTrue();
	}

	[Test]
	public async Task TypedConfigure_ParsesTypesAndDefaults()
	{
		var result = _service.ParseManifest(MinimalManifest(
			extraTop:
			"""
			configure:
			  storage:
			    label: "Storage object"
			  board_name:
			    label: "Board title"
			    type: string
			    default: "Community Board"
			  max_posts:
			    label: "Maximum posts"
			    type: number
			    default: "50"
			  announce:
			    label: "Announce new posts?"
			    type: boolean
			    default: "true"
			""",
			attributes:
			"""
			      FN_X: "get({{?storage}}/X) {{?board_name}} {{?max_posts}} {{?announce}}"
			"""));

		var parsed = await Assert.That(result.Value).IsTypeOf<ParsedPackageManifest>();
		var configure = parsed!.Manifest.Configure;
		await Assert.That(configure["storage"].Type).IsEqualTo(PackageConfigureType.Dbref);
		await Assert.That(configure["board_name"].Type).IsEqualTo(PackageConfigureType.String);
		await Assert.That(configure["board_name"].Default).IsEqualTo("Community Board");
		await Assert.That(configure["max_posts"].Type).IsEqualTo(PackageConfigureType.Number);
		await Assert.That(configure["announce"].Type).IsEqualTo(PackageConfigureType.Boolean);
	}

	[Test]
	public async Task DbrefConfigure_CannotHaveDefault_BadTypedDefaults_Error()
	{
		var dbrefDefault = _service.ParseManifest(MinimalManifest(extraTop:
			"""
			configure:
			  storage:
			    label: "Storage"
			    default: "#123"
			"""));
		var dbrefDefaultFailure = await Assert.That(dbrefDefault.Value).IsTypeOf<PackageManifestFailure>();
		await Assert.That(dbrefDefaultFailure!.Errors.Any(e => e.Message.Contains("dbref configure refs cannot"))).IsTrue();

		var badNumber = _service.ParseManifest(MinimalManifest(extraTop:
			"""
			configure:
			  count:
			    label: "Count"
			    type: number
			    default: "many"
			"""));
		var badNumberFailure = await Assert.That(badNumber.Value).IsTypeOf<PackageManifestFailure>();
		await Assert.That(badNumberFailure!.Errors.Any(e => e.Path == "configure.count.default")).IsTrue();
	}

	[Test]
	public async Task NonDbrefConfigure_CannotBeParent()
	{
		var result = _service.ParseManifest(MinimalManifest(
			extraTop:
			"""
			configure:
			  board_name:
			    label: "Board title"
			    type: string
			""",
			objectExtras: "    parent: \"{{?board_name}}\""));

		var failure = await Assert.That(result.Value).IsTypeOf<PackageManifestFailure>();
		await Assert.That(failure!.Errors.Any(e => e.Message.Contains("requires a dbref-typed"))).IsTrue();
	}

	[Test]
	public async Task UnusedConfigureDeclaration_Warns()
	{
		var result = _service.ParseManifest(MinimalManifest(extraTop:
			"""
			configure:
			  never_used:
			    label: "Orphan"
			"""));

		var parsed = await Assert.That(result.Value).IsTypeOf<ParsedPackageManifest>();
		await Assert.That(parsed!.Warnings.Any(w => w.Message.Contains("never_used"))).IsTrue();
	}

	#endregion

	#region Dependencies, conflicts, metadata

	[Test]
	public async Task FullFormDependency_ParsesSourceHint()
	{
		var result = _service.ParseManifest(MinimalManifest(extraTop:
			"""
			depends:
			  - package: who-where
			    version: ">=1.0 <2.0"
			    source:
			      repo: https://github.com/SharpMUSH/SharpMUSH-Packages
			      path: who-where/
			      branch: main
			"""));

		var parsed = await Assert.That(result.Value).IsTypeOf<ParsedPackageManifest>();
		var dependency = parsed!.Manifest.Dependencies[0];
		await Assert.That(dependency.PackageId).IsEqualTo("who-where");
		await Assert.That(dependency.Constraint.IsSatisfiedBy(new PackageVersion(1, 5, 0))).IsTrue();
		await Assert.That(dependency.Source).IsEqualTo(new PackageSourceHint(
			"https://github.com/SharpMUSH/SharpMUSH-Packages", "who-where/", "main"));
	}

	[Test]
	public async Task SourceWithoutRepo_IsError()
	{
		var result = _service.ParseManifest(MinimalManifest(extraTop:
			"""
			depends:
			  - package: who-where
			    source:
			      branch: main
			"""));

		var failure = await Assert.That(result.Value).IsTypeOf<PackageManifestFailure>();
		await Assert.That(failure!.Errors.Any(e =>
			e.Path == "depends[0].source" && e.Message.Contains("repo"))).IsTrue();
	}

	[Test]
	public async Task SelfDependency_DuplicateDependency_AreErrors()
	{
		var result = _service.ParseManifest(MinimalManifest(extraTop:
			"""
			depends:
			  - probe
			  - other-pkg
			  - other-pkg
			"""));

		var failure = await Assert.That(result.Value).IsTypeOf<PackageManifestFailure>();
		var messages = failure!.Errors.Select(e => e.Message).ToList();
		await Assert.That(messages.Any(m => m.Contains("cannot appear in its own"))).IsTrue();
		await Assert.That(messages.Any(m => m.Contains("Duplicate entry"))).IsTrue();
	}

	[Test]
	public async Task Conflicts_Parse_ButRejectOverlapWithDepends_AndSourceHints()
	{
		var ok = _service.ParseManifest(MinimalManifest(extraTop:
			"""
			conflicts:
			  - legacy-bbs: "<2.0"
			"""));
		var okParsed = await Assert.That(ok.Value).IsTypeOf<ParsedPackageManifest>();
		await Assert.That(okParsed!.Manifest.Conflicts[0].PackageId).IsEqualTo("legacy-bbs");

		var overlap = _service.ParseManifest(MinimalManifest(extraTop:
			"""
			depends:
			  - some-pkg
			conflicts:
			  - some-pkg
			"""));
		var overlapFailure = await Assert.That(overlap.Value).IsTypeOf<PackageManifestFailure>();
		await Assert.That(overlapFailure!.Errors.Any(e => e.Message.Contains("both 'depends' and 'conflicts'"))).IsTrue();

		var sourced = _service.ParseManifest(MinimalManifest(extraTop:
			"""
			conflicts:
			  - package: legacy-bbs
			    source: https://example.com/repo
			"""));
		var sourcedParsed = await Assert.That(sourced.Value).IsTypeOf<ParsedPackageManifest>();
		await Assert.That(sourcedParsed!.Warnings.Any(w => w.Path == "conflicts[0].source")).IsTrue();
	}

	[Test]
	public async Task ReservedKeys_WarnDistinctly()
	{
		var result = _service.ParseManifest(MinimalManifest(extraTop: "provides: [bbs]"));

		var parsed = await Assert.That(result.Value).IsTypeOf<ParsedPackageManifest>();
		await Assert.That(parsed!.Warnings.Any(w =>
			w.Path == "provides" && w.Message.Contains("reserved"))).IsTrue();
	}

	[Test]
	public async Task Replaces_MustBeValidAndNotSelf()
	{
		var ok = await Assert.That(_service.ParseManifest(MinimalManifest(extraTop: "replaces: old-probe")).Value).IsTypeOf<ParsedPackageManifest>();
		await Assert.That(ok!.Manifest.Replaces).IsEqualTo("old-probe");

		var self = _service.ParseManifest(MinimalManifest(extraTop: "replaces: probe"));
		await Assert.That(self.Value).IsTypeOf<PackageManifestFailure>();
	}

	[Test]
	public async Task TooManyKeywords_Warns()
	{
		var result = _service.ParseManifest(MinimalManifest(extraTop: "keywords: [a, b, c, d, e, f]"));

		var parsed = await Assert.That(result.Value).IsTypeOf<ParsedPackageManifest>();
		await Assert.That(parsed!.Warnings.Any(w => w.Path == "keywords")).IsTrue();
	}

	#endregion

	#region Document-level validation

	[Test]
	public async Task MissingRequiredFields_ReportsErrors()
	{
		var result = _service.ParseManifest("description: nothing else\n");

		var failure = await Assert.That(result.Value).IsTypeOf<PackageManifestFailure>();
		var errors = failure!.Errors.Select(e => e.Path).ToList();
		await Assert.That(errors).Contains("package");
		await Assert.That(errors).Contains("version");
		await Assert.That(errors).Contains("objects");
	}

	[Test]
	public async Task InvalidPackageSlug_AndOverlongId_AreErrors()
	{
		var bad = await Assert.That(_service.ParseManifest(MinimalManifest().Replace("package: probe", "package: Bad_Name")).Value).IsTypeOf<PackageManifestFailure>();
		await Assert.That(bad!.Errors.Any(e => e.Path == "package")).IsTrue();

		var overlong = _service.ParseManifest(
			MinimalManifest().Replace("package: probe", $"package: a{new string('b', 70)}"));
		await Assert.That(overlong.Value).IsTypeOf<PackageManifestFailure>();
	}

	[Test]
	public async Task DuplicateObjectRef_IsError()
	{
		var result = _service.ParseManifest(
			"""
			package: dupes
			version: "1.0"
			objects:
			  - ref: thing_one
			    type: thing
			    name: One
			  - ref: thing_one
			    type: thing
			    name: Two
			""");

		var failure = await Assert.That(result.Value).IsTypeOf<PackageManifestFailure>();
		await Assert.That(failure!.Errors.Any(e => e.Message.Contains("Duplicate object ref"))).IsTrue();
	}

	[Test]
	public async Task ParentCycle_IsError()
	{
		var result = _service.ParseManifest(
			"""
			package: cyclic
			version: "1.0"
			objects:
			  - ref: a
			    type: thing
			    name: A
			    parent: "{{b}}"
			  - ref: b
			    type: thing
			    name: B
			    parent: "{{a}}"
			""");

		var failure = await Assert.That(result.Value).IsTypeOf<PackageManifestFailure>();
		await Assert.That(failure!.Errors.Any(e => e.Message.Contains("Parent cycle"))).IsTrue();
	}

	[Test]
	public async Task InvalidYaml_ReportsLocation()
	{
		var result = _service.ParseManifest("package: [unclosed\nversion: 1.0");

		var failure = await Assert.That(result.Value).IsTypeOf<PackageManifestFailure>();
		await Assert.That(failure!.Errors.Any(e => e.Path.StartsWith("line "))).IsTrue();
	}

	[Test]
	public async Task UnknownTopLevelKey_Warns()
	{
		var result = _service.ParseManifest(MinimalManifest(extraTop: "dependz: [other-pkg]"));

		var parsed = await Assert.That(result.Value).IsTypeOf<ParsedPackageManifest>();
		await Assert.That(parsed!.Warnings.Any(w => w.Message.Contains("dependz"))).IsTrue();
	}

	[Test]
	public async Task NonStringAttributeValue_SuggestsBlockScalar()
	{
		var result = _service.ParseManifest(MinimalManifest(attributes:
			"""
			      FN_X: [ansi(hw,hello)]
			"""));

		var failure = await Assert.That(result.Value).IsTypeOf<PackageManifestFailure>();
		await Assert.That(failure!.Errors.Any(e => e.Message.Contains("block scalar"))).IsTrue();
	}

	[Test]
	public async Task BacktickAttributeTrees_AllowedBetweenSegments_NotAtEdges()
	{
		var ok = _service.ParseManifest(MinimalManifest(attributes:
			"""
			      FOO`BAR: "branch value"
			"""));
		await Assert.That(ok.Value).IsTypeOf<ParsedPackageManifest>();

		var bad = _service.ParseManifest(MinimalManifest(attributes:
			"""
			      "`FOO": "bad"
			"""));
		await Assert.That(bad.Value).IsTypeOf<PackageManifestFailure>();
	}

	[Test]
	public async Task ObjectFlagsAndLocks_Parse()
	{
		var result = _service.ParseManifest(MinimalManifest(
			objectExtras: "    flags: [no_command, dark]\n    locks:\n      use: \"{{a}}\"\n      page: \"=#1\""));

		var parsed = await Assert.That(result.Value).IsTypeOf<ParsedPackageManifest>();
		var obj = parsed!.Manifest.Objects.Single();
		await Assert.That(obj.Flags.ToArray()).IsEquivalentTo((string[])["no_command", "dark"]);
		await Assert.That(obj.Locks["use"]).IsEqualTo("{{a}}");
		await Assert.That(obj.Locks["page"]).IsEqualTo("=#1");
	}

	[Test]
	public async Task AttributeFlags_ParseFromMappingForm_ShorthandHasNone()
	{
		// Attribute flags are supported only via the mapping form (value: + flags:);
		// the bare-string shorthand carries no flags.
		var result = _service.ParseManifest(MinimalManifest(attributes:
			"""
			      WITH_FLAGS:
			        value: |-
			          think hi
			        flags: [no_command, veiled]
			      SHORTHAND: "plain value"
			"""));

		var parsed = await Assert.That(result.Value).IsTypeOf<ParsedPackageManifest>();
		var attrs = parsed!.Manifest.Objects.Single().Attributes;
		await Assert.That(attrs["WITH_FLAGS"].Flags.ToArray()).IsEquivalentTo((string[])["no_command", "veiled"]);
		await Assert.That(attrs["SHORTHAND"].Flags.Count).IsEqualTo(0);
	}

	[Test]
	public async Task ObjectPowers_Parse()
	{
		var result = _service.ParseManifest(MinimalManifest(objectExtras: "    powers: [pueblo, idle]"));

		var parsed = await Assert.That(result.Value).IsTypeOf<ParsedPackageManifest>();
		await Assert.That(parsed!.Manifest.Objects.Single().Powers.ToArray())
			.IsEquivalentTo((string[])["pueblo", "idle"]);
	}

	[Test]
	public async Task AttachObject_RejectsPowers()
	{
		// Attach objects manage attributes only — object-level powers (like flags
		// and locks) are creation-only and rejected.
		var result = _service.ParseManifest(
			"""
			package: hooks
			version: "1.0"
			objects:
			  - ref: handler
			    target: "{{$http_handler}}"
			    powers: [pueblo]
			    attributes:
			      GET: |-
			        think hi
			""");

		var failure = await Assert.That(result.Value).IsTypeOf<PackageManifestFailure>();
		await Assert.That(failure!.Errors.Any(e => e.Path == "objects[0].powers")).IsTrue();
	}

	#endregion

	#region Index

	[Test]
	public async Task Index_ParsesStringAndEnrichedEntries()
	{
		var result = _service.ParseIndex(
			"""
			name: Volund's Suite
			description: Curated softcode
			packages:
			  - core/
			  - path: bbs/
			    name: BBS
			    package: volund-bbs
			    version: 2.0.0
			    description: Boards
			""");

		var index = await Assert.That(result.Value).IsTypeOf<PackageIndex>();
		await Assert.That(index!.Packages[0]).IsEqualTo(new PackageIndexEntry("core/", null));
		await Assert.That(index.Packages[1].PackageId).IsEqualTo("volund-bbs");
		await Assert.That(index.Packages[1].Version).IsEqualTo(new PackageVersion(2, 0, 0));
	}

	[Test]
	public async Task Index_DuplicateIds_AndMissingPackages_AreErrors()
	{
		var duplicate = _service.ParseIndex(
			"""
			packages:
			  - path: a/
			    package: same-id
			  - path: b/
			    package: same-id
			""");
		var duplicateFailure = await Assert.That(duplicate.Value).IsTypeOf<PackageManifestFailure>();
		await Assert.That(duplicateFailure!.Errors.Any(e => e.Message.Contains("Duplicate package id"))).IsTrue();

		var missing = await Assert.That(_service.ParseIndex("name: Empty Repo\n").Value).IsTypeOf<PackageManifestFailure>();
		await Assert.That(missing!.Errors.Any(e => e.Path == "packages")).IsTrue();
	}

	#endregion

	#region Attach mode (decision 20.3)

	[Test]
	public async Task AttachObject_Parses_TargetAndAttributesOnly()
	{
		var result = _service.ParseManifest(
			"""
			package: hooks
			version: "1.0"
			objects:
			  - ref: handler
			    target: "{{$http_handler}}"
			    attributes:
			      GET: |-
			        think hello
			""");

		var parsed = await Assert.That(result.Value).IsTypeOf<ParsedPackageManifest>();
		var obj = parsed!.Manifest.Objects.Single();
		await Assert.That(obj.IsAttach).IsTrue();
		await Assert.That(obj.Target).IsEqualTo(new PackageRef(PackageRefKind.WellKnown, "http_handler"));
		await Assert.That(obj.Attributes.ContainsKey("GET")).IsTrue();
	}

	[Test]
	public async Task AttachObject_RejectsCreationOnlyKeys()
	{
		var result = _service.ParseManifest(
			"""
			package: hooks
			version: "1.0"
			objects:
			  - ref: handler
			    target: "{{$http_handler}}"
			    type: thing
			    name: Nope
			    flags: [wizard]
			    attributes:
			      GET: |-
			        think hi
			""");

		var failure = await Assert.That(result.Value).IsTypeOf<PackageManifestFailure>();
		var paths = failure!.Errors.Select(e => e.Path).ToList();
		await Assert.That(paths).Contains("objects[0].type");
		await Assert.That(paths).Contains("objects[0].name");
		await Assert.That(paths).Contains("objects[0].flags");
	}

	[Test]
	public async Task AttachObject_TargetKinds_WellKnownConfigureCrossPackage_NotSamePackage()
	{
		// Same-package internal target is rejected (declare attrs on the object directly).
		var internalTarget = _service.ParseManifest(
			"""
			package: hooks
			version: "1.0"
			objects:
			  - ref: other
			    type: thing
			    name: Other
			  - ref: handler
			    target: "{{other}}"
			    attributes:
			      GET: |-
			        think hi
			""");
		var internalTargetFailure = await Assert.That(internalTarget.Value).IsTypeOf<PackageManifestFailure>();
		await Assert.That(internalTargetFailure!.Errors.Any(e => e.Path == "objects[1].target")).IsTrue();

		var configureTarget = _service.ParseManifest(
			"""
			package: hooks
			version: "1.0"
			configure:
			  store:
			    label: "Storage"
			objects:
			  - ref: handler
			    target: "{{?store}}"
			    attributes:
			      GET: |-
			        think get({{?store}}/X)
			""");
		await Assert.That(configureTarget.Value).IsTypeOf<ParsedPackageManifest>();

		// Cross-package target (a declared dependency's object) is allowed.
		var crossTarget = _service.ParseManifest(
			"""
			package: ext
			version: "1.0"
			depends:
			  - base-pkg: ">=1.0"
			objects:
			  - ref: handler
			    target: "{{base-pkg/hub}}"
			    attributes:
			      FN_X: |-
			        think extension
			""");
		var crossTargetParsed = await Assert.That(crossTarget.Value).IsTypeOf<ParsedPackageManifest>();
		await Assert.That(crossTargetParsed!.Manifest.Objects.Single().Target)
			.IsEqualTo(new PackageRef(PackageRefKind.Internal, "hub", "base-pkg"));

		// Cross-package target to an UNDECLARED dependency is an error.
		var undeclared = _service.ParseManifest(
			"""
			package: ext
			version: "1.0"
			objects:
			  - ref: handler
			    target: "{{base-pkg/hub}}"
			    attributes:
			      FN_X: |-
			        think extension
			""");
		await Assert.That(undeclared.Value).IsTypeOf<PackageManifestFailure>();
	}

	[Test]
	public async Task AttachObject_RequiresAttributes_AndValidTarget()
	{
		var noAttrs = _service.ParseManifest(
			"""
			package: hooks
			version: "1.0"
			objects:
			  - ref: handler
			    target: "{{$http_handler}}"
			""");
		var noAttrsFailure = await Assert.That(noAttrs.Value).IsTypeOf<PackageManifestFailure>();
		await Assert.That(noAttrsFailure!.Errors.Any(e => e.Path == "objects[0].attributes")).IsTrue();

		var badTarget = _service.ParseManifest(
			"""
			package: hooks
			version: "1.0"
			objects:
			  - ref: handler
			    target: "not-a-ref"
			    attributes:
			      GET: |-
			        think hi
			""");
		var badTargetFailure = await Assert.That(badTarget.Value).IsTypeOf<PackageManifestFailure>();
		await Assert.That(badTargetFailure!.Errors.Any(e => e.Path == "objects[0].target")).IsTrue();
	}

	#endregion

	#region Ref indirection (decision 20.21)

	[Test]
	public async Task ReservedPmAttributeTree_IsError()
	{
		var result = _service.ParseManifest(MinimalManifest(attributes:
			"""
			      PM`REFS`CUSTOM: "nope"
			"""));

		var failure = await Assert.That(result.Value).IsTypeOf<PackageManifestFailure>();
		await Assert.That(failure!.Errors.Any(e => e.Message.Contains("reserved"))).IsTrue();
	}

	[Test]
	public async Task CrossKindRefNameCollisions_AreErrors()
	{
		// Internal ref 'storage' vs configure key 'storage' → same PM`REFS`STORAGE attr.
		var configureCollision = _service.ParseManifest(
			"""
			package: probe
			version: "1.0"
			configure:
			  storage:
			    label: "Storage"
			objects:
			  - ref: storage
			    type: thing
			    name: Storage Thing
			    attributes:
			      FN_X: "get({{?storage}}/DATA)"
			""");
		var configureCollisionFailure = await Assert.That(configureCollision.Value).IsTypeOf<PackageManifestFailure>();
		await Assert.That(configureCollisionFailure!.Errors.Any(e =>
			e.Message.Contains("PM`REFS`STORAGE"))).IsTrue();

		// Internal ref 'room_zero' vs use of {{$room_zero}}.
		var wellKnownCollision = _service.ParseManifest(
			"""
			package: probe
			version: "1.0"
			objects:
			  - ref: room_zero
			    type: thing
			    name: Misleading Name
			    attributes:
			      FN_X: "loc({{$room_zero}})"
			""");
		var wellKnownCollisionFailure = await Assert.That(wellKnownCollision.Value).IsTypeOf<PackageManifestFailure>();
		await Assert.That(wellKnownCollisionFailure!.Errors.Any(e =>
			e.Message.Contains("PM`REFS`ROOM_ZERO"))).IsTrue();
	}

	#endregion

	#region Community listings

	[Test]
	public async Task CommunityListing_Parses_WithOptionalsAndUnknownKeysTolerated()
	{
		var result = _service.ParseCommunityListing(
			"""
			name: Volund's MUSH Suite
			url: https://github.com/volund/mush-suite
			branch: stable
			description: "Core, BBS, jobs, and mail systems."
			maintainers: [Volund]
			homepage: https://volund.example.com
			future_field: ignored silently
			""");

		var listing = await Assert.That(result.Value).IsTypeOf<CommunityRepoListing>();
		await Assert.That(listing!.Name).IsEqualTo("Volund's MUSH Suite");
		await Assert.That(listing.Url).IsEqualTo("https://github.com/volund/mush-suite");
		await Assert.That(listing.Branch).IsEqualTo("stable");
		await Assert.That(listing.Maintainers).Contains("Volund");
		await Assert.That(listing.Homepage).IsEqualTo("https://volund.example.com");
	}

	[Test]
	public async Task CommunityListing_RequiresNameUrlDescription_AndValidUrl()
	{
		var missing = await Assert.That(_service.ParseCommunityListing("branch: main\n").Value).IsTypeOf<PackageManifestFailure>();
		var paths = missing!.Errors.Select(e => e.Path).ToList();
		await Assert.That(paths).Contains("name");
		await Assert.That(paths).Contains("url");
		await Assert.That(paths).Contains("description");

		var badUrl = _service.ParseCommunityListing(
			"""
			name: Bad
			url: "not a url"
			description: x
			""");
		var badUrlFailure = await Assert.That(badUrl.Value).IsTypeOf<PackageManifestFailure>();
		await Assert.That(badUrlFailure!.Errors.Any(e => e.Path == "url")).IsTrue();
	}

	#endregion

	[Test]
	public async Task RefScanner_FindsAllKinds_AndFlagsMalformed()
	{
		var tokens = PackageRefScanner
			.Scan("u({{bbs_parent}}/FN, {{$master_room}}, {{?game_config}}, {{who-where/ww_fn}}) {{not valid}} {{{{escaped}}}}")
			.ToList();

		var refs = tokens.Where(t => t.Ref is not null).Select(t => t.Ref!).ToList();
		await Assert.That(refs).Contains(new PackageRef(PackageRefKind.Internal, "bbs_parent"));
		await Assert.That(refs).Contains(new PackageRef(PackageRefKind.WellKnown, "master_room"));
		await Assert.That(refs).Contains(new PackageRef(PackageRefKind.Configure, "game_config"));
		await Assert.That(refs).Contains(new PackageRef(PackageRefKind.Internal, "ww_fn", "who-where"));
		await Assert.That(tokens.Count(t => t.Ref is null)).IsEqualTo(1);
		await Assert.That(tokens.Any(t => t.Raw.Contains("escaped"))).IsFalse();
	}

	/// <summary>Builds a minimal valid manifest with optional injected sections.</summary>
	private static string MinimalManifest(
		string? extraTop = null, string? objectExtras = null, string? attributes = null)
	{
		var top = extraTop is null ? "" : $"{extraTop.TrimEnd()}\n";
		var objectLines = objectExtras is null ? "" : $"{objectExtras.TrimEnd()}\n";
		var attrBlock = attributes is null
			? ""
			: $"    attributes:\n{attributes.TrimEnd()}\n";

		return
			$"""
			package: probe
			version: "1.0"
			{top}objects:
			  - ref: a
			    type: thing
			    name: A
			{objectLines}{attrBlock}
			""";
	}
}
