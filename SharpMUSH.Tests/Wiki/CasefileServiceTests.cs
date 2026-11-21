using Mediator;
using NSubstitute;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Commands.Database.Wiki;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Tests.Authentication;

namespace SharpMUSH.Tests.Wiki;

/// <summary>
/// Case files: one wiki page in the Case Files category whose members, characters or roles, are the only ones (with
/// wiki.admin) who see it; readers read, contributors also write and post developments, leads also let people in.
/// </summary>
public class CasefileServiceTests
{
	private const string Lead = "#10:1000";
	private const string Contributor = "#11:1100";
	private const string Reader = "#12:1200";
	private const string Outsider = "#13:1300";
	private const string Investigator = "#14:1400";

	private static readonly string[] Writer = [PortalPermission.WikiRead, PortalPermission.WikiCreate, PortalPermission.WikiEdit];

	private static WikiReader Character(string dbref, params string[] roles)
		=> WikiReader.From(Writer, dbref) with { Roles = roles.ToHashSet(StringComparer.OrdinalIgnoreCase) };

	private static readonly WikiReader Admin = WikiReader.From(
		[.. Writer, PortalPermission.WikiDelete, PortalPermission.WikiDrafts, PortalPermission.WikiAdmin], "#1:1") with
	{
		Roles = new HashSet<string>()
	};

	private sealed record World(WikiStoreService Wiki, WikiAccessService Access, CasefileService Casefiles);

	private static World Build()
	{
		var store = new InMemoryWikiStore();
		var wiki = new WikiStoreService(store, new WikiMarkdigPipeline());
		var roles = InMemoryRoleRegistry.Seeded();
		var access = new WikiAccessService(wiki, roles, new PermissionResolver());
		var mediator = Substitute.For<IMediator>();
		mediator.Send(Arg.Any<SetWikiRequirementsCommand>(), Arg.Any<CancellationToken>())
			.Returns(call => new ValueTask<Found<None>>(store.SetRequirementsAsync(call.Arg<SetWikiRequirementsCommand>().Set)));
		var localization = Substitute.For<IWikiLocalizationService>();
		localization.DefaultLocale.Returns("en");
		return new World(wiki, access, new CasefileService(wiki, access, localization, roles, mediator));
	}

	private static async Task<(World World, Casefile Casefile)> OpenAsync()
	{
		var world = Build();
		var casefile = (await world.Casefiles.CreateAsync(Character(Lead), "Harbour Fire", "Who set the warehouse alight?"))
			.Expect<Casefile>();
		(await world.Casefiles.SetMemberAsync(Character(Lead), "Harbour Fire", Contributor, WikiMemberLevel.Contributor)).Expect<Casefile>();
		(await world.Casefiles.SetMemberAsync(Character(Lead), "Harbour Fire", Reader, WikiMemberLevel.Reader)).Expect<Casefile>();
		return (world, casefile);
	}

	[Test]
	public async Task OpeningATakenTitle_TellsAnOutsiderOnlyThatItIsTaken()
	{
		var (world, _) = await OpenAsync();
		(await world.Wiki.CreateAsync("Town map", "Streets", Outsider)).Expect<WikiPage>();

		var outsider = (await world.Casefiles.CreateAsync(Character(Outsider), "Harbour Fire", "")).Expect<Error<string>>();
		var member = (await world.Casefiles.CreateAsync(Character(Reader), "Harbour Fire", "")).Expect<Error<string>>();
		var publicPage = (await world.Casefiles.CreateAsync(Character(Lead), "Town map", "")).Expect<Error<string>>();

		await Assert.That(outsider.Value).IsEqualTo("That title is taken. Choose another.");
		await Assert.That(member.Value).IsEqualTo("There is already a case file called Harbour Fire.");
		await Assert.That(publicPage.Value).IsEqualTo("There is already a page called Town map. Choose another title.");
	}

