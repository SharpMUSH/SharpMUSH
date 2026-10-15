using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Palette;

namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>
/// Each character's stack of contexts, in memory. A push puts a context on top (moving it there if it
/// was already on the stack); contexts sink as they age and fall off below <see cref="ContextWeights.Floor"/>
/// or past their lifetime. Read by the command palette and <c>context()</c>; written by <c>@context</c>,
/// and by the engine after a line the player typed runs a <c>$</c>-command.
/// </summary>
public interface IContextStack
{
	/// <summary>Puts <paramref name="name"/> on top of <paramref name="character"/>'s stack.</summary>
	void Push(DBRef character, string name, IReadOnlyDictionary<string, string>? values = null,
		TimeSpan? lifetime = null, string? info = null);

	/// <summary>Takes <paramref name="name"/> off the stack; false when it was not there.</summary>
	bool Drop(DBRef character, string name);

	/// <summary>The stack, top first, without the contexts that have fallen off.</summary>
	IReadOnlyList<WeightedContext> Read(DBRef character);
}
