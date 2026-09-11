using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Library.DiscriminatedUnions;

/// <summary>
/// The attribute a function applies, or the <see cref="CallState"/> to return because there is none.
/// </summary>
public union AttributeFunctionFetch(AttributeFunction, CallState);
