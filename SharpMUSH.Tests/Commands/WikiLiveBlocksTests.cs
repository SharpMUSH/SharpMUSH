using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Implementation.Commands.WikiCommand;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// A page's live listing blocks (<c>::: category</c>, <c>::: pagelist</c>, <c>::: recent</c>) list their pages in
/// <c>@wiki</c>, <c>rendermarkdown()</c> and <c>rendermarkdowncustom()</c>, and a category's page lists its members.
/// </summary>
public class WikiLiveBlocksTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IMUSHCodeParser FunctionParser => WebAppFactoryArg.FunctionParser;
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private IWikiService WikiService => WebAppFactoryArg.Services.GetRequiredService<IWikiService>();

	private async Task<TestIsolationHelpers.TestPlayer> PlayerAsync(string prefix)
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(WebAppFactoryArg.Services, Mediator, ConnectionService, prefix);
		await WebAppFactoryArg.Services.GetRequiredService<IRoleRegistryService>().AssignRoleToObjectAsync(player.DbRef.Number, BuiltInRoles.ApprovedSlug);
		await Mediator.Send(new InvalidateGrantsCommand(player.DbRef.Number));
		return player;
	}

	private async Task<WikiPage> PageAsync(string prefix, string markdown, string? category, bool published = true,
		WikiNamespace ns = WikiNamespace.Main)
	{
		var page = (await WikiService.CreateAsync(TestIsolationHelpers.GenerateUniqueName(prefix), markdown, "#1", ns)).Expect<WikiPage>();
		return (await WikiService.SetMetadataAsync(page.Id, category is null ? [] : [category], published)).Expect<WikiPage>();
	}

	/// <summary>The latest <c>@wiki</c> view of <paramref name="page"/> the player was sent, as it was sent.</summary>
	private async Task<MString> ViewAsync(TestIsolationHelpers.TestPlayer player, WikiPage page)
	{
		await Parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain($"@wiki {page.Namespace}:{page.Slug}"));
		return WebAppFactoryArg.Notifications.RawFor(player.DbRef)
			.Select(message => message switch { MString markup => markup, string text => MarkupText.Plain(text) })
			.Last(message => message.ToPlainText().Contains($"Wiki: {page.Title} [{page.Namespace}]", StringComparison.Ordinal));
	}

	[Test]
	public async Task ACategoryBlockListsItsPagesWhenThePageIsRead()
	{
		var player = await PlayerAsync("WikiLiveCategory");
		var category = TestIsolationHelpers.GenerateUniqueName("livecat");
		var first = await PageAsync("LiveFirst", "Body.", category);
		var draft = await PageAsync("LiveDraft", "Body.", category, published: false);
		var listing = await PageAsync("LiveListing", $"Members:\n\n::: category {category}\n:::\n\n::: pagelist nowhere\n:::", null);
		var later = await PageAsync("LiveLater", "Body.", category);

		var view = await ViewAsync(player, listing);
		var text = view.ToPlainText();
		// Answered when read, so a page filed after the listing was written is listed too.
		await Assert.That(text).Contains(first.Title);
		await Assert.That(text).Contains(later.Title);
		await Assert.That(text).DoesNotContain(draft.Title);
		await Assert.That(text).DoesNotContain("live listing");
		await Assert.That(text).Contains("[No namespace nowhere.]");
		await Assert.That(view.Render(MarkupFormat.Mxp)).Contains($"@wiki main:{later.Slug}");
	}

	[Test]
	public async Task ABlockListsTwentyPagesThenLinksToTheRest()
	{
		var player = await PlayerAsync("WikiLiveMore");
		var category = TestIsolationHelpers.GenerateUniqueName("livemore");
		var members = new List<WikiPage>();
		for (var i = 0; i <= LiveBlocksWiki.MaxPages; i++) members.Add(await PageAsync("LiveMember", "Body.", category));
		var listing = await PageAsync("LiveMoreListing", $"::: category {category}\n:::", null);

		var view = await ViewAsync(player, listing);
		await Assert.That(members.Count(m => view.ToPlainText().Contains(m.Title))).IsEqualTo(LiveBlocksWiki.MaxPages);
		await Assert.That(view.ToPlainText()).Contains("… and more");
		await Assert.That(view.Render(MarkupFormat.Mxp)).Contains($"@wiki/category {category}");
	}

	[Test]
	public async Task ARecentBlockListsPagesWithTheirDates()
	{
		var player = await PlayerAsync("WikiLiveRecent");
		var listing = await PageAsync("LiveRecent", "::: recent 3\n:::", null);

		var text = (await ViewAsync(player, listing)).ToPlainText();
		await Assert.That(text).Contains("Edited");
		await Assert.That(text).DoesNotContain("live listing");
	}

	[Test]
	public async Task ACategorysPageListsItsMembersUnderItsText()
	{
		var player = await PlayerAsync("WikiLiveCategoryPage");
		var category = TestIsolationHelpers.GenerateUniqueName("livepage");
		var categoryPage = (await WikiService.CreateAsync(category, "About these pages.", "#1", WikiNamespace.Category)).Expect<WikiPage>();
		await WikiService.SetMetadataAsync(categoryPage.Id, [], true);
		var member = await PageAsync("LivePageMember", "Body.", category);

		var text = (await ViewAsync(player, categoryPage)).ToPlainText();
		await Assert.That(text).Contains("About these pages.");
		await Assert.That(text).Contains("1 page(s), 0 subcategory(ies)");
		await Assert.That(text).Contains($"main:{member.Slug}");
	}

	[Test]
	public async Task RenderMarkdownListsACategorysPublishedPages()
	{
		var category = TestIsolationHelpers.GenerateUniqueName("livefn");
		var member = await PageAsync("LiveFnMember", "Body.", category);
		var draft = await PageAsync("LiveFnDraft", "Body.", category, published: false);

		var result = (await FunctionParser.FunctionParse(MarkupText.Plain($"rendermarkdown(::: category {category}%r:::)")))!.Message!;
		await Assert.That(result.ToPlainText()).Contains(member.Title);
		// Softcode never sees a draft, its executor's own or a wizard's.
		await Assert.That(result.ToPlainText()).DoesNotContain(draft.Title);
		await Assert.That(result.Render(MarkupFormat.Mxp)).Contains($"@wiki main:{member.Slug}");
	}

	[Test]
	public async Task RenderMarkdownCustomHandsTheListingToTheContainerTemplate()
	{
		var category = TestIsolationHelpers.GenerateUniqueName("livetpl");
		var member = await PageAsync("LiveTplMember", "Body.", category);
		var template = (await FunctionParser.EvaluateAsync(MarkupText.Plain("create(LiveBlockTemplateObj)"))).ToString().Trim();
		await FunctionParser.CommandParse(MarkupText.Plain($"&RENDERMARKUP`CONTAINER {template}=%0 %1 -> %3"));

		var result = (await FunctionParser.FunctionParse(MarkupText.Plain($"rendermarkdowncustom(::: category {category}%r:::,{template})")))!.Message!
			.ToPlainText();
		await Assert.That(result).StartsWith($"category {category} -> ");
		await Assert.That(result).Contains(member.Title);
	}
}
