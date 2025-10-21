using Mediator;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Queries.Database;

public record GetPowersQuery: IQuery<IEnumerable<SharpPower>>;