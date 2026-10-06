using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Wiki;

/// <summary>
/// Protection as <c>@wiki/protect</c> sets it, for tests that seed a protected page: a page requirement of
/// wiki.admin to edit and delete it.
/// </summary>
internal static class WikiProtection
{
	public static Task<Found<None>> ProtectAsync(this IWikiService wiki, string pageId, bool protect = true)
		=> wiki.SetRequirementsAsync(WikiRuleTarget.ForPage(pageId),
			protect
				? WikiRequirementSet.Protection
				: new Dictionary<WikiAction, IReadOnlyList<string>> { [WikiAction.Edit] = [], [WikiAction.Delete] = [] },
			"#1");

	/// <summary>True when the page carries requirements of its own.</summary>
	public static async Task<bool> IsRestrictedAsync(this IWikiService wiki, string pageId)
		=> (await wiki.GetRequirementsAsync()).HasPageRules(pageId);
}
