using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// PennMUSH <c>match.c</c>'s <c>parse_english</c>, tested directly now that it is its own class.
/// The adjective masks are cleared rather than set, so a wrong bit never fails loudly — it
/// silently searches somewhere the adjective said not to. Only an assertion on the resulting flags
/// catches that.
/// </summary>
public class EnglishMatcherTests
{
	/// <summary>
	/// <c>my</c>/<c>me</c> is <c>MAT_NEIGHBOR | MAT_EXIT | MAT_CONTAINER | MAT_REMOTE_CONTENTS</c>
	/// cleared: the inventory scope it names is the one that must survive.
	/// </summary>
	[Test]
	[Arguments("my sword")]
	[Arguments("me sword")]
	public async Task MyNarrowsToTheInventoryScope(string typed)
	{
		var (remaining, flags, count) = EnglishMatcher.Parse(typed, LocateFlags.All);

		await Assert.That(remaining).IsEqualTo("sword");
		await Assert.That(count).IsEqualTo(0);
		await Assert.That(flags.HasFlag(LocateFlags.MatchObjectsInLookerInventory)).IsTrue()
			.Because("'my' selects the inventory scope, so it is the one bit that must not be cleared");
		await Assert.That(flags.HasFlag(LocateFlags.MatchObjectsInLookerLocation)).IsFalse()
			.Because("MAT_NEIGHBOR is cleared, or 'my sword' goes on searching the room");
		await Assert.That(flags.HasFlag(LocateFlags.ExitsInTheRoomOfLooker)).IsFalse();
		await Assert.That(flags.HasFlag(LocateFlags.MatchAgainstLookerLocationName)).IsFalse();
		await Assert.That(flags.HasFlag(LocateFlags.MatchRemoteContents)).IsFalse();
	}

	/// <summary>
	/// <c>toward</c> clears <c>MAT_NEIGHBOR | MAT_POSSESSION | MAT_CONTAINER | MAT_REMOTE_CONTENTS</c>
	/// — note which is absent: the exit scope is the one the adjective selects.
	/// </summary>
	[Test]
	public async Task TowardKeepsTheExitScopeItSelects()
	{
		var (remaining, flags, _) = EnglishMatcher.Parse("toward north", LocateFlags.All);

		await Assert.That(remaining).IsEqualTo("north");
		await Assert.That(flags.HasFlag(LocateFlags.ExitsInTheRoomOfLooker)).IsTrue()
			.Because("clearing the exit scope would leave 'toward' unable to match any exit at all");
		await Assert.That(flags.HasFlag(LocateFlags.MatchObjectsInLookerLocation)).IsFalse();
		await Assert.That(flags.HasFlag(LocateFlags.MatchObjectsInLookerInventory)).IsFalse();
	}

	/// <summary>
	/// <c>here</c>/<c>this</c> clear more than <c>this here</c> does: the two-word form keeps the
	/// container and remote-contents scopes that the one-word form drops.
	/// </summary>
	[Test]
	public async Task ThisHereAndHereClearDifferentMasks()
	{
		// MAT_CONTAINER and MAT_REMOTE_CONTENTS are outside LocateFlags.All exactly as they are
		// outside MAT_EVERYTHING, and they are the two bits the forms differ on - asking with All
		// alone would compare two zeroes and pass whichever way the masks went.
		const LocateFlags everything = LocateFlags.All
			| LocateFlags.MatchAgainstLookerLocationName | LocateFlags.MatchRemoteContents;
		var (thisHere, thisHereFlags, _) = EnglishMatcher.Parse("this here rock", everything);
		var (here, hereFlags, _) = EnglishMatcher.Parse("here rock", everything);

		await Assert.That(thisHere).IsEqualTo("rock");
		await Assert.That(here).IsEqualTo("rock");
		await Assert.That(thisHereFlags.HasFlag(LocateFlags.MatchAgainstLookerLocationName)).IsTrue()
			.Because("'this here' clears only MAT_POSSESSION | MAT_EXIT");
		await Assert.That(thisHereFlags.HasFlag(LocateFlags.MatchRemoteContents)).IsTrue();
		await Assert.That(hereFlags.HasFlag(LocateFlags.MatchAgainstLookerLocationName)).IsFalse()
			.Because("'here' also clears MAT_REMOTE_CONTENTS | MAT_CONTAINER");
		await Assert.That(hereFlags.HasFlag(LocateFlags.MatchRemoteContents)).IsFalse();
	}

