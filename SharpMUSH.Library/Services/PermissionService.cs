using Microsoft.Extensions.Options;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Services;

public class PermissionService(ILockService lockService, IOptionsMonitor<SharpMUSHOptions> options) : IPermissionService
{
	public bool PassesLock(AnySharpObject who, AnySharpObject target, string lockString)
		=> lockService.Evaluate(lockString, target, who);

	public bool PassesLock(AnySharpObject who, AnySharpObject target, LockType lockType)
		=> lockService.Evaluate(lockType, target, who);

	public async ValueTask<bool> CanSet(AnySharpObject executor, AnySharpObject target, params SharpAttribute[] attribute)
		=> await CanSetInternal(executor, target, attribute, obeySafe: true);

	// AF_SAFE ignore-safe variant: PennMUSH's af_helper (src/set.c:509-511) is the one call
	// site in the whole codebase that passes safe=0 (Can_Write_Attr_Ignore_Safe,
	// hdrs/mushdb.h:120-121) instead of safe=1 (Can_Write_Attr) - and only when the flag
	// operation being performed is clearing AF_SAFE itself off the attribute
	// (`(af->clrf & AF_SAFE) && Can_Write_Attr_Ignore_Safe(...)`). Every other write, including
	// setting or unsetting any OTHER attribute flag, still goes through the normal safe-obeying
	// check. AttributeService.UnsetAttributeFlagAsync is the one caller: it uses this overload
	// only when the flag being unset is SAFE, and CanSet (obeying safe) for everything else.
	public async ValueTask<bool> CanSetIgnoringSafe(AnySharpObject executor, AnySharpObject target, params SharpAttribute[] attribute)
		=> await CanSetInternal(executor, target, attribute, obeySafe: false);

	private async ValueTask<bool> CanSetInternal(AnySharpObject executor, AnySharpObject target, SharpAttribute[] attribute, bool obeySafe)
	{
		if (!await Controls(executor, target)) return false;

		if (attribute.Length == 0) return true;

		// God bypasses every per-flag gate below: PennMUSH's Cannot_Write_This_Attr wraps its
		// whole body in `!God(p) && (...)` (src/attrib.c:364), and can_create_attr's AF_NODUMP
		// guard reads `player != GOD` directly (src/attrib.c:479-483).
		if (executor.IsGod())
			return true;

		// Each test below walks every level named in `attribute` - PennMUSH's own
		// can_write_attr_internal (src/attrib.c:383-408) calls Cannot_Write_This_Attr once per
		// ancestor node while walking the tree to the target, denying the whole write the
		// moment any single level fails. `Inheritable` (the object-@parent axis - whether a
		// flag survives onto a child object) has nothing to do with that walk and is
		// deliberately not consulted here.

		// AF_INTERNAL: Cannot_Write_This_Attr - denies writes to everyone but God. Unlike the
		// wizard-attribute-lock gate below, Wizard(p) does not exempt a write to an internal
		// attribute.
		if (attribute.Any(a => a.IsInternal()))
			return false;

		// AF_SAFE: Cannot_Write_This_Attr's `(s) && AF_Safe(a)` term - see CanSetIgnoringSafe
		// above for the one case (clearing SAFE itself) where `obeySafe` is false.
		if (obeySafe && attribute.Any(a => a.IsSafe()))
			return false;

		// AF_NODUMP: can_create_attr (src/attrib.c:479-483) - "Only GOD can create an
		// AF_NODUMP attribute (used for semaphores) or add a leaf to a tree with such an
		// attribute." The God exemption already happened above, so reaching here means deny.
		//
		// DELIBERATE PARITY DEVIATION: Penn's can_write_attr_internal (the plain-write path
		// used by every ordinary @set on an attribute that already exists) never tests
		// AF_NODUMP at all - only can_create_attr's ancestor walk does, so Penn lets a wizard
		// overwrite the VALUE of an attribute that already carries AF_NODUMP; only creating a
		// new nodump-flagged attribute, or a new leaf underneath one, is God-only. CanSet
		// cannot make that same distinction: it is handed a single already-persisted
		// SharpAttribute with no marker for "this call is checking whether the attribute
		// itself may be overwritten" vs "this call is gating a not-yet-created descendant" -
		// both AttributeService.SetAttributeAsync call sites (the `existing` loop for
		// overwrites, and the prefix walk for new leaves) invoke this same CanSet with the
		// same shape of argument. So this denies both cases: overwriting a nodump attribute's
		// own value is God-only here too, a strict superset of Penn (more restrictive, never
		// less). Inert today - no seeded attribute defaults to nodump - but it will bite if a
		// standard attribute (e.g. SEMAPHORE, GeneralCommands.cs:174) is ever given nodump for
		// parity: a wizard who could freely re-arm it in Penn would be denied here. Resolving
		// this precisely means threading a create-vs-overwrite flag through CanSet's signature
		// and both AttributeService call sites - out of scope for this fix.
		if (attribute.Any(a => a.IsNoDump()))
			return false;

		// Wizard(p) bypasses BOTH the AF_Wizard(a) check and the locked-attribute check below
		// entirely (Cannot_Write_This_Attr's `Wizard(p) || (!AF_Wizard(a) && ...)`).
		if (await executor.IsWizard())
			return true;

		// AF_WIZARD: a non-wizard, non-God executor may never write a wizard-flagged
		// attribute, regardless of ownership.
		if (attribute.Any(a => a.IsWizard()))
			return false;

		// AF_LOCKED: `!AF_Locked(a) || AL_CREATOR(a) == Owner(p)` - a non-wizard executor may
		// only write a locked attribute it created itself; owning the TARGET object is not
		// enough.
		var executorOwner = await executor.Object().Owner.WithCancellation(CancellationToken.None);
		foreach (var a in attribute.Where(a => a.IsLocked()))
		{
			var attrOwner = await a.Owner.WithCancellation(CancellationToken.None);

			// Fail closed on an unresolvable owner: `AL_CREATOR(a) == Owner(p)` cannot hold for
			// a creator that could not be looked up, but `attrOwner?.Id != executorOwner?.Id`
			// would read null == null as a match and permit the write.
			if (attrOwner is null || executorOwner is null || attrOwner.Id != executorOwner.Id)
				return false;
		}

		return true;
	}

