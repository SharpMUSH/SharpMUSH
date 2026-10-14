using System.Collections.Immutable;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Utilities;

namespace SharpMUSH.Implementation.Dice;

/// <summary>
/// Rolls a parsed formula. Modifiers apply in the order they are written, as Foundry applies them,
/// and every die rolled counts against <see cref="DiceRoll.MaxDice"/>. The first failure stops the
/// roll; the rest of the tree is walked without rolling anything.
/// </summary>
internal sealed class DiceEvaluator(Func<int, int> roll, DiceRollMode mode)
{
	private readonly ImmutableArray<DiceTermResult>.Builder _terms = ImmutableArray.CreateBuilder<DiceTermResult>();
	private int _rolled;
	private string? _failure;

	public Result<DiceRollResult> Evaluate(string formula, DiceNode tree)
	{
		var result = Evaluate(tree);
		return _failure is { } failure
			? new Error<string>(failure)
			: new DiceRollResult(formula, result.Value, $"{result.Text} = {Number(result.Value)}", _terms.ToImmutable());
	}

	private Evaluated Evaluate(DiceNode node)
	{
		if (_failure is not null) return Evaluated.Failed;

		return node switch
		{
			NumberNode number => new Evaluated(number.Value, Number(number.Value)),
			GroupNode group => Group(Evaluate(group.Inner)),
			NegateNode negate => Negate(Evaluate(negate.Operand)),
			BinaryNode binary => Binary(binary.Operator, Evaluate(binary.Left), Evaluate(binary.Right)),
			FunctionNode function => Function(function.Name, [.. function.Arguments.Select(Evaluate)]),
			VariableNode variable => Variable(variable.Name, Evaluate(variable.Value)),
			FlavorNode flavored => Flavored(Evaluate(flavored.Inner), flavored.Flavor),
			DiceTermNode term => Term(term),
			PoolNode pool => Pool(pool),
			_ => Fail(ErrorMessages.Returns.Error)
		};
	}

	private static Evaluated Group(Evaluated inner) => new(inner.Value, $"({inner.Text})");

	private static Evaluated Negate(Evaluated operand) => new(-operand.Value, $"-{operand.Text}");

	private static Evaluated Variable(string name, Evaluated value) => new(value.Value, $"{name}({value.Text})");

	private static Evaluated Flavored(Evaluated inner, string? flavor)
		=> string.IsNullOrEmpty(flavor) ? inner : inner with { Text = $"{inner.Text} {flavor}" };

	private Evaluated Binary(char op, Evaluated left, Evaluated right)
	{
		if (_failure is not null) return Evaluated.Failed;
		if (op is '/' or '%' && right.Value == 0) return Fail(ErrorMessages.Returns.DivisionByZero);

		var text = $"{left.Text} {op} {right.Text}";
		try
		{
			return new Evaluated(op switch
			{
				'+' => left.Value + right.Value,
				'-' => left.Value - right.Value,
				'*' => left.Value * right.Value,
				'/' => left.Value / right.Value,
				_ => left.Value % right.Value
			}, text);
		}
		catch (OverflowException)
		{
			return Fail(ErrorMessages.Returns.NumberOutOfRange);
		}
	}

	private Evaluated Function(string name, ImmutableArray<Evaluated> arguments)
	{
		if (_failure is not null) return Evaluated.Failed;

		var first = arguments[0].Value;
		var value = name switch
		{
			"floor" => Math.Floor(first),
			"ceil" => Math.Ceiling(first),
			// JavaScript's Math.round, which Foundry uses: halves go up, so -2.5 is -2.
			"round" => Math.Floor(first + 0.5m),
			"abs" => Math.Abs(first),
			"min" => arguments.Min(argument => argument.Value),
			_ => arguments.Max(argument => argument.Value)
		};
		return new Evaluated(value, $"{name}({string.Join(", ", arguments.Select(argument => argument.Text))})");
	}

