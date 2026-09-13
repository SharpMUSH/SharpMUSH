using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>A notification resource and arguments resolved separately for each receiving connection.</summary>
public sealed record LocalizedNotification(string Key, params object[] Arguments);

/// <summary>
/// One action on an object: a message to the actor, one message to everyone else present, and a
/// queued action attribute. PennMUSH <c>real_did_it</c> (<c>src/predicat.c:216</c>).
/// </summary>
/// <param name="Player">The enactor. <c>%#</c> in every evaluation, and the recipient of <paramref name="What"/>.</param>
/// <param name="Thing">Holds the attributes and is the executor that evaluates them.</param>
/// <param name="What">Attribute whose evaluated value is shown to <paramref name="Player"/>.</param>
/// <param name="Def">
/// Shown to <paramref name="Player"/> when <paramref name="What"/> is unset. Literal text, not a
/// resource key. A non-empty value takes precedence over <see cref="DidItRequest.DefaultNotification"/>.
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
/// caller writes <c>.ToString()</c>, which renders an objid — <c>#N:creation</c>, Penn's
/// <c>unparse_objid</c> — rather than the bare <c>#N</c> of <c>unparse_dbref</c>. That is the
/// codebase-wide spelling of a dbref in text: <c>loc()</c> and its siblings answer with objids
/// too, so a triad's <c>%0</c> compares equal to what softcode gets everywhere else.
/// </param>
/// <param name="Env1"><c>%1</c>, on the same terms as <paramref name="Env0"/>.</param>
/// <param name="Interact">Interaction gate applied to the o-message audience.</param>
/// <remarks>
/// Attribute messages evaluate once. An absent actor attribute can instead use
/// <see cref="DefaultNotification"/>, resolved per connection by <c>NotifyLocalized</c>.
/// Present attributes that evaluate to nothing suppress both defaults.
/// <paramref name="ODef"/> remains literal: the room broadcast callback carries rendered text,
/// not a resource key. Implementations of <see cref="IDidItService"/> must handle the optional
/// notification property to deliver localized defaults; the positional constructor remains unchanged.
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
	IPermissionService.InteractType Interact = IPermissionService.InteractType.Hear)
{
	/// <summary>Used when What is absent and Def is empty, retaining Thing as the sender.</summary>
	public LocalizedNotification? DefaultNotification { get; init; }
}

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

	/// <summary>Runs the failure triad with a default resolved in each recipient connection's locale.</summary>
	ValueTask<bool> FailLockLocalized(
		IMUSHCodeParser parser,
		AnySharpObject player,
		AnySharpObject thing,
		LockType lockType,
		LocalizedNotification? notification,
		AnySharpContainer? loc = null)
	{
		var (what, owhat, awhat) = LockMessages.FailureAttributes(lockType);
		return DidIt(parser, new DidItRequest(player, thing, What: what, OWhat: owhat, AWhat: awhat, Loc: loc)
		{
			DefaultNotification = notification
		});
	}
}
