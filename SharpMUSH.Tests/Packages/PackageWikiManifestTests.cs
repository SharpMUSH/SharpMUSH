using System.Text;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Services;
using SharpMUSH.Tests.Plugins;

namespace SharpMUSH.Tests.Packages;

/// <summary>The <c>wiki:</c> block and <c>locale:</c> key of a manifest (format 1.4), and reading <c>file:</c> bodies.</summary>
public class PackageWikiManifestTests
{
	private readonly PackageManifestService _service = new();

	private const string Header = """
		format: 1.4
		package: coast-guide
		version: 1.0.0
		""";

	private PackageManifestResult<ParsedPackageManifest> Parse(string body) => _service.ParseManifest(Header + "\n" + body);

	private PackageManifest Manifest(string body) => Parse(body).Expect<ParsedPackageManifest>().Manifest;

	private IReadOnlyList<PackageManifestIssue> Errors(string body)
		=> Parse(body).Expect<PackageManifestFailure>().Issues.Where(i => i.Severity == PackageManifestIssueSeverity.Error).ToList();

	[Test]
	public async Task AContentPackNeedsNoObjects()
	{
		var manifest = Manifest("""
			locale: pt-br
			wiki:
			  - page: coast_road
			    title: Coast Road
			    categories: [Travel, Places of Note]
			    file: wiki/coast-road.md
			  - page: travel
			    namespace: category
			    title: Travel
			    pin: true
			    markdown: |
			      Roads and ferries.
			  - page: smugglers
			    namespace: help
			    title: Smugglers
			    published: false
			    requires:
			      view: [Wiki.Admin]
			      delete: wiki.admin
			    markdown: Hidden.
			""");

		await Assert.That(manifest.Objects).IsEmpty();
		await Assert.That(manifest.Locale).IsEqualTo("pt-BR");
		await Assert.That(manifest.WikiPages.Count).IsEqualTo(3);

		var road = manifest.WikiPages[0];
		await Assert.That(road.Namespace).IsEqualTo(WikiNamespace.Main);
		await Assert.That(road.File).IsEqualTo("wiki/coast-road.md");
		await Assert.That(road.Markdown).IsNull();
		await Assert.That(road.Categories).IsEquivalentTo(new[] { "Travel", "Places of Note" });

		var travel = manifest.WikiPages[1];
		await Assert.That(travel.Pin).IsTrue();
		await Assert.That(travel.Markdown).IsEqualTo("Roads and ferries.\n");

		var smugglers = manifest.WikiPages[2];
		await Assert.That(smugglers.Published).IsFalse();
		await Assert.That(smugglers.Requires.View).IsEquivalentTo(new[] { "wiki.admin" });
		await Assert.That(smugglers.Requires.Delete).IsEquivalentTo(new[] { "wiki.admin" });
		await Assert.That(smugglers.Requires.Edit).IsEmpty();
	}

	[Test]
	public async Task APackageWithoutWikiOrObjectsIsStillRefused()
		=> await Assert.That(Errors("description: nothing").Any(e => e.Path == "objects")).IsTrue();

	[Test]
	public async Task AnUndeclaredCustomPermissionIsAWarning()
	{
		var parsed = Parse("""
			wiki:
			  - page: smugglers
			    title: Smugglers
			    requires:
			      view: [coast.gm]
			    markdown: Hidden.
			""").Expect<ParsedPackageManifest>();

		await Assert.That(parsed.Warnings.Single().Message).Contains("coast.gm");
	}

	[Test]
	public async Task ADeclaredCustomPermissionIsNoWarning()
	{
		var parsed = Parse("""
			categories:
			  permissions:
			    - name: Coast
			      description: The coast pack.
			permissions:
			  - name: coast.gm
			    category: Coast
			wiki:
			  - page: smugglers
			    title: Smugglers
			    requires:
			      view: [coast.gm]
			    markdown: Hidden.
			""").Expect<ParsedPackageManifest>();

		await Assert.That(parsed.Warnings).IsEmpty();
	}

