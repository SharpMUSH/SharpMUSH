using Bunit;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Client.Components.Help;
using SharpMUSH.Tests.BUnit.Pages;

namespace SharpMUSH.Tests.BUnit.Components.Help;

/// <summary>
/// The Help section sidebar: "Help · N topics", a search that opens a topic by its exact name or lands
/// on the index filtered to the term, Browse (the index and the getting-started entries the corpus
/// has), a bounded slice of the topic list, and Admin help for Wizard and God only.
/// </summary>
public class HelpSidebarTests : TrackingBunitContext
{
	private BunitNavigationManager Nav => Services.GetRequiredService<BunitNavigationManager>();

	private BunitAuthorizationContext Install(bool isStaff, Dictionary<string, Dictionary<string, string>>? corpora = null)
	{
		HelpApi.Install(this, isStaff, corpora ?? HelpPageTests.Corpora);
		Services.AddLocalization();
		var auth = AddAuthorization();
		if (isStaff) auth.SetAuthorized("headwiz").SetRoles("Wizard");
		return auth;
	}

	private IRenderedComponent<HelpSidebar> RenderAt(string path, bool collapsed = false)
	{
		Nav.NavigateTo(path);
		var cut = Render<HelpSidebar>(p => p.Add(x => x.Collapsed, collapsed));
		cut.WaitForAssertion(() => cut.Find(".help-side-browse a.kit-row"), TimeSpan.FromSeconds(5));
		if (!collapsed) cut.WaitForAssertion(() => cut.Find(".kit-side-sub"), TimeSpan.FromSeconds(5));
		return cut;
	}

	/// <summary>A corpus of <paramref name="count"/> topics, "topic-000" upward, so a slice has something to bound.</summary>
	private static Dictionary<string, Dictionary<string, string>> Large(int count) => new()
	{
		["help"] = Enumerable.Range(0, count)
			.Select(i => $"topic-{i:000}")
			.Append("help")
			.ToDictionary(t => t, t => $"# {t}\nBody of {t}.", StringComparer.OrdinalIgnoreCase),
	};

	[Test]
	public async Task WithNoHelpFiles_ThereIsNoTopicsHeadingOverNothing()
	{
		Install(isStaff: false, new() { ["help"] = new(), ["ahelp"] = new() });
		var cut = RenderAt("/help");
		await Assert.That(cut.Find(".kit-side-sub").TextContent).IsEqualTo("0 topics");
		await Assert.That(cut.FindAll(".help-side-topics-label").Count).IsEqualTo(0);
		await Assert.That(cut.FindAll(".help-side-topics").Count).IsEqualTo(0);
	}

	[Test]
	public async Task Header_CountsTheTopics()
	{
		Install(isStaff: false);
		var cut = RenderAt("/help");
		await Assert.That(cut.Find(".kit-side-title").TextContent).IsEqualTo("Help");
		await Assert.That(cut.Find(".kit-side-sub").TextContent).IsEqualTo("7 topics");
	}

	[Test]
	public async Task Browse_LinksTheIndex_AndTheGettingStartedEntriesTheCorpusHas()
	{
		Install(isStaff: false);
		var cut = RenderAt("/help");
		var rows = cut.FindAll(".help-side-browse a.kit-row");
		await Assert.That(rows.Select(r => r.GetAttribute("href")))
			.IsEquivalentTo(new[] { "/help", "/help/getting%20started", "/help/newbie" });
		await Assert.That(rows[0].GetAttribute("aria-current")).IsEqualTo("page");
	}

	[Test]
	public async Task Browse_LeavesOutEntriesTheCorpusDoesNotHave()
	{
		Install(isStaff: false, Large(3));
		var cut = RenderAt("/help");
		await Assert.That(cut.FindAll(".help-side-browse a.kit-row").Select(r => r.GetAttribute("href")))
			.IsEquivalentTo(new[] { "/help" });
	}

	[Test]
	public async Task OnATopic_TheSliceSurroundsIt_AndMarksIt()
	{
		Install(isStaff: false, Large(200));
		var cut = RenderAt("/help/topic-100");
		var rows = cut.FindAll(".help-side-topics a.kit-row");
		await Assert.That(rows.Count).IsEqualTo(HelpSidebar.TopicsShown).Because("the corpus is ~1500 topics in a real game");
		var current = cut.Find(".help-side-topics a.kit-row[aria-current='page']");
		await Assert.That(current.GetAttribute("href")).IsEqualTo("/help/topic-100");
		var labels = rows.Select(r => r.TextContent.Trim()).ToList();
		await Assert.That(labels.IndexOf("topic-100")).IsGreaterThan(2).Because("the neighbours before it are shown too");
	}

	/// <summary>
	/// A getting-started entry is listed once, in Browse. The topic slice listed it again, so on
	/// /help/newbie the same destination appeared twice and both rows claimed aria-current="page".
	/// </summary>
	[Test]
	public async Task OnAStarterTopic_ItIsListedAndMarkedOnce()
	{
		Install(isStaff: false);
		var cut = RenderAt("/help/newbie");
		await Assert.That(cut.FindAll("a.kit-row[href='/help/newbie']").Count).IsEqualTo(1);
		await Assert.That(cut.FindAll("a.kit-row[aria-current='page']").Count).IsEqualTo(1);
		await Assert.That(cut.Find(".help-side-browse a.kit-row[aria-current='page']").GetAttribute("href")).IsEqualTo("/help/newbie");
	}

