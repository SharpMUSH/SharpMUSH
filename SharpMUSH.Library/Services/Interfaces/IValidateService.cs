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

	ValueTask<bool> Valid(ValidationType type, MString value, ValidationTarget target);

	/// <summary>
	/// PennMUSH's <c>ok_player_name(name, player, thing)</c> (<c>src/predicat.c:709</c>): whether
	/// <paramref name="thing"/> may be called <paramref name="name"/> when <paramref name="player"/>
	/// asks. <paramref name="player"/> decides the wizard exemptions (spaces, banned names);
	/// <paramref name="thing"/> is the player who will carry the name — none for a new player — and may
	/// keep a banned name it already has. The name must be free of every other player.
	/// </summary>
	ValueTask<bool> ValidPlayerName(MString name, AnyOptionalSharpObject player, AnyOptionalSharpObject thing);
}