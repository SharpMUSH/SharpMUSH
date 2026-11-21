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
/// Private wiki pages: a page whose members, characters or roles, are the only ones (with wiki.admin) who see it;
/// readers read, contributors also write, leads also let people in and out. The casefiles package builds on these.
/// </summary>
public class WikiMembersServiceTests
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

	private sealed record World(WikiStoreService Wiki, WikiAccessService Access, WikiMembersService Members);

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
		return new World(wiki, access, new WikiMembersService(wiki, access, localization, roles, mediator));
	}

	/// <summary>Harbour Fire, private, led by <see cref="Lead"/>, with a contributor and a reader.</summary>
	private static async Task<(World World, WikiPage Page)> OpenAsync()
	{
		var world = Build();
		var page = (await world.Members.CreatePrivateAsync(Character(Lead), WikiNamespace.Main, "Harbour Fire",
			"Who set the warehouse alight?")).Expect<WikiPage>();
		(await world.Members.SetMemberAsync(Character(Lead), page, Contributor, WikiMemberLevel.Contributor)).Expect<WikiPageMembers>();
		(await world.Members.SetMemberAsync(Character(Lead), page, Reader, WikiMemberLevel.Reader)).Expect<WikiPageMembers>();
		return (world, page);
	}

	[Test]
	public async Task CreatingAPrivatePage_LeavesItsCreatorTheOnlyLead()
	{
		var world = Build();
		var page = (await world.Members.CreatePrivateAsync(Character(Lead), WikiNamespace.Main, "Harbour Fire", "Brief."))
			.Expect<WikiPage>();

		var members = (await world.Members.GetAsync(Character(Lead), page)).Expect<WikiPageMembers>();
		await Assert.That(members.Level).IsEqualTo(WikiMemberLevel.Lead);
		await Assert.That(members.Members).IsEquivalentTo([new WikiMember(Lead, WikiMemberLevel.Lead)]);
		await Assert.That(page.MarkdownSource).IsEqualTo("Brief.");
		await Assert.That(await world.Access.CanSeeAsync(Character(Outsider), page)).IsFalse();
		await Assert.That(await world.Access.CanSeeAsync(Admin, page)).IsTrue();
	}

	[Test]
	public async Task OnlyACharacterCreatesAPrivatePage()
	{
		var world = Build();
		var noOne = await world.Members.CreatePrivateAsync(WikiReader.From(Writer, null), WikiNamespace.Main, "Harbour Fire", "");
		var blank = await world.Members.CreatePrivateAsync(Character(Lead), WikiNamespace.Main, "  ", "");

		await Assert.That(noOne.Expect<Error<string>>().Value).IsEqualTo("Only a character can be the lead of a private page.");
		await Assert.That(blank.Expect<Error<string>>().Value).IsEqualTo("A page needs a title on one line.");
	}

	[Test]
	public async Task ATakenTitle_TellsAnOutsiderOnlyThatItIsTaken()
	{
		var (world, _) = await OpenAsync();
		(await world.Wiki.CreateAsync("Town map", "Streets", Outsider)).Expect<WikiPage>();

		var outsider = await world.Members.CreatePrivateAsync(Character(Outsider), WikiNamespace.Main, "Harbour Fire", "");
		var member = await world.Members.CreatePrivateAsync(Character(Reader), WikiNamespace.Main, "Harbour Fire", "");
		var publicPage = await world.Members.CreatePrivateAsync(Character(Lead), WikiNamespace.Main, "Town map", "");

		await Assert.That(outsider.Expect<Error<string>>().Value).IsEqualTo("That title is taken. Choose another.");
		await Assert.That(member.Expect<Error<string>>().Value).IsEqualTo("There is already a page called Harbour Fire.");
		await Assert.That(publicPage.Expect<Error<string>>().Value).IsEqualTo("There is already a page called Town map.");
		await Assert.That(await world.Members.TitleTakenAsync(Character(Outsider), WikiNamespace.Main, "Harbour Fire")).IsTrue();
		await Assert.That(await world.Members.TitleTakenAsync(Character(Reader), WikiNamespace.Main, "Harbour Fire")).IsFalse();
		await Assert.That(await world.Members.TitleTakenAsync(Character(Outsider), WikiNamespace.Main, "Something else")).IsFalse();
	}

	[Test]
	public async Task APrivatePage_IsListedOnlyToItsMembers()
	{
		var (world, page) = await OpenAsync();
		(await world.Wiki.SetMetadataAsync(page.Id, ["Case Files"], true)).Expect<WikiPage>();

		var outsider = await world.Access.VisibilityAsync(Character(Outsider));
		var member = await world.Access.VisibilityAsync(Character(Reader));
		await Assert.That((await world.Wiki.GetByCategoryAsync("Case Files", 0, 50, outsider)).Any(found => found.Id == page.Id)).IsFalse();
		await Assert.That((await world.Wiki.GetAllPagesAsync(0, 50, null, outsider)).Any(found => found.Id == page.Id)).IsFalse();
		await Assert.That((await world.Wiki.GetByCategoryAsync("Case Files", 0, 50, member)).Any(found => found.Id == page.Id)).IsTrue();
	}

	[Test]
	public async Task AnOutsider_GetsNoMemberList()
	{
		var (world, page) = await OpenAsync();
		var plain = (await world.Wiki.CreateAsync("Town map", "Streets", Outsider)).Expect<WikiPage>();

		await Assert.That(await world.Members.GetAsync(Character(Outsider), page) is NotFound).IsTrue();
		await Assert.That(await world.Members.GetAsync(Character(Lead), plain) is NotFound).IsTrue();
		var forAdmin = (await world.Members.GetAsync(Admin, page)).Expect<WikiPageMembers>();
		await Assert.That(forAdmin.Level).IsEqualTo(WikiMemberLevel.Lead);
		await Assert.That(forAdmin.Members.Select(member => member.Principal)).IsEquivalentTo([Lead, Contributor, Reader]);
	}

	[Test]
	public async Task AnUnpublishedPrivatePage_IsLeftOutForAMemberWhoCannotReadDrafts()
	{
		var (world, page) = await OpenAsync();
		var draft = (await world.Wiki.SetMetadataAsync(page.Id, ["Case Files"], false)).Expect<WikiPage>();

		await Assert.That(await world.Members.GetAsync(Character(Reader), draft) is NotFound).IsTrue();
		await Assert.That(await world.Members.GetAsync(Admin, draft) is WikiPageMembers).IsTrue();
	}

	[Test]
	public async Task Readers_Read_Contributors_Write_AndNobodyElse()
	{
		var (world, page) = await OpenAsync();

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
		var (world, page) = await OpenAsync();
		var unapproved = WikiReader.From([PortalPermission.WikiRead], Contributor) with { Roles = new HashSet<string>() };

		var decision = await world.Access.DecideAsync(unapproved, page, WikiAction.Edit);
		await Assert.That(decision.Describe()).IsEqualTo("needs wiki.edit");
	}

	[Test]
	public async Task ARoleLetsInEveryoneHoldingIt()
	{
		var (world, page) = await OpenAsync();
		var unknown = await world.Members.SetMemberAsync(Character(Lead), page, "role:investigators", WikiMemberLevel.Contributor);
		await Assert.That(unknown.Expect<Error<string>>().Value).IsEqualTo("Not a character or a role: role:investigators.");
		(await world.Members.SetMemberAsync(Character(Lead), page, "role:approved", WikiMemberLevel.Contributor))
			.Expect<WikiPageMembers>();

		await Assert.That(await world.Access.CanSeeAsync(Character(Investigator, "approved"), page)).IsTrue();
		await Assert.That(await world.Access.CanSeeAsync(Character(Investigator, "player"), page)).IsFalse();
		var seen = (await world.Members.GetAsync(Character(Investigator, "approved"), page)).Expect<WikiPageMembers>();
		await Assert.That(seen.Level).IsEqualTo(WikiMemberLevel.Contributor);
	}

	[Test]
	public async Task OnlyALeadLetsPeopleIn_AndTheLastLeadStays()
	{
		var (world, page) = await OpenAsync();

		var byContributor = await world.Members.SetMemberAsync(Character(Contributor), page, Outsider, WikiMemberLevel.Reader);
		await Assert.That(byContributor.Expect<Error<string>>().Value)
			.IsEqualTo("Permission denied. Only a lead of the page lets people in or out.");
		var byOutsider = await world.Members.SetMemberAsync(Character(Outsider), page, Outsider, WikiMemberLevel.Lead);
		await Assert.That(byOutsider.Expect<Error<string>>().Value).IsEqualTo("No such page.");

		var lastLead = await world.Members.SetMemberAsync(Character(Lead), page, Lead, null);
		await Assert.That(lastLead.Expect<Error<string>>().Value)
			.IsEqualTo("A private page keeps at least one character as its lead. Name another lead first.");

		(await world.Members.SetMemberAsync(Admin, page, Contributor, WikiMemberLevel.Lead)).Expect<WikiPageMembers>();
		await Assert.That((await world.Members.SetMemberAsync(Character(Lead), page, Lead, null)).Expect<Error<string>>().Value)
			.IsEqualTo(WikiMembersService.LeftMessage);
		await Assert.That(await world.Members.GetAsync(Character(Lead), page) is NotFound).IsTrue();
	}

	[Test]
	public async Task TakingOutSomeoneWhoIsNotIn_IsRefused()
	{
		var (world, page) = await OpenAsync();

		var result = await world.Members.SetMemberAsync(Character(Lead), page, Outsider, null);
		await Assert.That(result.Expect<Error<string>>().Value).IsEqualTo("They are not a member of the page.");
	}

	[Test]
	public async Task OnlyWikiAdminMakesAPublicPagePrivate()
	{
		var world = Build();
		var page = (await world.Wiki.CreateAsync("Town map", "Streets", Lead)).Expect<WikiPage>();

		var byAuthor = await world.Members.SetMemberAsync(Character(Lead), page, Lead, WikiMemberLevel.Lead);
		await Assert.That(byAuthor.Expect<Error<string>>().Value)
			.IsEqualTo("Permission denied. Only wiki.admin makes a public page private.");

		(await world.Members.SetMemberAsync(Admin, page, Lead, WikiMemberLevel.Lead)).Expect<WikiPageMembers>();
		await Assert.That(await world.Access.CanSeeAsync(Character(Outsider), page)).IsFalse();
		await Assert.That(await world.Access.CanSeeAsync(Character(Lead), page)).IsTrue();
	}

	[Test]
	public async Task ACharacterMemberIsMatchedByObjid_NotByAReusedDbref()
	{
		var (world, page) = await OpenAsync();

		await Assert.That(await world.Access.CanSeeAsync(Character("#12:9999"), page)).IsFalse();
		await Assert.That(await world.Access.CanSeeAsync(Character("#12:1200"), page)).IsTrue();
		var plainDbref = await world.Members.SetMemberAsync(Character(Lead), page, "#13", WikiMemberLevel.Reader);
		await Assert.That(plainDbref.Expect<Error<string>>().Value).IsEqualTo("Not a character or a role: #13.");
	}

	[Test]
	public async Task ProtectingAPrivatePage_KeepsItsMembers()
	{
		var (world, page) = await OpenAsync();
		(await world.Access.SetRequirementsAsync(Admin, "#1", WikiRuleTarget.ForPage(page.Id), WikiRequirementSet.Protection))
			.Expect<WikiRequirements>();

		var set = (await world.Access.RequirementsAsync()).For(WikiRuleTarget.ForPage(page.Id))!;
		await Assert.That(set.Members.Count).IsEqualTo(3);
		await Assert.That(set.For(WikiAction.Delete)).IsEquivalentTo([PortalPermission.WikiAdmin]);
		await Assert.That((await world.Access.DecideAsync(Character(Contributor), page, WikiAction.Edit)).Allowed).IsFalse();
	}

	[Test]
	public async Task ARoleAlone_CannotBeTheLastLead()
	{
		var (world, page) = await OpenAsync();
		(await world.Members.SetMemberAsync(Character(Lead), page, "role:approved", WikiMemberLevel.Lead)).Expect<WikiPageMembers>();

		var stepDown = await world.Members.SetMemberAsync(Character(Lead), page, Lead, null);
		await Assert.That(stepDown.Expect<Error<string>>().Value)
			.IsEqualTo("A private page keeps at least one character as its lead. Name another lead first.");
	}

	[Test]
	public async Task InsertUnder_PutsTextFirstUnderTheHeading_AndKeepsWhatFollows()
	{
		var withSection = WikiHelpers.InsertUnder("Brief.\n\n## Developments\n\n### Old\n\nText.\n\n## Notes\n\nKeep.", "Developments",
			"### New\n\nNow.");
		await Assert.That(withSection).IsEqualTo("Brief.\n\n## Developments\n\n### New\n\nNow.\n\n### Old\n\nText.\n\n## Notes\n\nKeep.\n");

		var anyLevel = WikiHelpers.InsertUnder("Brief.\n\n### developments\n", "Developments", "Now.");
		await Assert.That(anyLevel).IsEqualTo("Brief.\n\n### developments\n\nNow.\n");

		var without = WikiHelpers.InsertUnder("Brief.", "Developments", "### New\n\nNow.");
		await Assert.That(without).IsEqualTo("Brief.\n\n## Developments\n\n### New\n\nNow.\n");
	}
}
