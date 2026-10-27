using SharpMUSH.Implementation.Services;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// A typed command name is matched the way PennMUSH's prefix table matches it (src/ptab.c): the exact
/// name, else the one command the abbreviation begins. An abbreviation two commands share names neither,
/// so a lone <c>s</c> in a room with no such exit is <c>huh</c>, not an empty <c>say</c>.
/// </summary>
public class CommandAbbreviationTests
{
	private static (CommandDefinition, bool) Command(string name) => (new CommandDefinition(new SharpCommandAttribute
	{
		Name = name, Behavior = CommandBehavior.Default, MinArgs = 0, MaxArgs = 0
	}, _ => ValueTask.FromResult<Option<CallState>>(new CallState(name))), true);

	private static CommandTrie TrieOf(params string[] names)
	{
		var library = new CommandLibraryService();
		foreach (var name in names)
		{
			library.Add(name, Command(name));
		}

		return CommandTrie.For(library);
	}

	[Test]
	public async Task AnAbbreviationOneCommandBegins_NamesIt()
	{
		var trie = TrieOf("@TELEPORT", "SAY", "SCORE");

		await Assert.That(trie.FindAbbreviation("@tel")?.CommandName).IsEqualTo("@TELEPORT");
		await Assert.That(trie.FindAbbreviation("sa")?.CommandName).IsEqualTo("SAY");
	}

	[Test]
	public async Task AnAbbreviationTwoCommandsBegin_NamesNeither()
	{
		var trie = TrieOf("SAY", "SCORE", "SEMIPOSE");

		await Assert.That(trie.FindAbbreviation("s")).IsNull()
			.Because("PennMUSH's ptab_find returns nothing for a prefix that is not unique");
	}

	[Test]
	public async Task AnExactName_WinsOverLongerNamesItBegins()
	{
		var trie = TrieOf("W", "WHISPER", "WHO");

		await Assert.That(trie.FindAbbreviation("w")?.CommandName).IsEqualTo("W");
		await Assert.That(trie.FindAbbreviation("wh")).IsNull();
	}

	[Test]
	public async Task OneLetter_NamesOnlyACommandSpelledThatWay()
	{
		var trie = TrieOf("NEWS", "L", "LOOK", "LEAVE");

		await Assert.That(trie.FindAbbreviation("n")).IsNull()
			.Because("a lone direction letter with no exit is a walk, not NEWS");
		await Assert.That(trie.FindAbbreviation("ne")?.CommandName).IsEqualTo("NEWS");
		await Assert.That(trie.FindAbbreviation("l")?.CommandName).IsEqualTo("L");
	}
}
