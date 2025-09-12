using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Handlers.Database;

public class GetAttributeQueryHandler(ISharpDatabase database)
	: IQueryHandler<GetAttributeQuery, IEnumerable<SharpAttribute>?>
{
	public async ValueTask<IEnumerable<SharpAttribute>?> Handle(GetAttributeQuery request,
		CancellationToken cancellationToken)
		=> await database.GetAttributeAsync(request.DBRef, request.Attribute);
}

public class GetAttributesQueryHandler(ISharpDatabase database)
	: IQueryHandler<GetAttributesQuery, IEnumerable<SharpAttribute>?>
{
	public async ValueTask<IEnumerable<SharpAttribute>?> Handle(GetAttributesQuery request,
		CancellationToken cancellationToken)
		=> request.Mode switch
		{
			IAttributeService.AttributePatternMode.Exact => await database.GetAttributeAsync(request.DBRef, request.Pattern),
			IAttributeService.AttributePatternMode.Regex => await database.GetAttributesByRegexAsync(request.DBRef,
				request.Pattern),
			_ => await database.GetAttributesAsync(request.DBRef, request.Pattern)
		};
}