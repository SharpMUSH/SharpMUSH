using System.Diagnostics.CodeAnalysis;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Library.DiscriminatedUnions;

/// <summary>
/// The attribute a function applies, or the <see cref="CallState"/> to return because there is none.
/// </summary>
public partial union AttributeFunctionFetch(AttributeFunction, CallState)
{
	/// <summary>True with the fetched attribute, or false with the <see cref="CallState"/> to return.</summary>
	public bool TryGetValue(out AttributeFunction function, [MaybeNullWhen(true)] out CallState response)
	{
		switch (Value)
		{
			case AttributeFunction fetched:
				function = fetched;
				response = default;
				return true;
			case CallState held:
				function = default;
				response = held;
				return false;
			default:
				throw new InvalidOperationException($"A default {nameof(AttributeFunctionFetch)} holds neither case.");
		}
	}
}
