using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Services;

public class PermissionService(ILockService lockService) : IPermissionService
{
	public bool CanSet(AnySharpObject executor, AnySharpObject target, params SharpAttribute[] attribute)
	{
		if (!Controls(executor, target)) return false;

		var compressedAttribute = attribute[^1] with
		{
			Flags = attribute.SelectMany(a => a.Flags)
											 .Where(x => x.Inheritable == true)
											 .DistinctBy(x => x.Name)
		};

		return !(!executor.IsGod()
			// && (It's Internal // SAFE when we care about SAFE)
			|| !(executor.IsWizard()
				|| (!compressedAttribute.IsWizard()
					&& (!compressedAttribute.IsLocked()
						|| compressedAttribute.Owner.Value == target.Object().Owner.Value))));
	}

	public bool Controls(AnySharpObject executor, AnySharpObject target, params SharpAttribute[] attribute)
		=> Controls(executor, target); // TODO: Implement

	// TODO: Confirm Implementation
	// TODO: Optimize for lists.
	public bool CanViewAttribute(AnySharpObject viewer, AnySharpObject target, params SharpAttribute[] attribute)
		=> CanExamine(viewer, target) || attribute.Last().IsVisual();

	// TODO: Confirm Implementation.
	// TODO: Optimize for lists.
	public bool CanExecuteAttribute(AnySharpObject viewer, AnySharpObject target, params SharpAttribute[] attribute)
		=> CanEvalAttr(viewer, target, attribute.Last());

	public bool Controls(AnySharpObject who, AnySharpObject target)
	{
		if (who.HasPower("guest"))
			return false;

		if (who.Id() == target.Id())
			return true;

		if (who.IsGod())
			return true;

		if (target.IsGod())
			return false;

		if (who.IsWizard())
			return true;

		if (target.IsWizard() || (target.IsPriv() && !who.IsPriv()))
			return false;

		if (who.IsMistrust())
			return false;

		if (who.Owns(target) && (!target.Inheritable() || who.Inheritable()))
			return true;

		if (target.Inheritable() || target.IsPlayer)
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

	public bool CanExamine(AnySharpObject examiner, AnySharpObject examinee)
		=> examiner.Object().DBRef == examinee.Object().DBRef
			 || Controls(examiner, examinee)
			 || examiner.IsSee_All()
			 || (examinee.IsVisual() && lockService.Evaluate(LockType.Examine, examinee, examiner));

	public bool CanInteract(AnySharpObject from, AnySharpObject to, IPermissionService.InteractType type)
	{
		if (from == to || from.IsRoom || to.IsRoom) return true;

		var fromStep = from.MinusRoom();
		var toStep = to.MinusRoom();

		if (type == IPermissionService.InteractType.Hear && !lockService.Evaluate(LockType.Interact, to, from))
			return false;

		if (fromStep.Object().Id == toStep.Location().Object().Id
				|| toStep.Object().Id == fromStep.Location().Object().Id
				|| Controls(to, from))
			return true;

		return true;
	}

	public static bool CanEval(AnySharpObject evaluator, AnySharpObject evaluationTarget)
		=> !evaluationTarget.IsPriv()
			 || evaluator.IsGod()
			 || ((evaluator.IsWizard()
						|| (evaluator.IsRoyalty() && !evaluationTarget.IsWizard()))
					 && !evaluationTarget.IsGod());

	public static bool CanEvalAttr(
		AnySharpObject evaluator,
		AnySharpObject evaluationTarget,
		SharpAttribute attribute)
		=> CanEval(evaluator, evaluationTarget)
			 || attribute.IsPublic();

	public bool CouldDoIt(AnySharpObject who, AnyOptionalSharpObject thing1, string? what)
	{
		throw new NotImplementedException();
	}

	public bool CanGoto(AnySharpObject who, SharpExit exit, AnySharpContainer destination)
	{
		// TODO: Implement
		var _ = who;
		var _2 = exit;
		var _3 = destination;
		return true;
	}

	public bool CanNoSpoof(AnySharpObject executor)
		=> executor.HasPower("NOSPOOF") || executor.IsWizard() || executor.IsGod();
}