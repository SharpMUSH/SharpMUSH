using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Services;

public interface IPermissionService
{
	public enum InteractType
	{
		See, Hear, Match, Presence
	}

	public bool PassesLock(AnySharpObject who, AnySharpObject target, string lockString);

	public bool PassesLock(AnySharpObject who, AnySharpObject target, LockType lockType);
	
	public ValueTask<bool> CanSet(AnySharpObject executor, AnySharpObject target, params SharpAttribute[] attribute);

	public ValueTask<bool> Controls(AnySharpObject executor, AnySharpObject target, params SharpAttribute[] attribute);

	public ValueTask<bool> Controls(AnySharpObject executor, AnySharpObject target);

	ValueTask<bool> CanExamine(AnySharpObject examiner, AnySharpObject examinee);

	ValueTask<bool> CanViewAttribute(AnySharpObject viewer, AnySharpObject target, params SharpAttribute[] attribute);

	ValueTask<bool> CanExecuteAttribute(AnySharpObject viewer, AnySharpObject target, params SharpAttribute[] attribute);

	ValueTask<bool> CanInteract(AnySharpObject result, AnySharpObject executor, InteractType type);

	ValueTask<bool> CanNoSpoof(AnySharpObject executor);

	ValueTask<bool> CouldDoIt(AnySharpObject who, AnyOptionalSharpObject thing1, string? what);

	ValueTask<bool> CanGoto(AnySharpObject who, SharpExit exit, AnySharpContainer destnation);

}