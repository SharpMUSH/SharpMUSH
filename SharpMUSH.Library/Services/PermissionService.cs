using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Services;

public class PermissionService(ILockService lockService) : IPermissionService
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
	public async ValueTask<bool> CanViewAttribute(AnySharpObject viewer, AnySharpObject target, params SharpAttribute[] attribute)
		=> await CanExamine(viewer, target) || attribute.Last().IsVisual();

	// TODO: Confirm Implementation.
	// TODO: Optimize for lists.
	public ValueTask<bool> CanExecuteAttribute(AnySharpObject viewer, AnySharpObject target, params SharpAttribute[] attribute)
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

		/* TODO: Zone Master items here.*/
		/*
			if (!ZONE_CONTROL_ZMP && (Zone(what) != NOTHING) &&
					eval_lock(who, Zone(what), Zone_Lock))
				return 1;

			if (ZMaster(Owner(what)) && !IsPlayer(what) &&
					eval_lock(who, Owner(what), Zone_Lock))
				return 1;
		*/

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

		if (type == IPermissionService.InteractType.Hear && !lockService.Evaluate(LockType.Interact, to, from))
			return false;

		if (fromStep.Object().Id == (await toStep.Location()).Object().Id
		    || toStep.Object().Id == (await fromStep.Location()).Object().Id
		    || await Controls(to, from))
			return true;

		return true;
	}

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

	public ValueTask<bool> CouldDoIt(AnySharpObject who, AnyOptionalSharpObject thing1, string? what)
	{
		throw new NotImplementedException();
	}

	public ValueTask<bool> CanGoto(AnySharpObject who, SharpExit exit, AnySharpContainer destination)
	{
		// TODO: Implement
		var _ = who;
		var _2 = exit;
		var _3 = destination;
		return ValueTask.FromResult(true);
	}

	public async ValueTask<bool> CanNoSpoof(AnySharpObject executor)
		=> await executor.HasPower("NOSPOOF") || await executor.IsWizard() || executor.IsGod();
}