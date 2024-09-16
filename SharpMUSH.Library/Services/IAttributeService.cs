using OneOf;
using OneOf.Types;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Services;

public interface IAttributeService
{
	enum AttributeMode
	{
		Read = 0,
		Execute = 1
	}

	ValueTask<OptionalSharpAttributeOrError> GetAttributeAsync(AnySharpObject executor, AnySharpObject obj, string attribute, AttributeMode mode, bool parent = true);

	ValueTask<OneOf<SharpAttribute[], Error<string>>> GetVisibleAttributesAsync(AnySharpObject executor, AnySharpObject obj);

	ValueTask<OneOf<SharpAttribute[], Error<string>>> GetAttributePatternAsync(AnySharpObject executor, AnySharpObject obj, string attributePattern);
}