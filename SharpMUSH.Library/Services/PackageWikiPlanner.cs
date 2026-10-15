using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Models.Wiki;

namespace SharpMUSH.Library.Services;

/// <summary>A package page as the game has it now.</summary>
/// <param name="Id">The page's id.</param>
/// <param name="Content">Its fields, as the merge compares them.</param>
/// <param name="HasTranslations">Whether it has a translation, which the package never writes.</param>
public sealed record PackageWikiLivePage(string Id, PackageWikiContent Content, bool HasTranslations);

/// <summary>Everything a wiki plan reads, gathered by <see cref="IPackageWikiService"/>.</summary>
/// <param name="PackageId">The package.</param>
/// <param name="Version">The version being applied; it names the revisions the apply writes.</param>
/// <param name="Declared">The pages the version ships (none for an uninstall), each with its body read.</param>
/// <param name="Owned">The pages the package owns now, as it last wrote them.</param>
/// <param name="Live">The pages the game has, of those declared or owned, by (namespace, slug).</param>
/// <param name="OtherOwners">Pages another installed package owns, by (namespace, slug), to that package.</param>
/// <param name="CustomPermissions">The custom permissions the game has or the manifest declares.</param>
public sealed record PackageWikiPlanInputs(
	string PackageId,
	string Version,
	IReadOnlyList<PackageWikiPageSpec> Declared,
	IReadOnlyList<PackageWikiPageRecord> Owned,
	IReadOnlyDictionary<(string Namespace, string Slug), PackageWikiLivePage> Live,
	IReadOnlyDictionary<(string Namespace, string Slug), string> OtherOwners,
	IReadOnlySet<string> CustomPermissions);

/// <summary>
/// What one decision does to a page: the fields to make the live page hold, and what the package owns of it
/// afterwards.
/// </summary>
/// <param name="Write">The page as the apply leaves it: created when there is none, its differing fields written otherwise. Null writes nothing.</param>
/// <param name="Baseline">The baseline the package keeps (the new version's fields), or null when it lets go of the page.</param>
/// <param name="Delete">True to delete the page, last, once the apply has committed.</param>
public sealed record PackageWikiOutcome(PackageWikiContent? Write, PackageWikiContent? Baseline, bool Delete = false);

/// <summary>One page of a wiki plan: what the review screen shows, and what each decision writes.</summary>
/// <param name="Change">The review screen's row.</param>
/// <param name="Live">The page now, or null when there is none.</param>
/// <param name="Outcome">What the apply does; for a conflict, what keep mine does.</param>
/// <param name="TakeTheirs">For a conflict, what take theirs does.</param>
/// <param name="CustomBase">For a conflict, the fields an edited body is written with.</param>
/// <param name="Summary">The summary of the revision the apply writes.</param>
public sealed record PackageWikiStep(
	PackageWikiChange Change,
	PackageWikiLivePage? Live,
	PackageWikiOutcome Outcome,
	PackageWikiOutcome? TakeTheirs,
	PackageWikiContent? CustomBase,
	string Summary)
{
	/// <summary>The namespace and slug, the page's identity.</summary>
	public (string Namespace, string Slug) Key => (Change.Namespace, Change.Page);

	/// <summary>
	/// What <paramref name="decision"/> does; for a page that is not a conflict, or one with no decision, what
	/// keep mine does.
	/// </summary>
	public PackageWikiOutcome Decide(PackageConflictDecision? decision)
		=> (Change.Action, decision?.Resolution) switch
		{
			(PackageWikiAction.Conflict, PackageConflictResolution.TakeTheirs) => TakeTheirs ?? Outcome,
			(PackageWikiAction.Conflict, PackageConflictResolution.UseCustom) when CustomBase is not null =>
				new PackageWikiOutcome(CustomBase with { Markdown = PackageWikiContent.NormalizeMarkdown(decision!.CustomValue ?? "") },
					TakeTheirs?.Baseline ?? Outcome.Baseline ?? CustomBase),
			_ => Outcome
		};
}

/// <summary>
/// The pure half of <see cref="Interfaces.IPackageWikiService"/>: the plan table and the field-wise three-way
/// merge of a package's pages (docs/design/softcode-package-manager.md, "Wiki pages"). It reads nothing.
/// </summary>
public static class PackageWikiPlanner
{
	/// <summary>A page's identity, as the plan and the registry key it.</summary>
	public static (string Namespace, string Slug) KeyOf(PackageWikiPageSpec page) => (page.NamespaceName, page.Page);

