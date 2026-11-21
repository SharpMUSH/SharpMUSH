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
/// Casefiles: a private wiki category whose members, characters or roles, are the only ones (with wiki.admin) who
/// see its briefing, files and developments; readers read, contributors also write and post, leads also let people in.
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

		var outsider = (await world.Casefiles.CreateAsync(Character(Outsider), "Harbour Fire", "")).Expect<Error<string>>();
		var member = (await world.Casefiles.CreateAsync(Character(Reader), "Harbour Fire", "")).Expect<Error<string>>();

		await Assert.That(outsider.Value).IsEqualTo("That title is taken. Choose another.");
		await Assert.That(member.Value).IsEqualTo("There is already a casefile called Harbour Fire.");
	}

	[Test]
	public async Task OpeningACasefile_MakesItsLeadTheOpener_AndItsBriefingAndLogPrivate()
	{
		var (world, casefile) = await OpenAsync();

		await Assert.That(casefile.Summary.Title).IsEqualTo("Harbour Fire");
		await Assert.That(casefile.Summary.Level).IsEqualTo(WikiMemberLevel.Lead);
		await Assert.That(casefile.Briefing!.MarkdownSource).IsEqualTo("Who set the warehouse alight?");
		await Assert.That(casefile.Developments!.Title).IsEqualTo("Harbour Fire developments");

		var briefing = casefile.Briefing!;
		await Assert.That(await world.Access.CanSeeAsync(Character(Outsider), briefing)).IsFalse();
		await Assert.That(await world.Access.CanSeeAsync(Character(Outsider), casefile.Developments!)).IsFalse();
		await Assert.That(await world.Access.CanSeeAsync(Character(Reader), briefing)).IsTrue();
		await Assert.That(await world.Access.CanSeeAsync(Admin, briefing)).IsTrue();
		await Assert.That(await world.Casefiles.GetAsync(Character(Outsider), "Harbour Fire") is NotFound).IsTrue();
	}

	[Test]
	public async Task AnUnpublishedBriefing_IsLeftOutForAMemberWhoCannotReadIt()
	{
		var (world, casefile) = await OpenAsync();
		(await world.Wiki.SetMetadataAsync(casefile.Briefing!.Id, [], false)).Expect<WikiPage>();

		var shown = (await world.Casefiles.GetAsync(Character(Reader), "Harbour Fire")).Expect<Casefile>();
		var listed = (await world.Casefiles.ListAsync(Character(Reader))).Single();

		await Assert.That(shown.Briefing).IsNull();
		await Assert.That(shown.Summary.Summary).IsEmpty();
		await Assert.That(listed.Summary).IsEmpty();
	}

	[Test]
	public async Task AFileInTheCasefile_IsHiddenFromListingsForOutsiders()
	{
		var (world, _) = await OpenAsync();
		var file = (await world.Wiki.CreateAsync("Warehouse ledger", "Entries", Contributor, categories: ["Harbour Fire"]))
			.Expect<WikiPage>();

		var outsider = await world.Access.VisibilityAsync(Character(Outsider));
		var member = await world.Access.VisibilityAsync(Character(Reader));
		var all = await world.Wiki.GetAllPagesAsync(0, 50, null, outsider);
		await Assert.That(all.Any(page => page.Id == file.Id)).IsFalse();
		await Assert.That(all.Any(page => page.Namespace == "category" && page.Slug == "harbour_fire")).IsFalse();
		await Assert.That((await world.Wiki.GetAllPagesAsync(0, 50, null, member)).Any(page => page.Id == file.Id)).IsTrue();

		var shown = (await world.Casefiles.GetAsync(Character(Reader), "harbour_fire")).Expect<Casefile>();
		await Assert.That(shown.Files.Select(page => page.Title)).IsEquivalentTo(["Warehouse ledger"]);
	}

	[Test]
	public async Task Readers_Read_Contributors_Write_AndNobodyElse()
	{
		var (world, _) = await OpenAsync();
		var file = (await world.Wiki.CreateAsync("Witnesses", "Names", Contributor, categories: ["Harbour Fire"])).Expect<WikiPage>();

		await Assert.That((await world.Access.DecideAsync(Character(Reader), file, WikiAction.Read)).Allowed).IsTrue();
		var readerEdit = await world.Access.DecideAsync(Character(Reader), file, WikiAction.Edit);
		await Assert.That(readerEdit.Allowed).IsFalse();
		await Assert.That(readerEdit.Describe()).IsEqualTo("category harbour_fire is private: needs a contributor or lead, and you are a reader");
		await Assert.That((await world.Access.DecideAsync(Character(Contributor), file, WikiAction.Edit)).Allowed).IsTrue();
		await Assert.That((await world.Access.DecideAsync(Character(Outsider), file, WikiAction.Read)).Describe())
			.IsEqualTo("category harbour_fire is private: members only");

		await Assert.That((await world.Access.DecideCreateAsync(Character(Reader), "main", ["Harbour Fire"])).Allowed).IsFalse();
		await Assert.That((await world.Access.DecideCreateAsync(Character(Contributor), "main", ["Harbour Fire"])).Allowed).IsTrue();

		var outsiderPage = (await world.Wiki.CreateAsync("Rumours", "Talk", Outsider)).Expect<WikiPage>();
		await Assert.That((await world.Access.DecideCategoriesAsync(Character(Outsider), outsiderPage, ["Harbour Fire"])).Allowed)
			.IsFalse();
	}

	[Test]
	public async Task MembershipNeverLiftsTheGlobalScope()
	{
		var (world, casefile) = await OpenAsync();
		var unapproved = WikiReader.From([PortalPermission.WikiRead], Contributor) with { Roles = new HashSet<string>() };

		var decision = await world.Access.DecideAsync(unapproved, casefile.Developments!, WikiAction.Edit);
		await Assert.That(decision.Describe()).IsEqualTo("needs wiki.edit");
		await Assert.That(await world.Casefiles.PostDevelopmentAsync(unapproved, "Harbour Fire", "Ines", "Smoke", "Seen at dawn", [])
			is Error<string>).IsTrue();
	}

	[Test]
	public async Task ARoleLetsInEveryoneHoldingIt()
	{
		var (world, casefile) = await OpenAsync();
		(await world.Casefiles.SetMemberAsync(Character(Lead), "Harbour Fire", "role:investigators", WikiMemberLevel.Contributor))
			.Expect<Error<string>>();
		(await world.Casefiles.SetMemberAsync(Character(Lead), "Harbour Fire", "role:approved", WikiMemberLevel.Contributor))
			.Expect<Casefile>();

		await Assert.That(await world.Access.CanSeeAsync(Character(Investigator, "approved"), casefile.Briefing!)).IsTrue();
		await Assert.That(await world.Access.CanSeeAsync(Character(Investigator, "player"), casefile.Briefing!)).IsFalse();
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
		await Assert.That(lastLead.Expect<Error<string>>().Value).StartsWith("A casefile keeps at least one character as its lead");

		(await world.Casefiles.SetMemberAsync(Admin, "Harbour Fire", Contributor, WikiMemberLevel.Lead)).Expect<Casefile>();
		(await world.Casefiles.SetMemberAsync(Character(Lead), "Harbour Fire", Lead, null)).Expect<Error<string>>();
		await Assert.That(await world.Casefiles.GetAsync(Character(Lead), "Harbour Fire") is NotFound).IsTrue();
	}

	[Test]
	public async Task ACharacterMemberIsMatchedByObjid_NotByAReusedDbref()
	{
		var (world, casefile) = await OpenAsync();

		await Assert.That(await world.Access.CanSeeAsync(Character("#12:9999"), casefile.Briefing!)).IsFalse();
		await Assert.That(await world.Access.CanSeeAsync(Character("#12:1200"), casefile.Briefing!)).IsTrue();
	}

	[Test]
	public async Task APostedDevelopmentGoesToTheTopOfTheLog()
	{
		var (world, _) = await OpenAsync();
		(await world.Casefiles.PostDevelopmentAsync(Character(Contributor), "Harbour Fire", "Ines", "Smoke at dawn",
			"A dockhand saw smoke before the bell.", ["Witnesses"])).Expect<WikiPage>();
		var log = (await world.Casefiles.PostDevelopmentAsync(Character(Lead), "Harbour Fire", "Marta", "The ledger turns up",
			"Two pages are torn out.", [])).Expect<WikiPage>();

		var newest = log.MarkdownSource.IndexOf("The ledger turns up", StringComparison.Ordinal);
		var oldest = log.MarkdownSource.IndexOf("Smoke at dawn", StringComparison.Ordinal);
		await Assert.That(newest).IsGreaterThanOrEqualTo(0);
		await Assert.That(newest).IsLessThan(oldest);
		await Assert.That(log.MarkdownSource).Contains("*Posted by Ines. Files: [[Witnesses|Witnesses]]*");

		var byReader = await world.Casefiles.PostDevelopmentAsync(Character(Reader), "Harbour Fire", "Rui", "Guess", "Maybe", []);
		await Assert.That(byReader.Expect<Error<string>>().Value).StartsWith("Permission denied.");
	}

	[Test]
	public async Task ACategoryWithPages_BecomesACasefileOnlyForWikiAdmin()
	{
		var world = Build();
		(await world.Wiki.CreateAsync("Town map", "Streets", Lead, categories: ["Old Town"])).Expect<WikiPage>();

		var refused = await world.Casefiles.CreateAsync(Character(Lead), "Old Town", "");
		await Assert.That(refused.Expect<Error<string>>().Value).Contains("needs the wiki.admin permission");
		(await world.Casefiles.CreateAsync(Admin, "Old Town", "")).Expect<Casefile>();
	}

	[Test]
	public async Task ChangingWhatACasefileRequires_KeepsItsMembers()
	{
		var (world, _) = await OpenAsync();
		(await world.Access.SetRequirementsAsync(Admin, "#1", WikiRuleTarget.ForCategory("Harbour Fire"),
			new Dictionary<WikiAction, IReadOnlyList<string>> { [WikiAction.Delete] = [PortalPermission.WikiAdmin] }))
			.Expect<WikiRequirements>();

		var set = (await world.Access.RequirementsAsync()).For(WikiRuleTarget.ForCategory("harbour_fire"))!;
		await Assert.That(set.Members.Count).IsEqualTo(3);
		await Assert.That(set.For(WikiAction.Delete)).IsEquivalentTo([PortalPermission.WikiAdmin]);
	}

	[Test]
	public async Task APublicPageWithTheLogsTitle_IsNeverTakenAsTheLog()
	{
		var world = Build();
		(await world.Wiki.CreateAsync("Harbour Fire developments", "Public notes", Outsider)).Expect<WikiPage>();

		var refused = await world.Casefiles.CreateAsync(Character(Lead), "Harbour Fire", "");
		await Assert.That(refused.Expect<Error<string>>().Value).IsEqualTo("That title is taken. Choose another.");
		await Assert.That(await world.Casefiles.GetAsync(Admin, "Harbour Fire") is NotFound).IsTrue();
	}

	[Test]
	public async Task AMovedLog_RefusesPostsRatherThanWritingOutsideTheCasefile()
	{
		var (world, casefile) = await OpenAsync();
		var log = casefile.Developments!;
		(await world.Wiki.SetMetadataAsync(log.Id, [], true)).Expect<WikiPage>();

		var post = await world.Casefiles.PostDevelopmentAsync(Character(Contributor), "Harbour Fire", "Ines", "Smoke", "Seen.", []);
		await Assert.That(post.Expect<Error<string>>().Value).StartsWith("The developments log could not be made");
		var unchanged = (await world.Wiki.GetBySlugAsync(log.Slug)).Expect<WikiPage>();
		await Assert.That(unchanged.MarkdownSource).DoesNotContain("Smoke");
	}

	[Test]
	public async Task AMissingBriefing_IsATakenTitleToEveryoneButItsWriters()
	{
		var (world, _) = await OpenAsync();

		await Assert.That(await world.Casefiles.TitleTakenAsync(Character(Outsider), WikiNamespace.Category, "Harbour Fire")).IsTrue();
		await Assert.That(await world.Casefiles.TitleTakenAsync(Character(Reader), WikiNamespace.Category, "Harbour Fire")).IsTrue();
		await Assert.That(await world.Casefiles.TitleTakenAsync(Character(Contributor), WikiNamespace.Category, "Harbour Fire")).IsFalse();
		await Assert.That(await world.Casefiles.TitleTakenAsync(Character(Outsider), WikiNamespace.Main, "Harbour Fire developments")).IsTrue();
		await Assert.That(await world.Casefiles.TitleTakenAsync(Character(Outsider), WikiNamespace.Main, "Something else")).IsFalse();
	}

	[Test]
	public async Task ARoleAlone_CannotBeTheLastLead()
	{
		var (world, _) = await OpenAsync();
		(await world.Casefiles.SetMemberAsync(Character(Lead), "Harbour Fire", "role:approved", WikiMemberLevel.Lead)).Expect<Casefile>();

		var stepDown = await world.Casefiles.SetMemberAsync(Character(Lead), "Harbour Fire", Lead, null);
		await Assert.That(stepDown.Expect<Error<string>>().Value).StartsWith("A casefile keeps at least one character as its lead");
	}
}
