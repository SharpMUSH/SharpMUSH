using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>How a room is being looked at. PennMUSH's <c>LOOK_*</c> keys, <c>src/look.c</c>.</summary>
[Flags]
public enum LookKey
{
	Normal = 0,

	/// <summary>The look a mover gets on arrival. Obeys <c>TERSE</c>.</summary>
	Auto = 1,

	/// <summary>Looking through a transparent exit.</summary>
	Trans = 2,

	/// <summary>Looking through a cloudy transparent exit — description only, no name line.</summary>
	CloudyTrans = 4,

	/// <summary>Skip the contents listing.</summary>
	NoContents = 8
}

public interface ILookService
{
	/// <summary>
	/// PennMUSH <c>look_room</c> (<c>src/look.c:452</c>).
	/// </summary>
	/// <remarks>
	/// Takes the caller's parser and threads its state. <see cref="ParserState.FunctionRecursionDepths"/>
	/// is what bounds a <c>@describe</c> that evaluates itself, or two that evaluate each other;
	/// building a fresh state here would reset that counter on every hop.
	/// </remarks>
	/// <param name="parser">The caller's parser. Its state is pushed onto, never replaced.</param>
	/// <param name="looker">Who is looking. <c>%#</c> for every attribute this evaluates.</param>
	/// <param name="viewing">The already-resolved target. Nothing is shown for <c>None</c>.</param>
	/// <param name="key">How the look was reached.</param>
	/// <param name="lookOutside">The look came from <c>look/outside</c>, so <c>@idescribe</c> is skipped.</param>
	/// <param name="forceOpaque">The look came from <c>look/opaque</c>, so the contents are skipped.</param>
	ValueTask<CallState> LookRoom(
		IMUSHCodeParser parser,
		AnySharpObject looker,
		AnyOptionalSharpObject viewing,
		LookKey key,
		bool lookOutside = false,
		bool forceOpaque = false);
}
