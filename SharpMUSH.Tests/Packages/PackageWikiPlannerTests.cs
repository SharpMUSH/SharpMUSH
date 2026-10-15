using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Tests.Packages;

/// <summary>
/// <see cref="PackageWikiPlanner"/>, row by row of the plan table in docs/design/softcode-package-manager.md
/// ("Wiki pages"): what each combination of live page and baseline plans, and what each decision writes.
/// </summary>
public class PackageWikiPlannerTests
{
	private const string Package = "coast-guide";
	private const string Body = "# Coast Road\nIntro.\nMiddle.\nEnd.";

	private static PackageWikiPageSpec Page(
		string body = Body, string title = "Coast Road", string[]? categories = null, PackageWikiRequires? requires = null,
		string slug = "coast_road", WikiNamespace ns = WikiNamespace.Main, bool published = true, bool pin = false)
		=> new(slug, ns, title, categories ?? ["Travel"], null, body, requires ?? PackageWikiRequires.None, published, pin);

	private static PackageWikiContent Content(PackageWikiPageSpec page) => PackageWikiPlanner.ContentOf(page);

	private static PackageWikiPageRecord Owned(PackageWikiPageSpec page, string pageId = "page-1", string version = "1.0.0")
	{
		var content = Content(page);
		return new PackageWikiPageRecord(Package, pageId, page.NamespaceName, page.Page, content.Title, content.Markdown,
			content.Categories, content.Published, content.Requires, content.Pinned, version);
	}

	private static PackageWikiLivePage Live(PackageWikiContent content, string id = "page-1", bool translated = false)
		=> new(id, content, translated);

	private static PackageWikiPlanInputs Inputs(
		IReadOnlyList<PackageWikiPageSpec> declared,
		IReadOnlyList<PackageWikiPageRecord>? owned = null,
		IReadOnlyList<PackageWikiLivePage>? live = null,
		IReadOnlyDictionary<(string, string), string>? otherOwners = null,
		IReadOnlySet<string>? custom = null,
		string version = "1.1.0")
	{
		// Every live page in these tests is the coast_road main page unless a test keys its own.
		var livePages = new Dictionary<(string Namespace, string Slug), PackageWikiLivePage>();
		foreach (var page in live ?? [])
		{
			livePages[("main", "coast_road")] = page;
		}

		return new PackageWikiPlanInputs(Package, version, declared, owned ?? [], livePages,
			otherOwners ?? new Dictionary<(string, string), string>(), custom ?? new HashSet<string>());
	}

	private static PackageWikiStep Single(PackageWikiPlanInputs inputs) => PackageWikiPlanner.Plan(inputs).Single();

	private static PackageConflictDecision Decision(PackageWikiStep step, PackageConflictResolution resolution, string? custom = null)
		=> new(step.Change.TargetRef, step.Change.Page, resolution, custom);

	[Test]
	public async Task AnAbsentPageIsCreatedAndOwned()
	{
		var page = Page();
		var step = Single(Inputs([page], version: "1.0.0"));

		await Assert.That(step.Change.Action).IsEqualTo(PackageWikiAction.Create);
		await Assert.That(step.Summary).IsEqualTo("coast-guide 1.0.0");
		await Assert.That(step.Outcome.Write!.SameAs(Content(page))).IsTrue();
		await Assert.That(step.Outcome.Baseline!.SameAs(Content(page))).IsTrue();
		await Assert.That(step.Change.TargetRef).IsEqualTo("wiki:main");
	}

	[Test]
	public async Task APageTheGameHasExactlySoIsAdoptedWithoutAWrite()
	{
		var page = Page();
		var step = Single(Inputs([page], live: [Live(Content(page))]));

		await Assert.That(step.Change.Action).IsEqualTo(PackageWikiAction.Adopt);
		await Assert.That(step.Outcome.Write).IsNull();
		await Assert.That(step.Outcome.Baseline!.SameAs(Content(page))).IsTrue();
	}

	[Test]
	public async Task APageTheGameHasOtherwiseIsAnAddAddConflict()
	{
		var page = Page();
		var gameText = Content(page) with { Markdown = "# Coast Road\nThe game's own page." };
		var step = Single(Inputs([page], live: [Live(gameText)]));

		await Assert.That(step.Change.Action).IsEqualTo(PackageWikiAction.Conflict);
		await Assert.That(step.Change.Conflict).IsEqualTo(PackageConflictKind.AddAdd);

		// Keep mine: the page stays the game's and the package does not own it.
		var keep = step.Decide(Decision(step, PackageConflictResolution.KeepMine));
		await Assert.That(keep.Write).IsNull();
		await Assert.That(keep.Baseline).IsNull();

		// Take theirs: the package's page is written and owned.
		var take = step.Decide(Decision(step, PackageConflictResolution.TakeTheirs));
		await Assert.That(take.Write!.Markdown).IsEqualTo(Body);
		await Assert.That(take.Baseline!.Markdown).IsEqualTo(Body);

		// An edited page is written with the package's other fields, and owned.
		var custom = step.Decide(Decision(step, PackageConflictResolution.UseCustom, "Edited.\r\nText."));
		await Assert.That(custom.Write!.Markdown).IsEqualTo("Edited.\nText.");
		await Assert.That(custom.Write.Title).IsEqualTo("Coast Road");
		await Assert.That(custom.Baseline!.Markdown).IsEqualTo(Body);
	}