	public async ValueTask<bool> Controls(AnySharpObject executor, AnySharpObject target, params SharpAttribute[] attribute)
	{
		if (!await Controls(executor, target))
			return false;

		if (attribute.Length == 0)
			return true;

		var finalAttr = attribute[^1];

		if (executor.IsGod())
			return true;

		if (await executor.IsWizard() && !finalAttr.IsWizard())
			return true;

		if (finalAttr.IsLocked())
		{
			var attrOwner = await finalAttr.Owner.WithCancellation(CancellationToken.None);
			var targetOwner = await target.Object().Owner.WithCancellation(CancellationToken.None);

			// Same fail-closed rule as CanSetInternal: an attribute whose creator cannot be
			// resolved matches nobody, rather than matching every other unresolvable owner.
			if (attrOwner is null)
				return false;

			return (attrOwner.Id == executor.Id())
						 || (targetOwner is not null && attrOwner.Id == targetOwner.Id && await executor.Owns(target));
		}

		return true;
	}

	public async ValueTask<bool> CanViewAttribute(AnySharpObject viewer, AnySharpObject target,
		params SharpAttribute[] attribute)
	{
		// AF_INTERNAL denies reads to everyone, including wizards and God: PennMUSH's
		// Can_Read_Attr macro (hdrs/mushdb.h:100-101) checks `!AF_Internal(a)` before the
		// `See_All(p) ||` easy-out ever runs, so - unlike mortal_dark below - there is no
		// privileged escape from it. Leaf-only, like Penn: can_read_attr_internal
		// (src/attrib.c:282-320) tests AF_Internal once on the passed-in leaf attribute before
		// any tree walk, and the separate per-branch-segment loop below it only ever tests
		// AF_Private (no_inherit) - AF_Internal never propagates across `-segments.
		if (attribute.Length > 0 && attribute[^1].IsInternal())
			return false;

		// mortal_dark hides from non-privileged viewers regardless of ownership
		if (attribute.Length > 0 && attribute.Any(attr => attr.IsMortalDark())
				&& !viewer.IsGod() && !await viewer.IsWizard())
			return false;

		if (await CanExamine(viewer, target))
			return true;

		if (attribute.Length == 0)
			return false;

		// PennMUSH attrib.c:305-310 - AF_Nearby overrides AF_Visual's grant when the viewer
		// could not look at the target (can_look_at, hdrs/mushdb.h:104). Only pay for the
		// nearby/location lookups when some level of the path actually carries the flag.
		var canLook = attribute.Any(attr => attr.IsNearby()) && await CanLookAt(viewer, target);

		return attribute.All(attr => attr.IsVisual() && (!attr.IsNearby() || canLook));
	}

