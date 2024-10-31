using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Services;

public interface IPermissionService
{
	public enum InteractType
	{
		See, Hear, Match, Presence
	}

	public bool CanSet(AnySharpObject executor, AnySharpObject target, SharpAttribute attribute);

	public bool Controls(AnySharpObject executor, AnySharpObject target, SharpAttribute attribute);

	public bool Controls(AnySharpObject executor, AnySharpObject target);

	bool CanExamine(AnySharpObject examiner, AnySharpObject examinee);

	bool CanViewAttribute(AnySharpObject viewer, AnySharpObject target, params SharpAttribute[] attribute);

	bool CanExecuteAttribute(AnySharpObject viewer, AnySharpObject target, params SharpAttribute[] attribute);

	bool CanInteract(AnySharpObject result, AnySharpObject executor, InteractType type);
		
	bool CouldDoIt(AnySharpObject who, AnyOptionalSharpObject thing1, string? what);

	bool CanGoto(AnySharpObject who, SharpExit exit, AnySharpContainer destnation);

}