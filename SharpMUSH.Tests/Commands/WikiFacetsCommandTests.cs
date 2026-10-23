using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Models.Wiki.Semantics;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.Wiki.Semantics;

namespace SharpMUSH.Tests.Commands;

/// <summary><c>@wiki/facets</c>: a category's values with their counts, each a link that narrows them.</summary>
public class WikiFacetsCommandTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private IWikiService WikiService => WebAppFactoryArg.Services.GetRequiredService<IWikiService>();

	private static readonly PropertyDefinition School =
		new("facettest:school", WikiFactProperties.SchemaVersion, SemanticType.Keyword, Cardinality.Single, [], "School");

	private static readonly PropertyDefinition Rank =
		new("facettest:rank", WikiFactProperties.SchemaVersion, SemanticType.Integer, Cardinality.Single, [], "Rank");

	private async Task<TestIsolationHelpers.TestPlayer> PlayerAsync()
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(WebAppFactoryArg.Services, Mediator, ConnectionService, "WikiFacets");
		await WebAppFactoryArg.Services.GetRequiredService<IRoleRegistryService>().AssignRoleToObjectAsync(player.DbRef.Number, BuiltInRoles.ApprovedSlug);
		await Mediator.Send(new InvalidateGrantsCommand(player.DbRef.Number));
		return player;
	}

	private async Task PageAsync(string category, params WikiPageFact[] facts)
	{
		var page = (await WikiService.CreateAsync(TestIsolationHelpers.GenerateUniqueName("FacetPage"), "Body.", "#1")).Expect<WikiPage>();
		_ = (await WikiService.SetMetadataAsync(page.Id, [category], true)).Expect<WikiPage>();
		_ = (await WikiService.SetFactsAsync(page.Id, facts)).Expect<WikiPage>();
	}

	/// <summary>What <paramref name="command"/> sent the player, found by the panel's title.</summary>
	private async Task<MString> RunAsync(TestIsolationHelpers.TestPlayer player, string command, string title)
	{
		await Parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain(command));
		return WebAppFactoryArg.Notifications.RawFor(player.DbRef)
			.Select(message => message switch { MString markup => markup, string text => MarkupText.Plain(text) })
			.Last(message => message.ToPlainText().Contains(title, StringComparison.Ordinal));
	}

	[Test]
	public async Task ValuesAreCountedAndEachNarrowsTheFacets()
	{
		_ = (await WikiService.SetFactPropertyAsync(School)).Expect<PropertyDefinition>();
		_ = (await WikiService.SetFactPropertyAsync(Rank)).Expect<PropertyDefinition>();
		var category = TestIsolationHelpers.GenerateUniqueName("facetcat").ToLowerInvariant();
		await PageAsync(category, new WikiPageFact(School.Id, "evocation"), new WikiPageFact(Rank.Id, "3"));
		await PageAsync(category, new WikiPageFact(School.Id, "evocation"), new WikiPageFact(Rank.Id, "1"));
		await PageAsync(category, new WikiPageFact(School.Id, "abjuration"), new WikiPageFact(Rank.Id, "1"));
		var player = await PlayerAsync();

		var all = await RunAsync(player, $"@wiki/facets {category}", "Facets:");
		var text = all.ToPlainText();
		await Assert.That(text).Contains("3 page(s).");
		await Assert.That(text).Contains("evocation (2) · abjuration (1)");
		await Assert.That(text).Contains("1 (2) · 3 (1)");
		await Assert.That(all.Render(MarkupFormat.Mxp)).Contains($"@wiki/facets/noeval {category}=facettest:school=evocation");

		var narrowed = await RunAsync(player, $"@wiki/facets {category}=facettest:school=evocation", "where facettest:school=evocation");
		await Assert.That(narrowed.ToPlainText()).Contains("2 page(s) where facettest:school=evocation.");
		await Assert.That(narrowed.ToPlainText()).Contains("1 (1) · 3 (1)");
		await Assert.That(narrowed.Render(MarkupFormat.Mxp))
			.Contains($"@wiki/query/noeval category={category}, facettest:school=evocation");
	}

	[Test]
	public async Task ASettingIsRefused()
	{
		var player = await PlayerAsync();
		await RunAsync(player, "@wiki/facets spells=sort=facettest:rank", "Facets take fact conditions only");
	}
}
