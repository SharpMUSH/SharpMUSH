using MediatR;
using SharpMUSH.Implementation.Requests;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Implementation.Handlers.Parser;

public class AttributeEvaluationRequestHandler(IMUSHCodeParser _parser) : INotificationHandler<AttributeEvaluationRequest>
{
	public async Task Handle(AttributeEvaluationRequest request, CancellationToken ct)
	{
		var _ = _parser; // Quiet the linter.
		await Task.Delay(0);
		// => await _parser.FunctionParse(request.Handle, request.Input);
		throw new NotImplementedException();
	}
}