	[Test]
	public async Task OpeningACasefile_MakesOnePrivatePage_InTheCaseFilesCategory_LedByItsOpener()
	{
		var (world, casefile) = await OpenAsync();
		var page = casefile.Page;

		await Assert.That(casefile.Summary.Title).IsEqualTo("Harbour Fire");
		await Assert.That(casefile.Summary.Level).IsEqualTo(WikiMemberLevel.Lead);
		await Assert.That(page.Namespace).IsEqualTo("main");
		await Assert.That(page.Categories.Select(WikiHelpers.CategoryKey)).IsEquivalentTo([WikiHelpers.CategoryKey(CasefileService.Category)]);
		await Assert.That(page.MarkdownSource).IsEqualTo("Who set the warehouse alight?");

		await Assert.That(await world.Access.CanSeeAsync(Character(Outsider), page)).IsFalse();
		await Assert.That(await world.Access.CanSeeAsync(Character(Reader), page)).IsTrue();
		await Assert.That(await world.Access.CanSeeAsync(Admin, page)).IsTrue();
		await Assert.That(await world.Casefiles.GetAsync(Character(Outsider), "Harbour Fire") is NotFound).IsTrue();
	}

	[Test]
	public async Task TheCaseFilesCategory_ListsACasefileOnlyToItsMembers()
	{
		var (world, casefile) = await OpenAsync();

		var outsider = await world.Access.VisibilityAsync(Character(Outsider));
		var member = await world.Access.VisibilityAsync(Character(Reader));
		await Assert.That((await world.Wiki.GetByCategoryAsync(CasefileService.Category, 0, 50, outsider)).Any(page => page.Id == casefile.Page.Id))
			.IsFalse();
		await Assert.That((await world.Wiki.GetAllPagesAsync(0, 50, null, outsider)).Any(page => page.Id == casefile.Page.Id)).IsFalse();
		await Assert.That((await world.Wiki.GetByCategoryAsync(CasefileService.Category, 0, 50, member)).Any(page => page.Id == casefile.Page.Id))
			.IsTrue();
	}

	[Test]
	public async Task AnUnpublishedCasefile_IsLeftOutForAMemberWhoCannotReadIt()
	{
		var (world, casefile) = await OpenAsync();
		(await world.Wiki.SetMetadataAsync(casefile.Page.Id, [CasefileService.Category], false)).Expect<WikiPage>();

		await Assert.That(await world.Casefiles.GetAsync(Character(Reader), "Harbour Fire") is NotFound).IsTrue();
		await Assert.That(await world.Casefiles.ListAsync(Character(Reader))).IsEmpty();
	}

	[Test]
	public async Task Readers_Read_Contributors_Write_AndNobodyElse()
	{
		var (world, casefile) = await OpenAsync();
		var page = casefile.Page;

		await Assert.That((await world.Access.DecideAsync(Character(Reader), page, WikiAction.Read)).Allowed).IsTrue();
		var readerEdit = await world.Access.DecideAsync(Character(Reader), page, WikiAction.Edit);
		await Assert.That(readerEdit.Allowed).IsFalse();
		await Assert.That(readerEdit.Describe()).IsEqualTo($"page {page.Id} is private: needs a contributor or lead, and you are a reader");
		await Assert.That((await world.Access.DecideAsync(Character(Contributor), page, WikiAction.Edit)).Allowed).IsTrue();
		await Assert.That((await world.Access.DecideAsync(Character(Outsider), page, WikiAction.Read)).Describe())
			.IsEqualTo($"page {page.Id} is private: members only");
	}

	[Test]
	public async Task MembershipNeverLiftsTheGlobalScope()
	{
		var (world, casefile) = await OpenAsync();
		var unapproved = WikiReader.From([PortalPermission.WikiRead], Contributor) with { Roles = new HashSet<string>() };

		var decision = await world.Access.DecideAsync(unapproved, casefile.Page, WikiAction.Edit);
		await Assert.That(decision.Describe()).IsEqualTo("needs wiki.edit");
		var post = await world.Casefiles.PostDevelopmentAsync(unapproved, "Harbour Fire", "Ines", "Smoke", "Seen at dawn");
		await Assert.That(post.Expect<Error<string>>().Value).IsEqualTo("Permission denied. You need the wiki.edit permission.");
	}

