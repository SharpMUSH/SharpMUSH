using Mediator;
using SharpMUSH.Library.DiscriminatedUnions;

namespace SharpMUSH.Library.Queries.Database;

/// <summary>
/// The JSON in the account's bucket <paramref name="Name"/> (already folded by <see cref="Models.AccountBuckets.Name"/>).
/// Not cached: a read is one key lookup, and the writes are rare enough that nothing would be saved.
/// </summary>
public record GetAccountBucketQuery(string AccountId, string Name) : IQuery<Found<string>>;

/// <summary>The names of the account's buckets, in ordinal order. Not cached, as <see cref="GetAccountBucketQuery"/>.</summary>
public record GetAccountBucketNamesQuery(string AccountId) : IQuery<IReadOnlyList<string>>;
