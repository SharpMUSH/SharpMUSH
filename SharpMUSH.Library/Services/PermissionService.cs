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

		if (attribute.Length == 0) return true;

		var compressedAttribute = attribute[^1] with
		{
			Flags = attribute.SelectMany(a => a.Flags)
				.Where(x => x.Inheritable == true)
				.DistinctBy(x => x.Name)
		};

		// TODO: Internal and SAFE attribute flag checks not yet implemented.
		return !(!executor.IsGod()
						 && !(await executor.IsWizard()
									|| (!compressedAttribute.IsWizard()
											&& (!compressedAttribute.IsLocked()
													|| await compressedAttribute.Owner.WithCancellation(CancellationToken.None) ==
													await target.Object().Owner.WithCancellation(CancellationToken.None)))));
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
			return (attrOwner?.Id == executor.Id())
						 || (attrOwner?.Id == targetOwner?.Id && await executor.Owns(target));
		}

		return true;
	}

	public async ValueTask<bool> CanViewAttribute(AnySharpObject viewer, AnySharpObject target,
		params SharpAttribute[] attribute)
	{
		// mortal_dark hides from non-privileged viewers regardless of ownership
		if (attribute.Length > 0 && attribute.Any(attr => attr.IsMortalDark())
		    && !viewer.IsGod() && !await viewer.IsWizard())
			return false;

		if (await CanExamine(viewer, target))
			return true;

		return attribute.Length > 0 && attribute.All(attr => attr.IsVisual());
	}

	public async ValueTask<bool> CanViewAttribute(AnySharpObject viewer, AnySharpObject target,
		params LazySharpAttribute[] attribute)
	{
		// mortal_dark hides from non-privileged viewers regardless of ownership
		if (attribute.Length > 0 && attribute.Any(attr => attr.IsMortalDark())
		    && !viewer.IsGod() && !await viewer.IsWizard())
			return false;

		if (await CanExamine(viewer, target))
			return true;

		return attribute.Length > 0 && attribute.All(attr => attr.IsVisual());
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

		return lockService.Evaluate(LockType.Control, target, who);
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
		=> await executor.HasPower("NOSPOOF") || await executor.IsWizard();
}