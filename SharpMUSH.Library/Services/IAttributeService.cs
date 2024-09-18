using OneOf.Types;
using OneOf;
using SharpMUSH.Library.DiscriminatedUnions;

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

	ValueTask<SharpAttributesOrError> ClearAttributeAsync(AnySharpObject executor, AnySharpObject obj, string attribute, AttributeClearMode mode);

	ValueTask<SharpAttributesOrError> GetVisibleAttributesAsync(AnySharpObject executor, AnySharpObject obj);

	ValueTask<SharpAttributesOrError> GetAttributePatternAsync(AnySharpObject executor, AnySharpObject obj, string attributePattern, AttributePatternMode mode = AttributePatternMode.Exact);
}