	/// <summary>The fields a declared page asks for, as the merge compares them.</summary>
	public static PackageWikiContent ContentOf(PackageWikiPageSpec page)
		=> new(page.Title, PackageWikiContent.NormalizeMarkdown(page.Markdown ?? ""),
			WikiHelpers.NormalizeCategories(page.Categories, page.Namespace), page.Published, page.Requires,
			page.Namespace == WikiNamespace.Category && page.Pin);

	/// <summary>One step per declared page, in manifest order, then one per owned page the version drops.</summary>
	public static IReadOnlyList<PackageWikiStep> Plan(PackageWikiPlanInputs inputs)
	{
		var owned = inputs.Owned.ToDictionary(o => (o.Namespace, o.Slug));
		var steps = new List<PackageWikiStep>();
		var summary = $"{inputs.PackageId} {inputs.Version}";

		foreach (var page in inputs.Declared)
		{
			steps.Add(PlanPage(inputs, page, owned.GetValueOrDefault(KeyOf(page)), summary));
		}

		var declared = inputs.Declared.Select(KeyOf).ToHashSet();
		foreach (var record in inputs.Owned.Where(o => !declared.Contains((o.Namespace, o.Slug))))
		{
			steps.Add(PlanRelease(record, inputs.Live.GetValueOrDefault((record.Namespace, record.Slug)), summary));
		}

		return steps;
	}

	private static PackageWikiStep PlanPage(
		PackageWikiPlanInputs inputs, PackageWikiPageSpec page, PackageWikiPageRecord? baseline, string summary)
	{
		var key = KeyOf(page);
		var live = inputs.Live.GetValueOrDefault(key);
		var next = ContentOf(page);

		if (page.Markdown is null)
		{
			return Blocked(page, live, $"Its body, {page.File}, was not read.", summary);
		}

		if (inputs.OtherOwners.TryGetValue(key, out var otherPackage))
		{
			return Blocked(page, live, $"The {otherPackage} package owns this page; uninstall that package first, or link to a page of your own.", summary);
		}

		if (page.Requires.All.Where(scope => !PortalPermission.IsKnown(scope) && !inputs.CustomPermissions.Contains(scope)).ToList()
			is { Count: > 0 } unknown)
		{
			return Blocked(page, live, $"It requires {string.Join(", ", unknown)}, which the game does not have and the package does not declare.", summary);
		}

		// A page of the same slug that is not the one the package wrote is the game's: the package's was deleted.
		if (baseline is not null && live is not null && live.Id != baseline.PageId)
		{
			baseline = null;
		}

		return (live, baseline) switch
		{
			(null, null) => new PackageWikiStep(
				Change(page, PackageWikiAction.Create, next, null, null, next) with { Changes = Describe(null, next) },
				null, new PackageWikiOutcome(next, next), null, null, summary),
			({ } present, null) when present.Content.SameAs(next) => new PackageWikiStep(
				Change(page, PackageWikiAction.Adopt, next, null, present.Content, next) with { Detail = "The game already has this page exactly as the package ships it." },
				present, new PackageWikiOutcome(null, next), null, null, summary),
			({ } present, null) => new PackageWikiStep(
				Change(page, PackageWikiAction.Conflict, present.Content, null, present.Content, next) with
				{
					Conflict = PackageConflictKind.AddAdd,
					Changes = Describe(present.Content, next),
					Detail = "The game already has a page here. Keep mine leaves it the game's; take theirs writes the package's version as a new revision, and the package owns it."
				},
				present, new PackageWikiOutcome(null, null), new PackageWikiOutcome(next, next), next, summary),
			(null, { } owned) when owned.Content.SameAs(next) => new PackageWikiStep(
				Change(page, PackageWikiAction.Release, owned.Content, owned.Content, null, next) with { Detail = "Staff deleted it; it stays deleted and the package lets go of it." },
				null, new PackageWikiOutcome(null, null), null, null, summary),
			(null, { } owned) => new PackageWikiStep(
				Change(page, PackageWikiAction.Conflict, next, owned.Content, null, next) with
				{
					Conflict = PackageConflictKind.DeleteModify,
					Changes = Describe(owned.Content, next),
					Detail = "Staff deleted this page and the new version changes it. Keep mine leaves it deleted; take theirs creates it again."
				},
				null, new PackageWikiOutcome(null, null), new PackageWikiOutcome(next, next), next, summary),
			({ } present, { } owned) => PlanMerge(page, owned.Content, present, next, summary),
		};
	}

