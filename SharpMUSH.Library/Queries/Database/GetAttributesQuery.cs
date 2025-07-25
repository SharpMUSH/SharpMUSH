using Mediator;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Library.Queries.Database;

public record GetAttributesQuery(DBRef DBRef, string Pattern, IAttributeService.AttributePatternMode Mode) : IQuery<IEnumerable<SharpAttribute>?>;