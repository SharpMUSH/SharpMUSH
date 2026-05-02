using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Library.DiscriminatedUnions;

/// <summary>
/// A union of AnySharpObject and SharpErrorCallState.
/// Replaces AnySharpObjectOrErrorCallState.
/// </summary>
public union AnySharpObjectOrErrorCallState(AnySharpObject, SharpErrorCallState)
{
	public bool IsAnySharpObject => Value is AnySharpObject;
	public bool IsError          => Value is SharpErrorCallState;

	public AnySharpObject       AsSharpObject => (AnySharpObject)Value!;
	public CallState            AsError       => ((SharpErrorCallState)Value!).Value;
}
