using SharpMUSH.Client.Models.Applications;
using SharpMUSH.Client.Services;

namespace SharpMUSH.Tests.BUnit.Services;

/// <summary>
/// A page composes the game's own panels for its layout scope from the catalog (README §7.4): the Play
/// page asks for the "play" apps for its zones and its sidebar.
/// </summary>
public class ApplicationCatalogScopeTests
{
	private static PortalApplication App(string slug, string? scope, int order = 0) =>
		new(slug, slug, null, "Widget", $"http/{slug}/schema", null, null, "Player", null, ["RightSidebar"], order,
			Scope: scope);

	[TUnit.Core.Test]
	public async Task ForScope_ListsOnlyThatScope_CaseInsensitive()
	{
		var catalog = new ApplicationCatalog([App("weather", "play"), App("census", "PLAY"), App("hero", "home"), App("plain", null)]);

		var play = catalog.ForScope("play").Select(a => a.Slug).ToList();

		await Assert.That(play).IsEquivalentTo(["weather", "census"]);
	}

	[TUnit.Core.Test]
	public async Task ForScope_OrdersByOrderThenSlug()
	{
		var catalog = new ApplicationCatalog([App("zeta", "play", 1), App("beta", "play", 5), App("alpha", "play", 1)]);

		var play = catalog.ForScope("play").Select(a => a.Slug).ToList();

		await Assert.That(play).IsEquivalentTo(["alpha", "zeta", "beta"], TUnit.Assertions.Enums.CollectionOrdering.Matching);
	}

	[TUnit.Core.Test]
	public async Task ForScope_UnknownScope_IsEmpty()
	{
		var catalog = new ApplicationCatalog([App("weather", "play")]);

		await Assert.That(catalog.ForScope("wiki-index")).IsEmpty();
	}
}