	[Test]
	[Arguments("page: Coast Road\n    title: T\n    markdown: x", "is not a page slug")]
	[Arguments("page: coast/road\n    title: T\n    markdown: x", "is not a page slug")]
	[Arguments("page: road\n    namespace: character\n    title: T\n    markdown: x", "cannot ship character pages")]
	[Arguments("page: road\n    namespace: lore\n    title: T\n    markdown: x", "is not a namespace")]
	[Arguments("page: road\n    title: T", "exactly one of 'file' and 'markdown'")]
	[Arguments("page: road\n    title: T\n    file: a.md\n    markdown: x", "exactly one of 'file' and 'markdown'")]
	[Arguments("page: road\n    title: T\n    file: ../secrets.md", "is not a file inside the package directory")]
	[Arguments("page: road\n    title: T\n    file: /etc/passwd", "is not a file inside the package directory")]
	[Arguments("page: road\n    title: T\n    pin: true\n    markdown: x", "Only a category page")]
	[Arguments("page: road\n    title: T\n    published: maybe\n    markdown: x", "must be true or false")]
	[Arguments("page: road\n    markdown: x", "needs a title")]
	[Arguments("page: road\n    title: T\n    requires:\n      read: [wiki.admin]\n    markdown: x", "is not an action")]
	[Arguments("page: road\n    title: T\n    requires:\n      view: [\"not a permission!\"]\n    markdown: x", "is not a permission")]
	public async Task ABadPageIsRefused(string entry, string expected)
	{
		var errors = Errors($"wiki:\n  - {entry}\n");

		await Assert.That(errors.Any(e => e.Message.Contains(expected, StringComparison.Ordinal))).IsTrue()
			.Because(string.Join("; ", errors));
	}

	/// <summary>A key a later format may add is a warning, so a newer manifest still reads.</summary>
	[Test]
	public async Task AnUnknownPageKeyIsAWarning()
	{
		var parsed = Parse("wiki:\n  - page: road\n    title: T\n    colour: blue\n    markdown: x\n").Expect<ParsedPackageManifest>();

		await Assert.That(parsed.Warnings.Single().Message).Contains("colour");
	}

	[Test]
	public async Task APageListedTwiceIsRefused()
	{
		var errors = Errors("""
			wiki:
			  - page: road
			    title: Road
			    markdown: one
			  - page: road
			    title: Road again
			    markdown: two
			""");

		await Assert.That(errors.Any(e => e.Message.Contains("more than once"))).IsTrue();
	}

	[Test]
	public async Task TheSameSlugInTwoNamespacesIsTwoPages()
		=> await Assert.That(Manifest("""
			wiki:
			  - page: travel
			    title: Travel
			    markdown: one
			  - page: travel
			    namespace: category
			    title: Travel
			    markdown: two
			""").WikiPages.Count).IsEqualTo(2);

	[Test]
	public async Task ABadLocaleIsRefused()
		=> await Assert.That(Errors("locale: \"not a locale!\"\nwiki:\n  - page: road\n    title: Road\n    markdown: x\n")
			.Any(e => e.Path == "locale")).IsTrue();

	[Test]
	public async Task APluginPackageCannotShipPages()
	{
		var yaml = PluginPackageFixture.Yaml("wiki-plugin", "1.0.0") + "\nwiki:\n  - page: road\n    title: Road\n    markdown: x\n";
		var errors = _service.ParseManifest(yaml).Expect<PackageManifestFailure>().Issues;

		await Assert.That(errors.Any(e => e.Path == "wiki" && e.Severity == PackageManifestIssueSeverity.Error)).IsTrue();
	}

	[Test]
	public async Task TheWriterRoundTripsAContentPack()
	{
		var manifest = Manifest("""
			locale: en
			wiki:
			  - page: coast_road
			    title: Coast Road
			    categories: [Travel]
			    file: wiki/coast-road.md
			  - page: travel
			    namespace: category
			    title: Travel
			    pin: true
			    markdown: |
			      Roads and ferries.

			        An indented line.
			  - page: notes
			    namespace: help
			    title: "Notes: the coast"
			    published: false
			    requires:
			      edit: [wiki.admin]
			    markdown: " starts with a space"
			""");

		var yaml = PackageManifestWriter.Write(manifest);
		var again = _service.ParseManifest(yaml).Expect<ParsedPackageManifest>().Manifest;

		await Assert.That(yaml).DoesNotContain("objects:");
		await Assert.That(again.Locale).IsEqualTo("en");
		await Assert.That(again.WikiPages.Select(Describe)).IsEquivalentTo(manifest.WikiPages.Select(Describe));
	}

