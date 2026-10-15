using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Library.Models.Packages;

/// <summary>
/// The permissions a package page requires, per action (the <c>requires:</c> map of a <c>wiki:</c> entry,
/// format 1.4). Each list is lowercase, without duplicates, in ordinal order, so two sets compare by their
/// lists. A page's requirements only add to what its namespace and categories require.
/// </summary>
/// <param name="View">What reading the page requires (the wiki's <c>Read</c> action).</param>
/// <param name="Edit">What editing it requires.</param>
/// <param name="Delete">What deleting it requires.</param>
public sealed record PackageWikiRequires(IReadOnlyList<string> View, IReadOnlyList<string> Edit, IReadOnlyList<string> Delete)
{
	/// <summary>A page that requires nothing of its own.</summary>
	public static PackageWikiRequires None { get; } = new([], [], []);

	/// <summary>True when no action requires anything.</summary>
	public bool IsEmpty => View.Count == 0 && Edit.Count == 0 && Delete.Count == 0;

	/// <summary>Every permission named, once each.</summary>
	public IEnumerable<string> All => View.Concat(Edit).Concat(Delete).Distinct(StringComparer.Ordinal);

	/// <summary>The same set with each list lowercased, de-duplicated and sorted.</summary>
	public static PackageWikiRequires Of(IEnumerable<string>? view, IEnumerable<string>? edit, IEnumerable<string>? delete)
		=> new(Normalize(view), Normalize(edit), Normalize(delete));

	/// <summary>True when both sets name the same permissions for every action.</summary>
	public bool SameAs(PackageWikiRequires other)
		=> View.SequenceEqual(other.View, StringComparer.Ordinal)
			&& Edit.SequenceEqual(other.Edit, StringComparer.Ordinal)
			&& Delete.SequenceEqual(other.Delete, StringComparer.Ordinal);

	private static IReadOnlyList<string> Normalize(IEnumerable<string>? scopes)
		=> (scopes ?? [])
			.Select(scope => scope.Trim().ToLowerInvariant())
			.Where(scope => scope.Length > 0)
			.Distinct(StringComparer.Ordinal)
			.Order(StringComparer.Ordinal)
			.ToList();
}

/// <summary>
/// A wiki page a package ships (the <c>wiki:</c> block, format 1.4). The page's identity is
/// <c>(Namespace, Page)</c>: the slug is never derived from the title, so a new title moves nothing.
/// </summary>
/// <param name="Page">The page's slug, unchanged by <see cref="WikiHelpers.Slugify"/>.</param>
/// <param name="Namespace">Main, Help or Category; biographies and engine pages are not package content.</param>
/// <param name="Title">The page's title.</param>
/// <param name="Categories">The categories it is filed in, as names; keyed with <see cref="WikiHelpers.CategoryKey"/>.</param>
/// <param name="File">The body's path inside the package directory (<c>file:</c>), or null for an inline body.</param>
/// <param name="Markdown">
/// The body: the inline <c>markdown:</c>, or the file's text once <c>PackageWikiBodies</c> has read it. Null only
/// for a <c>file:</c> page that has not been read yet, which cannot be planned.
/// </param>
/// <param name="Requires">What the page itself requires per action.</param>
/// <param name="Published">False to install it as a draft.</param>
/// <param name="Pin">For a category page, pin the category to the wiki home.</param>
public sealed record PackageWikiPageSpec(
	string Page,
	WikiNamespace Namespace,
	string Title,
	IReadOnlyList<string> Categories,
	string? File,
	string? Markdown,
	PackageWikiRequires Requires,
	bool Published = true,
	bool Pin = false)
{
	/// <summary>The namespace as stored and routed, lowercase.</summary>
	public string NamespaceName => WikiHelpers.NamespaceName(Namespace);

	/// <summary><c>Help:orc_license_notice</c>, the way messages name the page.</summary>
	public string Label => $"{(Namespace == WikiNamespace.Main ? "" : $"{Namespace}:")}{Page}";
}

/// <summary>
/// What a package page holds, field by field: the unit the upgrade merge compares. A baseline, the live page and
/// the new version are each one of these. <see cref="Categories"/> are keys, sorted; <see cref="Markdown"/> has
/// <c>\n</c> line ends.
/// </summary>
public sealed record PackageWikiContent(
	string Title,
	string Markdown,
	IReadOnlyList<string> Categories,
	bool Published,
	PackageWikiRequires Requires,
	bool Pinned)
{
	/// <summary>True when every field equals <paramref name="other"/>'s.</summary>
	public bool SameAs(PackageWikiContent other)
		=> Title == other.Title
			&& Markdown == other.Markdown
			&& Published == other.Published
			&& Pinned == other.Pinned
			&& Categories.SequenceEqual(other.Categories, StringComparer.Ordinal)
			&& Requires.SameAs(other.Requires);

	/// <summary>Line ends as the merge compares them.</summary>
	public static string NormalizeMarkdown(string markdown) => markdown.Replace("\r\n", "\n").Replace('\r', '\n');
}

