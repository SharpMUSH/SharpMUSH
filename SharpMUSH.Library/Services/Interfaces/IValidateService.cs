using OneOf;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Services.Interfaces;

public interface IValidateService
{
	enum ValidationType
	{
		Name, 
		AttributeName, 
		AttributeValue, 
		PlayerName, 
		PlayerAlias,
		Password,
		CommandName, 
		FunctionName, 
		FlagName, 
		PowerName, 
		QRegisterName,
		ColorName, 
		AnsiCode, 
		ChannelName,
		Timezone,
		LockType,
		LockKey,
		BoolExp
	}
	
	ValueTask<bool> Valid(ValidationType type, MString value, OneOf<AnySharpObject, SharpAttributeEntry>? target = null);
}