	public async ValueTask<bool> CanViewAttribute(AnySharpObject viewer, AnySharpObject target,
		params LazySharpAttribute[] attribute)
	{
		// See the SharpAttribute overload above for the AF_INTERNAL rationale.
		if (attribute.Length > 0 && attribute[^1].IsInternal())
			return false;

		// mortal_dark hides from non-privileged viewers regardless of ownership
		if (attribute.Length > 0 && attribute.Any(attr => attr.IsMortalDark())
				&& !viewer.IsGod() && !await viewer.IsWizard())
			return false;

		if (await CanExamine(viewer, target))
			return true;

		if (attribute.Length == 0)
			return false;

		// See the SharpAttribute overload above for the nearby/canlook rationale.
		var canLook = attribute.Any(attr => attr.IsNearby()) && await CanLookAt(viewer, target);

		return attribute.All(attr => attr.IsVisual() && (!attr.IsNearby() || canLook));
	}

	/// <summary>
	/// PennMUSH's <c>can_look_at</c> (<c>hdrs/mushdb.h:104</c>): whether <paramref name="viewer"/>
	/// could look at <paramref name="target"/> - same location, one location away through a
	/// non-opaque room (or one the viewer controls), or the Long_Fingers power/privilege.
	/// Gates the <c>nearby</c> attribute flag's override of <c>visual</c> in
	/// <see cref="CanViewAttribute(AnySharpObject,AnySharpObject,SharpAttribute[])"/>.
	/// </summary>
	private async ValueTask<bool> CanLookAt(AnySharpObject viewer, AnySharpObject target)
	{
		if (await viewer.HasLongFingers())
			return true;

		if (await LocateService.Nearby(viewer, target))
			return true;

		var targetLocation = (await target.Where()).WithExitOption();
		if (await LocateService.Nearby(viewer, targetLocation)
				&& (!await targetLocation.IsOpaque() || await Controls(viewer, targetLocation)))
			return true;

		var viewerLocation = (await viewer.Where()).WithExitOption();
		return await LocateService.Nearby(viewerLocation, target)
					 && (!await viewerLocation.IsOpaque() || await Controls(viewer, viewerLocation));
	}

	public async ValueTask<bool> CanSee(AnySharpObject viewer, AnySharpObject target)
	{
		if (await viewer.IsPriv() || await viewer.IsSee_All())
		{
			return true;
		}

		return !await target.IsDark();
	}

	public async ValueTask<bool> CanSee(AnySharpObject viewer, SharpObject target)
	{
		if (await viewer.IsPriv() || await viewer.IsSee_All())
		{
			return true;
		}

		return !await target.IsDark();
	}

	public async ValueTask<bool> CanHide(AnySharpObject executor)
		=> await executor.IsPriv() || await executor.HasPower("HIDE");

	public async ValueTask<bool> CanLogin(AnySharpObject executor)
		=> await executor.IsPriv() || await executor.HasPower("LOGIN");

	public async ValueTask<bool> CanIdle(AnySharpObject executor)
		=> await executor.IsPriv() || await executor.HasPower("IDLE");