	/// <summary>Leaving a starter out of the slice keeps the slice around where it falls in the list.</summary>
	[Test]
	public async Task OnAStarterTopic_TheSliceStillSurroundsWhereItFalls()
	{
		var corpus = new Dictionary<string, Dictionary<string, string>>
		{
			["help"] = Enumerable.Range(0, 200)
				.Select(i => $"alpha-{i:000}")
				.Append("newbie")
				.Append("help")
				.ToDictionary(t => t, t => $"# {t}\nBody of {t}.", StringComparer.OrdinalIgnoreCase),
		};
		Install(isStaff: false, corpus);
		var cut = RenderAt("/help/newbie");
		var labels = cut.FindAll(".help-side-topics a.kit-row").Select(r => r.TextContent.Trim()).ToList();
		await Assert.That(labels).DoesNotContain("newbie");
		await Assert.That(labels.Count).IsEqualTo(HelpSidebar.TopicsShown);
		await Assert.That(labels[^1]).IsEqualTo("alpha-199").Because("newbie sorts after every alpha-* entry, so its neighbours are the last of them");
	}

	[Test]
	public async Task OnTheIndex_TheSliceIsTheStartOfTheList()
	{
		Install(isStaff: false, Large(200));
		var cut = RenderAt("/help");
		var rows = cut.FindAll(".help-side-topics a.kit-row");
		await Assert.That(rows.Count).IsEqualTo(HelpSidebar.TopicsShown);
		await Assert.That(rows[0].TextContent.Trim()).IsEqualTo("topic-000").Because("the index entry itself is the Help index row");
	}

	[Test]
	public async Task Searching_ShowsTheMatches_Bounded()
	{
		Install(isStaff: false, Large(200));
		var cut = RenderAt("/help?q=topic-1");
		var rows = cut.FindAll(".help-side-topics a.kit-row");
		await Assert.That(rows.Count).IsEqualTo(HelpSidebar.TopicsShown);
		await Assert.That(rows.All(r => r.TextContent.Contains("topic-1", StringComparison.Ordinal))).IsTrue();
		await Assert.That(cut.Find(".help-side-topics-label").TextContent).Contains("100 matches")
			.Because("topic-100 to topic-199");
	}

	[Test]
	public async Task Search_OpensATopic_ByItsExactName()
	{
		Install(isStaff: false);
		var cut = RenderAt("/help");
		await cut.Find(".kit-side-search input").InputAsync("NEWBIE");
		await cut.Find(".kit-side-search").SubmitAsync();
		cut.WaitForAssertion(() => { if (!Nav.Uri.EndsWith("/help/newbie", StringComparison.Ordinal)) throw new InvalidOperationException($"still at {Nav.Uri}"); }, TimeSpan.FromSeconds(5));
		await Assert.That(Nav.Uri).EndsWith("/help/newbie");
	}

	[Test]
	public async Task Search_LandsOnTheFilteredIndex_Otherwise()
	{
		Install(isStaff: false);
		var cut = RenderAt("/help/newbie");
		await cut.Find(".kit-side-search input").InputAsync("mail-*");
		await cut.Find(".kit-side-search").SubmitAsync();
		cut.WaitForAssertion(() => { if (!Nav.Uri.EndsWith("/help?q=mail-%2A", StringComparison.Ordinal)) throw new InvalidOperationException($"still at {Nav.Uri}"); }, TimeSpan.FromSeconds(5));
		await Assert.That(Nav.Uri).EndsWith("/help?q=mail-%2A");
	}

	[Test]
	public async Task AdminHelp_IsNotOffered_ToAMortal()
	{
		var auth = Install(isStaff: false);
		auth.SetAuthorized("mortal");
		var cut = RenderAt("/help");
		await Assert.That(cut.FindAll(".help-side-admin").Count).IsEqualTo(0);
	}

	[Test]
	public async Task AdminHelp_ForStaff_LinksTheAdminIndex_AndMarksTheCurrentAdminTopic()
	{
		Install(isStaff: true);
		var cut = RenderAt("/help/admin/Security");
		cut.WaitForAssertion(() => cut.Find(".help-side-admin a[href='/help/admin/Security']"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.Find(".help-side-admin a.kit-row").GetAttribute("href")).IsEqualTo("/help/admin/ahelp");
		await Assert.That(cut.Find(".help-side-admin a[href='/help/admin/Security']").GetAttribute("aria-current")).IsEqualTo("page");
		await Assert.That(cut.FindAll(".help-side-topics a.kit-row[aria-current='page']").Count).IsEqualTo(0)
			.Because("an admin topic is not the general topic of the same name");
	}

	[Test]
	public async Task Collapsed_KeepsTheBrowseIcons_Only()
	{
		Install(isStaff: true);
		var cut = RenderAt("/help", collapsed: true);
		cut.WaitForAssertion(() =>
		{
			if (cut.FindAll(".help-side-browse a.kit-row--collapsed").Count < 3) throw new InvalidOperationException("the index has not loaded yet");
		}, TimeSpan.FromSeconds(5));
		await Assert.That(cut.FindAll(".help-side-admin a.kit-row--collapsed").Count).IsEqualTo(1);
		await Assert.That(cut.FindAll(".kit-side-head").Count).IsEqualTo(0);
		await Assert.That(cut.FindAll(".kit-side-search").Count).IsEqualTo(0);
		await Assert.That(cut.FindAll(".help-side-topics").Count).IsEqualTo(0);
		await Assert.That(cut.FindAll(".help-side-browse a.kit-row--collapsed").Count).IsEqualTo(3);
		await Assert.That(cut.FindAll(".kit-row-label").Count).IsEqualTo(0);
	}
}