	/// <summary>The field-wise three-way merge of a page the package owns, live against baseline against new.</summary>
	private static PackageWikiStep PlanMerge(
		PackageWikiPageSpec page, PackageWikiContent baseline, PackageWikiLivePage live, PackageWikiContent next, string summary)
	{
		var mine = live.Content;
		var title = Scalar(baseline.Title, mine.Title, next.Title);
		var published = Scalar(baseline.Published, mine.Published, next.Published);
		var body = Body(baseline.Markdown, mine.Markdown, next.Markdown, summary);
		var categories = SetMerge(baseline.Categories, mine.Categories, next.Categories);
		var requires = PackageWikiRequires.Of(
			SetMerge(baseline.Requires.View, mine.Requires.View, next.Requires.View),
			SetMerge(baseline.Requires.Edit, mine.Requires.Edit, next.Requires.Edit),
			SetMerge(baseline.Requires.Delete, mine.Requires.Delete, next.Requires.Delete));
		var pinned = next.Pinned != baseline.Pinned ? next.Pinned : mine.Pinned;

		PackageWikiContent Merged(bool preferMine) => new(
			title.Resolve(preferMine), body.Resolve(preferMine), categories, published.Resolve(preferMine), requires, pinned);

		var ours = Merged(preferMine: true);
		var merged = mine.SameAs(baseline) ? summary : $"{summary}, merged";
		if (title.Clash || published.Clash || body.Clash)
		{
			var clashes = new List<string>();
			if (title.Clash) clashes.Add($"title: staff made it '{mine.Title}', the new version '{next.Title}'");
			if (published.Clash) clashes.Add($"published: staff made it {mine.Published}, the new version {next.Published}");
			if (body.Clash) clashes.Add($"body: {body.Clashes} clashing region(s), marked in the merged text");

			var theirs = Merged(preferMine: false);
			return new PackageWikiStep(
				Change(page, PackageWikiAction.Conflict, ours, baseline, mine, next) with
				{
					Conflict = PackageConflictKind.ModifyModify,
					MergedMarkdown = body.Marked,
					Changes = [.. clashes, .. Describe(mine, ours)],
					Detail = "Staff and the new version changed the same part of this page. Keep mine settles each clash for the game's text and takes the rest of the new version; take theirs settles each clash for the new version."
				},
				live,
				new PackageWikiOutcome(ours.SameAs(mine) ? null : ours, next),
				new PackageWikiOutcome(theirs.SameAs(mine) ? null : theirs, next),
				ours, merged);
		}

		if (ours.SameAs(mine))
		{
			return new PackageWikiStep(
				Change(page, PackageWikiAction.NoChange, mine, baseline, mine, next) with
				{
					Detail = mine.SameAs(next) ? null : "Staff changed this page and the new version does not; the game's page stands."
				},
				live, new PackageWikiOutcome(null, next), null, null, merged);
		}

		return new PackageWikiStep(
			Change(page, PackageWikiAction.Update, ours, baseline, mine, next) with
			{
				MergedMarkdown = ours.Markdown,
				Changes = Describe(mine, ours),
				Detail = mine.SameAs(baseline) ? null : "Staff changed this page too; their changes are kept."
			},
			live, new PackageWikiOutcome(ours, next), null, null, merged);
	}

	/// <summary>An owned page the new version drops, or every owned page on uninstall.</summary>
	private static PackageWikiStep PlanRelease(PackageWikiPageRecord owned, PackageWikiLivePage? live, string summary)
	{
		var baseline = owned.Content;
		PackageWikiChange Row(PackageWikiAction action, string detail) => new(
			owned.Namespace, owned.Slug, live?.Content.Title ?? owned.Title, action,
			BaseMarkdown: owned.Markdown, LiveMarkdown: live?.Content.Markdown, Detail: detail);

		if (live is null || live.Id != owned.PageId)
		{
			return new PackageWikiStep(Row(PackageWikiAction.Release, "It is already gone; the package lets go of it."),
				null, new PackageWikiOutcome(null, null), null, null, summary);
		}

		if (live.HasTranslations)
		{
			return new PackageWikiStep(Row(PackageWikiAction.Release, $"Kept {live.Content.Title}: it has a translation the package did not write."),
				live, new PackageWikiOutcome(null, null), null, null, summary);
		}

		if (!live.Content.SameAs(baseline))
		{
			return new PackageWikiStep(Row(PackageWikiAction.Release, $"Kept {live.Content.Title}: staff changed it."),
				live, new PackageWikiOutcome(null, null), null, null, summary);
		}

		return new PackageWikiStep(Row(PackageWikiAction.Delete, "Nobody changed it, so it is deleted."),
			live, new PackageWikiOutcome(null, null, Delete: true), null, null, summary);
	}

