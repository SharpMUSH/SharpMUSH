using Antlr4.Runtime;
using Antlr4.Runtime.Tree;

#nullable disable

// The parts of the softcode parser's C# bridge that do not depend on the grammar. The rest, one
// interface per family of context copies and the visitor that reads them, is generated with the
// grammar into SharpMUSHParser.Contexts.cs; see docs/design/softcode-grammar.md.
public partial class SharpMUSHParser
{
	/// <summary>A rule of the softcode grammar, whichever context copy of it was parsed.</summary>
	public interface ISoftcodeContext : IRuleNode
	{
		IToken Start { get; }
		IToken Stop { get; }
		int Depth();
		bool IsEmpty { get; }
	}

	/// <summary>A paren group, which <see cref="ExitRule"/> splices into its parent.</summary>
	public interface IParenGroupContext : ISoftcodeContext;

	/// <summary>
	/// Whether the code was written with paren_groups on, which picks the <c>__G</c> entry rules: a bare
	/// <c>(</c> then opens a group whose <c>,</c>, <c>=</c> and <c>;</c> are text.
	/// </summary>
	public bool ParenGroups { get; init; }

	/// <summary>
	/// Splices a paren group into its parent as it exits, so its text reads as the run of text it is
	/// part of, as it did when groups were a counter rather than a rule.
	/// </summary>
	public override void ExitRule()
	{
		var exiting = Context;
		base.ExitRule();
		if (exiting is not IParenGroupContext || !BuildParseTree
			|| Context is not { children: { Count: > 0 } siblings } parent
			|| !ReferenceEquals(siblings[^1], exiting))
		{
			return;
		}

		siblings.RemoveAt(siblings.Count - 1);
		foreach (var child in exiting.children ?? [])
		{
			switch (child)
			{
				case RuleContext rule: rule.Parent = parent; break;
				case TerminalNodeImpl terminal: terminal.Parent = parent; break;
			}

			siblings.Add(child);
		}
	}
}

/// <summary>The children of a softcode rule by type, for the accessors every copy shares.</summary>
internal static class SoftcodeTree
{
	public static T Rule<T>(IParseTree node, int index) where T : class
	{
		for (var i = 0; i < node.ChildCount; i++)
		{
			if (node.GetChild(i) is T match && index-- == 0)
			{
				return match;
			}
		}

		return null;
	}

	public static T[] Rules<T>(IParseTree node) where T : class
	{
		var matches = new List<T>();
		for (var i = 0; i < node.ChildCount; i++)
		{
			if (node.GetChild(i) is T match)
			{
				matches.Add(match);
			}
		}

		return [.. matches];
	}

	public static ITerminalNode Token(IParseTree node, int type, int index)
	{
		for (var i = 0; i < node.ChildCount; i++)
		{
			if (node.GetChild(i) is ITerminalNode terminal && terminal.Symbol.Type == type && index-- == 0)
			{
				return terminal;
			}
		}

		return null;
	}

	public static ITerminalNode[] Tokens(IParseTree node, int type)
	{
		var matches = new List<ITerminalNode>();
		for (var i = 0; i < node.ChildCount; i++)
		{
			if (node.GetChild(i) is ITerminalNode terminal && terminal.Symbol.Type == type)
			{
				matches.Add(terminal);
			}
		}

		return [.. matches];
	}
}