	private Evaluated Term(DiceTermNode term)
	{
		var count = Evaluate(term.Count);
		var sides = term.Sides is { } sidesNode ? Evaluate(sidesNode) : new Evaluated(0, string.Empty);
		if (_failure is not null) return Evaluated.Failed;

		var dice = Math.Floor(count.Value);
		var faces = Math.Floor(sides.Value);
		if (dice < 0) return Fail(ErrorMessages.Returns.NumberOutOfRange);
		if (dice > DiceRoll.MaxDice) return Fail(ErrorMessages.Returns.RollTooManyDice);
		if (term.Kind == DieKind.Numbered && faces is < 1 or > DiceRoll.MaxSides) return Fail(ErrorMessages.Returns.RollSidesOutOfRange);

		var die = new DieShape(term.Kind, (int)faces);
		var results = new ResultSet(this, die);
		for (var i = 0; i < dice; i++) results.Add(RollOne(die), 0);
		var total = results.Apply(term.Modifiers);
		if (_failure is not null) return Evaluated.Failed;

		var rolled = results.ToResults();
		_terms.Add(new DiceTermResult(term.Formula, die.Description, term.Flavor, total, rolled));
		return Flavored(new Evaluated(total, $"{term.Formula}[{string.Join(", ", rolled.Select(result => result.Text))}]"), term.Flavor);
	}

	private Evaluated Pool(PoolNode pool)
	{
		var elements = pool.Elements.Select(Evaluate).ToList();
		if (_failure is not null) return Evaluated.Failed;

		var results = new ResultSet(this, DieShape.Pool(elements.Select(element => element.Value)));
		foreach (var element in elements) results.Add(element.Value, 0);
		var total = results.Apply(pool.Modifiers);
		if (_failure is not null) return Evaluated.Failed;

		var rolled = results.ToResults();
		_terms.Add(new DiceTermResult(pool.Formula, DieShape.PoolDescription, pool.Flavor, total, rolled));
		var text = $"{{{string.Join(", ", elements.Select(element => element.Text))}}}{pool.ModifierText}"
			+ $"[{string.Join(", ", rolled.Select(result => result.Text))}]";
		return Flavored(new Evaluated(total, text), pool.Flavor);
	}

	/// <summary>One die, or a zero once the roll has failed.</summary>
	private decimal RollOne(DieShape die)
	{
		if (++_rolled > DiceRoll.MaxDice)
		{
			Fail(ErrorMessages.Returns.RollTooManyDice);
			return 0;
		}

		return mode switch
		{
			DiceRollMode.Minimize => die.Lowest,
			DiceRollMode.Maximize => die.Highest,
			_ => die.Kind switch
			{
				DieKind.Fate => roll(3) - 2,
				DieKind.Coin => roll(2) - 1,
				_ => roll(die.Sides)
			}
		};
	}

	/// <summary>A modifier's number or bracketed formula, rolled now.</summary>
	private decimal? Value(DiceNode? node) => node is null ? null : Evaluate(node).Value;

	private Evaluated Fail(string message)
	{
		_failure ??= message;
		return Evaluated.Failed;
	}

	private static string Number(decimal value) => MushNumber.Unparse(value);

	private readonly record struct Evaluated(decimal Value, string Text)
	{
		public static Evaluated Failed => new(0, string.Empty);
	}

	/// <summary>What a die can show: the faces modifiers default to, and how JSON names it.</summary>
	private readonly record struct DieShape(DieKind Kind, int Sides, decimal Lowest, decimal Highest, bool IsPool)
	{
		public const string PoolDescription = "pool";

		public DieShape(DieKind kind, int sides)
			: this(kind, sides,
				kind switch { DieKind.Fate => -1, DieKind.Coin => 0, _ => 1 },
				kind switch { DieKind.Fate => 1, DieKind.Coin => 1, _ => sides },
				false)
		{
		}

		public static DieShape Pool(IEnumerable<decimal> values)
		{
			var list = values.ToList();
			return new DieShape(DieKind.Numbered, 0, list.Count > 0 ? list.Min() : 0, list.Count > 0 ? list.Max() : 0, true);
		}

		/// <summary><c>6</c> for a d6, <c>F</c> for fate, <c>C</c> for a coin, <c>pool</c> for a pool.</summary>
		public string Description => IsPool
			? PoolDescription
			: Kind switch { DieKind.Fate => "F", DieKind.Coin => "C", _ => Sides.ToString(System.Globalization.CultureInfo.InvariantCulture) };
	}

