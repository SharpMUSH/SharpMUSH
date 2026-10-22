using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Services;
using SharpMUSH.Tests.Database.Lightning;
using SharpMUSH.Tests.Server;

namespace SharpMUSH.Tests.Wiki;

/// <summary>
/// Category names come from every published category page, not the first thousand: a name past any
/// fixed window would fall back to its key and drop out of localized search.
/// </summary>
public class WikiCategoryNameCompletenessTests : LightningDatabaseFixture
{
	private const int Categories = 1100;

	private WikiStoreService Wiki => new(Db, new WikiMarkdigPipeline());

	private WikiLocalizationService Localization()
	{
		var monitor = Substitute.For<IOptionsMonitor<SharpMUSHOptions>>();
		monitor.CurrentValue.Returns(TestSharpMushOptions.Create(wikiDefaultLocale: "en"));
		return new WikiLocalizationService(Wiki, new WikiLocaleResolver(monitor), NullLogger<WikiLocalizationService>.Instance);
	}

	private async Task<WikiPage> CategoryPage(int n, string title, bool published = true)
	{
		var page = (await Wiki.CreateAsync(title, "body", "#1", WikiNamespace.Category, "en", slug: $"c{n:D4}")).Expect<WikiPage>();
		return published ? page : (await Wiki.SetMetadataAsync(page.Id, [], published: false)).Expect<WikiPage>();
	}

	[Test]
	public async Task CategoryNamesPastTheFirstThousandAreLocalizedAndDraftsStayOut()
	{
		for (var n = 0; n < Categories - 3; n++)
			await CategoryPage(n, $"Topic {n:D4}");
		var translated = await CategoryPage(Categories - 3, "Late Topic");
		await Wiki.UpsertTranslationAsync(translated.Id, "fr", "Sujet tardif", "corps", "#1", null,
			published: true, expectedRevisionNumber: null);
		var draftTranslation = await CategoryPage(Categories - 2, "Last Topic");
		await Wiki.UpsertTranslationAsync(draftTranslation.Id, "fr", "Sujet brouillon", "corps", "#1", null,
			published: false, expectedRevisionNumber: null);
		await CategoryPage(Categories - 1, "Draft Topic", published: false);
		await ReopenAsync();

		var french = await Localization().GetCategoryNamesAsync("fr", WikiVisibility.PublishedOnly);
		var english = await Localization().GetCategoryNamesAsync("en", WikiVisibility.PublishedOnly);

		await Assert.That(french.Count).IsEqualTo(Categories - 1);
		await Assert.That(french[$"c{Categories - 3:D4}"]).IsEqualTo("Sujet tardif");
		await Assert.That(english[$"c{Categories - 3:D4}"]).IsEqualTo("Late Topic");
		await Assert.That(WikiHelpers.CategoryLabel($"c{Categories - 3:D4}", french)).IsEqualTo("Sujet tardif");
		await Assert.That(french[$"c{Categories - 2:D4}"]).IsEqualTo("Last Topic");
		await Assert.That(french.ContainsKey($"c{Categories - 1:D4}")).IsFalse();
	}
}
