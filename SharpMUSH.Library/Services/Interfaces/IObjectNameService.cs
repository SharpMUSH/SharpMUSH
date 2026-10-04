using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>Renames an object: <c>@name</c> and <c>name()</c>, including a player's alias list.</summary>
public interface IObjectNameService
{
	ValueTask<CallState> SetName(AnySharpObject executor, AnySharpObject obj, MString name, bool notify);
}