	public async ValueTask<bool> CanFind(AnySharpObject viewer, AnySharpObject target)
	{
		if (await viewer.IsPriv() || await viewer.IsSee_All())
		{
			return true;
		}

		return !await target.HasFlag("UNFINDABLE");
	}

	/// <summary>
	/// Check if viewer can execute an attribute on target.
	/// Checks full attribute path - all parent attributes must allow evaluation.
	/// </summary>
	public async ValueTask<bool> CanExecuteAttribute(AnySharpObject viewer, AnySharpObject target,
		params SharpAttribute[] attribute)
	{
		if (attribute.Length == 0)
			return false;

		return await attribute.ToAsyncEnumerable()
			.AllAsync(async (attr, _) => await CanEvalAttr(viewer, target, attr));
	}

	/// <summary>
	/// Check if viewer can execute a lazy attribute on target.
	/// Checks full attribute path - all parent attributes must allow evaluation.
	/// </summary>
	public async ValueTask<bool> CanExecuteAttribute(AnySharpObject viewer, AnySharpObject target,
		params LazySharpAttribute[] attribute)
	{
		if (attribute.Length == 0)
			return false;

		return await attribute.ToAsyncEnumerable()
			.AllAsync(async (attr, _) => await CanEvalAttr(viewer, target, attr));
	}

	public async ValueTask<bool> Controls(AnySharpObject who, AnySharpObject target)
	{
		if (await who.HasPower("guest"))
			return false;

		if (who.Id() == target.Id())
			return true;

		if (who.IsGod())
			return true;

		if (target.IsGod())
			return false;

		if (await who.IsWizard())
			return true;

		if (await target.IsWizard() || (await target.IsPriv() && !await who.IsPriv()))
			return false;

		if (await who.IsMistrust())
			return false;

		if (await who.Owns(target) && (!await target.Inheritable() || await who.Inheritable()))
			return true;

		if (await target.Inheritable() || target.IsPlayer)
			return false;

		// Zone Master Object (ZMO) control
		// If zone_control_zmp_only is false, check if target has a zone and if who passes the Zone_Lock
		if (!options.CurrentValue.Database.ZoneControlZmpOnly)
		{
			var targetZone = await target.Object().Zone.WithCancellation(CancellationToken.None);
			if (!targetZone.IsNone && lockService.Evaluate(LockType.Zone, targetZone.Known, who))
			{
				return true;
			}
		}

		// Zone Master Player (ZMP) control
		// If target's owner has SHARED flag and who passes the owner's Zone_Lock
		if (!target.IsPlayer)
		{
			var targetOwner = await target.Object().Owner.WithCancellation(CancellationToken.None);
			var ownerObject = new AnySharpObject(targetOwner);
			if (await ownerObject.HasFlag("SHARED"))
			{
				if (lockService.Evaluate(LockType.Zone, ownerObject, who))
				{
					return true;
				}
			}
		}

		// PennMUSH controls() (predicat.c:416) reads the control lock raw and skips it when unset, rather
		// than evaluating it: an unset lock passes everybody, so evaluating it here would grant control of
		// every object without an explicit control lock to everyone. Only a lock that was actually set,
		// and that `who` passes, grants control.
		var controlLock = LockService.GetIfSet(LockType.Control, target);

		return controlLock is not null && lockService.Evaluate(controlLock, target, who);
	}

	public async ValueTask<bool> CanExamine(AnySharpObject examiner, AnySharpObject examinee)
		=> examiner.Object().DBRef == examinee.Object().DBRef
			 || await Controls(examiner, examinee)
			 || await examiner.IsSee_All()
			 || (await examinee.IsVisual() && lockService.Evaluate(LockType.Examine, examinee, examiner));

	/// <inheritdoc />
	public async ValueTask<bool> CanReadLock(AnySharpObject viewer, AnySharpObject target, LockService.LockFlags lockFlags)
		=> await viewer.IsSee_All()
			 || await Controls(viewer, target)
			 || ((await target.IsVisual() || lockFlags.HasFlag(LockService.LockFlags.Visual))
				 && lockService.Evaluate(LockType.Examine, target, viewer));

