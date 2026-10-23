using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Implementation.Commands.WikiCommand;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Models.Wiki.Semantics;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.Wiki.Semantics;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// <c>@wiki &lt;page&gt;</c> shows the page's facts under their labels, each value a link, and the pages whose facts
/// name it.
/// </summary>
// Kept apart from the test that rebuilds the shared world's fact index, which would leave "Named by" empty.
[NotInParallel("wiki-fact-index")]
public class WikiViewFactsTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private IWikiService WikiService => WebAppFactoryArg.Services.GetRequiredService<IWikiService>();

	private static readonly PropertyDefinition Mentor =
		new("viewtest:mentor", WikiFactProperties.SchemaVersion, SemanticType.PageRef, Cardinality.Multiple, [], "Mentor");

	private static readonly PropertyDefinition School =
		new("viewtest:school", WikiFactProperties.SchemaVersion, SemanticType.Keyword, Cardinality.Single, [], "School");

	[Before(Test)]
	public async Task DefineFactProperties()
	{
		_ = (await WikiService.SetFactPropertyAsync(Mentor)).Expect<PropertyDefinition>();
		_ = (await WikiService.SetFactPropertyAsync(School)).Expect<PropertyDefinition>();
		await WebAppFactoryArg.Services.GetRequiredService<IWikiFactIndexBuilder>().Idle;
	}

	private async Task<TestIsolationHelpers.TestPlayer> PlayerAsync(string prefix)
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(WebAppFactoryArg.Services, Mediator, ConnectionService, prefix);
		await WebAppFactoryArg.Services.GetRequiredService<IRoleRegistryService>().AssignRoleToObjectAsync(player.DbRef.Number, BuiltInRoles.ApprovedSlug);
		await Mediator.Send(new InvalidateGrantsCommand(player.DbRef.Number));
		return player;
	}

	private async Task<WikiPage> PageAsync(string prefix, bool published, params WikiPageFact[] facts)
	{
		var page = (await WikiService.CreateAsync(TestIsolationHelpers.GenerateUniqueName(prefix), "Body.", "#1")).Expect<WikiPage>();
		await WikiService.SetMetadataAsync(page.Id, [], published);
		return (await WikiService.SetFactsAsync(page.Id, facts)).Expect<WikiPage>();
	}

	/// <summary>The latest <c>@wiki</c> view of <paramref name="page"/> the player was sent, as it was sent.</summary>
	private async Task<MString> ViewAsync(TestIsolationHelpers.TestPlayer player, WikiPage page)
	{
		await Parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain($"@wiki main:{page.Slug}"));
		return WebAppFactoryArg.Notifications.RawFor(player.DbRef)
			.Select(message => message switch { MString markup => markup, string text => MarkupText.Plain(text) })
			.Last(message => message.ToPlainText().Contains($"Wiki: {page.Title} [main]", StringComparison.Ordinal));
	}

	[Test]
	public async Task APagesFactsAreShownUnderTheirLabelsAndLinked()
	{
		var player = await PlayerAsync("WikiViewFacts");
		var mentor = await PageAsync("ViewMentor", true);
		var student = await PageAsync("ViewStudent", true, new WikiPageFact(Mentor.Id, mentor.Id), new(School.Id, "evocation"));

		var view = await ViewAsync(player, student);
		var text = view.ToPlainText();
		await Assert.That(text).Contains("Facts");
		await Assert.That(text).Contains($"Mentor: {mentor.Title}");
		await Assert.That(text).Contains("School: evocation");
		await Assert.That(text).DoesNotContain("Named by");
		var mxp = view.Render(MarkupFormat.Mxp);
		await Assert.That(mxp).Contains($"@wiki main:{mentor.Slug}");
		await Assert.That(mxp).Contains("@wiki/query/noeval viewtest:school=evocation");
	}

	[Test]
	public async Task APageListsThePagesNamingItThatTheReaderMaySee()
	{
		var player = await PlayerAsync("WikiViewNamed");
		var mentor = await PageAsync("NamedMentor", true);
		var students = new List<WikiPage>();
		for (var i = 0; i < ViewWiki.MaxNamers; i++) students.Add(await PageAsync("NamedStudent", true, new WikiPageFact(Mentor.Id, mentor.Id)));
		var draft = await PageAsync("NamedDraft", false, new WikiPageFact(Mentor.Id, mentor.Id));

		var text = (await ViewAsync(player, mentor)).ToPlainText();
		await Assert.That(text).Contains("Named by");
		foreach (var student in students) await Assert.That(text).Contains(student.Title);
		// The reader may not see the draft, so it is neither listed nor counted toward "more".
		await Assert.That(text).DoesNotContain(draft.Title);
		await Assert.That(text).DoesNotContain("more");

		var extra = await PageAsync("NamedStudent", true, new WikiPageFact(Mentor.Id, mentor.Id));
		var view = await ViewAsync(player, mentor);
		await Assert.That(view.ToPlainText()).Contains("more");
		await Assert.That(view.Render(MarkupFormat.Mxp)).Contains($"@wiki/query/noeval viewtest:mentor=main:{mentor.Slug}");
		await Assert.That(students.Append(extra).Count(s => view.ToPlainText().Contains(s.Title))).IsEqualTo(ViewWiki.MaxNamers);
	}

	[Test]
	public async Task ADraftsFactsAreWithheldWithItsBody()
	{
		var player = await PlayerAsync("WikiViewDraft");
		var draft = await PageAsync("ViewDraft", false, new WikiPageFact(School.Id, "abjuration"));

		var text = (await ViewAsync(player, draft)).ToPlainText();
		await Assert.That(text).Contains("is not shown");
		await Assert.That(text).DoesNotContain("abjuration");
	}
}