	[Test]
	public async Task ARoleLetsInEveryoneHoldingIt()
	{
		var (world, casefile) = await OpenAsync();
		(await world.Casefiles.SetMemberAsync(Character(Lead), "Harbour Fire", "role:investigators", WikiMemberLevel.Contributor))
			.Expect<Error<string>>();
		(await world.Casefiles.SetMemberAsync(Character(Lead), "Harbour Fire", "role:approved", WikiMemberLevel.Contributor))
			.Expect<Casefile>();

		await Assert.That(await world.Access.CanSeeAsync(Character(Investigator, "approved"), casefile.Page)).IsTrue();
		await Assert.That(await world.Access.CanSeeAsync(Character(Investigator, "player"), casefile.Page)).IsFalse();
		var listed = await world.Casefiles.ListAsync(Character(Investigator, "approved"));
		await Assert.That(listed.Select(summary => summary.Level)).IsEquivalentTo([WikiMemberLevel.Contributor]);
	}

	[Test]
	public async Task OnlyALeadLetsPeopleIn_AndTheLastLeadStays()
	{
		var (world, _) = await OpenAsync();

		var byContributor = await world.Casefiles.SetMemberAsync(Character(Contributor), "Harbour Fire", Outsider, WikiMemberLevel.Reader);
		await Assert.That(byContributor.Expect<Error<string>>().Value).StartsWith("Permission denied.");

		var lastLead = await world.Casefiles.SetMemberAsync(Character(Lead), "Harbour Fire", Lead, null);
		await Assert.That(lastLead.Expect<Error<string>>().Value).StartsWith("A case file keeps at least one character as its lead");

		(await world.Casefiles.SetMemberAsync(Admin, "Harbour Fire", Contributor, WikiMemberLevel.Lead)).Expect<Casefile>();
		await Assert.That((await world.Casefiles.SetMemberAsync(Character(Lead), "Harbour Fire", Lead, null)).Expect<Error<string>>().Value)
			.IsEqualTo(CasefileService.LeftMessage);
		await Assert.That(await world.Casefiles.GetAsync(Character(Lead), "Harbour Fire") is NotFound).IsTrue();
	}

	[Test]
	public async Task ACharacterMemberIsMatchedByObjid_NotByAReusedDbref()
	{
		var (world, casefile) = await OpenAsync();

		await Assert.That(await world.Access.CanSeeAsync(Character("#12:9999"), casefile.Page)).IsFalse();
		await Assert.That(await world.Access.CanSeeAsync(Character("#12:1200"), casefile.Page)).IsTrue();
	}

	[Test]
	public async Task APostedDevelopmentGoesToTheTopOfThePagesDevelopments()
	{
		var (world, _) = await OpenAsync();
		(await world.Casefiles.PostDevelopmentAsync(Character(Contributor), "Harbour Fire", "Ines", "Smoke at dawn",
			"A dockhand saw smoke before the bell.")).Expect<WikiPage>();
		var page = (await world.Casefiles.PostDevelopmentAsync(Character(Lead), "Harbour Fire", "Marta", "The ledger turns up",
			"Two pages are torn out.")).Expect<WikiPage>();

		var text = page.MarkdownSource;
		var briefing = text.IndexOf("Who set the warehouse alight?", StringComparison.Ordinal);
		var heading = text.IndexOf(CasefileService.DevelopmentsHeading, StringComparison.Ordinal);
		var newest = text.IndexOf("The ledger turns up", StringComparison.Ordinal);
		var oldest = text.IndexOf("Smoke at dawn", StringComparison.Ordinal);
		await Assert.That(briefing).IsEqualTo(0);
		await Assert.That(heading).IsGreaterThan(briefing);
		await Assert.That(newest).IsGreaterThan(heading);
		await Assert.That(newest).IsLessThan(oldest);
		await Assert.That(text).Contains("*Posted by Ines*");
		await Assert.That(text.Split(CasefileService.DevelopmentsHeading).Length).IsEqualTo(2);

		var byReader = await world.Casefiles.PostDevelopmentAsync(Character(Reader), "Harbour Fire", "Rui", "Guess", "Maybe");
		await Assert.That(byReader.Expect<Error<string>>().Value).StartsWith("Permission denied.");
	}