	/// <summary>
	/// An adjective only applies when the scope it narrows is in play to begin with — Penn guards
	/// each branch on the corresponding <c>MAT_</c> bit, so with the scope absent the word is just
	/// part of the name.
	/// </summary>
	[Test]
	public async Task AnAdjectiveWhoseScopeIsNotSearchedIsPartOfTheName()
	{
		var (remaining, _, count) = EnglishMatcher.Parse("my sword", LocateFlags.MatchObjectsInLookerLocation);

		await Assert.That(remaining).IsEqualTo("my sword")
			.Because("without MAT_POSSESSION in the flags, 'my' is not an adjective here");
		await Assert.That(count).IsEqualTo(0);
	}

	/// <summary>
	/// <c>match.c:590</c> — an ordinal that does not agree with its number "wasn't really a count
	/// adjective. Reset and press on", which restores the name rather than consuming the token.
	/// The teen exception is the case a naive <c>%10</c> rule gets wrong.
	/// </summary>
	[Test]
	[Arguments("1st", true)]
	[Arguments("2nd", true)]
	[Arguments("3rd", true)]
	[Arguments("4th", true)]
	[Arguments("11th", true)]
	[Arguments("12th", true)]
	[Arguments("13th", true)]
	[Arguments("21st", true)]
	[Arguments("0th", false)]
	[Arguments("11st", false)]
	[Arguments("12nd", false)]
	[Arguments("13rd", false)]
	[Arguments("1nd", false)]
	public async Task OnlyAWellFormedOrdinalIsReadAsACount(string ordinal, bool valid)
	{
		var (remaining, _, count) = EnglishMatcher.Parse($"{ordinal} sword", LocateFlags.All);

		if (valid)
		{
			await Assert.That(remaining).IsEqualTo("sword");
			await Assert.That(count).IsEqualTo(int.Parse(ordinal[..^2]));
		}
		else
		{
			await Assert.That(remaining).IsEqualTo($"{ordinal} sword")
				.Because("a malformed ordinal leaves the name as typed, token included");
			await Assert.That(count).IsEqualTo(0);
		}
	}

	/// <summary>
	/// <c>match.c:560</c> — <c>mname = strchr(*name, ' '); if (!mname) return 0;</c>. A count with
	/// no noun after it is not a count adjective, and an object really named <c>2nd</c> stays
	/// findable.
	/// </summary>
	[Test]
	public async Task ACountWithNoNounAfterItIsJustAName()
	{
		var (remaining, _, count) = EnglishMatcher.Parse("2nd", LocateFlags.All);

		await Assert.That(remaining).IsEqualTo("2nd");
		await Assert.That(count).IsEqualTo(0);
	}

	/// <summary>
	/// <c>\d+</c> caps no length, and this path is reached by anything a player types. Penn's
	/// <c>strtoul</c> saturates and then fails the suffix test; declining to read it as a count is
	/// the same answer, and must not be an exception.
	/// </summary>
	[Test]
	public async Task AnUnrepresentableCountIsDeclinedRatherThanThrown()
	{
		var (remaining, _, count) = EnglishMatcher.Parse("99999999999999999999th thing", LocateFlags.All);

		await Assert.That(remaining).IsEqualTo("99999999999999999999th thing");
		await Assert.That(count).IsEqualTo(0);
	}

	/// <summary>
	/// An adjective that consumes the whole name leaves nothing to search for, so Penn restores
	/// both the name and the flags it started with.
	/// </summary>
	[Test]
	public async Task AnAdjectiveWithNoNounRestoresTheOriginalNameAndFlags()
	{
		var (remaining, flags, count) = EnglishMatcher.Parse("my ", LocateFlags.All);

		await Assert.That(remaining).IsEqualTo("my ");
		await Assert.That(flags).IsEqualTo(LocateFlags.All);
		await Assert.That(count).IsEqualTo(0);
	}
}
