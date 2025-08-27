using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Library.Services.Interfaces;

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

	ValueTask<bool> CouldDoIt(AnySharpObject who, AnyOptionalSharpObject thing1);

	ValueTask<bool> CanGoto(AnySharpObject who, SharpExit exit, AnySharpContainer destnation);

	ValueTask<bool> CanFind(AnySharpObject viewer, AnySharpObject target);
	
	ValueTask<bool> CanSee(AnySharpObject viewer, AnySharpObject target);
	
	ValueTask<bool> CanHide(AnySharpObject executor);
	
	ValueTask<bool> CanLogin(AnySharpObject executor);
	
	ValueTask<bool> CanIdle(AnySharpObject executor);
	
	bool ChannelOkType(AnySharpObject target, SharpChannel channel);
	
	ValueTask<bool> ChannelStandardCan(AnySharpObject target, string[] channelType);
	
	ValueTask<bool> ChannelCanPrivate(AnySharpObject target, SharpChannel channel);
	
	ValueTask<bool> ChannelCanAccess(AnySharpObject target, SharpChannel channel);
	
	ValueTask<bool> ChannelCanJoin(AnySharpObject target, SharpChannel channel);
	
	ValueTask<bool> ChannelCanSpeak(AnySharpObject target, SharpChannel channel);
	
	ValueTask<bool> ChannelCanCemit(AnySharpObject target, SharpChannel channel);
	
	ValueTask<bool> ChannelCanModifyAsync(AnySharpObject target, SharpChannel channel);
	
	ValueTask<bool> ChannelCanSeeAsync(AnySharpObject target, SharpChannel channel);
	
	ValueTask<bool> ChannelCanHide(AnySharpObject target, SharpChannel channel);
	
	ValueTask<bool> ChannelCanNukeAsync(AnySharpObject target, SharpChannel channel);
	
	ValueTask<bool> ChannelCanDecomposeAsync(AnySharpObject target, SharpChannel channel);
}