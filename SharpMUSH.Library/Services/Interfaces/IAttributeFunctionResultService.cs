using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>Optional result-preserving attribute evaluation; legacy text evaluation remains available.</summary>
public interface IAttributeFunctionResultService
{
	ValueTask<CallState> EvaluateAttributeFunctionResultAsync(IMUSHCodeParser parser, AnySharpObject executor,
		AnySharpObject obj, string attribute, Dictionary<string, CallState> args,
		bool evalParent = true, bool ignorePermissions = false);
}
