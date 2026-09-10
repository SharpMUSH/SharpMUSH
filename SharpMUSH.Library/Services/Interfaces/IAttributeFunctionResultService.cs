using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>Optional result-preserving attribute evaluation; legacy text evaluation remains available.</summary>
public interface IAttributeFunctionResultService
{
	ValueTask<CallState> EvaluateAttributeFunctionResultAsync(IMUSHCodeParser parser, AnySharpObject executor,
		AnySharpObject obj, string attribute, Dictionary<string, CallState> args,
		bool evalParent = true, bool ignorePermissions = false);

	/// <summary>Evaluates object/attribute, lambda, or apply notation without losing parser metadata.</summary>
	ValueTask<CallState> EvaluateAttributeFunctionResultAsync(IMUSHCodeParser parser, AnySharpObject executor,
		MString objAndAttribute, Dictionary<string, CallState> args, bool evalParent = true,
		bool ignorePermissions = false, bool ignoreLambda = false);
}
