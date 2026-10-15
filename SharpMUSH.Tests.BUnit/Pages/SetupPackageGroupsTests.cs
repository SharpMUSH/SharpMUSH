using SharpMUSH.Client.Models;
using SharpMUSH.Library.Models.Portal.Setup;

namespace SharpMUSH.Tests.BUnit.Pages;

/// <summary>How the setup wizard's package step gathers the bundled packages.</summary>
public class SetupPackageGroupsTests
{
	private static BundledPackageState Package(string id, string? category, params string[] dependsOn)
		=> new(id, "", Installed: false, Requires: null, Available: true, dependsOn, Category: category);

	[Test]
	public async Task GroupsByCategory_InTheServersOrder_WithUncategorizedLast()
	{
		var groups = SetupPackageGroups.For(
		[
			Package("loose", null),
			Package("scene", "Scenes"),
			Package("named", "uncategorized"),
			Package("jobs", "Staff"),
			Package("scene-verse", "scenes", "scene"),
		], "Uncategorized");

		await Assert.That(groups.Select(g => g.Title)).IsEquivalentTo(new[] { "Scenes", "Staff", "Uncategorized" }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await Assert.That(groups[0].Packages.Select(p => p.Package.Id)).IsEquivalentTo(new[] { "scene", "scene-verse" }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await Assert.That(groups[2].Packages.Select(p => p.Package.Id)).IsEquivalentTo(new[] { "loose", "named" }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
	}

	[Test]
	public async Task APackageThatBuildsOnAnotherOfItsGroup_IsListedUnderIt()
	{
		var group = SetupPackageGroups.For(
		[
			Package("health", "Staff"),
			Package("jobs", "Staff"),
			Package("jobs-app", "Staff", "jobs"),
			Package("health-jobs", "Staff", "health", "jobs"),
			Package("scene", "Scenes"),
			Package("timeline", "Staff", "scene"),
		], "Uncategorized").Single(g => g.Title == "Staff");

		await Assert.That(group.Packages.Select(p => (p.Package.Id, p.AddOn))).IsEquivalentTo(new[]
		{
			("health", false), ("health-jobs", true), ("jobs", false), ("jobs-app", true), ("timeline", false),
		}, TUnit.Assertions.Enums.CollectionOrdering.Matching);
	}

	[Test]
	public async Task AnAddOnOfAnAddOn_IsListedUnderTheFirstPackage()
	{
		var group = SetupPackageGroups.For(
		[
			Package("scene", "Scenes"),
			Package("highlights", "Scenes", "scene"),
			Package("time", "Scenes"),
			Package("highlights-app", "Scenes", "highlights"),
		], "Uncategorized").Single();

		await Assert.That(group.Packages.Select(p => (p.Package.Id, p.AddOn))).IsEquivalentTo(new[]
		{
			("scene", false), ("highlights", true), ("highlights-app", true), ("time", false),
		}, TUnit.Assertions.Enums.CollectionOrdering.Matching);
	}
}
