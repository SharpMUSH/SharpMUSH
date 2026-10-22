using System.Text.Json;
using SharpMUSH.Library.API;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Tests.Packages;

/// <summary>Behavioral format-1.14 contract; deliberately uses only pre-1.14 public APIs.</summary>
public class PackageWikiLandingPageTests
{
	private readonly PackageManifestService _service = new();

	private static string Document(string? value, string format = "1.14", string page = "main:guide") =>
		"format: " + format + "\npackage: independent-guide\nversion: 1.0.0\n"
		+ "wiki:\n  - page: " + JsonSerializer.Serialize(page)
		+ "\n    title: An independent guide\n    markdown: Start here.\n"
		+ (value is null ? string.Empty : "wiki_landing_page: " + value + "\n");

	private PackageManifestResult<ParsedPackageManifest> Parse(string? value, string format = "1.14", string page = "main:guide") =>
		_service.ParseManifest(Document(value, format, page));

	// JSON observation permits this test-only release to compile before the additive property exists.
	private static string? LandingLabel(PackageManifest manifest)
	{
		var json = JsonSerializer.SerializeToElement(manifest);
		return json.TryGetProperty("WikiLandingPage", out var landing)
			&& landing.ValueKind == JsonValueKind.Object
			&& landing.TryGetProperty("Label", out var label) ? label.GetString() : null;
	}

	[Test]
	[Arguments("main:guide")]
	[Arguments("help:guide")]
	[Arguments("category:guide")]
	public async Task LandingTargetParsesOnlyDeclaredCanonicalPackageWikiKeys(string target)
	{
		var parsed = Parse(JsonSerializer.Serialize("  " + target + "  "), page: target).Expect<ParsedPackageManifest>();
		// Assert observable behavior before inspecting the new serialized field.
		var yaml = PackageManifestWriter.Write(parsed.Manifest);
		await Assert.That(yaml).Contains("wiki_landing_page:");
		await Assert.That(parsed.Warnings).IsEmpty();
		await Assert.That(LandingLabel(parsed.Manifest)).IsEqualTo(target);
		await Assert.That(parsed.Manifest.WikiPages.Single().Label).IsEqualTo(target);
		await Assert.That(_service.ParseManifest(yaml).Expect<ParsedPackageManifest>().Warnings).IsEmpty();
	}

	[Test]
	[Arguments("main:guide\t")]
	[Arguments("\t  main:guide \t")]
	public async Task LandingTrimsSurroundingWhitespace(string value)
	{
		var parsed = Parse(JsonSerializer.Serialize(value)).Expect<ParsedPackageManifest>();
		await Assert.That(PackageManifestWriter.Write(parsed.Manifest)).Contains("wiki_landing_page:");
		await Assert.That(parsed.Warnings).IsEmpty();
		await Assert.That(LandingLabel(parsed.Manifest)).IsEqualTo("main:guide");
	}

	[Test]
	[Arguments("")]
	[Arguments("   ")]
	[Arguments("guide")]
	[Arguments("main:")]
	[Arguments("main:absent")]
	[Arguments("help:guide")]
	[Arguments("Main:guide")]
	[Arguments("character:guide")]
	[Arguments("unknown:guide")]
	[Arguments("main:Guide")]
	[Arguments("main:two words")]
	[Arguments("main:one/two")]
	[Arguments("main:one\\two")]
	[Arguments("main:one:two")]
	[Arguments("main:guide?query")]
	[Arguments("main:guide#fragment")]
	[Arguments("main:guide\nnext")]
	[Arguments("main:gui\tde")]
	[Arguments("main:guide\u0001")]
	[Arguments("main:guide\u2028next")]
	[Arguments("main:guide\u2029next")]
	public async Task MissingUndeclaredUnavailableAndMalformedTargetsAreRefused(string value)
	{
		var failure = Parse(JsonSerializer.Serialize(value)).Expect<PackageManifestFailure>();
		await Assert.That(failure.Errors.Any(issue => issue.Path == "wiki_landing_page")).IsTrue();
	}

