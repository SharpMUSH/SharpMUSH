using MediatR;
using SharpMUSH.Implementation.Requests;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Implementation.Handlers.Parser;

public class AttributeEvaluationRequestHandler(IMUSHCodeParser _parser) : INotificationHandler<AttributeEvaluationRequest>
{
	public async Task Handle(AttributeEvaluationRequest request, CancellationToken ct)
	{
		var contents = await _parser.Database.GetAttributeAsync(request.Attribute.DB, request.Attribute.Name.Split('`'));
		_parser.FunctionParse(contents!.Last()!.Value);
	}
}
