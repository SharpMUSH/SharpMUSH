using Mediator;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Queries;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Handlers;

public class GetAttributeServiceQueryHandler(IAttributeService attributeService) : IQueryHandler<GetAttributeServiceQuery, OptionalSharpAttributeOrError>
{
	public async ValueTask<OptionalSharpAttributeOrError> Handle(GetAttributeServiceQuery request, CancellationToken cancellationToken)
	{
		return await attributeService.GetAttributeAsync(request.executor, request.obj, request.attribute, request.mode, request.parent);
	}
}