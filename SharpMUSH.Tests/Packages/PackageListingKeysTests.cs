using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Tests.Packages;

/// <summary>
/// <c>subcategory:</c> and <c>bridge:</c> (format 1.20): where the package library lists a package, read,
/// gated on the format, checked, and written back.
/// </summary>
public class PackageListingKeysTests
{
	private static string Yaml(string lines, string format = "\"1.20\"", string depends = """
		depends:
		  - package: sheet-fate
		    version: ">=1.0"
		  - package: plus-help
		    version: ">=1.0"
		""")
		=> $"""
			format: {format}
			package: sheet-fate-help
			title: "Fate sheet: +help topics"
			version: 1.0.1
			category: Game System
			{lines}
			{depends}
			objects:
			  - ref: core
			    type: thing
			    name: Bridge Core
			""";

	private static PackageManifestResult<ParsedPackageManifest> Parse(string yaml)
		=> new PackageManifestService().ParseManifest(yaml);

	[Test]
	public async Task BothKeysAreRead()
	{
		var manifest = Parse(Yaml("subcategory: \"  Fate   Core \"\nbridge: true")).Expect<ParsedPackageManifest>().Manifest;

		await Assert.That(manifest.Subcategory).IsEqualTo("Fate Core");
		await Assert.That(manifest.Bridge).IsTrue();
	}

	[Test]
	public async Task NeitherKeyMeansNoSubcategoryAndNoBridge()
	{
		var manifest = Parse(Yaml("")).Expect<ParsedPackageManifest>().Manifest;

		await Assert.That(manifest.Subcategory).IsNull();
		await Assert.That(manifest.Bridge).IsFalse();
	}

	[Test]
	public async Task AnEarlierFormatIgnoresBothWithWarnings()
	{
		var parsed = Parse(Yaml("subcategory: Fate\nbridge: true", "\"1.19\"")).Expect<ParsedPackageManifest>();

		await Assert.That(parsed.Manifest.Subcategory).IsNull();
		await Assert.That(parsed.Manifest.Bridge).IsFalse();
		await Assert.That(parsed.Warnings.Select(w => w.Message)).IsEquivalentTo(new[]
		{
			"'subcategory' needs format 1.20 or later and is ignored; set format: \"1.20\".",
			"'bridge' needs format 1.20 or later and is ignored; set format: \"1.20\"."
		});
	}

	[Test]
	[Arguments("bridge: sometimes")]
	[Arguments("subcategory: This subcategory name runs on past forty characters")]
	public async Task ABadValueIsRefused(string lines)
	{
		await Assert.That(Parse(Yaml(lines)).Expect<PackageManifestFailure>().Errors).IsNotEmpty();
	}

	[Test]
	public async Task ABridgeNeedsBothHalvesInDepends()
	{
		var failure = Parse(Yaml("bridge: true", depends: """
			depends:
			  - package: sheet-fate
			    version: ">=1.0"
			""")).Expect<PackageManifestFailure>();

		await Assert.That(failure.Errors.Single().Message).IsEqualTo("A bridge joins two packages; list both in 'depends'.");
	}

	[Test]
	public async Task TheWriterWritesBothBack()
	{
		var manifest = Parse(Yaml("subcategory: Fate\nbridge: true")).Expect<ParsedPackageManifest>().Manifest;

		var again = Parse(PackageManifestWriter.Write(manifest)).Expect<ParsedPackageManifest>().Manifest;

		await Assert.That(again.Subcategory).IsEqualTo("Fate");
		await Assert.That(again.Bridge).IsTrue();
	}

	[Test]
	public async Task ABrowseEntryCarriesWhatTheLibraryListsBy()
	{
		var manifest = Parse(Yaml("subcategory: Fate\nbridge: true")).Expect<ParsedPackageManifest>().Manifest;

		var entry = PackageRepoEntry.From("sheet-fate-help", manifest, []);

		await Assert.That(entry.Title).IsEqualTo("Fate sheet: +help topics");
		await Assert.That(entry.Category).IsEqualTo("Game System");
		await Assert.That(entry.Subcategory).IsEqualTo("Fate");
		await Assert.That(entry.Bridge).IsTrue();
		await Assert.That(entry.Contents).IsEqualTo(PackageContents.Softcode);
		await Assert.That(entry.DependsOn).IsEquivalentTo(new[] { "sheet-fate", "plus-help" });
	}
}
