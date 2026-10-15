using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Portal;

namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>
/// What the command palette offers a character: the <c>PALETTE`</c> entries of every object in its
/// command scopes that it may use, its <c>@input</c> session's entries, the built-ins it may run, its pins
/// and its context stack.
/// </summary>
public interface IPaletteService
{
	/// <param name="inputSession">The <c>@input</c> session the client's terminal shows a prompt for, if any.</param>
	ValueTask<PaletteResponse> ResolveAsync(AnySharpObject character, Guid? inputSession = null,
		CancellationToken cancellationToken = default);

	/// <summary>
	/// What the character's context stack puts forward, in the order the empty palette's Now and Here
	/// groups show it: what <c>context()</c> returns.
	/// </summary>
	ValueTask<IReadOnlyList<PaletteSuggestion>> SuggestAsync(AnySharpObject character,
		CancellationToken cancellationToken = default);
}
