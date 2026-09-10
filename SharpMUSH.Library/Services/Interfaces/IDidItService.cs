using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>
/// One action on an object: a message to the actor, one message to everyone else present, and a
/// queued action attribute. PennMUSH <c>real_did_it</c> (<c>src/predicat.c:216</c>).
/// </summary>
/// <param name="Player">The enactor. <c>%#</c> in every evaluation, and the recipient of <paramref name="What"/>.</param>
/// <param name="Thing">Holds the attributes and is the executor that evaluates them.</param>
/// <param name="What">Attribute whose evaluated value is shown to <paramref name="Player"/>.</param>
/// <param name="Def">
/// Shown to <paramref name="Player"/> when <paramref name="What"/> is unset. Literal text, not a
/// resource key — see the note below.
/// </param>
/// <param name="OWhat">Attribute evaluated once and shown to everyone else in <paramref name="Loc"/>.</param>
/// <param name="ODef">Fallback for <paramref name="OWhat"/>, rendered as "&lt;Name&gt; &lt;ODef&gt;".</param>
/// <param name="AWhat">Action attribute queued on <paramref name="Thing"/> with <paramref name="Player"/> as enactor.</param>
/// <param name="Loc">Audience for <paramref name="OWhat"/>. Defaults to the player's location.</param>
/// <param name="Env0">
/// <c>%0</c> for every evaluation and for the queued action. Text, because
/// <c>real_did_it</c>'s <c>PE_REGS</c> is text: <c>did_it_with</c> is only the dbref-flavoured
/// wrapper and stores <c>unparse_dbref(env0)</c> (<c>src/predicat.c:159</c>), while
/// <c>do_name</c> puts the old and new names straight in (<c>src/set.c:155-157</c>). A dbref
/// caller writes <c>.ToString()</c>.
/// </param>
/// <param name="Env1"><c>%1</c>, on the same terms as <paramref name="Env0"/>.</param>
/// <param name="Interact">Interaction gate applied to the o-message audience.</param>
/// <remarks>
/// <b>Triad defaults are deliberately not localized.</b> PennMUSH wraps each of them in <c>T()</c>,
/// which resolves once against the server's locale; SharpMUSH resolves per connection instead
/// (<c>NotifyService.NotifyLocalized</c> reads a <c>Locale</c> from each connection's own metadata),
/// and a single player can hold several connections with different locales at once. The
/// evaluated-attribute half of the triad genuinely must render once regardless — running softcode
/// per recipient would be wrong — but even the literal-default half has no seam to fix this through:
/// <paramref name="Def"/> and <paramref name="ODef"/> reach players as rendered
/// <c>MString</c>/<c>string</c> values, and <c>ICommunicationService.SendToRoomAsync</c>'s broadcast callback
/// (<c>Func&lt;AnySharpObject, OneOf&lt;MString, string&gt;&gt;</c>) has no way to hand back a
/// resource key instead and let the send resolve it per recipient the way <c>NotifyLocalized</c>
/// does for a single target. Both stay literal until <c>INotifyService</c>/<c>ICommunicationService</c>
/// grow a key-carrying broadcast path. Callers pass <c>ErrorMessages.Notifications.*</c> constants so
/// the wording still has one home.
/// </remarks>
public record DidItRequest(
	AnySharpObject Player,
	AnySharpObject Thing,
	string? What = null,
	MString? Def = null,
	string? OWhat = null,
	string? ODef = null,
	string? AWhat = null,
	AnySharpContainer? Loc = null,
	string? Env0 = null,
	string? Env1 = null,
	IPermissionService.InteractType Interact = IPermissionService.InteractType.Hear);

public interface IDidItService
{
	/// <summary>Runs one triad. Returns true when any attribute was present and used.</summary>
	ValueTask<bool> DidIt(IMUSHCodeParser parser, DidItRequest request);

	/// <summary>
	/// Runs the failure triad for a lock that was just failed. PennMUSH <c>fail_lock</c>
	/// (<c>src/lock.c:832</c>).
	/// </summary>
	ValueTask<bool> FailLock(
		IMUSHCodeParser parser,
		AnySharpObject player,
		AnySharpObject thing,
		LockType lockType,
		MString? def = null,
		AnySharpContainer? loc = null);
}
