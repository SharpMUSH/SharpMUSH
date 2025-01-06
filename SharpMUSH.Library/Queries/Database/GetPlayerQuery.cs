using Mediator;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Queries.Database;

public record GetPlayerQuery(string Name) : IQuery<IEnumerable<SharpPlayer>>/*, ICacheable*/;