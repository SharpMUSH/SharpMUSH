using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Services.Interfaces;

public interface IValidateService
{
	enum ValidationType
	{
		Invalid = 0,
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

	ValueTask<bool> Valid(ValidationType type, MString value, AttributeTarget target);
}