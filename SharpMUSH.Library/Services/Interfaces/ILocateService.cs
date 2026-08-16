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
	/// <summary>
	/// fun_locate's <c>N</c> switch: <c>NOTYPE</c>, which is the *absence* of a type preference rather
	/// than a preference of its own. Nothing reads it — <see cref="LocateService.PreferredTypes"/>
	/// returning <see cref="SharpObjectTypes.None"/> is the same statement — but the switch is
	/// documented, so the letter has to land somewhere.
	/// </summary>
	NoTypePreference = 1,
	OnlyMatchTypePreference = NoTypePreference << 1,
	ExitsPreference = OnlyMatchTypePreference << 1,
	PreferLockPass = ExitsPreference << 1,
	PlayersPreference = PreferLockPass << 1,
	RoomsPreference = PlayersPreference << 1,
	ThingsPreference = RoomsPreference << 1,
	UseLastIfAmbiguous = ThingsPreference << 1,
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

	/// <summary>
	/// <c>MAT_GLOBAL</c> — search the Master Room's exits. Its own flag, and deliberately not part of
	/// <see cref="All"/>: <c>MAT_EVERYTHING</c> does not include it either, and the master-room scope
	/// used to be gated on <c>HasFlag(All)</c>, which is a different question.
	/// </summary>
	MatchGlobalExits = NoVisibilityCheck << 1,

	/// <summary>
	/// <c>MAT_EVERYTHING</c>, member for member. <c>MAT_CONTAINER</c>
	/// (<see cref="MatchAgainstLookerLocationName"/>), <c>MAT_CARRIED_EXIT</c>
	/// (<see cref="ExitsInsideOfLooker"/>), <c>MAT_REMOTES</c> (<see cref="MatchRemoteContents"/>) and
	/// <see cref="MatchGlobalExits"/> are all outside it; <c>fun_locate</c> adds the first two by hand
	/// when it injects a default, which is why they are not folded in here.
	/// </summary>
	All = MatchMeForLooker | MatchHereForLookerLocation | AbsoluteMatch | MatchWildCardForPlayerName |
				MatchObjectsInLookerLocation | MatchObjectsInLookerInventory | ExitsInTheRoomOfLooker |
				EnglishStyleMatching
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