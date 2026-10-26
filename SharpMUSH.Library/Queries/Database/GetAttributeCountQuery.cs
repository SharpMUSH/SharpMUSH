using Mediator;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Queries.Database;

/// <summary>How many attribute nodes an object holds, read fresh each time: every attribute write moves it.</summary>
public record GetAttributeCountQuery(DBRef Object) : IQuery<long>;
