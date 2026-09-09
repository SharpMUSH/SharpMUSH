using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>
/// How a room is being looked at. PennMUSH's <c>LOOK_*</c> keys (<c>hdrs/externs.h:242-248</c>),
/// with the same bit values. Penn's <c>LOOK_OUTSIDE</c> (8) has no member here; it is carried as
/// <c>LookRoom</c>'s <c>lookOutside</c> parameter.
/// </summary>
[Flags]
public enum LookKey
{
	Normal = 0,

	/// <summary>The look a mover gets on arrival. Obeys <c>TERSE</c>.</summary>
	Auto = 1,

	/// <summary>
	/// Looking through an exit set <c>CLOUDY</c>. On its own it suppresses both the name line and
	/// the description (<c>look.c:469</c> and <c>look.c:503</c>, which key off
	/// <c>LOOK_CLOUDYTRANS</c> — the mask of this bit and <see cref="Trans"/> — being non-zero).
	/// Together with <see cref="Trans"/> it additionally drops the contents (<c>look.c:529</c>).
	/// </summary>
	Cloudy = 2,

	/// <summary>
	/// Looking through an exit set <c>TRANSPARENT</c>. Suppresses the name line, but puts the
	/// description back (<c>look.c:503-504</c>).
	/// </summary>
	Trans = 4,

	/// <summary>Skip the contents listing. Penn's <c>LOOK_NOCONTENTS</c>, set by <c>look/opaque</c>.</summary>
	NoContents = 16
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
	ValueTask<CallState> LookRoom(
		IMUSHCodeParser parser,
		AnySharpObject looker,
		AnyOptionalSharpObject viewing,
		LookKey key,
		bool lookOutside = false);
}
