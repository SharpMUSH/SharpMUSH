using Mediator;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Queries.Database;

/// <summary>
/// How many objects have <paramref name="Parent"/> as their parent — <c>nchildren()</c>. Counted by the
/// store from the parent index, without loading a child. Uncached, like the other store counts.
/// </summary>
public record GetChildCountQuery(DBRef Parent) : IQuery<int>;

/// <summary>
/// How many rooms, exits, things and players there are, in the whole database or, when
/// <paramref name="Owner"/> is given, owned by that player — <c>@stats</c> and <c>lstats()</c>. Counted by
/// the store from its type and owner indexes. Uncached, like the other store counts.
/// </summary>
public record GetObjectTypeCountsQuery(DBRef? Owner) : IQuery<ObjectTypeCounts>;

/// <summary>
/// The highest dbref number an object holds, or <see cref="NotFound"/> for an empty database —
/// <c>nextdbref()</c>. One seek to the end of the object table. Uncached: every create and destroy can
/// move it.
/// </summary>
public record GetHighestDbrefQuery : IQuery<Found<int>>;
