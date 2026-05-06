using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Handlers;

/// <summary>
/// Handler for evaluating an attribute as MUSHcode during lock evaluation.
/// PennMUSH eval locks (ATTR/pattern) evaluate the attribute on the gated object
/// with the unlocker as enactor, then compare the result to the pattern.
/// </summary>
public class EvaluateAttributeForLockQueryHandler(
	IAttributeService attributeService,
	IMUSHCodeParser parser) : IQueryHandler<EvaluateAttributeForLockQuery, string?>
{
	public async ValueTask<string?> Handle(EvaluateAttributeForLockQuery query, CancellationToken cancellationToken)
	{
		// Evaluate the attribute on the gated object with the unlocker as executor/enactor
		// PennMUSH: call_ufun(&ufun, buff, player, player, pe_info, NULL)
		// where player = unlocker, and the attribute is on target (gated object)
		try
		{
			var result = await attributeService.EvaluateAttributeFunctionAsync(
				parser,
				query.Unlocker,       // executor = unlocker (PennMUSH: player)
				query.GatedObject,    // obj = gated object (where the attribute lives)
				query.AttributeName,
				new Dictionary<string, CallState>(),
				evalParent: false,
				ignorePermissions: true);

			return result?.ToPlainText();
		}
		catch (Exception)
		{
			return null;
		}
	}
}
