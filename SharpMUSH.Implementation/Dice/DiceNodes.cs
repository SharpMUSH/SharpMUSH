using System.Collections.Immutable;

namespace SharpMUSH.Implementation.Dice;

/// <summary>
/// A parsed roll formula. Names are resolved while parsing, so a tree that parsed holds no
/// unresolved names, and rolling it can only fail on the dice limit or a division by zero.
/// </summary>
public abstract record DiceNode;

public sealed record NumberNode(decimal Value) : DiceNode;

public sealed record GroupNode(DiceNode Inner) : DiceNode;

public sealed record NegateNode(DiceNode Operand) : DiceNode;

public sealed record BinaryNode(char Operator, DiceNode Left, DiceNode Right) : DiceNode;

public sealed record FunctionNode(string Name, ImmutableArray<DiceNode> Arguments) : DiceNode;

/// <summary>A named value; <paramref name="Value"/> is its number or its parsed formula.</summary>
public sealed record VariableNode(string Name, DiceNode Value) : DiceNode;

/// <summary>Foundry's flavor text, <c>5[fire]</c>, on a term that is not dice.</summary>
public sealed record FlavorNode(DiceNode Inner, string Flavor) : DiceNode;

public enum DieKind
{
	/// <summary><c>d6</c>, <c>d%</c>: faces 1 to the number of sides.</summary>
	Numbered,

	/// <summary><c>dF</c>: faces -1, 0 and 1.</summary>
	Fate,

	/// <summary><c>dc</c>: faces 0 (tails) and 1 (heads).</summary>
	Coin
}

/// <summary>
/// <c>&lt;count&gt;d&lt;sides&gt;&lt;modifiers&gt;[&lt;flavor&gt;]</c>. Only numbered dice have
/// <paramref name="Sides"/>.
/// </summary>
public sealed record DiceTermNode(
	string Formula,
	DiceNode Count,
	DiceNode? Sides,
	DieKind Kind,
	ImmutableArray<DiceModifier> Modifiers,
	string? Flavor) : DiceNode;

/// <summary>
/// <c>{&lt;formula&gt;, &lt;formula&gt;, ...}&lt;modifiers&gt;</c>: each formula's total is one
/// result, which the modifiers keep, drop or count as they would dice.
/// </summary>
public sealed record PoolNode(
	string Formula,
	ImmutableArray<DiceNode> Elements,
	string ModifierText,
	ImmutableArray<DiceModifier> Modifiers,
	string? Flavor) : DiceNode;

/// <summary>A parse failure, carried up the tree in place of a node; never rolled.</summary>
public sealed record InvalidNode(string Message) : DiceNode;

public enum DiceModifierKind
{
	KeepHighest,
	KeepLowest,
	DropHighest,
	DropLowest,
	Explode,
	ExplodeOnce,
	Reroll,
	RerollRecursive,
	CountSuccesses,
	CountFailures,
	CountEven,
	CountOdd,
	DeductFailures,
	SubtractFailures,
	MarginOfSuccess,
	Minimum,
	Maximum
}

public enum DiceComparison
{
	Equal,
	Greater,
	GreaterOrEqual,
	Less,
	LessOrEqual
}

/// <summary>
/// One modifier. <paramref name="Amount"/> is how many dice a keep or drop takes, the bound of
/// min and max, or the most explosions an <c>x</c> may add; <paramref name="Target"/> with
/// <paramref name="Comparison"/> is the faces an explode, reroll or count acts on. Either may be a
/// number or a bracketed formula (<c>cs&gt;=(DIFFICULTY)</c>), and is absent when not written, which
/// means that modifier's default.
/// </summary>
public sealed record DiceModifier(DiceModifierKind Kind, DiceNode? Amount, DiceComparison Comparison, DiceNode? Target)
{
	public static bool Matches(DiceComparison comparison, decimal target, decimal face) => comparison switch
	{
		DiceComparison.Equal => face == target,
		DiceComparison.Greater => face > target,
		DiceComparison.GreaterOrEqual => face >= target,
		DiceComparison.Less => face < target,
		_ => face <= target
	};
}
