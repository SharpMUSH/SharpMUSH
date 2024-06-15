using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Services
{
	public class PermissionService(ILockService lockService) : IPermissionService
	{
		public bool CanSet(AnySharpObject executor, AnySharpObject target, SharpAttribute attribute)
		{
			throw new NotImplementedException();
		}

		public bool Controls(AnySharpObject executor, AnySharpObject target, SharpAttribute attribute)
		{
			throw new NotImplementedException();
		}

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

			if (target.Inheritable() || target.IsPlayer())
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

		public bool CanInteract(AnySharpObject result, AnySharpObject executor, IPermissionService.InteractType type)
		{
			throw new NotImplementedException();
		}

		public static bool CanEval(
			AnySharpObject evaluator,
			AnySharpObject evaluationTarget) 
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
	}
}
