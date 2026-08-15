using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>
/// PennMUSH's object-type mask (<c>match.c</c>'s <c>type</c> parameter, whose <c>NOTYPE</c> is
/// <see cref="None"/>). Held apart from <see cref="LocateFlags"/> because a type preference and a
/// search scope are different questions: <c>ExitsPreference</c> says "prefer an exit", while the
/// <c>Exits*</c> flags say "walk the exit lists".
/// </summary>
[Flags]
public enum SharpObjectTypes
{
	None = 0,
	Player = 1,
	Room = 2,
	Exit = 4,
	Thing = 8,
	Any = Player | Room | Exit | Thing
}

[Flags]
public enum LocateFlags
{
	NoTypePreference = 1,
	OnlyMatchTypePreference = NoTypePreference << 1,
	ExitsPreference = OnlyMatchTypePreference << 1,
	PreferLockPass = ExitsPreference << 1,
	PlayersPreference = PreferLockPass << 1,
	RoomsPreference = PlayersPreference << 1,
	ThingsPreference = RoomsPreference << 1,
	FailIfNotPreferred = ThingsPreference << 1,
	UseLastIfAmbiguous = FailIfNotPreferred << 1,
	AbsoluteMatch = UseLastIfAmbiguous << 1,
	ExitsInTheRoomOfLooker = AbsoluteMatch << 1,
	ExitsInsideOfLooker = ExitsInTheRoomOfLooker << 1,
	MatchHereForLookerLocation = ExitsInsideOfLooker << 1,
	MatchObjectsInLookerInventory = MatchHereForLookerLocation << 1,
	MatchAgainstLookerLocationName = MatchObjectsInLookerInventory << 1,
	OnlyMatchObjectsInLookerInventory = MatchAgainstLookerLocationName << 1,
	MatchRemoteContents = OnlyMatchObjectsInLookerInventory << 1,
	MatchMeForLooker = MatchRemoteContents << 1,
	OnlyMatchObjectsInLookerLocation = MatchMeForLooker << 1,
	MatchObjectsInLookerLocation = OnlyMatchObjectsInLookerLocation << 1,
	MatchWildCardForPlayerName = MatchObjectsInLookerLocation << 1,
	MatchOptionalWildCardForPlayerName = MatchWildCardForPlayerName << 1,
	EnglishStyleMatching = MatchOptionalWildCardForPlayerName << 1,
	NoPartialMatches = EnglishStyleMatching << 1,
	OnlyMatchLookerControlledObjects = NoPartialMatches << 1,
	/// <summary>
	/// Skips the visibility check after locating the object. Used by functions like
	/// hasflag() that should work on any object the executor can reference by dbref,
	/// matching PennMUSH behavior.
	/// </summary>
	NoVisibilityCheck = OnlyMatchLookerControlledObjects << 1,

	All = (MatchMeForLooker | MatchHereForLookerLocation | AbsoluteMatch | MatchOptionalWildCardForPlayerName |
				 MatchObjectsInLookerLocation | MatchObjectsInLookerInventory | ExitsInTheRoomOfLooker | EnglishStyleMatching |
				 MatchRemoteContents)
}

public interface ILocateService
{
	ValueTask<AnyOptionalSharpObjectOrError> LocateAndNotifyIfInvalid(
		IMUSHCodeParser parser,
		AnySharpObject looker,
		AnySharpObject executor,
		string name,
		LocateFlags flags);

	ValueTask<AnySharpObjectOrErrorCallState> LocateAndNotifyIfInvalidWithCallState(
		IMUSHCodeParser parser,
		AnySharpObject looker,
		AnySharpObject executor,
		string name,
		LocateFlags flags);

	ValueTask<CallState> LocateAndNotifyIfInvalidWithCallStateFunction(
		IMUSHCodeParser parser,
		AnySharpObject looker,
		AnySharpObject executor,
		string name,
		LocateFlags flags,
		Func<AnySharpObject, ValueTask<CallState>> foundFunc);

	ValueTask<CallState> LocateAndNotifyIfInvalidWithCallStateFunction(
		IMUSHCodeParser parser,
		AnySharpObject looker,
		AnySharpObject executor,
		string name,
		LocateFlags flags,
		Func<AnySharpObject, CallState> foundFunc);

	ValueTask<AnyOptionalSharpObjectOrError> Locate(
		IMUSHCodeParser parser,
		AnySharpObject looker,
		AnySharpObject executor,
		string name,
		LocateFlags flags);

	ValueTask<AnyOptionalSharpObjectOrError> LocatePlayerAndNotifyIfInvalid(
		IMUSHCodeParser parser,
		AnySharpObject looker,
		AnySharpObject executor,
		string name);

	ValueTask<AnySharpObjectOrErrorCallState> LocatePlayerAndNotifyIfInvalidWithCallState(
		IMUSHCodeParser parser,
		AnySharpObject looker,
		AnySharpObject executor,
		string name);

	ValueTask<CallState> LocatePlayerAndNotifyIfInvalidWithCallStateFunction(
		IMUSHCodeParser parser,
		AnySharpObject looker,
		AnySharpObject executor,
		string name,
		Func<SharpPlayer, ValueTask<CallState>> foundFunc);

	ValueTask<AnyOptionalSharpObjectOrError> LocatePlayer(
		IMUSHCodeParser parser,
		AnySharpObject looker,
		AnySharpObject executor,
		string name);

	ValueTask<AnySharpContainer> Room(AnySharpObject content);
}