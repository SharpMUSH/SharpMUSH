using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Packages;

namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>A page an apply let go of untouched, deleted once the apply has committed.</summary>
/// <param name="PageId">The page's id.</param>
/// <param name="Title">Its title, for the notes.</param>
/// <param name="UnpinCategory">The category key to unpin first, when the package pinned it.</param>
public sealed record PackageWikiDeletion(string PageId, string Title, string? UnpinCategory);

/// <summary>
/// Creates, merges and lets go of the wiki pages a package ships in <c>wiki:</c> (format 1.4).
/// <list type="bullet">
/// <item>Installing creates each page as the Package Manager wizard, or adopts one the game already has exactly so;
/// a page the game has otherwise is a conflict, and one another package owns blocks the plan. Each page the
/// package owns is recorded with every field as it wrote it (<see cref="PackageWikiPageRecord"/>).</item>
/// <item>An upgrade merges each field three ways, live against that baseline against the new version: the body
/// line by line, the title and published flag as values, categories, requirements and the pin as sets. Clashes
/// are conflicts, decided on the review screen; the baseline always advances to the new version.</item>
/// <item>A page the version drops, and every page on uninstall, is deleted when nobody changed it, and otherwise
/// kept as the game's.</item>
/// </list>
/// Writes are tracked by the operation's <see cref="PackageWriteTransaction"/>; deletes, which cannot be undone,
/// are returned for the caller to run after it commits.
/// </summary>
public interface IPackageWikiService
{
	/// <summary>What applying <paramref name="pages"/> for <paramref name="packageId"/> would do, read-only.</summary>
	/// <param name="declaredPermissions">The custom permissions the manifest declares, which a page may require before they exist.</param>
	Task<IReadOnlyList<PackageWikiChange>> PlanAsync(
		string packageId,
		string version,
		IReadOnlyList<PackageWikiPageSpec> pages,
		IReadOnlyCollection<string> declaredPermissions,
		CancellationToken cancellationToken = default);

	/// <summary>
	/// Applies <paramref name="pages"/> (empty to let go of every page the package owns), each conflict settled by
	/// its decision in <paramref name="decisions"/>, keyed by <see cref="PackageWikiChange.TargetRef"/> and page.
	/// Returns the pages to delete once the operation commits, or why it cannot be applied.
	/// </summary>
	/// <param name="locale">The pages' source locale, or null for the game's default.</param>
	/// <param name="editorDbref">Who the pages are written as: the Package Manager wizard.</param>
	Task<Result<IReadOnlyList<PackageWikiDeletion>>> ApplyAsync(
		PackageWriteTransaction writes,
		string packageId,
		string version,
		IReadOnlyList<PackageWikiPageSpec> pages,
		IReadOnlyCollection<string> declaredPermissions,
		string? locale,
		Func<PackageWikiChange, PackageConflictDecision?> decisions,
		string editorDbref,
		List<string> notes,
		CancellationToken cancellationToken = default);

	/// <summary>
	/// Deletes the pages an apply let go of untouched, unpinning a category the package pinned while it is still
	/// pinned. Runs after the operation committed, as work <paramref name="writes"/> owns: closing waits for it,
	/// and a page it could not delete stops the transaction reporting a clean commit. A deleted page keeps
	/// neither its id nor its history.
	/// </summary>
	Task DeleteReleasedAsync(PackageWriteTransaction writes, IReadOnlyList<PackageWikiDeletion> deletions,
		string editorDbref, List<string> notes);
}
