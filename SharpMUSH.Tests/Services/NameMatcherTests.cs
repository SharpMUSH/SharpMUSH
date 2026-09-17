using SharpMUSH.Library.Services;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// PennMUSH <c>match.c</c>'s name rules, tested where they now live rather than through a whole
/// locate. Every case below was previously reachable only by standing up a database, a permission
/// service and a candidate list; the rules themselves touch none of those.
/// </summary>
public class NameMatcherTests
{
	private readonly TestObjectFactory _factory = new();

	/// <summary>
	/// <c>string_match</c> (<c>strutil.c</c>) is a prefix test against <em>any word</em> of the
	/// source, not just the first — most object names are more than one word, and testing only the
	/// leading one leaves every other word unreachable.
	/// </summary>
	[Test]
	[Arguments("Big Sword", "sword", true)]
	[Arguments("Big Sword", "big", true)]
	[Arguments("Big Sword", "bi", true)]
	[Arguments("Big Sword", "swo", true)]
	[Arguments("Big Sword", "word", false)]
	[Arguments("Big Sword", "", false)]
	[Arguments("Sword", "sword", true)]
	public async Task StringMatchIsAPrefixOfAnyWord(string source, string sub, bool expected)
		=> await Assert.That(NameMatcher.StringMatch(source, sub)).IsEqualTo(expected);

	/// <summary>
	/// The word separator is <c>isalnum</c>, not whitespace: <c>Myrddin's</c> advances past the
	/// apostrophe, so <c>s</c> begins a word and <c>splitting on spaces</c> is a different function.
	/// </summary>
	[Test]
	[Arguments("Myrddin's Hat", "s", true)]
	[Arguments("Myrddin's Hat", "hat", true)]
	[Arguments("red-hot poker", "hot", true)]
	public async Task StringMatchTreatsEveryNonAlphanumericAsAWordBreak(string source, string sub, bool expected)
		=> await Assert.That(NameMatcher.StringMatch(source, sub)).IsEqualTo(expected);

	/// <summary>
	/// A whole-name equality is <see cref="NameMatcher.MatchKind.Exact"/> whether partial matching
	/// was offered or not; an abbreviation is <see cref="NameMatcher.MatchKind.Partial"/> only when
	/// it was.
	/// </summary>
	[Test]
	public async Task ExactOutranksPartialAndPartialNeedsPermission()
	{
		var room = _factory.CreateRoom(1, "Room");
		var thing = _factory.CreateThing(2, "Big Sword", room);

		await Assert.That(NameMatcher.Classify(thing, "Big Sword", allowPartial: false))
			.IsEqualTo(NameMatcher.MatchKind.Exact);
		await Assert.That(NameMatcher.Classify(thing, "big sword", allowPartial: false))
			.IsEqualTo(NameMatcher.MatchKind.Exact)
			.Because("match.c compares names case-insensitively");
		await Assert.That(NameMatcher.Classify(thing, "sword", allowPartial: true))
			.IsEqualTo(NameMatcher.MatchKind.Partial);
		await Assert.That(NameMatcher.Classify(thing, "sword", allowPartial: false))
			.IsEqualTo(NameMatcher.MatchKind.None)
			.Because("NoPartialMatches must leave an abbreviation unmatched, not downgrade it");
	}

	/// <summary>
	/// <c>match_aliases</c> answers for players and exits and returns 0 for everything else, and it
	/// compares each <c>;</c>-separated entry for equality — there is no partial alias match
	/// anywhere in <c>match.c</c> (issue #794).
	/// </summary>
	[Test]
	public async Task AliasesMatchExactlyAndOnlyForPlayersAndExits()
	{
		var room = _factory.CreateRoom(3, "Alias Room");
		var player = _factory.CreatePlayer(4, "Wizard", ["Wiz", "Merlin"], room);
		var thing = _factory.CreateThing(5, "Statue", ["Stat"], room);

		await Assert.That(NameMatcher.Classify(player, "Wiz", allowPartial: false))
			.IsEqualTo(NameMatcher.MatchKind.Exact);
		await Assert.That(NameMatcher.Classify(player, "merlin", allowPartial: false))
			.IsEqualTo(NameMatcher.MatchKind.Exact);
		await Assert.That(NameMatcher.Classify(player, "Merl", allowPartial: true))
			.IsEqualTo(NameMatcher.MatchKind.None)
			.Because("an alias answers exactly or not at all - there is no partial alias branch, and "
				+ "'Merl' is no prefix of any word of the NAME 'Wizard'");
		await Assert.That(NameMatcher.Classify(player, "Wiz", allowPartial: true))
			.IsEqualTo(NameMatcher.MatchKind.Exact)
			.Because("'Wiz' is an alias outright, which is decided before the partial branch is reached");
		await Assert.That(NameMatcher.Classify(player, "Wizar", allowPartial: true))
			.IsEqualTo(NameMatcher.MatchKind.Partial)
			.Because("abbreviating a player is served by string_match over the player's NAME (#794)");
		await Assert.That(NameMatcher.Classify(thing, "Stat", allowPartial: false))
			.IsEqualTo(NameMatcher.MatchKind.None)
			.Because("match_aliases returns 0 for a thing, so a thing's aliases never answer");
	}

	/// <summary>
	/// <c>MATCH_LIST</c>'s partial branch is <c>string_match(Name(match), name)</c> and is never
	/// reached for an exit: an exit answers by its full name or by one of its aliases, never by
	/// abbreviation.
	/// </summary>
	[Test]
	public async Task AnExitIsNeverMatchedPartially()
	{
		var room = _factory.CreateRoom(6, "Exit Room");
		var exit = _factory.CreateExit(7, "North Gate", ["n", "north"], room);

		await Assert.That(NameMatcher.Classify(exit, "North Gate", allowPartial: true))
			.IsEqualTo(NameMatcher.MatchKind.Exact);
		await Assert.That(NameMatcher.Classify(exit, "n", allowPartial: true))
			.IsEqualTo(NameMatcher.MatchKind.Exact)
			.Because("'n' is an alias, and an alias is an exact match");
		await Assert.That(NameMatcher.Classify(exit, "gate", allowPartial: true))
			.IsEqualTo(NameMatcher.MatchKind.None)
			.Because("there is no partial branch for an exit in match.c");
	}
}
