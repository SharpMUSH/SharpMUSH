using SharpMUSH.Implementation.Services;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// The shared command trie is rebuilt when its library changes. A change that leaves the entry count
/// where it was must still be seen: a count read after a weakly consistent enumeration can include a
/// command the enumeration missed (#1251 review), and a remove plus an add is the same shape.
/// </summary>
public class CommandTrieCacheTests
{
	private static (CommandDefinition, bool) Command(string name, int maxArgs = 0) => (new CommandDefinition(new SharpCommandAttribute
	{
		Name = name, Behavior = CommandBehavior.Default, MinArgs = 0, MaxArgs = maxArgs
	}, _ => ValueTask.FromResult<Option<CallState>>(new CallState(name))), true);

	[Test]
	public async Task AChangeThatKeepsTheCountIsSeenByTheNextLookup()
	{
		var library = new CommandLibraryService();
		library.Add("@ZZALPHA", Command("@ZZALPHA"));
		await Assert.That(CommandTrie.For(library).FindExact("@ZZALPHA").HasValue).IsTrue();

		library.Remove("@ZZALPHA");
		library.Add("@ZZBETA", Command("@ZZBETA"));

		var trie = CommandTrie.For(library);
		await Assert.That(trie.FindExact("@ZZBETA").HasValue).IsTrue();
		await Assert.That(trie.FindExact("@ZZALPHA").HasValue).IsFalse();
	}

	[Test]
	public async Task ReplacingAnEntryIsSeenByTheNextLookup()
	{
		var library = new CommandLibraryService();
		library.Add("@ZZGAMMA", Command("@ZZGAMMA"));
		await Assert.That(CommandTrie.For(library).FindExact("@ZZGAMMA")!.Value.Attribute.MaxArgs).IsEqualTo(0);

		library["@ZZGAMMA"] = Command("@ZZGAMMA", maxArgs: 2);

		await Assert.That(CommandTrie.For(library).FindExact("@ZZGAMMA")!.Value.Attribute.MaxArgs).IsEqualTo(2);
	}
}
