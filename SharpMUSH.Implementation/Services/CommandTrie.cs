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
