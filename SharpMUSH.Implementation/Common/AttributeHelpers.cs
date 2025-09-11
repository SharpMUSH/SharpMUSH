using Mediator;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Common;

public class AttributeHelpers
{
	public static async ValueTask<string> GetPronounIndicatingAttribute(IAttributeService attributeService,
		IMediator mediator, IMUSHCodeParser parser, string attr)	
	{
		var executor = await parser.CurrentState.KnownExecutorObject(mediator);
		
		var attribute = await attributeService.GetAttributeAsync(
			executor,
			executor,
			attr,
			IAttributeService.AttributeMode.Read); 

		return attribute.IsAttribute 
			? attribute.AsAttribute.Last().Value.ToPlainText() 
			: "N";
	}
}