using MediatR;
using SharpMUSH.Implementation.Requests;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Implementation.Handlers.Parser;

public class AttributeEvaluationRequestHandler(IMUSHCodeParser _parser) : INotificationHandler<AttributeEvaluationRequest>
{
	public async Task Handle(AttributeEvaluationRequest request, CancellationToken ct)
	{
		// TODO: GetAttributeAsync should return an MString to begin with. 
		// Which gets to the whole 'how do we serialize the MString' question.
		var contents = await _parser.Database.GetAttributeAsync(request.Attribute.DB, request.Attribute.Name.Split('`'));
		await _parser.FunctionParse(MModule.single(contents!.Last()!.Value));
	}
}
