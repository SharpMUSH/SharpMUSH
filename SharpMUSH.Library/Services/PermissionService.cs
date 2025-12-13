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
	{
		if (!await Controls(executor, target)) return false;

		var compressedAttribute = attribute[^1] with
		{
			Flags = attribute.SelectMany(a => a.Flags)
				.Where(x => x.Inheritable == true)
				.DistinctBy(x => x.Name)
		};

		return !(!executor.IsGod()
		         // && (It's Internal // SAFE when we care about SAFE)
		         || !(await executor.IsWizard()
		              || (!compressedAttribute.IsWizard()
		                  && (!compressedAttribute.IsLocked()
		                      || await compressedAttribute.Owner.WithCancellation(CancellationToken.None) ==
		                      await target.Object().Owner.WithCancellation(CancellationToken.None)))));
	}

	public ValueTask<bool> Controls(AnySharpObject executor, AnySharpObject target, params SharpAttribute[] attribute)
		=> Controls(executor, target); // TODO: Implement

	// TODO: Confirm Implementation
	// TODO: Optimize for lists.
	public async ValueTask<bool> CanViewAttribute(AnySharpObject viewer, AnySharpObject target,
		params SharpAttribute[] attribute)
		=> await CanExamine(viewer, target) || attribute.Last().IsVisual();

	public async ValueTask<bool> CanViewAttribute(AnySharpObject viewer, AnySharpObject target,
		params LazySharpAttribute[] attribute)
		=> await CanExamine(viewer, target) || attribute.Last().IsVisual();

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

	// TODO: Confirm Implementation.
	// TODO: Optimize for lists.
	public ValueTask<bool> CanExecuteAttribute(AnySharpObject viewer, AnySharpObject target,
		params SharpAttribute[] attribute)
		=> CanEvalAttr(viewer, target, attribute.Last());

	public ValueTask<bool> CanExecuteAttribute(AnySharpObject viewer, AnySharpObject target,
		params LazySharpAttribute[] attribute)
		=> CanEvalAttr(viewer, target, attribute.Last());

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

		return lockService.Evaluate(LockType.Control, target, who);
	}

	public async ValueTask<bool> CanExamine(AnySharpObject examiner, AnySharpObject examinee)
		=> examiner.Object().DBRef == examinee.Object().DBRef
		   || await Controls(examiner, examinee)
		   || await examiner.IsSee_All()
		   || (await examinee.IsVisual() && lockService.Evaluate(LockType.Examine, examinee, examiner));

	public async ValueTask<bool> CanInteract(AnySharpObject from, AnySharpObject to, IPermissionService.InteractType type)
	{
		if (from.Id() == to.Id() || from.IsRoom || to.IsRoom) return true;

		var fromStep = from.MinusRoom();
		var toStep = to.MinusRoom();

		if (type.HasFlag(IPermissionService.InteractType.Hear) && !lockService.Evaluate(LockType.Interact, to, from))
			return false;

		// TODO: This looks like this is 'return true or true'.
		if (fromStep.Object().Id == (await toStep.Location()).Object().Id
		    || toStep.Object().Id == (await fromStep.Location()).Object().Id
		    || await Controls(to, from))
			return true;

		return true;
	}

	public async ValueTask<bool> CanInteract(AnySharpContent result, AnySharpObject executor,
		IPermissionService.InteractType type)
		=> await CanInteract(result.WithRoomOption(), executor, type);

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
		/*
		 // D:\pennmush\src\move.c
		 // Most details are in do_move (395)

      if (!eval_lock_with(player, Location(player), Leave_Lock, pe_info)) {
        fail_lock(player, Location(player), Leave_Lock,
                  T("You can't go that way."), NOTHING);
        return;
      }

      // could_doit(player, exit_m, pe_info)


		 */

		var _ = who;
		var _2 = exit;
		var _3 = destination;
		return ValueTask.FromResult(true);
	}

	public bool ChannelOkType(AnySharpObject target, SharpChannel channel)
		=> channel.Privs.Contains("Player") && target.IsPlayer
		   || channel.Privs.Contains("Object") && target.IsThing;

	public async ValueTask<bool> ChannelStandardCan(AnySharpObject target, string[] channelType)
		=> !channelType.Contains("Disabled")
		   && (!channelType.Contains("Wizard")
		       || await target.IsWizard())
		   && (!channelType.Contains("Admin")
		       || await target.HasPower("CHAT_PRIVS")
		       || await target.IsPriv());

	public async ValueTask<bool> ChannelCanPrivate(AnySharpObject target, SharpChannel channel)
		=> !await target.IsWizard()
		   || await ChannelStandardCan(target, channel.Privs);

	public async ValueTask<bool> ChannelCanAccess(AnySharpObject target, SharpChannel channel)
		=> await ChannelStandardCan(target, channel.Privs);

	public async ValueTask<bool> ChannelCanJoin(AnySharpObject target, SharpChannel channel)
		=> await ChannelCanAccess(target, channel) && lockService.Evaluate(channel.JoinLock, channel, target);

	public async ValueTask<bool> ChannelCanSpeak(AnySharpObject target, SharpChannel channel)
		=> await ChannelCanAccess(target, channel) && lockService.Evaluate(channel.SpeakLock, channel, target);

	public async ValueTask<bool> ChannelCanCemit(AnySharpObject target, SharpChannel channel)
		=> !channel.Privs.Contains("NoCemit") && await ChannelCanSpeak(target, channel);

	public async ValueTask<bool> ChannelCanModifyAsync(AnySharpObject target, SharpChannel channel) =>
		await target.IsWizard()
		|| (await channel.Owner.WithCancellation(CancellationToken.None)).Id == target.Id()
		|| (
			!await target.HasPower("guest")
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

	public async ValueTask<bool> ChannelCanHide(AnySharpObject target, SharpChannel channel)
		=> await target.CanHide()
		   || (
			   channel.Privs.Contains("CanHide")
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
		=> await executor.HasPower("NOSPOOF") || await executor.IsWizard() || executor.IsGod();
}