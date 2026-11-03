using SharpMUSH.Client.Models;
using SharpMUSH.Library.Models.Packages;

namespace SharpMUSH.Tests.BUnit.Pages;

/// <summary>How the package library gathers a remote's packages.</summary>
public class PackageLibraryGroupsTests
{
	private static PackageRepoEntry Entry(string id, string? category, string? subcategory = null, bool bridge = false,
		params string[] dependsOn)
		=> new(id, id, "1.0.0", null, [], Title: null, Category: category, Subcategory: subcategory,
			Contents: PackageContents.Softcode, Bridge: bridge, DependsOn: dependsOn);

	[Test]
	public async Task GroupsByCategoryThenSubcategory_WithUncategorizedLast()
	{
		var groups = PackageLibraryGroups.For(
		[
			Entry("who-where", null),
			Entry("sheet-fate", "Game System", "Fate"),
			Entry("pf2e", "Game System", "Pathfinder 2e"),
			Entry("roll-tables", "game system"),
			Entry("sheet-fate-wiki", "Game System", "fate"),
		], "Uncategorized");

		await Assert.That(groups.Select(g => g.Title)).IsEquivalentTo(new[] { "Game System", "Uncategorized" },
			TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await Assert.That(groups[0].Subcategories.Select(s => (s.Title, s.Count))).IsEquivalentTo(new (string?, int)[]
		{
			(null, 1), ("Fate", 2), ("Pathfinder 2e", 1)
		}, TUnit.Assertions.Enums.CollectionOrdering.Matching);
	}

	[Test]
	public async Task ABridgeIsListedUnderThePackageItExtends()
	{
		var groups = PackageLibraryGroups.For(
		[
			Entry("sheet-fate", "Game System", "Fate"),
			Entry("sheet-fate-help", "Game System", "Fate", true, "sheet-fate", "plus-help"),
			Entry("duties-fate", "Game System", "Fate", true, "duties", "sheet-fate"),
			Entry("atomic-robo", "Game System", "Fate", false, "sheet-fate"),
			Entry("scene-help", "Scenes", null, true, "scene", "plus-help"),
		], "Uncategorized");

		var fate = groups.Single(g => g.Title == "Game System").Subcategories.Single().Families;
		await Assert.That(fate.Select(f => f.Package.PackageId)).IsEquivalentTo(new[] { "atomic-robo", "sheet-fate" },
			TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await Assert.That(fate.Single(f => f.Package.PackageId == "sheet-fate").AddOns.Select(a => a.PackageId))
			.IsEquivalentTo(new[] { "duties-fate", "sheet-fate-help" }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await Assert.That(groups.Single(g => g.Title == "Scenes").Subcategories.Single().Families.Single().Package.PackageId)
			.IsEqualTo("scene-help");
	}

	[Test]
	public async Task SearchReadsTheTitleIdDescriptionAndSubcategory()
	{
		var entry = Entry("sheet-fate", "Game System", "Fate") with { Title = "Fate sheet", Description = "Aspects and stunts." };

		await Assert.That(PackageLibraryGroups.Matches(entry, "stunts")).IsTrue();
		await Assert.That(PackageLibraryGroups.Matches(entry, "FATE")).IsTrue();
		await Assert.That(PackageLibraryGroups.Matches(entry, "pathfinder")).IsFalse();
	}
}
