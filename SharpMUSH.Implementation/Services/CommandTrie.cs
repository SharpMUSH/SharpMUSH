using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Services;
using System.Runtime.CompilerServices;

namespace SharpMUSH.Implementation.Services;

/// <summary>
/// A trie (prefix tree) data structure for efficient command name lookup with prefix matching.
/// Optimizes the command discovery process by allowing O(k) lookup time where k is the command name length,
/// instead of O(n) linear search through all commands.
/// </summary>
public class CommandTrie
{
	private class TrieNode
	{
		public Dictionary<char, TrieNode> Children { get; } = new();
		public CommandDefinition? Command { get; set; }
		public string? CommandName { get; set; }
	}

	private readonly TrieNode _root = new();

	/// <summary>
	/// One built trie per command library. The library is the unit of identity: every parser
	/// instance derived from the same DI singleton shares its library, so they share its trie.
	/// </summary>
	private sealed class CachedTrie
	{
		/// <summary>Published as one object so a lookup never pairs one build's trie with another's version.</summary>
		public volatile Built? Current;
	}

	/// <param name="Version">The library's <see cref="LibraryService{TKey,TValue}.Version"/>, read before the build.</param>
	private sealed record Built(CommandTrie Trie, long Version);

	private static readonly ConditionalWeakTable<LibraryService<string, CommandDefinition>, CachedTrie> Cache = new();

	/// <summary>
	/// The trie for <paramref name="commandLibrary"/>, built on first use and shared until the
	/// library changes. Building walks every registered command and allocates a node per character,
	/// so it is not something to do per parse: rebuilding it on every parser copy was four fifths of
	/// all bytes allocated by a trivial evaluation.
	/// <para>
	/// Any add, replace or remove bumps the library's version, which is compared on every lookup, so a
	/// command registered by whoever holds the library - a plugin, a test - is visible to the next
	/// parse without that code knowing a trie exists. The version is read before the build: the
	/// library's enumeration is not a snapshot, so a change made during it may be missed, and a version
	/// read afterwards would vouch for a trie that lacks it.
	/// </para>
	/// </summary>
	public static CommandTrie For(LibraryService<string, CommandDefinition> commandLibrary)
	{
		var cached = Cache.GetValue(commandLibrary, static _ => new CachedTrie());
		if (cached.Current is { } built && built.Version == commandLibrary.Version)
		{
			return built.Trie;
		}

		lock (cached)
		{
			var version = commandLibrary.Version;
			if (cached.Current is { } current && current.Version == version)
			{
				return current.Trie;
			}

			// A change landing during the build bumps the version past the one recorded here, so the
			// next lookup rebuilds rather than trusting a trie the enumeration may have built without it.
			var trie = Build(commandLibrary);
			cached.Current = new Built(trie, version);
			return trie;
		}
	}

	/// <summary>
	/// Discards the cached trie for <paramref name="commandLibrary"/>; the next lookup rebuilds it
	/// from the live library. Changes made through the library are already seen by version; this is for
	/// callers that want the next lookup rebuilt regardless.
	/// </summary>
	public static void Invalidate(LibraryService<string, CommandDefinition> commandLibrary)
	{
		if (Cache.TryGetValue(commandLibrary, out var cached))
		{
			// Under the build lock, so a build already under way cannot publish over the invalidation.
			lock (cached)
			{
				cached.Current = null;
			}
		}
	}

	private static CommandTrie Build(LibraryService<string, CommandDefinition> commandLibrary)
	{
		var trie = new CommandTrie();

		foreach (var (commandName, commandInfo) in commandLibrary)
		{
			// SOCKET commands (CONNECT/WHO/QUIT/REGISTER/LOGIN/MAKE/PLAY) are dispatched exclusively
			// by the dedicated pre-login SOCKET blocks in the visitor (exact match for any Handle,
			// unambiguous-prefix abbreviation only while pre-login). They must NOT enter the general
			// in-game command trie: FindAbbreviation would otherwise abbreviate them for a logged-in
			// player (e.g. bare "q" -> QUIT), silently disconnecting them. The trie is only ever
			// consulted post-login, so SOCKET commands never belong here.
			if (commandInfo.IsSystem
					&& !commandInfo.LibraryInformation.Attribute.Behavior.HasFlag(CommandBehavior.SOCKET))
			{
				trie.Add(commandName, commandInfo.LibraryInformation);
			}
		}

		return trie;
	}

