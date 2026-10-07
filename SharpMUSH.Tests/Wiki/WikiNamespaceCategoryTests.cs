using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Tests.Wiki;

/// <summary>
/// A page in the character namespace is in the Character category whatever its list says; pages elsewhere get
/// nothing from their namespace.
/// </summary>
public class WikiNamespaceCategoryTests
{
	[Test]
	public async Task ABiographyIsInTheCharacterCategoryFromItsFirstWrite()
	{
		var wiki = InMemoryWikiStore.CreateService();
		var page = (await wiki.CreateAsync("Mannaz Byron", "Bio.", "#1", WikiNamespace.Character, categories: ["Nobles"])).Expect<WikiPage>();

		await Assert.That(page.Categories).IsEquivalentTo(["character", "nobles"]);
		await Assert.That((await wiki.GetByCategoryAsync("Character")).Select(p => p.Id)).IsEquivalentTo([page.Id]);
	}

	[Test]
	public async Task ClearingABiographysCategoriesKeepsCharacter()
	{
		var wiki = InMemoryWikiStore.CreateService();
		var page = (await wiki.CreateAsync("Mannaz Byron", "Bio.", "#1", WikiNamespace.Character)).Expect<WikiPage>();

		var updated = (await wiki.SetMetadataAsync(page.Id, [], published: true)).Expect<WikiPage>();

		await Assert.That(updated.Categories).IsEquivalentTo(["character"]);
	}

	[Test]
	public async Task APageOutsideTheCharacterNamespaceIsNotPutInIt()
	{
		var wiki = InMemoryWikiStore.CreateService();
		var page = (await wiki.CreateAsync("Harbour", "x", "#1", WikiNamespace.Main)).Expect<WikiPage>();

		await Assert.That(page.Categories).IsEmpty();
		await Assert.That((await wiki.SetMetadataAsync(page.Id, ["lore"], true)).Expect<WikiPage>().Categories).IsEquivalentTo(["lore"]);
	}

	[Test]
	public async Task SettingMetadataOnAMissingPageIsNotFound()
		=> await Assert.That(await InMemoryWikiStore.CreateService().SetMetadataAsync("404", [], true) is NotFound).IsTrue();

	[Test]
	public async Task PinningKeysTheNameAndRefusesABlankOne()
	{
		var wiki = InMemoryWikiStore.CreateService();

		await Assert.That((await wiki.SetCategoryPinnedAsync("Places of Note", true)).Expect<bool>()).IsTrue();
		await Assert.That(await wiki.GetPinnedCategoriesAsync()).IsEquivalentTo(["character", "places_of_note"]);
		await Assert.That(await wiki.SetCategoryPinnedAsync("  ", true) is Error<string>).IsTrue();
	}
}