	/// <summary>Every field of a page, as one string: the record's lists compare by reference.</summary>
	private static string Describe(PackageWikiPageSpec page)
		=> string.Join("|", page.Page, page.Namespace, page.Title, string.Join(",", page.Categories), page.File, page.Markdown,
			string.Join(",", page.Requires.View), string.Join(",", page.Requires.Edit), string.Join(",", page.Requires.Delete),
			page.Published, page.Pin);

	[Test]
	public async Task FileBodiesAreReadFromThePackageDirectory()
	{
		var dir = Directory.CreateTempSubdirectory("wiki-pack-");
		try
		{
			Directory.CreateDirectory(Path.Combine(dir.FullName, "wiki"));
			await File.WriteAllTextAsync(Path.Combine(dir.FullName, "wiki", "coast-road.md"), "﻿# Coast Road\r\nText.\r\n");
			var manifest = Manifest("wiki:\n  - page: coast_road\n    title: Coast Road\n    file: wiki/coast-road.md\n");

			await Assert.That(PackageWikiBodies.NeedsFiles(manifest)).IsTrue();
			var loaded = (await PackageWikiBodies.LoadAsync(manifest, new DirectoryPackageFileSource(dir.FullName))).Expect<PackageManifest>();

			await Assert.That(loaded.WikiPages.Single().Markdown).IsEqualTo("# Coast Road\nText.\n");
			await Assert.That(PackageWikiBodies.NeedsFiles(loaded)).IsFalse();
		}
		finally
		{
			dir.Delete(recursive: true);
		}
	}

	[Test]
	public async Task AMissingTooLargeOrNonTextBodyIsRefused()
	{
		var dir = Directory.CreateTempSubdirectory("wiki-pack-");
		try
		{
			await File.WriteAllBytesAsync(Path.Combine(dir.FullName, "binary.md"), [0xC3, 0x28]);
			await File.WriteAllBytesAsync(Path.Combine(dir.FullName, "huge.md"),
				Encoding.UTF8.GetBytes(new string('x', PackageWikiBodies.MaxBodyBytes + 1)));
			var manifest = Manifest("""
				wiki:
				  - page: missing
				    title: Missing
				    file: missing.md
				  - page: binary
				    title: Binary
				    file: binary.md
				  - page: huge
				    title: Huge
				    file: huge.md
				""");

			var error = (await PackageWikiBodies.LoadAsync(manifest, new DirectoryPackageFileSource(dir.FullName))).Expect<Error<string>>();

			await Assert.That(error.Value).Contains("missing.md is not in the package");
			await Assert.That(error.Value).Contains("binary.md is not UTF-8 text");
			await Assert.That(error.Value).Contains("huge.md is larger than");
			await Assert.That((await PackageWikiBodies.LoadAsync(manifest, null)).Value).IsTypeOf<Error<string>>();
		}
		finally
		{
			dir.Delete(recursive: true);
		}
	}

	[Test]
	public async Task ADirectorySourceNeverReadsOutsideItsDirectory()
	{
		var outer = Directory.CreateTempSubdirectory("wiki-outer-");
		try
		{
			var inner = Directory.CreateDirectory(Path.Combine(outer.FullName, "pack"));
			await File.WriteAllTextAsync(Path.Combine(outer.FullName, "secret.md"), "secret");
			var files = new DirectoryPackageFileSource(inner.FullName);

			await Assert.That(await files.ReadFileAsync("../secret.md")).IsNull();
			await Assert.That(await files.ReadFileAsync(Path.Combine(outer.FullName, "secret.md"))).IsNull();
			await Assert.That(PackageFilePath.Normalize("wiki/./a.md")).IsNull();
			await Assert.That(PackageFilePath.Normalize(" wiki/a.md ")).IsEqualTo("wiki/a.md");
		}
		finally
		{
			outer.Delete(recursive: true);
		}
	}
}
