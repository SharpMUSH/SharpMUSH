using OneOf;
using OneOf.Types;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Library.Services;

public interface IAttributeService
{
	enum AttributeMode
	{
		Read = 0,
		Execute = 1
	}

	enum AttributePatternMode
	{
		Exact = 0,
		Wildcard = 1,
		Regex = 2
	}

	enum AttributeClearMode
	{
		Safe = 0,
		Unsafe = 1
	}

	ValueTask<OptionalSharpAttributeOrError> GetAttributeAsync(AnySharpObject executor, AnySharpObject obj, string attribute, AttributeMode mode, bool parent = true);

	ValueTask<OneOf<Success, Error<string>>> SetAttributeAsync(AnySharpObject executor, AnySharpObject obj, string attribute, MString value);

	ValueTask<OneOf<Success, Error<string>>> ClearAttributeAsync(AnySharpObject executor, AnySharpObject obj, string attribute, AttributeClearMode mode);

	ValueTask<SharpAttributesOrError> GetVisibleAttributesAsync(AnySharpObject executor, AnySharpObject obj, int depth = 1);

	ValueTask<SharpAttributesOrError> GetAttributePatternAsync(AnySharpObject executor, AnySharpObject obj, string attributePattern, AttributePatternMode mode = AttributePatternMode.Exact);

	ValueTask<OneOf<Success, Error<string>>> SetAttributeFlagAsync(AnySharpObject executor, AnySharpObject obj, string attribute, string flag);

	ValueTask<OneOf<Success, Error<string>>> UnsetAttributeFlagAsync(AnySharpObject executor, AnySharpObject obj, string attribute, string flag);

	ValueTask<MString> EvaluateAttributeFunctionAsync(IMUSHCodeParser parser, AnySharpObject executor, AnySharpObject obj,
		string attribute, Dictionary<string, CallState> args, bool evalParent = true, bool ignorePermissions = false);
	
	ValueTask<MString> EvaluateAttributeFunctionAsync(IMUSHCodeParser parser, AnySharpObject executor, MString  obj,
		MString attribute, Dictionary<string, CallState> args, bool evalParent = true, bool ignorePermissions = false);

}