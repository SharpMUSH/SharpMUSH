using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Extensions;

/// <summary>Preserves parser results when supported, with the original text contract as fallback.</summary>
public static class AttributeFunctionResultExtensions
{
	public static async ValueTask<CallState> EvaluateAttributeFunctionResultAsync(this IAttributeService service,
		IMUSHCodeParser parser, AnySharpObject executor, AnySharpObject obj, string attribute,
		Dictionary<string, CallState> args, bool evalParent = true, bool ignorePermissions = false)
		=> service is IAttributeFunctionResultService results
			? await results.EvaluateAttributeFunctionResultAsync(parser, executor, obj, attribute, args, evalParent, ignorePermissions)
			: new CallState(await service.EvaluateAttributeFunctionAsync(parser, executor, obj, attribute, args, evalParent, ignorePermissions));

	public static async ValueTask<CallState> EvaluateAttributeFunctionResultAsync(this IAttributeService service,
		IMUSHCodeParser parser, AnySharpObject executor, MString objAndAttribute,
		Dictionary<string, CallState> args, bool evalParent = true, bool ignorePermissions = false, bool ignoreLambda = false)
		=> service is IAttributeFunctionResultService results
			? await results.EvaluateAttributeFunctionResultAsync(parser, executor, objAndAttribute, args, evalParent, ignorePermissions, ignoreLambda)
			: new CallState(await service.EvaluateAttributeFunctionAsync(parser, executor, objAndAttribute, args, evalParent, ignorePermissions, ignoreLambda));
}
