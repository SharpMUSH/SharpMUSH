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
	
	public bool CanSet(AnySharpObject executor, AnySharpObject target, params SharpAttribute[] attribute);

	public bool Controls(AnySharpObject executor, AnySharpObject target, params SharpAttribute[] attribute);

	public bool Controls(AnySharpObject executor, AnySharpObject target);

	bool CanExamine(AnySharpObject examiner, AnySharpObject examinee);

	bool CanViewAttribute(AnySharpObject viewer, AnySharpObject target, params SharpAttribute[] attribute);

	bool CanExecuteAttribute(AnySharpObject viewer, AnySharpObject target, params SharpAttribute[] attribute);

	bool CanInteract(AnySharpObject result, AnySharpObject executor, InteractType type);

	bool CanNoSpoof(AnySharpObject executor);

	bool CouldDoIt(AnySharpObject who, AnyOptionalSharpObject thing1, string? what);

	bool CanGoto(AnySharpObject who, SharpExit exit, AnySharpContainer destnation);

	bool ChannelOkType(AnySharpObject target, SharpChannel channel);
	
	bool ChannelStandardCan(AnySharpObject target, string[] channelType);
	
	bool ChannelCanPrivate(AnySharpObject target, SharpChannel channel);
	
	bool ChannelCanAccess(AnySharpObject target, SharpChannel channel);
	
	bool ChannelCanJoin(AnySharpObject target, SharpChannel channel);
	
	bool ChannelCanSpeak(AnySharpObject target, SharpChannel channel);
	
	bool ChannelCanCemit(AnySharpObject target, SharpChannel channel);
	
	ValueTask<bool> ChannelCanModifyAsync(AnySharpObject target, SharpChannel channel);
	
	ValueTask<bool> ChannelCanSeeAsync(AnySharpObject target, SharpChannel channel);
	
	bool ChannelCanHide(AnySharpObject target, SharpChannel channel);
	
	ValueTask<bool> ChannelCanNukeAsync(AnySharpObject target, SharpChannel channel);
	
	ValueTask<bool> ChannelCanDecomposeAsync(AnySharpObject target, SharpChannel channel);
}