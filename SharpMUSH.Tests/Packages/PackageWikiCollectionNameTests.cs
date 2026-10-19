using System.Text.Json;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Tests.Packages;

/// <summary>A collection's optional public label is authored separately from stable package identity.</summary>
public class PackageWikiCollectionNameTests
{
	private readonly PackageManifestService _service = new();

	private const string Content = """
		package: generic-rules
		version: 1.0.0
		wiki:
		  - page: main:guide
		    title: Rules Guide
		    markdown: Rules.
		""";

	private PackageManifestResult<ParsedPackageManifest> Parse(string? value, string format = "1.13") =>
		_service.ParseManifest("format: " + format + "\n" + Content + "\n"
			+ (value is null ? string.Empty : "wiki_collection_name: " + value + "\n"));

	private static string? Label(PackageManifest manifest) =>
		JsonSerializer.SerializeToElement(manifest).TryGetProperty("WikiCollectionName", out var label) ? label.GetString() : null;

	[Test]
	[Arguments("Pathfinder 2e")]
	[Arguments("  Independent Rules  ")]
	[Arguments("Règles & étoiles: 第二版")]
	public async Task AuthoredCollectionNameIsTrimmedAndIndependentOfThePackageId(string value)
	{
		var parsed = Parse(JsonSerializer.Serialize(value)).Expect<ParsedPackageManifest>();
		await Assert.That(Label(parsed.Manifest)).IsEqualTo(value.Trim());
		await Assert.That(parsed.Manifest.Name).IsEqualTo("generic-rules");
		await Assert.That(parsed.Warnings).IsEmpty();
	}

	[Test]
	public async Task CollectionNameAllowsExactlyOneHundredCharacters()
	{
		var label = new string('x', 100);
		var parsed = Parse(JsonSerializer.Serialize(label)).Expect<ParsedPackageManifest>();
		await Assert.That(Label(parsed.Manifest)).IsEqualTo(label);
		await Assert.That(parsed.Warnings).IsEmpty();
		var tooLong = Parse(JsonSerializer.Serialize(label + "x")).Expect<PackageManifestFailure>();
		await Assert.That(tooLong.Errors.Any(issue => issue.Path == "wiki_collection_name")).IsTrue();
	}

	[Test]
	[Arguments("")]
	[Arguments("   ")]
	[Arguments("Rules\nSecond line")]
	[Arguments("Rules\rSecond line")]
	[Arguments("Rules\tSecond")]
	[Arguments("Rules\u0001Second")]
	[Arguments("Rules\u007fSecond")]
	[Arguments("Rules\u0085Second")]
	[Arguments("Rules\u2028Second")]
	[Arguments("Rules\u2029Second")]
	public async Task EmptyControlAndMultilineNamesAreRefused(string value)
	{
		var failure = Parse(JsonSerializer.Serialize(value)).Expect<PackageManifestFailure>();
		await Assert.That(failure.Errors.Any(issue => issue.Path == "wiki_collection_name")).IsTrue();
	}

	[Test]
	[Arguments("[]")]
	[Arguments("{name: Rules}")]
	[Arguments("null")]
	[Arguments("|\n  First line\n  Second line")]
	public async Task CollectionNameRequiresANonemptySingleLineScalar(string value)
	{
		var failure = Parse(value).Expect<PackageManifestFailure>();
		await Assert.That(failure.Errors.Any(issue => issue.Path == "wiki_collection_name")).IsTrue();
	}

	[Test]
	[Arguments("1.4")]
	[Arguments("1.12")]
	[Arguments("1.13")]
	public async Task AbsentCollectionNamePreservesLegacyManifests(string format)
	{
		var parsed = Parse(null, format).Expect<ParsedPackageManifest>();
		await Assert.That(Label(parsed.Manifest)).IsNull();
		await Assert.That(PackageManifestWriter.Write(parsed.Manifest)).DoesNotContain("wiki_collection_name:");
		await Assert.That(parsed.Warnings).IsEmpty();
	}

	[Test]
	[Arguments("\"Ignored name\"")]
	[Arguments("[]")]
	[Arguments("|\n  First line\n  Second line")]
	public async Task OlderFormatsWarnAndIgnoreTheNewKeyIncludingItsMalformedValue(string value)
	{
		var parsed = Parse(value, "1.12").Expect<ParsedPackageManifest>();
		await Assert.That(Label(parsed.Manifest)).IsNull();
		await Assert.That(parsed.Warnings.Any(issue => issue.Path == "wiki_collection_name" && issue.Message.Contains("1.13", StringComparison.Ordinal))).IsTrue();
	}

	[Test]
	public async Task WriterPreservesCollectionNameAndItsFormatWithoutChangingIdentity()
	{
		var original = Parse("\"  Rules: a collector's guide & more  \"").Expect<ParsedPackageManifest>().Manifest;
		var yaml = PackageManifestWriter.Write(original);
		await Assert.That(yaml).Contains("wiki_collection_name:");
		await Assert.That(yaml).Contains("format: 1.13");
		var restored = _service.ParseManifest(yaml).Expect<ParsedPackageManifest>();
		await Assert.That(Label(restored.Manifest)).IsEqualTo("Rules: a collector's guide & more");
		await Assert.That(restored.Manifest.Name).IsEqualTo("generic-rules");
		await Assert.That(restored.Manifest.WikiPages.Single().Label).IsEqualTo("main:guide");
		await Assert.That(restored.Warnings).IsEmpty();
	}
}