	[Test]
	public async Task APageAnotherPackageOwnsIsBlocked()
	{
		var step = Single(Inputs([Page()], otherOwners: new Dictionary<(string, string), string> { [("main", "coast_road")] = "harbour-guide" }));

		await Assert.That(step.Change.Action).IsEqualTo(PackageWikiAction.Blocked);
		await Assert.That(step.Change.Detail).Contains("harbour-guide");
		await Assert.That(step.Outcome.Write).IsNull();
	}

	[Test]
	public async Task APermissionNobodyHasIsBlocked_AndADeclaredOneIsNot()
	{
		var page = Page(requires: PackageWikiRequires.Of(["coast.gm"], ["wiki.admin"], null));

		var unknown = Single(Inputs([page]));
		await Assert.That(unknown.Change.Action).IsEqualTo(PackageWikiAction.Blocked);
		await Assert.That(unknown.Change.Detail).Contains("coast.gm");

		var declared = Single(Inputs([page], custom: new HashSet<string> { "coast.gm" }));
		await Assert.That(declared.Change.Action).IsEqualTo(PackageWikiAction.Create);
	}

	[Test]
	public async Task AFilePageWhoseBodyWasNotReadIsBlocked()
	{
		var page = Page() with { File = "wiki/coast-road.md", Markdown = null };
		var step = Single(Inputs([page]));

		await Assert.That(step.Change.Action).IsEqualTo(PackageWikiAction.Blocked);
		await Assert.That(step.Change.Detail).Contains("wiki/coast-road.md");
	}

	[Test]
	public async Task AnUntouchedPageTakesTheNewVersion()
	{
		var v1 = Page();
		var v2 = Page(body: Body + "\nA new line.", title: "The Coast Road");
		var step = Single(Inputs([v2], owned: [Owned(v1)], live: [Live(Content(v1))]));

		await Assert.That(step.Change.Action).IsEqualTo(PackageWikiAction.Update);
		await Assert.That(step.Summary).IsEqualTo("coast-guide 1.1.0");
		await Assert.That(step.Outcome.Write!.SameAs(Content(v2))).IsTrue();
		await Assert.That(step.Outcome.Baseline!.SameAs(Content(v2))).IsTrue();
		await Assert.That(step.Change.Changes!).Contains("title: 'Coast Road' -> 'The Coast Road'");
	}

	[Test]
	public async Task AnUnchangedPackagePageIsNoChange()
	{
		var v1 = Page();
		var step = Single(Inputs([v1], owned: [Owned(v1)], live: [Live(Content(v1))]));

		await Assert.That(step.Change.Action).IsEqualTo(PackageWikiAction.NoChange);
		await Assert.That(step.Outcome.Write).IsNull();
	}

	[Test]
	public async Task StaffEditsTheVersionDidNotTouchStand()
	{
		var v1 = Page();
		var staff = Content(v1) with { Markdown = "# Coast Road\nIntro, by staff.\nMiddle.\nEnd." };
		var step = Single(Inputs([v1], owned: [Owned(v1)], live: [Live(staff)]));

		await Assert.That(step.Change.Action).IsEqualTo(PackageWikiAction.NoChange);
		await Assert.That(step.Outcome.Write).IsNull();
		await Assert.That(step.Outcome.Baseline!.Markdown).IsEqualTo(Body);
	}

	[Test]
	public async Task EditsToDifferentPassagesMergeIntoOneRevision()
	{
		var v1 = Page();
		var v2 = Page(body: "# Coast Road\nIntro.\nMiddle.\nEnd, from the package.");
		var staff = Content(v1) with { Markdown = "# Coast Road\nIntro, by staff.\nMiddle.\nEnd." };
		var step = Single(Inputs([v2], owned: [Owned(v1)], live: [Live(staff)]));

		await Assert.That(step.Change.Action).IsEqualTo(PackageWikiAction.Update);
		await Assert.That(step.Summary).IsEqualTo("coast-guide 1.1.0, merged");
		await Assert.That(step.Outcome.Write!.Markdown).IsEqualTo("# Coast Road\nIntro, by staff.\nMiddle.\nEnd, from the package.");

		// The baseline advances to what the version ships, not to the merge.
		await Assert.That(step.Outcome.Baseline!.Markdown).IsEqualTo(v2.Markdown);
	}