	/// <summary>The results of one term, as the modifiers leave them.</summary>
	private sealed class ResultSet(DiceEvaluator evaluator, DieShape shape)
	{
		private readonly List<Die> _dice = [];
		private readonly List<(DiceComparison Comparison, decimal Target)> _margins = [];

		public void Add(decimal value, int depth) => _dice.Add(new Die(value, depth));

		public decimal Apply(ImmutableArray<DiceModifier> modifiers)
		{
			foreach (var modifier in modifiers)
			{
				if (evaluator._failure is not null) break;
				Apply(modifier);
			}

			var total = Active().Sum(die => die.Count ?? die.Value);
			foreach (var (comparison, target) in _margins)
			{
				total = comparison is DiceComparison.Less or DiceComparison.LessOrEqual ? target - total : total - target;
			}
			return total;
		}

		public ImmutableArray<DieResult> ToResults() => [.. _dice.Select(die => die.ToResult())];

		private void Apply(DiceModifier modifier)
		{
			var amount = evaluator.Value(modifier.Amount);
			var target = evaluator.Value(modifier.Target);
			if (evaluator._failure is not null) return;

			bool Matches(Die die, decimal fallback) => DiceModifier.Matches(modifier.Comparison, target ?? fallback, die.Value);

			switch (modifier.Kind)
			{
				case DiceModifierKind.KeepHighest:
				case DiceModifierKind.KeepLowest:
				case DiceModifierKind.DropHighest:
				case DiceModifierKind.DropLowest:
					KeepOrDrop(modifier.Kind, (int)Math.Max(0, Math.Min(DiceRoll.MaxDice, Math.Floor(amount ?? 1))));
					break;
				case DiceModifierKind.Explode:
				case DiceModifierKind.ExplodeOnce:
					// A range (minimize, maximize) is the dice as first rolled; explosions would run away.
					if (evaluator.Rolling) Explode(die => Matches(die, shape.Highest), modifier.Kind == DiceModifierKind.ExplodeOnce, amount);
					break;
				case DiceModifierKind.Reroll:
				case DiceModifierKind.RerollRecursive:
					if (evaluator.Rolling) Reroll(die => Matches(die, shape.Lowest), modifier.Kind == DiceModifierKind.RerollRecursive);
					break;
				case DiceModifierKind.CountSuccesses:
					CountAs(die => Matches(die, shape.Highest), success: true);
					break;
				case DiceModifierKind.CountFailures:
					CountAs(die => Matches(die, shape.Lowest), success: false);
					break;
				case DiceModifierKind.CountEven:
					CountAs(die => decimal.Remainder(die.Value, 2) == 0, success: true);
					break;
				case DiceModifierKind.CountOdd:
					CountAs(die => Math.Abs(decimal.Remainder(die.Value, 2)) == 1, success: true);
					break;
				case DiceModifierKind.DeductFailures:
					foreach (var die in Active().Where(die => Matches(die, shape.Lowest)))
					{
						die.Failure = true;
						die.Count = -1;
					}
					break;
				case DiceModifierKind.SubtractFailures:
					foreach (var die in Active().Where(die => Matches(die, shape.Lowest)))
					{
						die.Failure = true;
						die.Count = -die.Value;
					}
					break;
				case DiceModifierKind.MarginOfSuccess:
					if (target is { } margin) _margins.Add((modifier.Comparison, margin));
					break;
				case DiceModifierKind.Minimum when amount is { } minimum:
					foreach (var die in Active().Where(die => die.Value < minimum)) die.Value = minimum;
					break;
				case DiceModifierKind.Maximum when amount is { } maximum:
					foreach (var die in Active().Where(die => die.Value > maximum)) die.Value = maximum;
					break;
			}
		}