	[Test]
	[Arguments("null")]
	[Arguments("[]")]
	[Arguments("[main:guide]")]
	[Arguments("{page: main:guide}")]
	[Arguments("|\n  main:guide\n  help:guide")]
	public async Task LandingRequiresOneNonemptyScalar(string value)
	{
		var failure = Parse(value).Expect<PackageManifestFailure>();
		await Assert.That(failure.Errors.Any(issue => issue.Path == "wiki_landing_page")).IsTrue();
	}

	[Test]
	public async Task LandingSlugUsesTheTwoHundredUtf8ByteBound()
	{
		var target = "main:" + new string('é', 100);
		var parsed = Parse(JsonSerializer.Serialize(target), page: target).Expect<ParsedPackageManifest>();
		await Assert.That(PackageManifestWriter.Write(parsed.Manifest)).Contains("wiki_landing_page:");
		await Assert.That(parsed.Warnings).IsEmpty();
		await Assert.That(LandingLabel(parsed.Manifest)).IsEqualTo(target);
		// Exercise wiki-name validation and landing rejection for the same oversized authored identity.
		var oversized = target + "é";
		var failure = Parse(JsonSerializer.Serialize(oversized), page: oversized).Expect<PackageManifestFailure>();
		await Assert.That(failure.Errors.Any(issue => issue.Path == "wiki[0].page")).IsTrue();
		await Assert.That(failure.Errors.Any(issue => issue.Path == "wiki_landing_page")).IsTrue();
	}

	[Test]
	public async Task DuplicateDeclaredTargetIsAmbiguousAndRefused()
	{
		var yaml = Document("main:guide");
		// Append the duplicate within wiki, before the top-level landing key.
		yaml = yaml.Replace("wiki_landing_page:", "  - page: main:guide\n    title: Another guide\n    markdown: Duplicate.\nwiki_landing_page:", StringComparison.Ordinal);
		var failure = _service.ParseManifest(yaml).Expect<PackageManifestFailure>();
		await Assert.That(failure.Errors.Any(issue => issue.Path == "wiki[1].page")).IsTrue();
	}

	[Test]
	[Arguments("plugin")]
	[Arguments("ui")]
	public async Task ManagedPackagesCannotDeclareLandingMetadata(string kind)
	{
		var yaml = "format: 1.14\npackage: managed-guide\nversion: 1.0.0\nkind: " + kind + "\n"
			+ "binaries:\n  min_server_version: \">=1.0\"\n  files:\n    - file: "
			+ (kind == "ui" ? "ui.json" : "guide.dll") + "\n      sha256: " + new string('0', 64) + "\n";
		// Prove rejection is introduced by landing metadata, not an invalid managed-package fixture.
		_service.ParseManifest(yaml).Expect<ParsedPackageManifest>();
		var failure = _service.ParseManifest(yaml + "wiki_landing_page: main:guide\n").Expect<PackageManifestFailure>();
		await Assert.That(failure.Errors.Any(issue => issue.Path == "wiki_landing_page")).IsTrue();
	}

	[Test]
	public async Task ApplicationPackageMayNameItsDeclaredWikiPage()
	{
		var yaml = Document("main:guide") + """
			kind: application
			application:
			  slug: independent-guide
			  display_name: Independent guide
			  type: page
			  schema_url: http/guide/schema
			  minimum_role: player
			""";
		var parsed = _service.ParseManifest(yaml).Expect<ParsedPackageManifest>();
		await Assert.That(PackageManifestWriter.Write(parsed.Manifest)).Contains("wiki_landing_page:");
		await Assert.That(parsed.Warnings).IsEmpty();
		await Assert.That(LandingLabel(parsed.Manifest)).IsEqualTo("main:guide");
		await Assert.That(parsed.Manifest.Kind).IsEqualTo(PackageKind.Application);
	}

