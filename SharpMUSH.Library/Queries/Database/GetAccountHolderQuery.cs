using Mediator;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Queries.Database;

/// <summary>The holder of an account's attributes that <paramref name="Holder"/> names (<see cref="AccountHolders"/>).</summary>
public record GetAccountHolderQuery(DBRef Holder) : IQuery<Found<SharpAccountHolder>>;