	public async ValueTask<bool> CanInteract(AnySharpObject from, AnySharpObject to, IPermissionService.InteractType type)
	{
		if (from.Id() == to.Id() || from.IsRoom || to.IsRoom) return true;

		if (type.HasFlag(IPermissionService.InteractType.Hear) && !lockService.Evaluate(LockType.Interact, to, from))
			return false;

		return await ValueTask.FromResult(true);
	}

	public async ValueTask<bool> CanInteract(AnySharpObject interactor, AnySharpContent interactee,
		IPermissionService.InteractType type)
		=> await CanInteract(interactor, interactee.WithRoomOption(), type);

	public static async ValueTask<bool> CanEval(AnySharpObject evaluator, AnySharpObject evaluationTarget)
		=> !await evaluationTarget.IsPriv()
			 || evaluator.IsGod()
			 || ((await evaluator.IsWizard()
						|| (await evaluator.IsRoyalty() && !await evaluationTarget.IsWizard()))
					 && !evaluationTarget.IsGod());

	public static async ValueTask<bool> CanEvalAttr(
		AnySharpObject evaluator,
		AnySharpObject evaluationTarget,
		SharpAttribute attribute)
		=> await CanEval(evaluator, evaluationTarget)
			 || attribute.IsPublic();

	public static async ValueTask<bool> CanEvalAttr(
		AnySharpObject evaluator,
		AnySharpObject evaluationTarget,
		LazySharpAttribute attribute)
		=> await CanEval(evaluator, evaluationTarget)
			 || attribute.IsPublic();


	/// <summary>
	/// Checks against basic lock.
	/// </summary>
	/// <param name="who">Who wants to pass the lock.</param>
	/// <param name="thing">Against what thing?</param>
	/// <returns>Whether or not they pass te basic lock.</returns>
	public ValueTask<bool> CouldDoIt(AnySharpObject who, AnyOptionalSharpObject thing)
		=> ValueTask.FromResult(thing switch
		{
			{ IsNone: true } => false,
			_ => PassesLock(who, thing.Known, LockType.Basic)
		});

	public ValueTask<bool> CanGoto(AnySharpObject who, SharpExit exit, AnySharpContainer destination)
	{
		var _ = who;
		var _2 = exit;
		var _3 = destination;
		return ValueTask.FromResult(true);
	}

	/// <summary>PennMUSH <c>Chan_Ok_Type</c> — hdrs/extchat.h:196.</summary>
	public bool ChannelOkType(AnySharpObject target, SharpChannel channel)
		=> (target.IsPlayer && channel.HasPriv("Player"))
			 || (target.IsThing && channel.HasPriv("Object"));

	/// <summary>PennMUSH <c>Chan_Can</c> — hdrs/extchat.h:198. The DISABLED bit lives here, so it gates
	/// join, speak, see, hide and modify from one place.</summary>
	public async ValueTask<bool> ChannelStandardCan(AnySharpObject target, string[] channelType)
		=> !channelType.HasPriv("Disabled")
			 && (!channelType.HasPriv("Wizard")
					 || await target.IsWizard())
			 && (!channelType.HasPriv("Admin")
					 || await target.HasPower("CHAT_PRIVS")
					 || await target.IsPriv());

	/// <summary>
	/// PennMUSH <c>Chan_Can_Priv(p, t) = Wizard(p) || Chan_Can(p, t)</c> — hdrs/extchat.h:203, "who can
	/// change channel privileges to type t". Note the argument is the type being SET, not the channel's
	/// current type.
	/// </summary>
	public async ValueTask<bool> ChannelCanPriv(AnySharpObject target, string[] channelType)
		=> await target.IsWizard()
			 || await ChannelStandardCan(target, channelType);

	public async ValueTask<bool> ChannelCanAccess(AnySharpObject target, SharpChannel channel)
		=> await ChannelStandardCan(target, channel.Privs);

	public async ValueTask<bool> ChannelCanJoin(AnySharpObject target, SharpChannel channel)
		=> await ChannelCanAccess(target, channel) && lockService.Evaluate(channel.JoinLock, channel, target);

