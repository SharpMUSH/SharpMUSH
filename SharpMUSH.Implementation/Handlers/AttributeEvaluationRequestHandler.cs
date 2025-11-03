using Mediator;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Requests;

namespace SharpMUSH.Implementation.Handlers;

public class AttributeEvaluationRequestHandler : IRequestHandler<AttributeEvaluationRequest, CallState>
{
	public ValueTask<CallState> Handle(AttributeEvaluationRequest request, CancellationToken cancellationToken)
	{
		throw new NotImplementedException();
	}
}