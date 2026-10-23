using SharpMUSH.Library.DiscriminatedUnions;

namespace SharpMUSH.Library.Models.Wiki;

/// <summary>A page states <paramref name="Value"/> for <paramref name="Property"/>, written as a fact is.</summary>
public sealed record WikiFactEquals(string Property, string Value);

/// <summary>
/// A page states a whole number from <paramref name="Min"/> to <paramref name="Max"/>, both included, for
/// <paramref name="Property"/>; a missing bound is open.
/// </summary>
public sealed record WikiFactRange(string Property, long? Min, long? Max);

public union WikiFactCondition(WikiFactEquals, WikiFactRange);

/// <summary>
/// The pages meeting every one of <paramref name="Conditions"/>, at most <paramref name="Take"/> of them, from
/// where <paramref name="After"/> (a previous page's <see cref="WikiFactQueryPage.Next"/>) left off.
/// </summary>
public sealed record WikiFactQuery(IReadOnlyList<WikiFactCondition> Conditions, int Take, string? After = null)
{
	public const int MaxConditions = 4;
	public const int MaxTake = 50;

	/// <summary>Index entries one call reads at most; past them it returns what it found and where to go on.</summary>
	public const int MaxCandidates = 2048;

	/// <summary>Only pages in this namespace; null for every namespace.</summary>
	public string? Namespace { get; init; }

	/// <summary>Only pages in this category, as a key (<see cref="Services.WikiHelpers.CategoryKey"/>); null for any.</summary>
	public string? Category { get; init; }
}

/// <summary>
/// One page of a fact query's results. <paramref name="Next"/> continues it; null when nothing is left. A page can
/// hold fewer than were asked for, even none, when the call read <see cref="WikiFactQuery.MaxCandidates"/> entries.
/// </summary>
public sealed record WikiFactQueryPage(IReadOnlyList<WikiPage> Pages, string? Next);

/// <summary>The fact index is not ready, so it cannot answer yet.</summary>
public readonly record struct WikiFactIndexBuilding(WikiFactIndexStatus Status);

public union WikiFactQueryResult(WikiFactQueryPage, WikiFactIndexBuilding, Error<string>);
