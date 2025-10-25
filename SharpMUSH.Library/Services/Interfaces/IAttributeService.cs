using OneOf;
using OneOf.Types;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Library.Services.Interfaces;

public interface IAttributeService
{
	enum AttributeMode
	{
		Read = 0,
		Execute = 1,
		Set = 2,
		SystemSet = 3
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

	ValueTask<OptionalLazySharpAttributeOrError> LazilyGetAttributeAsync(AnySharpObject executor, AnySharpObject obj, string attribute, AttributeMode mode, bool parent = true);

	ValueTask<OneOf<Success, Error<string>>> SetAttributeAsync(AnySharpObject executor, AnySharpObject obj, string attribute, MString value);

	ValueTask<OneOf<Success, Error<string>>> ClearAttributeAsync(AnySharpObject executor, AnySharpObject obj, string attribute, AttributePatternMode patternMode, AttributeClearMode clearMode);

	ValueTask<LazySharpAttributesOrError> LazilyGetVisibleAttributesAsync(AnySharpObject executor, AnySharpObject obj, int depth = 1);

	ValueTask<LazySharpAttributesOrError> LazilyGetAttributePatternAsync(AnySharpObject executor, AnySharpObject obj, string attributePattern, bool checkParents, AttributePatternMode mode = AttributePatternMode.Exact);

	ValueTask<SharpAttributesOrError> GetVisibleAttributesAsync(AnySharpObject executor, AnySharpObject obj, int depth = 1);

	ValueTask<SharpAttributesOrError> GetAttributePatternAsync(AnySharpObject executor, AnySharpObject obj, string attributePattern, bool checkParents, AttributePatternMode mode = AttributePatternMode.Exact);

	ValueTask<OneOf<Success, Error<string>>> SetAttributeFlagAsync(AnySharpObject executor, AnySharpObject obj, string attribute, string flag);

	ValueTask<OneOf<Success, Error<string>>> UnsetAttributeFlagAsync(AnySharpObject executor, AnySharpObject obj, string attribute, string flag);

	ValueTask<MString> EvaluateAttributeFunctionAsync(IMUSHCodeParser parser, AnySharpObject executor, AnySharpObject obj,
		string attribute, Dictionary<string, CallState> args, bool evalParent = true, bool ignorePermissions = false);
	
	ValueTask<MString> EvaluateAttributeFunctionAsync(IMUSHCodeParser parser, AnySharpObject executor, MString objAndAttribute, Dictionary<string, CallState> args, bool evalParent = true, bool ignorePermissions = false);

}