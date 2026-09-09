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
/// <param name="Def">Shown to <paramref name="Player"/> when <paramref name="What"/> is unset.</param>
/// <param name="OWhat">Attribute evaluated once and shown to everyone else in <paramref name="Loc"/>.</param>
/// <param name="ODef">Fallback for <paramref name="OWhat"/>, rendered as "&lt;Name&gt; &lt;ODef&gt;".</param>
/// <param name="AWhat">Action attribute queued on <paramref name="Thing"/> with <paramref name="Player"/> as enactor.</param>
/// <param name="Loc">Audience for <paramref name="OWhat"/>. Defaults to the player's location.</param>
/// <param name="Env0"><c>%0</c> for every evaluation and for the queued action.</param>
/// <param name="Env1"><c>%1</c> for every evaluation and for the queued action.</param>
/// <param name="Interact">Interaction gate applied to the o-message audience.</param>
public record DidItRequest(
	AnySharpObject Player,
	AnySharpObject Thing,
	string? What = null,
	MString? Def = null,
	string? OWhat = null,
	string? ODef = null,
	string? AWhat = null,
	AnySharpContainer? Loc = null,
	DBRef? Env0 = null,
	DBRef? Env1 = null,
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
