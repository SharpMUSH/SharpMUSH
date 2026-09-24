using Mediator;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Queries.Database;

/// <summary>Every mail alias in creation order. Not cached: the list is small and written by @malias.</summary>
public record GetMailAliasesQuery : IStreamQuery<SharpMailAlias>;
