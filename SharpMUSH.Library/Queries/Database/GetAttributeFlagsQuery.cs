using Mediator;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Queries.Database;

public record GetAttributeFlagsQuery() : IRequest<IEnumerable<SharpAttributeFlag>>, ICacheable;