	[Test]
	[Arguments("1.4")]
	[Arguments("1.12")]
	[Arguments("1.13")]
	public async Task AbsentLandingPreservesLegacyManifests(string format)
	{
		var parsed = Parse(null, format).Expect<ParsedPackageManifest>();
		var yaml = PackageManifestWriter.Write(parsed.Manifest);
		await Assert.That(yaml).Contains("format: " + format);
		await Assert.That(yaml).DoesNotContain("wiki_landing_page:");
		await Assert.That(parsed.Warnings).IsEmpty();
		await Assert.That(LandingLabel(parsed.Manifest)).IsNull();
		var restored = _service.ParseManifest(yaml).Expect<ParsedPackageManifest>();
		await Assert.That(restored.Manifest.Format.ToString()).IsEqualTo(format);
		await Assert.That(restored.Manifest.WikiPages.Single().Label).IsEqualTo("main:guide");
		await Assert.That(restored.Warnings).IsEmpty();
	}

	[Test]
	[Arguments("main:guide")]
	[Arguments("null")]
	[Arguments("[]")]
	[Arguments("{page: main:guide}")]
	[Arguments("|\n  first line\n  second line")]
	public async Task LegacyFormatsWarnAndIgnoreLandingIncludingMalformedNode(string value)
	{
		var parsed = Parse(value, "1.13").Expect<ParsedPackageManifest>();
		await Assert.That(LandingLabel(parsed.Manifest)).IsNull();
		await Assert.That(PackageManifestWriter.Write(parsed.Manifest)).DoesNotContain("wiki_landing_page:");
		await Assert.That(parsed.Warnings.Any(issue => issue.Path == "wiki_landing_page"
			&& issue.Message.Contains("1.14", StringComparison.Ordinal)
			&& issue.Message.Contains("ignored", StringComparison.OrdinalIgnoreCase))).IsTrue();
		await Assert.That(parsed.Warnings.Any(issue => issue.Message.Contains("Unknown key", StringComparison.Ordinal))).IsFalse();
	}

	[Test]
	[Arguments("main:collector's_guide")]
	[Arguments("main:collector\"s_guide")]
	public async Task LandingWriterRoundTripsEscapedScalarAndFormat(string target)
	{
		var manifest = Parse(JsonSerializer.Serialize(target), page: target).Expect<ParsedPackageManifest>().Manifest;
		var yaml = PackageManifestWriter.Write(manifest);
		var landingLine = yaml.Split('\n').SingleOrDefault(line => line.StartsWith("wiki_landing_page:", StringComparison.Ordinal));
		await Assert.That(landingLine is not null).IsTrue();
		var scalar = (landingLine ?? "")["wiki_landing_page:".Length..].Trim();
		await Assert.That(scalar.StartsWith('\'') || scalar.StartsWith('"')).IsTrue();
		await Assert.That(yaml).Contains("format: 1.14");
		var restored = _service.ParseManifest(yaml).Expect<ParsedPackageManifest>();
		await Assert.That(LandingLabel(restored.Manifest)).IsEqualTo(target);
		await Assert.That(restored.Manifest.WikiPages.Single().Label).IsEqualTo(target);
		await Assert.That(restored.Warnings).IsEmpty();
	}

	[Test]
	public async Task WriterRejectsLandingBelowItsFormatFloor()
	{
		var original = Parse("main:guide").Expect<ParsedPackageManifest>().Manifest;
		var inconsistent = original with
		{
			Format = new PackageFormatVersion(1, 13)
		};
		var rejected = false;
		try
		{
			PackageManifestWriter.Write(inconsistent);
		}
		catch (ArgumentException)
		{
			rejected = true;
		}
		await Assert.That(rejected).IsTrue();
	}

	[Test]
	public async Task NewerMinorWarningIsIndependentOfTheLandingFieldFloor()
	{
		var parsed = Parse(null, "1.99").Expect<ParsedPackageManifest>();
		await Assert.That(parsed.Warnings.Count).IsEqualTo(1);
		await Assert.That(parsed.Warnings.Single().Path).IsEqualTo("format");
		await Assert.That(parsed.Warnings.Single().Message.Contains("newer", StringComparison.OrdinalIgnoreCase)).IsTrue();
		await Assert.That(PackageManifestWriter.Write(parsed.Manifest)).Contains("format: 1.99");
	}

