using Mediator;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Queries.Database;

public record GetAttributesQuery(DBRef DBRef, string Pattern, bool CheckParents, IAttributeService.AttributePatternMode Mode) : IQuery<IAsyncEnumerable<SharpAttribute>?>;

public record GetLazyAttributesQuery(DBRef DBRef, string Pattern, bool CheckParents, IAttributeService.AttributePatternMode Mode) : IQuery<IAsyncEnumerable<LazySharpAttribute>?>;