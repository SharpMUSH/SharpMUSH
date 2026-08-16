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

/// <remarks>
/// <b>Bit positions are explicit and fixed.</b> This enum is public in a package external plugins
/// reference (see the contract note in SharpMUSH.Library.csproj), so its numeric values are part of
/// that contract. Written as a <c>&lt;&lt; 1</c> chain, removing any member silently renumbered every
/// member below it — which is exactly what happened twice: <c>FailIfNotPreferred</c> (bit 7) and
/// <c>NoVisibilityCheck</c> (bit 25) were each dropped, shifting the tail down and leaving a plugin
/// built against 1.0.0 passing one flag and this server reading another.
///
/// Both slots stay reserved rather than reused, so every surviving member keeps the value it was
/// published with. Add new members at the first free bit; never renumber an existing one.
/// </remarks>
[Flags]
public enum LocateFlags
{
	/// <summary>
	/// fun_locate's <c>N</c> switch: <c>NOTYPE</c>, which is the *absence* of a type preference rather
	/// than a preference of its own. Nothing reads it — <see cref="LocateService.PreferredTypes"/>
	/// returning <see cref="SharpObjectTypes.None"/> is the same statement — but the switch is
	/// documented, so the letter has to land somewhere.
	/// </summary>
	NoTypePreference = 1 << 0,
	OnlyMatchTypePreference = 1 << 1,
	ExitsPreference = 1 << 2,
	PreferLockPass = 1 << 3,
	PlayersPreference = 1 << 4,
	RoomsPreference = 1 << 5,
	ThingsPreference = 1 << 6,
	// 1 << 7 — reserved, was FailIfNotPreferred: set by 'F' alongside OnlyMatchTypePreference and read
	// nowhere. Do not reuse.
	UseLastIfAmbiguous = 1 << 8,
	AbsoluteMatch = 1 << 9,
	ExitsInTheRoomOfLooker = 1 << 10,
	ExitsInsideOfLooker = 1 << 11,
	MatchHereForLookerLocation = 1 << 12,
	MatchObjectsInLookerInventory = 1 << 13,
	MatchAgainstLookerLocationName = 1 << 14,
	OnlyMatchObjectsInLookerInventory = 1 << 15,
	MatchRemoteContents = 1 << 16,
	MatchMeForLooker = 1 << 17,
	OnlyMatchObjectsInLookerLocation = 1 << 18,
	MatchObjectsInLookerLocation = 1 << 19,
	MatchWildCardForPlayerName = 1 << 20,
	MatchOptionalWildCardForPlayerName = 1 << 21,
	EnglishStyleMatching = 1 << 22,
	NoPartialMatches = 1 << 23,
	OnlyMatchLookerControlledObjects = 1 << 24,
	// 1 << 25 — reserved, was NoVisibilityCheck: it opted out of fun_locate's dark/can-examine gate,
	// which no longer runs for any caller but locate() itself. Do not reuse.

	/// <summary>
	/// <c>MAT_GLOBAL</c> — search the Master Room's exits. Its own flag, and deliberately not part of
	/// <see cref="All"/>: <c>MAT_EVERYTHING</c> does not include it either, and the master-room scope
	/// used to be gated on <c>HasFlag(All)</c>, which is a different question.
	/// </summary>
	MatchGlobalExits = 1 << 26,

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