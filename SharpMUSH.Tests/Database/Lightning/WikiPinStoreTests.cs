using SharpMUSH.Library;

namespace SharpMUSH.Tests.Database.Lightning;

/// <summary>The categories pinned to the wiki home, in the Lightning store.</summary>
public class WikiPinStoreTests : LightningDatabaseFixture
{
	private IWikiStore Wiki => Db;

	[Test]
	public async Task ANewWorldPinsCharacter()
		=> await Assert.That(await Wiki.GetPinnedCategoriesAsync()).IsEquivalentTo(["character"]);

	[Test]
	public async Task PinsSurviveAReopenAndSayWhetherAnythingChanged()
	{
		await Assert.That(await Wiki.SetCategoryPinnedAsync("lore", true)).IsTrue();
		await Assert.That(await Wiki.SetCategoryPinnedAsync("lore", true)).IsFalse();
		await Assert.That(await Wiki.SetCategoryPinnedAsync("character", false)).IsTrue();
		await Assert.That(await Wiki.SetCategoryPinnedAsync("character", false)).IsFalse();
		await ReopenAsync();

		await Assert.That(await Wiki.GetPinnedCategoriesAsync()).IsEquivalentTo(["lore"]);
	}
}