	public async ValueTask<bool> ChannelCanSpeak(AnySharpObject target, SharpChannel channel)
		=> await ChannelCanAccess(target, channel) && lockService.Evaluate(channel.SpeakLock, channel, target);

	public async ValueTask<bool> ChannelCanCemit(AnySharpObject target, SharpChannel channel)
		=> !channel.HasPriv("NoCemit") && await ChannelCanSpeak(target, channel);

	/// <summary>
	/// PennMUSH <c>Chan_Can_Modify</c> — hdrs/extchat.h:210.
	///
	/// <para>The mod lock is only consulted when one is actually set. PennMUSH gets away with evaluating
	/// it unconditionally because it never leaves it unset: <c>do_chan_admin</c> stamps
	/// <c>=#&lt;creator&gt;</c> onto every channel as it is created (<c>src/extchat.c:1755-1763</c>).
	/// SharpMUSH's <c>CreateChannelCommand</c> writes no lock, and an empty lock string evaluates TRUE for
	/// everybody — so evaluating it here handed modify rights over every channel in the game to every
	/// non-guest, which is <c>@channel/privs</c>, <c>/rename</c>, <c>/wipe</c>, <c>/decompile</c> and
	/// <c>/mute</c>.</para>
	///
	/// <para>This is the same trap, and the same fix, as the control lock in <see cref="Controls"/> above:
	/// an unset lock must be skipped, not evaluated. Owner and wizard still pass, so the effective result
	/// matches PennMUSH for every channel PennMUSH would have created.</para>
	/// </summary>
	public async ValueTask<bool> ChannelCanModifyAsync(AnySharpObject target, SharpChannel channel) =>
		await target.IsWizard()
		|| (await channel.Owner.WithCancellation(CancellationToken.None)).Id == target.Id()
		|| (
			!await target.HasPower("guest")
			&& !string.IsNullOrWhiteSpace(channel.ModLock)
			&& await ChannelCanAccess(target, channel)
			&& lockService.Evaluate(channel.ModLock, channel, target)
		);

	public async ValueTask<bool> ChannelCanSeeAsync(AnySharpObject target, SharpChannel channel)
		=> await target.IsPriv()
			 || await target.IsSee_All()
			 || (
				 await ChannelCanAccess(target, channel)
				 && lockService.Evaluate(channel.SeeLock, channel, target)
			 )
			 || (
				 await channel.Members.Value.AnyAsync(x => x.Member.Id() == target.Id())
				 && await ChannelCanSpeak(target, channel)
			 );

	/// <summary>
	/// PennMUSH <c>Chan_Can_Hide</c> — hdrs/extchat.h:216. The channel privilege is <c>Hide_Ok</c>
	/// (<c>CHANNEL_CANHIDE</c>, spelled <c>hide_ok</c> in <c>chan_privs</c>); this read
	/// <c>"CanHide"</c>, a name no channel has ever carried.
	/// </summary>
	public async ValueTask<bool> ChannelCanHide(AnySharpObject target, SharpChannel channel)
		=> await target.CanHide()
			 || (
				 channel.HasPriv("Hide_Ok")
				 && await ChannelCanAccess(target, channel)
				 && lockService.Evaluate(channel.HideLock, channel, target)
			 );

	public async ValueTask<bool> ChannelCanNukeAsync(AnySharpObject target, SharpChannel channel)
		=> await target.IsWizard()
			 || (await channel.Owner.WithCancellation(CancellationToken.None)).Id ==
			 (await target.Object().Owner.WithCancellation(CancellationToken.None)).Id;

	public async ValueTask<bool> ChannelCanDecomposeAsync(AnySharpObject target, SharpChannel channel)
		=> await target.IsSee_All()
			 || (await channel.Owner.WithCancellation(CancellationToken.None)).Id == target.Id()
			 || await ChannelCanModifyAsync(target, channel);

	public async ValueTask<bool> CanNoSpoof(AnySharpObject executor)
		=> await executor.HasPower("NOSPOOF") || await executor.IsWizard();
}