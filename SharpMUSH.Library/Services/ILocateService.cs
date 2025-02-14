using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Library.Services;

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
	All = (MatchMeForLooker | MatchHereForLookerLocation | AbsoluteMatch | MatchOptionalWildCardForPlayerName | MatchObjectsInLookerLocation | MatchObjectsInLookerInventory | ExitsInTheRoomOfLooker | EnglishStyleMatching)
}

public interface ILocateService
{
	public ValueTask<AnyOptionalSharpObjectOrError> LocateAndNotifyIfInvalid(
		IMUSHCodeParser parser,
		AnySharpObject looker,
		AnySharpObject executor,
		string name,
		LocateFlags flags);

	public ValueTask<AnyOptionalSharpObjectOrError> Locate(
		IMUSHCodeParser parser,
		AnySharpObject looker,
		AnySharpObject executor,
		string name,
		LocateFlags flags);

	public ValueTask<AnySharpContainer> Room(AnySharpObject content);
}