using SharpMUSH.Library;
using TUnit.Assertions.Enums;

namespace SharpMUSH.Tests.Database.Lightning;

/// <summary>The categories pinned to the wiki home, in the Lightning store.</summary>
public class WikiPinStoreTests : LightningDatabaseFixture
{
	private IWikiStore Wiki => Db;

	[Test]
	public async Task ANewWorldPinsCharacterAndHelp()
		=> await Assert.That(await Wiki.GetPinnedCategoriesAsync()).IsEquivalentTo(["character", "help"]);

	[Test]
	public async Task PinsSurviveAReopenAndSayWhetherAnythingChanged()
	{
		await Assert.That(await Wiki.SetCategoryPinnedAsync("lore", true)).IsTrue();
		await Assert.That(await Wiki.SetCategoryPinnedAsync("lore", true)).IsFalse();
		await Assert.That(await Wiki.SetCategoryPinnedAsync("character", false)).IsTrue();
		await Assert.That(await Wiki.SetCategoryPinnedAsync("character", false)).IsFalse();
		await Assert.That(await Wiki.SetCategoryPinnedAsync("help", false)).IsTrue();
		await ReopenAsync();

		await Assert.That(await Wiki.GetPinnedCategoriesAsync()).IsEquivalentTo(["lore"]);
	}

	[Test]
	public async Task ANewPinGoesLastAndAnOrderSurvivesAReopen()
	{
		await Wiki.SetCategoryPinnedAsync("lore", true);
		await Wiki.SetCategoryPinnedAsync("beasts", true);
		await Assert.That(await Wiki.GetPinnedCategoriesAsync())
			.IsEquivalentTo(["character", "help", "lore", "beasts"], CollectionOrdering.Matching)
			.Because("the seeded pins have no place and come first by key; each new pin goes last");

		await Assert.That(await Wiki.SetPinnedCategoryOrderAsync(["beasts", "help", "lore", "character"])).IsTrue();
		await Assert.That(await Wiki.SetPinnedCategoryOrderAsync(["beasts", "help", "lore", "character"])).IsFalse();
		await ReopenAsync();

		await Assert.That(await Wiki.GetPinnedCategoriesAsync())
			.IsEquivalentTo(["beasts", "help", "lore", "character"], CollectionOrdering.Matching);
	}

	[Test]
	public async Task AnOrderPassesOverUnpinnedKeysAndKeepsTheRestAfterTheOnesItNames()
	{
		await Wiki.SetCategoryPinnedAsync("lore", true);

		await Assert.That(await Wiki.SetPinnedCategoryOrderAsync(["lore", "ghosts"])).IsTrue();

		await Assert.That(await Wiki.GetPinnedCategoriesAsync())
			.IsEquivalentTo(["lore", "character", "help"], CollectionOrdering.Matching);
	}

	[Test]
	public async Task UnpinningKeepsTheOthersInOrderAndRepinningGoesLast()
	{
		await Wiki.SetPinnedCategoryOrderAsync(["help", "character"]);
		await Wiki.SetCategoryPinnedAsync("lore", true);
		await Wiki.SetCategoryPinnedAsync("help", false);
		await Wiki.SetCategoryPinnedAsync("help", true);

		await Assert.That(await Wiki.GetPinnedCategoriesAsync())
			.IsEquivalentTo(["character", "lore", "help"], CollectionOrdering.Matching);
	}
}