	[Test]
	public async Task EditsToTheSamePassageAreAModifyModifyConflict()
	{
		var v1 = Page();
		var v2 = Page(body: "# Coast Road\nIntro, from the package.\nMiddle.\nEnd, from the package.");
		var staff = Content(v1) with { Markdown = "# Coast Road\nIntro, by staff.\nMiddle.\nEnd." };
		var step = Single(Inputs([v2], owned: [Owned(v1)], live: [Live(staff)]));

		await Assert.That(step.Change.Action).IsEqualTo(PackageWikiAction.Conflict);
		await Assert.That(step.Change.Conflict).IsEqualTo(PackageConflictKind.ModifyModify);
		await Assert.That(step.Change.MergedMarkdown!).Contains("<<<<<<< game\nIntro, by staff.\n=======\nIntro, from the package.\n>>>>>>> coast-guide 1.1.0");

		// Keep mine settles the clash for the game and still takes the version's other change.
		var keep = step.Decide(Decision(step, PackageConflictResolution.KeepMine));
		await Assert.That(keep.Write!.Markdown).IsEqualTo("# Coast Road\nIntro, by staff.\nMiddle.\nEnd, from the package.");
		await Assert.That(keep.Baseline!.Markdown).IsEqualTo(v2.Markdown);

		var take = step.Decide(Decision(step, PackageConflictResolution.TakeTheirs));
		await Assert.That(take.Write!.Markdown).IsEqualTo(v2.Markdown);

		var custom = step.Decide(Decision(step, PackageConflictResolution.UseCustom, "Hand merged."));
		await Assert.That(custom.Write!.Markdown).IsEqualTo("Hand merged.");
		await Assert.That(custom.Baseline!.Markdown).IsEqualTo(v2.Markdown);
	}

	[Test]
	public async Task ATitleBothSidesChangedIsAConflict()
	{
		var v1 = Page();
		var v2 = Page(title: "The Coast Road");
		var staff = Content(v1) with { Title = "Old Coast Road" };
		var step = Single(Inputs([v2], owned: [Owned(v1)], live: [Live(staff)]));

		await Assert.That(step.Change.Conflict).IsEqualTo(PackageConflictKind.ModifyModify);
		await Assert.That(step.Decide(Decision(step, PackageConflictResolution.KeepMine)).Write).IsNull();
		await Assert.That(step.Decide(Decision(step, PackageConflictResolution.TakeTheirs)).Write!.Title).IsEqualTo("The Coast Road");
	}

	[Test]
	public async Task CategoriesAndRequirementsMergeAsSets()
	{
		var v1 = Page(categories: ["Travel", "Roads"], requires: PackageWikiRequires.Of(null, ["wiki.admin"], null));
		var v2 = Page(categories: ["Travel", "Coast"], requires: PackageWikiRequires.Of(null, ["wiki.admin"], ["wiki.admin"]));
		var staff = Content(v1) with { Categories = ["lore", "roads", "travel"] };
		var step = Single(Inputs([v2], owned: [Owned(v1)], live: [Live(staff)]));

		await Assert.That(step.Change.Action).IsEqualTo(PackageWikiAction.Update);

		// Staff's lore stays, the version's coast is added and the roads it dropped goes.
		await Assert.That(step.Outcome.Write!.Categories).IsEquivalentTo(new[] { "coast", "lore", "travel" });
		await Assert.That(step.Outcome.Write.Requires.Delete).IsEquivalentTo(new[] { "wiki.admin" });
	}

	[Test]
	public async Task StaffUnpinningACategoryStandsUntilTheVersionChangesThePin()
	{
		var v1 = Page(slug: "travel", ns: WikiNamespace.Category, title: "Travel", categories: [], pin: true);
		var unpinned = Content(v1) with { Pinned = false };
		var inputs = new PackageWikiPlanInputs(Package, "1.1.0", [v1], [Owned(v1)],
			new Dictionary<(string, string), PackageWikiLivePage> { [("category", "travel")] = Live(unpinned) },
			new Dictionary<(string, string), string>(), new HashSet<string>());

		var step = Single(inputs);

		await Assert.That(step.Change.Action).IsEqualTo(PackageWikiAction.NoChange);
		await Assert.That(step.Outcome.Write).IsNull();
	}

	[Test]
	public async Task APageStaffDeletedStaysDeletedWhenTheVersionLeftItAlone()
	{
		var v1 = Page();
		var step = Single(Inputs([v1], owned: [Owned(v1)]));

		await Assert.That(step.Change.Action).IsEqualTo(PackageWikiAction.Release);
		await Assert.That(step.Outcome.Write).IsNull();
		await Assert.That(step.Outcome.Baseline).IsNull();
	}

