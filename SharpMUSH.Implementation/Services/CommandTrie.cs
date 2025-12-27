using SharpMUSH.Library.Definitions;

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

		// Navigate to the prefix node
		foreach (var ch in lowerPrefix)
		{
			if (!node.Children.TryGetValue(ch, out node))
				return null; // Prefix not found
		}

		// If the prefix itself is a complete command, return it
		if (node.Command is CommandDefinition cmd)
			return (node.CommandName!, cmd);

		// BFS to find the shortest command with this prefix
		var queue = new Queue<TrieNode>();
		queue.Enqueue(node);

		while (queue.Count > 0)
		{
			var current = queue.Dequeue();

			if (current.Command is CommandDefinition cmd2)
				return (current.CommandName!, cmd2);

			// Sort children by key to ensure consistent ordering
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

		// Navigate to the prefix node
		foreach (var ch in lowerPrefix)
		{
			if (!node.Children.TryGetValue(ch, out node))
				return results; // Prefix not found
		}

		// Collect all commands in this subtree
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
