using OneOf;
using OneOf.Types;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Services;

public interface IAttributeService
{
	ValueTask<OneOf<MString, None, Error<string>>> GetAttributeContentsAsync(AnySharpObject obj, string attribute, bool parent = true);

	ValueTask<OneOf<SharpAttribute[], Error<string>>> GetVisibleAttributesAsync(AnySharpObject obj, string attribute);


}