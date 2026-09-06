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
		public volatile CommandTrie? Trie;

		/// <summary>The library's entry count when <see cref="Trie"/> was built.</summary>
		public int BuiltFromCount;
	}

	private static readonly ConditionalWeakTable<LibraryService<string, CommandDefinition>, CachedTrie> Cache = new();

	/// <summary>
	/// The trie for <paramref name="commandLibrary"/>, built on first use and shared until the
	/// library changes. Building walks every registered command and allocates a node per character,
	/// so it is not something to do per parse: rebuilding it on every parser copy was four fifths of
	/// all bytes allocated by a trivial evaluation.
	/// <para>
	/// Staleness is caught two ways. Any add or remove changes the library's count, which is compared
	/// on every lookup, so a command registered by whoever holds the library - a plugin, a test -
	/// is visible to the next parse without that code knowing a trie exists. A plugin reload
	/// removes and re-adds the same number of names, which a count cannot see; the plugin manager
	/// calls <see cref="Invalidate"/> for that.
	/// </para>
	/// </summary>
	public static CommandTrie For(LibraryService<string, CommandDefinition> commandLibrary)
	{
		var cached = Cache.GetValue(commandLibrary, static _ => new CachedTrie());
		var trie = cached.Trie;
		if (trie is not null && cached.BuiltFromCount == commandLibrary.Count)
		{
			return trie;
		}

		lock (cached)
		{
			if (cached.Trie is { } current && cached.BuiltFromCount == commandLibrary.Count)
			{
				return current;
			}

			var built = Build(commandLibrary);
			cached.BuiltFromCount = commandLibrary.Count;
			cached.Trie = built;
			return built;
		}
	}

	/// <summary>
	/// Discards the cached trie for <paramref name="commandLibrary"/>; the next lookup rebuilds it
	/// from the live library. Needed only where a change leaves the count unchanged.
	/// </summary>
	public static void Invalidate(LibraryService<string, CommandDefinition> commandLibrary)
	{
		if (Cache.TryGetValue(commandLibrary, out var cached))
		{
			// Under the build lock: a build that finished enumerating the library before this
			// invalidation but publishes after it would otherwise install a trie holding the removed
			// commands, with a count that matches the library.
			lock (cached)
			{
				cached.Trie = null;
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
			// in-game command trie: FindShortestMatch would otherwise abbreviate them for a logged-in
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
	/// Finds the shortest command name that starts with the given prefix.
	/// This implements PennMUSH-compatible command abbreviation where "@tel" matches "@teleport".
	/// </summary>
	/// <param name="prefix">The command prefix to search for (case-insensitive)</param>
	/// <returns>The shortest matching command definition, or null if no match found</returns>
	public (string CommandName, CommandDefinition Definition)? FindShortestMatch(string prefix)
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

		if (node.Command is CommandDefinition cmd)
			return (node.CommandName!, cmd);

		var queue = new Queue<TrieNode>();
		queue.Enqueue(node);

		while (queue.Count > 0)
		{
			var current = queue.Dequeue();

			if (current.Command is CommandDefinition cmd2)
				return (current.CommandName!, cmd2);

			foreach (var child in current.Children.OrderBy(kvp => kvp.Key))
			{
				queue.Enqueue(child.Value);
			}
		}

		return null;
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
