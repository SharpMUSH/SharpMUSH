using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Library.Services.Interfaces;

public interface IManipulateSharpObjectService
{
	ValueTask<CallState> SetName(AnySharpObject executor, AnySharpObject obj, MString name, bool notify);
	
	ValueTask<CallState> SetPassword(AnySharpObject executor, SharpPlayer player, string newPassword, bool notify);
	
	ValueTask<CallState> SetOrUnsetFlag(AnySharpObject executor, AnySharpObject obj, string flagOrFlagAlias, bool notify);
	
	ValueTask<CallState> SetPower(AnySharpObject executor, AnySharpObject obj, string powerOrPowerAlias, bool notify);
	
	ValueTask<CallState> UnsetPower(AnySharpObject executor, AnySharpObject obj, string powerOrPowerAlias, bool notify);
	
	ValueTask<CallState> SetOwner(AnySharpObject executor, AnySharpObject obj, SharpPlayer newOwner, bool notify);
	
	ValueTask<CallState> SetParent(AnySharpObject executor, AnySharpObject obj, AnySharpObject newParent, bool notify);
	
	ValueTask<CallState> UnsetParent(AnySharpObject executor, AnySharpObject obj, bool notify);
}