	[Test]
	public async Task CollectionNameAtOneThirteenKeepsItsExistingBehavior()
	{
		var parsed = _service.ParseManifest(Document(null, "1.13") + "wiki_collection_name: '  A guide: its own label  '\n").Expect<ParsedPackageManifest>();
		await Assert.That(parsed.Manifest.WikiCollectionName).IsEqualTo("A guide: its own label");
		await Assert.That(parsed.Warnings).IsEmpty();
		var yaml = PackageManifestWriter.Write(parsed.Manifest);
		await Assert.That(yaml).Contains("format: 1.13");
		await Assert.That(yaml).DoesNotContain("wiki_landing_page:");
		await Assert.That(_service.ParseManifest(yaml).Expect<ParsedPackageManifest>().Manifest.WikiCollectionName).IsEqualTo("A guide: its own label");
	}

	[Test]
	[Arguments("old-format")]
	[Arguments("plugin")]
	[Arguments("ui")]
	[Arguments("undeclared")]
	[Arguments("duplicate")]
	[Arguments("namespace")]
	[Arguments("unknown-namespace")]
	[Arguments("uppercase")]
	[Arguments("space")]
	[Arguments("surrounding-space")]
	[Arguments("control")]
	[Arguments("newline")]
	[Arguments("oversized")]
	[Arguments("null-page")]
	public async Task ProgrammaticLandingValidationAndWriterRejectInconsistentTargets(string inconsistency)
	{
		var source = Parse(null).Expect<ParsedPackageManifest>().Manifest;
		var landing = new PackageWikiLandingPageSpec(WikiNamespace.Main, "guide");
		var manifest = source with { WikiLandingPage = landing };
		manifest = inconsistency switch
		{
			"old-format" => manifest with { Format = new PackageFormatVersion(1, 13) },
			"plugin" => manifest with { Kind = PackageKind.Plugin },
			"ui" => manifest with { Kind = PackageKind.Ui },
			"undeclared" => manifest with { Wiki = [] },
			"duplicate" => manifest with { Wiki = [source.WikiPages[0], source.WikiPages[0]] },
			"namespace" => manifest with { WikiLandingPage = landing with { Namespace = WikiNamespace.Character } },
			"unknown-namespace" => manifest with { WikiLandingPage = landing with { Namespace = (WikiNamespace)999 } },
			"uppercase" => manifest with { WikiLandingPage = landing with { Page = "Guide" } },
			"space" => manifest with { WikiLandingPage = landing with { Page = "two words" } },
			"surrounding-space" => manifest with { WikiLandingPage = landing with { Page = " guide " } },
			"control" => manifest with { WikiLandingPage = landing with { Page = "guide\u0001" } },
			"newline" => manifest with { WikiLandingPage = landing with { Page = "guide\nnext" } },
			"oversized" => manifest with { WikiLandingPage = landing with { Page = new string('é', 101) } },
			"null-page" => manifest with { WikiLandingPage = landing with { Page = null! } },
			_ => throw new ArgumentOutOfRangeException(nameof(inconsistency))
		};

		var issues = PackageManifestService.ValidateWikiLandingPage(manifest);
		await Assert.That(issues.Any(issue => issue.Path == "wiki_landing_page"
			&& issue.Severity == PackageManifestIssueSeverity.Error)).IsTrue();
		var rejected = false;
		try
		{
			PackageManifestWriter.Write(manifest);
		}
		catch (ArgumentException)
		{
			rejected = true;
		}
		await Assert.That(rejected).IsTrue();
	}