	private static PackageWikiStep Blocked(PackageWikiPageSpec page, PackageWikiLivePage? live, string detail, string summary)
		=> new(new PackageWikiChange(page.NamespaceName, page.Page, page.Title, PackageWikiAction.Blocked,
				LiveMarkdown: live?.Content.Markdown, NewMarkdown: page.Markdown, Detail: detail),
			live, new PackageWikiOutcome(null, null), null, null, summary);

	private static PackageWikiChange Change(PackageWikiPageSpec page, PackageWikiAction action, PackageWikiContent shown,
		PackageWikiContent? baseline, PackageWikiContent? live, PackageWikiContent next)
		=> new(page.NamespaceName, page.Page, shown.Title, action,
			BaseMarkdown: baseline?.Markdown, LiveMarkdown: live?.Markdown, NewMarkdown: next.Markdown);

	/// <summary>Each field that differs between <paramref name="from"/> (null for no page) and <paramref name="to"/>, one per line.</summary>
	public static IReadOnlyList<string> Describe(PackageWikiContent? from, PackageWikiContent to)
	{
		var lines = new List<string>();
		if (from?.Title != to.Title) lines.Add($"title: {(from is null ? "" : $"'{from.Title}' -> ")}'{to.Title}'");
		if (from is not null && from.Markdown != to.Markdown) lines.Add("body: changed");
		if (from?.Published != to.Published && (from is not null || !to.Published)) lines.Add($"published: {to.Published}");
		Sets("categories", from?.Categories ?? [], to.Categories);
		Sets("requires view", from?.Requires.View ?? [], to.Requires.View);
		Sets("requires edit", from?.Requires.Edit ?? [], to.Requires.Edit);
		Sets("requires delete", from?.Requires.Delete ?? [], to.Requires.Delete);
		if ((from?.Pinned ?? false) != to.Pinned) lines.Add(to.Pinned ? "pinned to the wiki home" : "unpinned from the wiki home");
		return lines;

		void Sets(string what, IReadOnlyList<string> before, IReadOnlyList<string> after)
		{
			var added = after.Except(before, StringComparer.Ordinal).Select(s => $"+{s}");
			var removed = before.Except(after, StringComparer.Ordinal).Select(s => $"-{s}");
			if (added.Concat(removed).ToList() is { Count: > 0 } diff) lines.Add($"{what}: {string.Join(' ', diff)}");
		}
	}

	/// <summary>The live set with what the version added since its baseline added, and what it removed removed.</summary>
	private static IReadOnlyList<string> SetMerge(IReadOnlyList<string> baseline, IReadOnlyList<string> live, IReadOnlyList<string> next)
		=> live.Union(next.Except(baseline, StringComparer.Ordinal), StringComparer.Ordinal)
			.Except(baseline.Except(next, StringComparer.Ordinal), StringComparer.Ordinal)
			.Order(StringComparer.Ordinal)
			.ToList();

	/// <summary>A one-value field: the side that changed it wins, and both changing it differently clashes.</summary>
	private readonly record struct Merge<T>(T Mine, T Theirs, bool Clash)
	{
		public T Resolve(bool preferMine) => preferMine ? Mine : Theirs;
	}

	private static Merge<T> Scalar<T>(T baseline, T mine, T next)
	{
		var comparer = EqualityComparer<T>.Default;
		if (comparer.Equals(mine, baseline) || comparer.Equals(mine, next)) return new(next, next, false);
		if (comparer.Equals(next, baseline)) return new(mine, mine, false);
		return new(mine, next, true);
	}

	/// <summary>The body, merged line by line.</summary>
	private readonly record struct BodyMerge(string Mine, string Theirs, string? Marked, int Clashes)
	{
		public bool Clash => Clashes > 0;

		public string Resolve(bool preferMine) => preferMine ? Mine : Theirs;
	}

	private static BodyMerge Body(string baseline, string mine, string next, string package)
	{
		if (mine == baseline || mine == next) return new(next, next, null, 0);
		if (next == baseline) return new(mine, mine, null, 0);

		var merge = LineMerge.Merge(baseline, mine, next);
		return merge.IsClean
			? new(merge.Resolve(true), merge.Resolve(true), null, 0)
			: new(merge.Resolve(preferOurs: true), merge.Resolve(preferOurs: false), merge.Marked("game", package), merge.Conflicts.Count);
	}
}
