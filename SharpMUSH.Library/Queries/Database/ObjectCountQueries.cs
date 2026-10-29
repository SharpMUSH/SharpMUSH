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
/// The dbref number the next creation takes — <c>nextdbref()</c>. Uncached: every create can move it.
/// </summary>
public record GetNextDbrefQuery : IQuery<int>;
