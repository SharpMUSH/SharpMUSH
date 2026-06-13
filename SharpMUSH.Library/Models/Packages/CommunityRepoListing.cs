namespace SharpMUSH.Library.Models.Packages;

/// <summary>
/// One accepted community package repo, as listed by a curated file under
/// <c>community/</c> in an official package repo. Listings are added by pull
/// request; acceptance grants the community trust tier (decision 20.8 —
/// trust itself is never self-declared, the curated listing is the source).
/// </summary>
/// <param name="Name">Display name of the repo.</param>
/// <param name="Url">Git repo URL.</param>
/// <param name="Branch">Recommended branch, or null for the remote default.</param>
/// <param name="Description">Short description shown in the browse UI.</param>
/// <param name="Maintainers">Maintainer display names.</param>
/// <param name="Homepage">Project homepage, or null.</param>
public sealed record CommunityRepoListing(
	string Name,
	string Url,
	string? Branch,
	string Description,
	IReadOnlyList<string> Maintainers,
	string? Homepage);

/// <summary>
/// The result of reading an official repo's <c>community/</c> directory:
/// the parseable listings plus per-file errors for the rest (a broken
/// submission must not hide the others).
/// </summary>
/// <param name="Listings">Valid listings, ordered by name.</param>
/// <param name="Errors">Human-readable per-file parse failures.</param>
public sealed record CommunityRepoDirectory(
	IReadOnlyList<CommunityRepoListing> Listings,
	IReadOnlyList<string> Errors);
