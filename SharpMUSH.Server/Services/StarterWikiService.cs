using Microsoft.Extensions.Logging;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Resources;

namespace SharpMUSH.Server.Services;

/// <summary>Persisted record that a game's starter wiki pages were written (expanded server data).</summary>
public sealed class StarterWikiState
{
	public bool Applied { get; set; }
}

/// <summary>
/// The first pages a new game's admins build its wiki from: Getting Started, Theme, Setting and Policies, the
/// categories that file them, and a Home page that links to all four. The setup wizard offers them once; unlike
/// the pages <see cref="StartupHandler"/> seeds, they are not put back when an administrator deletes one.
/// </summary>
/// <remarks>
/// The layout follows what established MUSH wikis converge on: four front doors from the main page, theme
/// (premise, tone, the stories told) kept apart from setting (places, history, factions, peoples), and policy
/// pages gathered under one category. Each hub lists its category's pages, so a new page shows up there once
/// it is tagged.
/// </remarks>
public class StarterWikiService(
	IWikiService wiki,
	IExpandedObjectDataService serverData,
	ILogger<StarterWikiService> logger)
{
	/// <summary>The dbref the starter pages are written as, as for the pages seeded at boot.</summary>
	private const string Author = "#1";

	/// <summary>One apply at a time, so a second request waits and then finds the set applied.</summary>
	private readonly SemaphoreSlim _applying = new(1, 1);

	/// <summary>Every page the starter set writes, in the order it writes them.</summary>
	public static IReadOnlyList<StarterWikiPages.Page> Pages => StarterWikiPages.All;

	/// <summary>Whether the starter pages have been written to this game.</summary>
	public async Task<bool> AppliedAsync()
		=> (await serverData.GetExpandedServerDataAsync<StarterWikiState>())?.Applied ?? false;

	/// <summary>
	/// Writes each starter page the game does not have, and replaces Home while it is still the page seeded at
	/// boot. A page the game already has is left as it is. Theme, Setting and Policies are protected (a page requirement of
	/// wiki.admin to edit and delete), so only wiki administrators edit them. Every category the set creates is
	/// pinned to the wiki home. Records that the set was applied even when some pages failed, naming those.
	/// Once applied, it writes nothing again, so a starter page an administrator deleted stays deleted.
	/// </summary>
	public async Task<Result<Success>> ApplyAsync()
	{
		await _applying.WaitAsync();
		try
		{
			return await AppliedAsync() ? new Success() : await WritePagesAsync();
		}
		finally
		{
			_applying.Release();
		}
	}

	private async Task<Result<Success>> WritePagesAsync()
	{
		var failures = new List<string>();
		foreach (var page in StarterWikiPages.All)
		{
			switch (await wiki.CreateAsync(page.Title, page.Markdown, Author, page.Namespace, sourceLocale: "en",
				categories: page.Categories))
			{
				case WikiPage created:
					await PinAsync(page, failures);
					if (page.Protect && await wiki.SetRequirementsAsync(WikiRuleTarget.ForPage(created.Id), WikiRequirementSet.Protection, Author) is NotFound)
					{
						failures.Add($"{page.Title} was written but could not be protected.");
					}

					break;
				case Error<string> error when await wiki.GetBySlugAsync(WikiHelpers.Slugify(page.Title), page.Namespace) is WikiPage:
					await PinAsync(page, failures);
					logger.LogDebug("Starter wiki page {Title} already exists ({Reason}); left as it is.", page.Title, error.Value);
					break;
				case Error<string> error:
					failures.Add($"{page.Title} could not be written: {error.Value}");
					break;
			}
		}

		await ReplaceDefaultHomeAsync(failures);
		await serverData.SetExpandedServerDataAsync(new StarterWikiState { Applied = true });

		return failures.Count == 0 ? new Success() : new Error<string>(string.Join(" ", failures));
	}

	/// <summary>
	/// Pins a starter category page's category to the wiki home, so every category the starter set creates is
	/// shown there until an administrator unpins it.
	/// </summary>
	private async Task PinAsync(StarterWikiPages.Page page, List<string> failures)
	{
		if (page.Namespace == WikiNamespace.Category
			&& await wiki.SetCategoryPinnedAsync(page.Title, pinned: true) is Error<string> error)
		{
			failures.Add($"Category {page.Title} could not be pinned: {error.Value}");
		}
	}

	/// <summary>
	/// Home is seeded at every boot, so it always exists. The starter Home replaces it only while nobody has
	/// changed the seeded text: an administrator's own Home stays.
	/// </summary>
	private async Task ReplaceDefaultHomeAsync(List<string> failures)
	{
		if (await wiki.GetBySlugAsync(WikiHelpers.Slugify("Home"), WikiNamespace.Main) is not WikiPage home
			|| home.MarkdownSource != SeededWikiPages.Home)
		{
			return;
		}

		if (await wiki.UpdateAsync(home.Id, StarterWikiPages.Home, Author, "Starter wiki pages") is NotFound)
		{
			failures.Add("Home could not be updated.");
		}
	}
}