	[Test]
	public async Task APostKeepsWhatTheDevelopmentsSectionHasAfterIt()
	{
		var withSection = CasefileService.WithDevelopment("Brief.\n\n## Developments\n\n### Old\n\nText.\n\n## Notes\n\nKeep.", "### New\n\nNow.");
		await Assert.That(withSection).IsEqualTo("Brief.\n\n## Developments\n\n### New\n\nNow.\n\n### Old\n\nText.\n\n## Notes\n\nKeep.\n");

		var without = CasefileService.WithDevelopment("Brief.", "### New\n\nNow.");
		await Assert.That(without).IsEqualTo("Brief.\n\n## Developments\n\n### New\n\nNow.\n");
	}

	[Test]
	public async Task AProtectedCasefile_RefusesPostsFromItsContributors()
	{
		var (world, casefile) = await OpenAsync();
		(await world.Access.SetRequirementsAsync(Admin, "#1", WikiRuleTarget.ForPage(casefile.Page.Id), WikiRequirementSet.Protection))
			.Expect<WikiRequirements>();

		var post = await world.Casefiles.PostDevelopmentAsync(Character(Contributor), "Harbour Fire", "Ines", "Smoke", "Seen.");
		await Assert.That(post.Expect<Error<string>>().Value).IsEqualTo("Permission denied. The page requires wiki.admin.");
	}

	[Test]
	public async Task ProtectingACasefile_KeepsItsMembers()
	{
		var (world, casefile) = await OpenAsync();
		(await world.Access.SetRequirementsAsync(Admin, "#1", WikiRuleTarget.ForPage(casefile.Page.Id), WikiRequirementSet.Protection))
			.Expect<WikiRequirements>();

		var set = (await world.Access.RequirementsAsync()).For(WikiRuleTarget.ForPage(casefile.Page.Id))!;
		await Assert.That(set.Members.Count).IsEqualTo(3);
		await Assert.That(set.For(WikiAction.Delete)).IsEquivalentTo([PortalPermission.WikiAdmin]);
	}

	[Test]
	public async Task AHiddenCasefile_IsATakenTitleToOutsidersOnly()
	{
		var (world, _) = await OpenAsync();

		await Assert.That(await world.Casefiles.TitleTakenAsync(Character(Outsider), WikiNamespace.Main, "Harbour Fire")).IsTrue();
		await Assert.That(await world.Casefiles.TitleTakenAsync(Character(Reader), WikiNamespace.Main, "Harbour Fire")).IsFalse();
		await Assert.That(await world.Casefiles.TitleTakenAsync(Character(Outsider), WikiNamespace.Main, "Something else")).IsFalse();
	}

	[Test]
	public async Task ARoleAlone_CannotBeTheLastLead()
	{
		var (world, _) = await OpenAsync();
		(await world.Casefiles.SetMemberAsync(Character(Lead), "Harbour Fire", "role:approved", WikiMemberLevel.Lead)).Expect<Casefile>();

		var stepDown = await world.Casefiles.SetMemberAsync(Character(Lead), "Harbour Fire", Lead, null);
		await Assert.That(stepDown.Expect<Error<string>>().Value).StartsWith("A case file keeps at least one character as its lead");
	}
}