	[Test]
	[Arguments("\u0085main:guide")]
	[Arguments("main:guide\v")]
	[Arguments("main:guide\n")]
	public async Task LandingDoesNotTrimAwayControlCharactersOrLineBreaks(string value)
	{
		var failure = Parse(JsonSerializer.Serialize(value)).Expect<PackageManifestFailure>();
		await Assert.That(failure.Errors.Any(issue => issue.Path == "wiki_landing_page")).IsTrue();
	}

	[Test]
	public async Task ProgrammaticLandingValidationAcceptsDeclaredPageAndAbsentLegacyMetadata()
	{
		var source = Parse(null).Expect<ParsedPackageManifest>().Manifest;
		var manifest = source with { WikiLandingPage = new PackageWikiLandingPageSpec(WikiNamespace.Main, "guide") };
		await Assert.That(PackageManifestService.ValidateWikiLandingPage(manifest)).IsEmpty();
		await Assert.That(LandingLabel(_service.ParseManifest(PackageManifestWriter.Write(manifest))
			.Expect<ParsedPackageManifest>().Manifest)).IsEqualTo("main:guide");
		await Assert.That(PackageManifestService.ValidateWikiLandingPage(source with { Format = new PackageFormatVersion(1, 13) })).IsEmpty();
	}

	[Test]
	public async Task LegacyManifestJsonWithoutLandingPropertyReadsWithNoEntrance()
	{
		const string json = """
			{"Format":{"Major":1,"Minor":13},"Name":"legacy-guide","Version":{"Major":1,"Minor":0,"Patch":0},
			"Authors":[],"Description":"Legacy guide","Keywords":[],"Conflicts":[],"Dependencies":[],"Configure":{},"Objects":[]}
			""";
		var manifest = JsonSerializer.Deserialize<PackageManifest>(json)!;
		await Assert.That(manifest.Name).IsEqualTo("legacy-guide");
		await Assert.That(manifest.Format.ToString()).IsEqualTo("1.13");
		await Assert.That(manifest.WikiLandingPage).IsNull();
		await Assert.That(PackageManifestService.ValidateWikiLandingPage(manifest)).IsEmpty();
		await Assert.That(PackageManifestWriter.Write(manifest)).DoesNotContain("wiki_landing_page:");
	}

	[Test]
	public async Task ExistingPositionalConstructorsAndDeconstructorsRemainSourceCompatible()
	{
		// Supply every existing positional argument; deconstruction fixes the existing arity.
		// This is source compatibility evidence, not a legacy-compiled binary consumer test.
		var manifest = new PackageManifest(new PackageFormatVersion(1, 13), "legacy-guide", new PackageVersion(1, 0, 0),
			[], "Legacy guide", null, null, [], null, null, null, [], [],
			new Dictionary<string, PackageConfigureSpec>(), [], PackageKind.Softcode, null, null, null, null, null, null, null, null, null, null);
		manifest.Deconstruct(out var format, out var name, out _, out _, out _, out _, out _, out _, out _, out _, out _, out _, out _,
			out _, out _, out _, out _, out _, out _, out _, out _, out _, out _, out _, out _, out _);
		var installed = new InstalledPackageRecord(name, "1.0.0", "https://example.invalid/guide.git", null, "commit", null,
			DateTimeOffset.UnixEpoch, 1, null, null, null, PackageKind.Softcode, null, null);
		installed.Deconstruct(out var installedId, out _, out _, out _, out _, out _, out _, out _, out _, out _, out _, out _, out _, out _);
		var snapshot = new PackageRevisionSnapshot("1.0.0", [], [], null, null, null, null);
		snapshot.Deconstruct(out var version, out _, out _, out _, out _, out _, out _);
		var navigation = new WikiNavigationDto(name, 0, [], []);
		navigation.Deconstruct(out var collection, out var total, out _, out _);
		await Assert.That(format.ToString()).IsEqualTo("1.13");
		await Assert.That(installedId).IsEqualTo(name);
		await Assert.That(version).IsEqualTo("1.0.0");
		await Assert.That(collection).IsEqualTo(name);
		await Assert.That(total).IsEqualTo(0);
	}
}