/// <summary>
/// A page a package created and still owns (the <c>pkg.wiki</c> table), with every field as the package last
/// wrote it: the baseline an upgrade merges against, and what release compares with to see whether staff
/// changed the page.
/// </summary>
/// <param name="PackageId">The owning package.</param>
/// <param name="PageId">The page's id when the package wrote it; a page of the same slug with another id is not the package's.</param>
/// <param name="Namespace">The namespace, lowercase.</param>
/// <param name="Slug">The slug, the manifest's <c>page</c>.</param>
/// <param name="Title">The title the package last wrote.</param>
/// <param name="Markdown">The body the package last wrote.</param>
/// <param name="Categories">The category keys the package last wrote.</param>
/// <param name="Published">Whether the package last wrote it published.</param>
/// <param name="Requires">What the package last made the page require.</param>
/// <param name="Pinned">Whether the package pinned the page's category (category pages only).</param>
/// <param name="BaselineVersion">The package version that wrote this baseline.</param>
public sealed record PackageWikiPageRecord(
	string PackageId,
	string PageId,
	string Namespace,
	string Slug,
	string Title,
	string Markdown,
	IReadOnlyList<string> Categories,
	bool Published,
	PackageWikiRequires Requires,
	bool Pinned,
	string BaselineVersion)
{
	/// <summary>The fields as the merge compares them.</summary>
	public PackageWikiContent Content => new(Title, Markdown, Categories, Published, Requires, Pinned);
}

/// <summary>What happens to one package page in a plan.</summary>
public enum PackageWikiAction
{
	/// <summary>The page does not exist; the package creates it and owns it.</summary>
	Create,

	/// <summary>The game already has the page exactly as the package ships it; the package owns it and writes nothing.</summary>
	Adopt,

	/// <summary>The package owns the page and the new version changes it; the merged page is written.</summary>
	Update,

	/// <summary>Nothing to write: the page is as the new version has it, or staff changed what the version did not.</summary>
	NoChange,

	/// <summary>Needs a decision on the review screen; see <see cref="PackageWikiChange.Conflict"/>.</summary>
	Conflict,

	/// <summary>The package lets go of an untouched page, which is deleted.</summary>
	Delete,

	/// <summary>The package lets go of the page and leaves it as it is: staff changed it, or it is already gone.</summary>
	Release,

	/// <summary>The page cannot be applied: another package owns it, or it names a permission the game lacks.</summary>
	Blocked
}

/// <summary>
/// One page in a plan, shown in the review screen's "Wiki pages" section. A conflict's decision travels as a
/// <see cref="PackageConflictDecision"/> with <see cref="TargetRef"/> and <see cref="Page"/>, so the apply
/// endpoint takes it like an attribute's.
/// </summary>
/// <param name="Namespace">The namespace, lowercase.</param>
/// <param name="Page">The slug.</param>
/// <param name="Title">The title after the apply (the new version's, or the live one when the page is kept).</param>
/// <param name="Action">What the apply does.</param>
/// <param name="Conflict">For <see cref="PackageWikiAction.Conflict"/>: add-add, modify-modify or delete-modify.</param>
/// <param name="BaseMarkdown">The body as the package last wrote it, or null for none.</param>
/// <param name="LiveMarkdown">The body now, or null when there is no page.</param>
/// <param name="NewMarkdown">The new version's body, or null when the version drops the page.</param>
/// <param name="MergedMarkdown">
/// The body the apply writes for a merge; for a modify-modify conflict, the merge with each clash marked between
/// <c>&lt;&lt;&lt;&lt;&lt;&lt;&lt;</c> and <c>&gt;&gt;&gt;&gt;&gt;&gt;&gt;</c>, which is where an edited merge starts.
/// </param>
/// <param name="Changes">What changes, one field per line (<c>title: 'Old' -> 'New'</c>).</param>
/// <param name="Detail">Why, for a conflict, a release or a blocked page.</param>
public sealed record PackageWikiChange(
	string Namespace,
	string Page,
	string Title,
	PackageWikiAction Action,
	PackageConflictKind? Conflict = null,
	string? BaseMarkdown = null,
	string? LiveMarkdown = null,
	string? NewMarkdown = null,
	string? MergedMarkdown = null,
	IReadOnlyList<string>? Changes = null,
	string? Detail = null)
{
	/// <summary>The prefix of a wiki page's <see cref="PackageConflictDecision.TargetRef"/>.</summary>
	public const string TargetRefPrefix = "wiki:";

	/// <summary><c>wiki:&lt;namespace&gt;</c>, the decision's target.</summary>
	public string TargetRef => TargetRefPrefix + Namespace;
}
