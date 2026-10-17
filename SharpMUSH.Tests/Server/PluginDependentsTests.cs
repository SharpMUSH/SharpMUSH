using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Server.Services;

namespace SharpMUSH.Tests.Server;

/// <summary>
/// The packages that block disabling a plugin, listed with prerequisites first so a reviewed removal can reverse
/// the order and remove each dependent before its prerequisites.
/// </summary>
public class PluginDependentsTests
{
	private static InstalledPackageRecord Installed(string id, params string[] plugins) =>
		new(id, "1.0.0", "repo", null, "commit", null, DateTimeOffset.UnixEpoch, 1, Plugins: plugins);

	[Test]
	public async Task TwoPackagesNamingThePlugin_ListTheDependencyFirst()
	{
		var installed = new[] { Installed("radio-scene", "scene"), Installed("scene", "scene") };
		var dependencies = new[] { new PackageDependencyRecord("radio-scene", "scene", ">=1.0") };

		var dependents = PluginAdministrationService.Dependents("scene", null, installed, dependencies);

		await Assert.That(dependents).IsEquivalentTo(["scene", "radio-scene"], TUnit.Assertions.Enums.CollectionOrdering.Matching);
	}

	[Test]
	public async Task DependentsOfThePluginsOwnPackage_ComeAfterIt()
	{
		var installed = new[] { Installed("scene-verse"), Installed("scene", "scene"), Installed("radio-scene", "scene") };
		var dependencies = new[]
		{
			new PackageDependencyRecord("scene-verse", "scene", ">=1.0"),
			new PackageDependencyRecord("radio-scene", "scene", ">=1.0")
		};

		var dependents = PluginAdministrationService.Dependents("scene", "scene", installed, dependencies);

		await Assert.That(dependents[0]).IsEqualTo("scene");
		await Assert.That(dependents).IsEquivalentTo(["scene", "scene-verse", "radio-scene"]);
	}
}