	[Test]
	public async Task APageStaffDeletedThatTheVersionChangedIsADeleteModifyConflict()
	{
		var v1 = Page();
		var v2 = Page(body: Body + "\nNew.");
		var step = Single(Inputs([v2], owned: [Owned(v1)]));

		await Assert.That(step.Change.Conflict).IsEqualTo(PackageConflictKind.DeleteModify);
		var keep = step.Decide(Decision(step, PackageConflictResolution.KeepMine));
		await Assert.That(keep.Write).IsNull();
		await Assert.That(keep.Baseline).IsNull();
		await Assert.That(step.Decide(Decision(step, PackageConflictResolution.TakeTheirs)).Write!.Markdown).IsEqualTo(v2.Markdown);
	}

	[Test]
	public async Task APageOfTheSameSlugThatIsNotThePackagesIsTheGames()
	{
		var v1 = Page();
		var other = Content(v1) with { Markdown = "Someone else's page." };
		var step = Single(Inputs([v1], owned: [Owned(v1, pageId: "page-1")], live: [Live(other, id: "page-2")]));

		await Assert.That(step.Change.Conflict).IsEqualTo(PackageConflictKind.AddAdd);
	}

	[Test]
	public async Task AnUntouchedPageTheVersionDropsIsDeleted()
	{
		var v1 = Page();
		var step = Single(Inputs([], owned: [Owned(v1)], live: [Live(Content(v1))]));

		await Assert.That(step.Change.Action).IsEqualTo(PackageWikiAction.Delete);
		await Assert.That(step.Outcome.Delete).IsTrue();
		await Assert.That(step.Outcome.Baseline).IsNull();
	}

	[Test]
	public async Task AChangedPageTheVersionDropsIsKept()
	{
		var v1 = Page();
		var staff = Content(v1) with { Categories = ["lore", "travel"] };
		var step = Single(Inputs([], owned: [Owned(v1)], live: [Live(staff)]));

		await Assert.That(step.Change.Action).IsEqualTo(PackageWikiAction.Release);
		await Assert.That(step.Change.Detail).IsEqualTo("Kept Coast Road: staff changed it.");
		await Assert.That(step.Outcome.Delete).IsFalse();
	}

	[Test]
	public async Task ATranslatedPageTheVersionDropsIsKept()
	{
		var v1 = Page();
		var step = Single(Inputs([], owned: [Owned(v1)], live: [Live(Content(v1), translated: true)]));

		await Assert.That(step.Change.Action).IsEqualTo(PackageWikiAction.Release);
		await Assert.That(step.Outcome.Delete).IsFalse();
	}

	[Test]
	public async Task ADroppedPageAlreadyGoneIsLetGo()
	{
		var step = Single(Inputs([], owned: [Owned(Page())]));

		await Assert.That(step.Change.Action).IsEqualTo(PackageWikiAction.Release);
		await Assert.That(step.Outcome.Delete).IsFalse();
	}

	/// <summary>The timeout is the measure: a plan that compared pages pairwise, or re-diffed whole bodies, misses it.</summary>
	[Test]
	[Timeout(10_000)]
	public async Task ALargePackPlansQuickly(CancellationToken cancellationToken)
	{
		// 500 pages of 200 lines each, every one owned, edited by staff at the top and by the version at the bottom.
		var lines = Enumerable.Range(0, 200).Select(i => $"Line {i} of the rules text.").ToArray();
		var body = string.Join('\n', lines);
		var declared = new List<PackageWikiPageSpec>();
		var owned = new List<PackageWikiPageRecord>();
		var live = new Dictionary<(string, string), PackageWikiLivePage>();
		for (var i = 0; i < 500; i++)
		{
			var v1 = Page(body: body, slug: $"rule_{i}", title: $"Rule {i}");
			var v2 = v1 with { Markdown = body.Replace("Line 199 of", "Line 199, revised, of") };
			declared.Add(v2);
			owned.Add(Owned(v1, pageId: $"page-{i}"));
			live[("main", v1.Page)] = Live(Content(v1) with { Markdown = body.Replace("Line 0 of", "Line 0, by staff, of") }, id: $"page-{i}");
		}

		var steps = PackageWikiPlanner.Plan(new PackageWikiPlanInputs(Package, "1.1.0", declared, owned, live,
			new Dictionary<(string, string), string>(), new HashSet<string>()));

		cancellationToken.ThrowIfCancellationRequested();
		await Assert.That(steps.Count).IsEqualTo(500);
		await Assert.That(steps.All(s => s.Change.Action == PackageWikiAction.Update)).IsTrue();
	}
}
