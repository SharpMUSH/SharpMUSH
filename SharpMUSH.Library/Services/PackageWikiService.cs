using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Services;

/// <inheritdoc />
public sealed class PackageWikiService(
	IWikiService wiki,
	IWikiLocalizationService localization,
	IPackageRegistryService packages,
	IRoleRegistryService roles) : IPackageWikiService
{
	/// <inheritdoc />
	public async Task<IReadOnlyList<PackageWikiChange>> PlanAsync(
		string packageId,
		string version,
		IReadOnlyList<PackageWikiPageSpec> pages,
		IReadOnlyCollection<string> declaredPermissions,
		CancellationToken cancellationToken = default)
		=> [.. PackageWikiPlanner.Plan(await GatherAsync(packageId, version, pages, declaredPermissions, cancellationToken))
			.Select(step => step.Change)];

	/// <inheritdoc />
	public async Task<Result<IReadOnlyList<PackageWikiDeletion>>> ApplyAsync(
		PackageWriteTransaction writes,
		string packageId,
		string version,
		IReadOnlyList<PackageWikiPageSpec> pages,
		IReadOnlyCollection<string> declaredPermissions,
		string? locale,
		Func<PackageWikiChange, PackageConflictDecision?> decisions,
		string editorDbref,
		List<string> notes,
		CancellationToken cancellationToken = default)
	{
		writes.Origin.RequireWiki(wiki, localization, packages, roles);
		using var admitted = writes.AdmitHelper();
		var steps = PackageWikiPlanner.Plan(await GatherAsync(packageId, version, pages, declaredPermissions, cancellationToken));
		if (steps.Where(s => s.Change.Action == PackageWikiAction.Blocked).ToList() is { Count: > 0 } blocked)
		{
			return new Error<string>($"Wiki pages are blocked: {string.Join("; ", blocked.Select(s => $"{Label(s.Change)}: {s.Change.Detail}"))}");
		}

		var undecided = steps.Where(s => s.Change.Action == PackageWikiAction.Conflict && decisions(s.Change) is null).ToList();
		if (undecided.Count > 0)
		{
			return new Error<string>($"Unresolved wiki conflicts: {string.Join(", ", undecided.Select(s => Label(s.Change)))}");
		}

		var sourceLocale = locale ?? localization.DefaultLocale;
		var deletions = new List<PackageWikiDeletion>();
		foreach (var step in steps)
		{
			var decision = step.Change.Action == PackageWikiAction.Conflict ? decisions(step.Change) : null;
			var outcome = step.Decide(decision);
			switch (await ApplyStepAsync(writes, packageId, version, step, outcome, sourceLocale, editorDbref, cancellationToken))
			{
				case Error<string> error:
					return error;
				case PackageWikiDeletion deletion:
					deletions.Add(deletion);
					break;
			}

			if (Note(step, decision, outcome) is { } note)
			{
				notes.Add(note);
			}
		}

		return deletions;
	}

	/// <inheritdoc />
	public async Task DeleteReleasedAsync(PackageWriteTransaction writes, IReadOnlyList<PackageWikiDeletion> deletions,
		string editorDbref, List<string> notes)
	{
		if (deletions.Count == 0)
		{
			return;
		}

		writes.Origin.RequireWiki(wiki, localization, packages, roles);
		using var admitted = writes.AdmitReleased();
		var finished = false;
		try
		{
			var pinned = (await wiki.GetPinnedCategoriesAsync()).ToHashSet(StringComparer.Ordinal);
			foreach (var deletion in deletions)
			{
				try
				{
					if (deletion.UnpinCategory is { } category && pinned.Contains(category))
					{
						await wiki.SetCategoryPinnedAsync(category, pinned: false);
					}

					notes.Add(await wiki.DeleteAsync(deletion.PageId, editorDbref) switch
					{
						None => $"Deleted wiki page {deletion.Title}: nobody had changed it.",
						NotFound => $"Wiki page {deletion.Title} was already gone.",
					});
				}
				catch (Exception ex) when (ex is not OperationCanceledException)
				{
					// The operation has committed; a page that could not be deleted is left as the game's.
					writes.ReleasedWorkFailed();
					notes.Add($"Wiki page {deletion.Title} could not be deleted and is left as the game's: {ex.Message}");
				}
			}
			finished = true;
		}
		finally
		{
			if (!finished) writes.ReleasedWorkFailed();
		}
	}

	/// <summary>Reads what a wiki plan compares: the pages declared or owned, who owns them, and the permissions the game has.</summary>
	private async Task<PackageWikiPlanInputs> GatherAsync(
		string packageId,
		string version,
		IReadOnlyList<PackageWikiPageSpec> pages,
		IReadOnlyCollection<string> declaredPermissions,
		CancellationToken cancellationToken)
	{
		var owned = await packages.GetPackageWikiPagesAsync(packageId);
		var live = new Dictionary<(string Namespace, string Slug), PackageWikiLivePage>();
		var otherOwners = new Dictionary<(string Namespace, string Slug), string>();
		var custom = new HashSet<string>(declaredPermissions, StringComparer.Ordinal);
		if (pages.Count == 0 && owned.Count == 0)
		{
			return new PackageWikiPlanInputs(packageId, version, pages, owned, live, otherOwners, custom);
		}

		foreach (var other in (await packages.GetAllPackageWikiPagesAsync()).Where(p => p.PackageId != packageId))
		{
			otherOwners.TryAdd((other.Namespace, other.Slug), other.PackageId);
		}

		if (pages.Any(page => !page.Requires.IsEmpty))
		{
			custom.UnionWith((await roles.GetCustomPermissionsAsync(cancellationToken)).Select(p => p.Scope));
		}

		var requirements = await wiki.GetRequirementsAsync();
		var pinned = (await wiki.GetPinnedCategoriesAsync()).ToHashSet(StringComparer.Ordinal);
		var declared = pages.Select(PackageWikiPlanner.KeyOf).ToHashSet();
		foreach (var key in declared.Concat(owned.Select(o => (o.Namespace, o.Slug))).Distinct())
		{
			if (WikiHelpers.ParseNamespace(key.Item1) is not { } ns
				|| await wiki.GetBySlugAsync(key.Item2, ns) is not WikiPage page)
			{
				continue;
			}

			// Only a page the package lets go of is asked about translations: that is what decides whether it is untouched.
			var translated = !declared.Contains(key) && (await wiki.GetTranslationsAsync(page.Id)).Count > 0;
			live[key] = new PackageWikiLivePage(page.Id, LiveContent(page, ns, requirements, pinned), translated);
		}

		return new PackageWikiPlanInputs(packageId, version, pages, owned, live, otherOwners, custom);
	}

	private static PackageWikiContent LiveContent(WikiPage page, WikiNamespace ns, WikiRequirements requirements, IReadOnlySet<string> pinned)
	{
		var set = requirements.For(WikiRuleTarget.ForPage(page.Id));
		return new PackageWikiContent(
			page.Title,
			PackageWikiContent.NormalizeMarkdown(page.MarkdownSource),
			page.Categories.Order(StringComparer.Ordinal).ToList(),
			page.Published,
			PackageWikiRequires.Of(set?.For(WikiAction.Read), set?.For(WikiAction.Edit), set?.For(WikiAction.Delete)),
			ns == WikiNamespace.Category && pinned.Contains(page.Slug));
	}

	/// <summary>
	/// Makes the page what <paramref name="outcome"/> says and records what the package owns of it. Returns the page
	/// to delete after the commit, <see cref="None"/>, or why it failed.
	/// </summary>
	private async Task<PackageWikiStepResult> ApplyStepAsync(
		PackageWriteTransaction writes,
		string packageId,
		string version,
		PackageWikiStep step,
		PackageWikiOutcome outcome,
		string sourceLocale,
		string editorDbref,
		CancellationToken cancellationToken)
	{
		var (nsName, slug) = step.Key;
		var pageId = step.Live?.Id;
		if (outcome.Write is { } content)
		{
			var written = step.Live is { } live
				? await UpdateAsync(writes, step, live, content, editorDbref)
				: await CreateAsync(writes, step, content, sourceLocale, editorDbref);
			switch (written)
			{
				case string id:
					pageId = id;
					break;
				case Error<string> error:
					return error;
			}
		}

		if (outcome.Baseline is { } baseline)
		{
			if (pageId is null)
			{
				return new Error<string>($"Wiki page {Label(step.Change)} has no page to own.");
			}

			await writes.UpsertPackageWikiPageAsync(new PackageWikiPageRecord(packageId, pageId, nsName, slug,
				baseline.Title, baseline.Markdown, baseline.Categories, baseline.Published, baseline.Requires, baseline.Pinned,
				version));
		}
		else
		{
			await writes.RemovePackageWikiPageAsync(packageId, nsName, slug);
		}

		cancellationToken.ThrowIfCancellationRequested();
		return outcome.Delete && step.Live is { } doomed
			? new PackageWikiDeletion(doomed.Id, doomed.Content.Title, doomed.Content.Pinned ? slug : null)
			: new None();
	}

	/// <summary>Creates the page; undone by deleting it, which takes its requirements along.</summary>
	private async Task<Result<string>> CreateAsync(
		PackageWriteTransaction writes, PackageWikiStep step, PackageWikiContent content, string sourceLocale, string editorDbref)
	{
		var (nsName, slug) = step.Key;
		var ns = WikiHelpers.ParseNamespace(nsName) ?? WikiNamespace.Main;
		if (await wiki.CreateAsync(content.Title, content.Markdown, editorDbref, ns, sourceLocale, content.Categories,
				slug: slug, editSummary: step.Summary) is not WikiPage page)
		{
			return new Error<string>($"Wiki page {Label(step.Change)} could not be created: a page took its place.");
		}

		writes.Track($"wiki page {Label(step.Change)} (created)", async () => await wiki.DeleteAsync(page.Id, editorDbref));

		if (!content.Published)
		{
			await wiki.SetMetadataAsync(page.Id, content.Categories, published: false);
		}

		if (!content.Requires.IsEmpty)
		{
			await wiki.SetRequirementsAsync(WikiRuleTarget.ForPage(page.Id), Required(content.Requires, null), editorDbref);
		}

		if (content.Pinned)
		{
			await PinAsync(writes, slug, pinned: true);
		}

		return page.Id;
	}

	/// <summary>
	/// Writes each field of the page that differs from <paramref name="content"/>. Each write is undone only while
	/// the field still holds what this wrote: someone else's later edit stands.
	/// </summary>
	private async Task<Result<string>> UpdateAsync(
		PackageWriteTransaction writes, PackageWikiStep step, PackageWikiLivePage live, PackageWikiContent content, string editorDbref)
	{
		var id = live.Id;
		var before = live.Content;
		var label = Label(step.Change);

		if (before.Markdown != content.Markdown)
		{
			if (await wiki.UpdateAsync(id, content.Markdown, editorDbref, step.Summary) is not WikiPage)
			{
				return new Error<string>($"Wiki page {label} is gone.");
			}

			writes.Track($"wiki page {label} body", async () =>
			{
				if (await wiki.GetByIdAsync(id) is WikiPage now
					&& PackageWikiContent.NormalizeMarkdown(now.MarkdownSource) == content.Markdown)
				{
					await wiki.UpdateAsync(id, before.Markdown, editorDbref, $"Undo {step.Summary}");
				}
			});
		}

		if (before.Title != content.Title)
		{
			if (await wiki.SetTitleAsync(id, content.Title) is not WikiPage)
			{
				return new Error<string>($"Wiki page {label} could not be retitled '{content.Title}'.");
			}

			writes.Track($"wiki page {label} title", async () =>
			{
				if (await wiki.GetByIdAsync(id) is WikiPage now && now.Title == content.Title)
				{
					await wiki.SetTitleAsync(id, before.Title);
				}
			});
		}

		if (before.Published != content.Published || !before.Categories.SequenceEqual(content.Categories, StringComparer.Ordinal))
		{
			await wiki.SetMetadataAsync(id, content.Categories, content.Published);
			writes.Track($"wiki page {label} categories", async () =>
			{
				if (await wiki.GetByIdAsync(id) is WikiPage now && now.Published == content.Published
					&& now.Categories.Order(StringComparer.Ordinal).SequenceEqual(content.Categories, StringComparer.Ordinal))
				{
					await wiki.SetMetadataAsync(id, before.Categories, before.Published);
				}
			});
		}

		if (!before.Requires.SameAs(content.Requires))
		{
			var target = WikiRuleTarget.ForPage(id);
			var previous = (await wiki.GetRequirementsAsync()).For(target);
			await wiki.SetRequirementsAsync(target, Required(content.Requires, previous), editorDbref);
			writes.Track($"wiki page {label} requirements", async () =>
				await wiki.SetRequirementsAsync(target, previous?.Required ?? new Dictionary<WikiAction, IReadOnlyList<string>>(), editorDbref));
		}

		if (before.Pinned != content.Pinned)
		{
			await PinAsync(writes, step.Change.Page, content.Pinned);
		}

		return id;
	}

	/// <summary>Pins or unpins a category; undone only when it changed something.</summary>
	private async Task PinAsync(PackageWriteTransaction writes, string category, bool pinned)
	{
		if (await wiki.SetCategoryPinnedAsync(category, pinned) is true)
		{
			writes.Track($"wiki category {category} pin", async () => await wiki.SetCategoryPinnedAsync(category, !pinned));
		}
	}

	/// <summary>
	/// A page's requirement set as the wiki stores it: view is the wiki's read. An action the package does not
	/// speak to (create) keeps what the page already has.
	/// </summary>
	private static IReadOnlyDictionary<WikiAction, IReadOnlyList<string>> Required(PackageWikiRequires requires, WikiRequirementSet? current)
	{
		var result = new Dictionary<WikiAction, IReadOnlyList<string>>
		{
			[WikiAction.Read] = requires.View,
			[WikiAction.Edit] = requires.Edit,
			[WikiAction.Delete] = requires.Delete,
		};
		if (current?.For(WikiAction.Create) is { Count: > 0 } create)
		{
			result[WikiAction.Create] = create;
		}

		return result;
	}

	/// <summary>What the notes say about a step, or null when it did nothing worth saying.</summary>
	private static string? Note(PackageWikiStep step, PackageConflictDecision? decision, PackageWikiOutcome outcome)
	{
		var change = step.Change;
		var label = Label(change);
		return change.Action switch
		{
			PackageWikiAction.Create => $"Created wiki page {label}.",
			PackageWikiAction.Adopt => $"Adopted wiki page {label}: the game already had it as the package ships it.",
			PackageWikiAction.Update => $"Updated wiki page {label} ({step.Summary}).",
			PackageWikiAction.Release => change.Detail,
			PackageWikiAction.Conflict when outcome.Write is null && outcome.Baseline is null => $"Left wiki page {label} as the game has it; the package does not own it.",
			PackageWikiAction.Conflict when outcome.Write is null => $"Kept the game's wiki page {label}.",
			PackageWikiAction.Conflict => $"Wrote wiki page {label} ({(decision?.Resolution == PackageConflictResolution.UseCustom ? "edited merge" : step.Summary)}).",
			_ => null
		};
	}

	private static string Label(PackageWikiChange change)
		=> change.Namespace == "main" ? change.Page : $"{WikiHelpers.CategoryLabel(change.Namespace)}:{change.Page}";
}

/// <summary>What applying one wiki step came to: a page to delete after the commit, nothing more, or a failure.</summary>
public union PackageWikiStepResult(PackageWikiDeletion, None, Error<string>);
