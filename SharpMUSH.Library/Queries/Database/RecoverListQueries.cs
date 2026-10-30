using Mediator;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Queries.Database;

/// <summary>The recover entry holding a number. Uncached: a purge or a recovery can end it at any time.</summary>
public record GetRecoverEntryQuery(int Number) : IQuery<Found<RecoverEntry>>;

/// <summary>Every recover entry, by number. Uncached.</summary>
public record GetRecoverEntriesQuery : IStreamQuery<RecoverEntry>;

/// <summary>The recover list's size, and its kept entries' apart. Uncached.</summary>
public record MeasureRecoverListQuery : IQuery<RecoverListUsage>;
