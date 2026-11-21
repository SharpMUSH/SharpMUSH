using Mediator;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Queries.Database;

/// <summary>
/// The account's value <paramref name="Name"/> (already folded by <see cref="AccountValues.Name"/>). Not cached: a read
/// is one key lookup.
/// </summary>
public record GetAccountValueQuery(string AccountId, string Name) : IQuery<Found<AccountValue>>;

/// <summary>The account's values in ordinal order of name. Not cached, as <see cref="GetAccountValueQuery"/>.</summary>
public record GetAccountValuesQuery(string AccountId) : IQuery<IReadOnlyList<AccountValue>>;