		private IEnumerable<Die> Active() => _dice.Where(die => die.Active);

		/// <summary>Each active die counts 1 when it matches and 0 when it does not.</summary>
		private void CountAs(Func<Die, bool> matches, bool success)
		{
			foreach (var die in Active())
			{
				var hit = matches(die);
				if (success) die.Success = hit;
				else die.Failure = hit;
				die.Count = hit ? 1 : 0;
			}
		}

		/// <summary>
		/// Keeping more results than there are keeps them all, and dropping more drops them all: kh3 on
		/// two dice is the two dice.
		/// </summary>
		private void KeepOrDrop(DiceModifierKind kind, int amount)
		{
			var ascending = Active().OrderBy(die => die.Value).ToList();
			var discarded = kind switch
			{
				DiceModifierKind.KeepHighest => ascending.Take(Math.Max(0, ascending.Count - amount)),
				DiceModifierKind.KeepLowest => ascending.Skip(Math.Min(amount, ascending.Count)),
				DiceModifierKind.DropHighest => ascending.Skip(Math.Max(0, ascending.Count - amount)),
				_ => ascending.Take(amount)
			};
			foreach (var die in discarded)
			{
				die.Active = false;
				die.Dropped = true;
			}
		}

		/// <summary>
		/// Each matching die adds another, placed after it. <paramref name="once"/> (<c>xo</c>) lets only
		/// the dice already there explode; otherwise added dice explode too, up to
		/// <see cref="DiceRoll.MaxRepeats"/> in a row. <paramref name="limit"/> (<c>x5=10</c>) caps how many
		/// dice the modifier adds.
		/// </summary>
		private void Explode(Func<Die, bool> matches, bool once, decimal? limit)
		{
			var existing = _dice.ToHashSet();
			var added = 0;
			for (var i = 0; i < _dice.Count && evaluator._failure is null; i++)
			{
				if (limit is { } most && added >= most) break;
				var die = _dice[i];
				if (!die.Active || die.Exploded || !matches(die)) continue;
				if (once ? !existing.Contains(die) : die.Depth >= DiceRoll.MaxRepeats) continue;

				die.Exploded = true;
				_dice.Insert(i + 1, new Die(evaluator.RollOne(shape), die.Depth + 1));
				added++;
			}
		}

		/// <summary>
		/// Each matching die is replaced by a new one placed after it. <c>r</c> rerolls once; <c>rr</c>
		/// rerolls the new die too while it matches, up to <see cref="DiceRoll.MaxRepeats"/> times.
		/// </summary>
		private void Reroll(Func<Die, bool> matches, bool recursive)
		{
			var existing = _dice.ToHashSet();
			for (var i = 0; i < _dice.Count && evaluator._failure is null; i++)
			{
				var die = _dice[i];
				if (!die.Active || !matches(die)) continue;
				if (recursive ? die.Depth >= DiceRoll.MaxRepeats : !existing.Contains(die)) continue;

				die.Active = false;
				die.Rerolled = true;
				_dice.Insert(i + 1, new Die(evaluator.RollOne(shape), die.Depth + 1));
			}
		}
	}

	private bool Rolling => mode == DiceRollMode.Random;

	private sealed class Die(decimal value, int depth)
	{
		public decimal Value { get; set; } = value;
		public int Depth { get; } = depth;

		/// <summary>What the die adds to the total when a count modifier has recast it.</summary>
		public decimal? Count { get; set; }

		public bool Active { get; set; } = true;
		public bool Dropped { get; set; }
		public bool Rerolled { get; set; }
		public bool Exploded { get; set; }
		public bool Success { get; set; }
		public bool Failure { get; set; }

		public DieResult ToResult() => new(Value, Active, Dropped, Rerolled, Exploded, Success, Failure);
	}
}
