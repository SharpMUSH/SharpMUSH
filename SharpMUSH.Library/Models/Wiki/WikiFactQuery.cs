using SharpMUSH.Library.DiscriminatedUnions;

namespace SharpMUSH.Library.Models.Wiki;

/// <summary>A page states <paramref name="Value"/> for <paramref name="Property"/>, written as a fact is.</summary>
public sealed record WikiFactEquals(string Property, string Value);

/// <summary>
/// A page states a value from <paramref name="Min"/> to <paramref name="Max"/>, both included, for
/// <paramref name="Property"/>, a number or a date property; a missing bound is open. The bounds are keys in the
/// property's order (<see cref="Wiki.Semantics.WikiFactQueries.OrderKey"/>): a whole number is its own key.
/// </summary>
public sealed record WikiFactRange(string Property, long? Min, long? Max);

/// <summary>A page states one of <paramref name="Values"/> for <paramref name="Property"/>, written as facts are.</summary>
public sealed record WikiFactOneOf(string Property, IReadOnlyList<string> Values);

/// <summary>A page states any value for <paramref name="Property"/>.</summary>
public sealed record WikiFactStated(string Property);

/// <summary>A page does not meet <paramref name="Condition"/>. It narrows a query and never drives one.</summary>
public sealed record WikiFactNot(WikiFactCondition Condition);

public union WikiFactCondition(WikiFactEquals, WikiFactRange, WikiFactOneOf, WikiFactStated, WikiFactNot);

/// <summary>
/// The pages meeting every one of <paramref name="Conditions"/>, at most <paramref name="Take"/> of them, from
/// where <paramref name="After"/> (a previous page's <see cref="WikiFactQueryPage.Next"/>) left off.
/// </summary>
public sealed record WikiFactQuery(IReadOnlyList<WikiFactCondition> Conditions, int Take, string? After = null)
{
	public const int MaxConditions = 4;

	/// <summary>The most values one condition names, as <c>a|b</c>.</summary>
	public const int MaxValues = 8;
	public const int MaxTake = 50;

	/// <summary>Index entries one call reads at most; past them it returns what it found and where to go on.</summary>
	public const int MaxCandidates = 2048;

	/// <summary>Only pages in this namespace; null for every namespace.</summary>
	public string? Namespace { get; init; }

	/// <summary>Only pages in this category, as a key (<see cref="Services.WikiHelpers.CategoryKey"/>); null for any.</summary>
	public string? Category { get; init; }

	/// <summary>
	/// The property whose values order the pages, lowest first unless <see cref="Descending"/>; a page that states
	/// none is not listed. Null for the order the index finds them in.
	/// </summary>
	public string? Sort { get; init; }

	public bool Descending { get; init; }

	/// <summary>The properties whose values a reader is shown beside each page. A store checks them and returns pages.</summary>
	public IReadOnlyList<string> Show { get; init; } = [];

	public const int MaxShown = 4;
}

/// <summary>
/// One page of a fact query's results. <paramref name="Next"/> continues it; null when nothing is left. A page can
/// hold fewer than were asked for, even none, when the call read <see cref="WikiFactQuery.MaxCandidates"/> entries.
/// </summary>
public sealed record WikiFactQueryPage(IReadOnlyList<WikiPage> Pages, string? Next);

/// <summary>The fact index is not ready, so it cannot answer yet.</summary>
public readonly record struct WikiFactIndexBuilding(WikiFactIndexStatus Status);

public union WikiFactQueryResult(WikiFactQueryPage, WikiFactIndexBuilding, Error<string>);
