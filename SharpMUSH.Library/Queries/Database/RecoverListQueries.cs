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

/// <summary>How many numbers are free: <c>stats()</c>'s garbage count. Uncached.</summary>
public record CountFreeNumbersQuery : IQuery<int>;

/// <summary>Whether a number is free, as <c>hastype(#N, GARBAGE)</c> asks. Uncached.</summary>
public record IsFreeNumberQuery(int Number) : IQuery<bool>;

/// <summary>Every free number, the earliest due first. Uncached.</summary>
public record GetFreeNumbersQuery : IStreamQuery<FreeNumber>;

/// <summary>Every attribute naming one of the numbers as a bare <c>#N</c>. Uncached; reads the whole world a page at a time.</summary>
public record FindNumberReferencesQuery(IReadOnlySet<int> Numbers) : IStreamQuery<NumberReference>;
