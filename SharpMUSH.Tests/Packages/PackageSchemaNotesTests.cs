using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Tests.Packages;

/// <summary>An apply names the problems in the portal documents a package writes.</summary>
public class PackageSchemaNotesTests
{
	private static PackageChangeset Writing(params string[] values) => new(
		"demo", null, "1.0.0", PackageRevisionKind.Install, [],
		[.. values.Select((value, i) => new PackageAttributeChange("main", null, $"WEB`SCHEMA`{i}", PackageAttributeAction.Create, NewValue: value))],
		[], [], [], []);

	[Test]
	public async Task A_whole_document_and_a_softcode_template_are_both_checked()
	{
		var notes = PackageInstallService.SchemaNotes(Writing(
			"""{"kind":"view","pages":[{"sections":[{"elements":[{"kind":"dial"}]}]}]}""",
			"""json_fill(lit({"kind":"form","pages":[{"next":"nowhere","sections":[]}]}),/title,%0)""")).ToList();

		await Assert.That(notes).IsEquivalentTo([
			"Portal document in main/WEB`SCHEMA`0: pages[0].sections[0].elements[0]: unknown kind \"dial\"",
			"Portal document in main/WEB`SCHEMA`1: pages[0]: next names no page \"nowhere\"",
		]);
	}

	[Test]
	public async Task Softcode_other_JSON_and_what_a_template_leaves_to_be_filled_are_not_problems()
	{
		var notes = PackageInstallService.SchemaNotes(Writing(
			"think lit(hello (world))",
			"""{"fields":{"a":{"value":1}}}""",
			"""lit({"kind":"view","pages":[}""",
			"""json_fill(lit({"kind":"view","pages":[{"sections":[{"elements":[{"kind":"keyvalue","fields":[]}]}]}],"actions":{"go":{"route":"/http/demo/act"}}}),/pages/0/sections/0/elements/0/fields,%0)""")).ToList();

		await Assert.That(notes).IsEmpty();
	}

	[Test]
	public async Task Every_shipped_package_writes_sound_documents()
	{
		var dir = new DirectoryInfo(AppContext.BaseDirectory);
		while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "examples", "packages"))) dir = dir.Parent;
		var root = Path.Combine(dir!.FullName, "examples", "packages");

		var notes = new List<string>();
		foreach (var manifest in Directory.EnumerateFiles(root, "package.yaml", SearchOption.AllDirectories))
			notes.AddRange(PackageInstallService.SchemaNotes(Writing(await File.ReadAllTextAsync(manifest))).Select(note => $"{Path.GetRelativePath(root, manifest)}: {note}"));

		await Assert.That(notes).IsEmpty();
	}
}