	/// <summary>
	/// Adds a command to the trie.
	/// </summary>
	/// <param name="commandName">The command name (case-insensitive)</param>
	/// <param name="definition">The command definition</param>
	public void Add(string commandName, CommandDefinition definition)
	{
		var node = _root;
		var lowerName = commandName.ToLowerInvariant();

		foreach (var ch in lowerName)
		{
			if (!node.Children.TryGetValue(ch, out var child))
			{
				child = new TrieNode();
				node.Children[ch] = child;
			}
			node = child;
		}

		node.Command = definition;
		node.CommandName = commandName;
	}

	/// <summary>
	/// The command <paramref name="prefix"/> names: the command spelled exactly that way, else the only
	/// command that begins with it. A prefix two or more commands begin with names none of them, so it
	/// falls through to exits, <c>$</c>-commands and <c>huh</c>.
	/// <para>This is PennMUSH's <c>ptab_find</c> (src/ptab.c), which every typed command goes through:
	/// <c>@tel</c> is <c>@teleport</c> while nothing else begins <c>@tel</c>, a lone <c>s</c> is not
	/// <c>say</c> (<c>score</c>, <c>semipose</c> and <c>session</c> begin with it too), and adding a command
	/// can take an abbreviation away from another, as it does in PennMUSH.</para>
	/// </summary>
	/// <param name="prefix">The command name or abbreviation (case-insensitive)</param>
	/// <returns>The command it names, or null when it names none or more than one</returns>
	public (string CommandName, CommandDefinition Definition)? FindAbbreviation(string prefix)
	{
		if (string.IsNullOrEmpty(prefix))
			return null;

		var node = _root;
		var lowerPrefix = prefix.ToLowerInvariant();

		foreach (var ch in lowerPrefix)
		{
			if (!node.Children.TryGetValue(ch, out node))
				return null;
		}

		if (node.Command is CommandDefinition exact)
			return (node.CommandName!, exact);

		return SoleCommandUnder(node);
	}

	/// <summary>The one command in <paramref name="node"/>'s subtree, or null when it holds none or several.</summary>
	private static (string CommandName, CommandDefinition Definition)? SoleCommandUnder(TrieNode node)
	{
		(string, CommandDefinition)? found = null;
		var pending = new Stack<TrieNode>();
		pending.Push(node);

		while (pending.Count > 0)
		{
			var current = pending.Pop();

			if (current.Command is CommandDefinition command)
			{
				if (found is not null)
					return null;
				found = (current.CommandName!, command);
			}

			foreach (var child in current.Children.Values)
			{
				pending.Push(child);
			}
		}

		return found;
	}

	/// <summary>
	/// Finds an exact command match (no prefix matching).
	/// </summary>
	/// <param name="commandName">The exact command name to find</param>
	/// <returns>The command definition if found, or null if not found</returns>
	public CommandDefinition? FindExact(string commandName)
	{
		if (string.IsNullOrEmpty(commandName))
			return null;

		var node = _root;
		var lowerName = commandName.ToLowerInvariant();

		foreach (var ch in lowerName)
		{
			if (!node.Children.TryGetValue(ch, out node))
				return null;
		}

		return node.Command;
	}

	/// <summary>
	/// Gets all commands that start with the given prefix.
	/// </summary>
	/// <param name="prefix">The prefix to search for (case-insensitive)</param>
	/// <returns>All commands that match the prefix</returns>
	public List<(string CommandName, CommandDefinition Definition)> FindAllMatches(string prefix)
	{
		var results = new List<(string, CommandDefinition)>();

		if (string.IsNullOrEmpty(prefix))
			return results;

		var node = _root;
		var lowerPrefix = prefix.ToLowerInvariant();

		foreach (var ch in lowerPrefix)
		{
			if (!node.Children.TryGetValue(ch, out node))
				return results;
		}

		CollectAllCommands(node, results);

		return results;
	}

	private void CollectAllCommands(TrieNode node, List<(string, CommandDefinition)> results)
	{
		if (node.Command is CommandDefinition cmd && node.CommandName is string name)
		{
			results.Add((name, cmd));
		}

		foreach (var child in node.Children.Values)
		{
			CollectAllCommands(child, results);
		}
	}
}
