using System.Collections.Immutable;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Utilities;

namespace SharpMUSH.Implementation.Dice;

/// <summary>
/// Rolls a parsed formula. Modifiers apply in the order they are written,
/// and every die rolled counts against <see cref="DiceRoll.MaxDice"/>. The first failure stops the
/// roll; the rest of the tree is walked without rolling anything.
///
/// <para>Minimize and maximize work out the formula's lowest and highest totals rather than rolling:
/// each node yields the pair of results that reach its lowest and highest value, so subtraction takes
/// the right side's highest for the lowest total, and a product takes whichever corner is extreme.
/// A dice term tries every die on the same face, for each face where a modifier can change its mind.</para>
/// </summary>
internal sealed class DiceEvaluator(Func<int, int> roll, DiceRollMode mode)
{
	/// <summary>How many arrangements of one term or pool a range may try.</summary>
	private const int MaxRangeCases = 4096;

	private int _rolled;
	private int _cases;
	private string? _failure;

	public Result<DiceRollResult> Evaluate(string formula, DiceNode tree)
	{
		var result = mode switch
		{
			DiceRollMode.Minimize => Range(tree).Low,
			DiceRollMode.Maximize => Range(tree).High,
			_ => Evaluate(tree)
		};
		return _failure is { } failure
			? new Error<string>(failure)
			: new DiceRollResult(formula, result.Value, $"{result.Text} = {Number(result.Value)}", result.Terms);
	}

	private Evaluated Evaluate(DiceNode node)
	{
		if (_failure is not null) return Evaluated.Failed;

		return node switch
		{
			NumberNode number => Evaluated.Of(number.Value),
			GroupNode group => Group(Evaluate(group.Inner)),
			NegateNode negate => Negate(Evaluate(negate.Operand)),
			BinaryNode binary => Chain(binary),
			FunctionNode { Name: "match" } match => Match(match),
			FunctionNode function => Function(function.Name, [.. function.Arguments.Select(Evaluate)]),
			VariableNode variable => Variable(variable.Name, Evaluate(variable.Value)),
			FlavorNode flavored => Flavored(Evaluate(flavored.Inner), flavored.Flavor),
			DiceTermNode term => Term(term),
			PoolNode pool => Pool(pool),
			_ => Fail(ErrorMessages.Returns.Error)
		};
	}

	private Span Range(DiceNode node)
	{
		if (_failure is not null) return Span.Failed;

		return node switch
		{
			NumberNode number => Span.Point(Evaluated.Of(number.Value)),
			GroupNode group => Range(group.Inner).Map(Group),
			NegateNode negate => NegateRange(Range(negate.Operand)),
			BinaryNode binary => ChainRange(binary),
			FunctionNode { Name: "match" } match => MatchRange(match),
			FunctionNode function => FunctionRange(function.Name, [.. function.Arguments.Select(Range)]),
			VariableNode variable => Range(variable.Value).Map(value => Variable(variable.Name, value)),
			FlavorNode flavored => Range(flavored.Inner).Map(inner => Flavored(inner, flavored.Flavor)),
			DiceTermNode term => TermRange(term),
			PoolNode pool => PoolRange(pool),
			_ => new Span(Fail(ErrorMessages.Returns.Error), Evaluated.Failed)
		};
	}

	/// <summary>
	/// <c>1+2+3...</c> parses as a chain down the left side, as long as the formula. It is walked in a
	/// loop, left to right, so only brackets, pools, functions and values add to the call depth.
	/// </summary>
	private Evaluated Chain(BinaryNode node)
	{
		var spine = LeftSpine(node, out var first);
		var result = Evaluate(first);
		while (spine.TryPop(out var binary)) result = Binary(binary.Operator, result, Evaluate(binary.Right));
		return result;
	}

	private Span ChainRange(BinaryNode node)
	{
		var spine = LeftSpine(node, out var first);
		var result = Range(first);
		while (spine.TryPop(out var binary)) result = BinaryRange(binary.Operator, result, Range(binary.Right));
		return result;
	}

	private static Stack<BinaryNode> LeftSpine(BinaryNode node, out DiceNode first)
	{
		var spine = new Stack<BinaryNode>();
		first = node;
		while (first is BinaryNode binary)
		{
			spine.Push(binary);
			first = binary.Left;
		}
		return spine;
	}

	private static Evaluated Group(Evaluated inner) => inner with { Text = $"({inner.Text})" };

	private static Evaluated Negate(Evaluated operand) => operand with { Value = -operand.Value, Text = $"-{operand.Text}" };

	/// <summary>Negation swaps the ends.</summary>
	private static Span NegateRange(Span operand) => new(Negate(operand.High), Negate(operand.Low));

	private static Evaluated Variable(string name, Evaluated value) => value with { Text = $"{name}({value.Text})" };

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
			}, text, left.Terms.AddRange(right.Terms));
		}
		catch (OverflowException)
		{
			return Fail(ErrorMessages.Returns.NumberOutOfRange);
		}
	}

	/// <summary>
	/// Sums pair lowest with lowest; a difference pairs the left's lowest with the right's highest.
	/// Products and quotients are extreme at a corner. A divisor whose range takes in zero has no
	/// range to give, since the roll could divide by zero.
	/// </summary>
	private Span BinaryRange(char op, Span left, Span right)
	{
		if (_failure is not null) return Span.Failed;

		switch (op)
		{
			case '+':
				return new Span(Binary(op, left.Low, right.Low), Binary(op, left.High, right.High));
			case '-':
				return new Span(Binary(op, left.Low, right.High), Binary(op, left.High, right.Low));
			case '/' or '%' when right.Low.Value <= 0 && right.High.Value >= 0:
				return new Span(Fail(ErrorMessages.Returns.DivisionByZero), Evaluated.Failed);
			case '%':
				return RemainderRange(left, right);
			default:
				return Span.Of([.. left.Ends.SelectMany(l => right.Ends.Select(r => Binary(op, l, r)))]);
		}
	}

	private Evaluated Function(string name, ImmutableArray<Evaluated> arguments)
	{
		if (_failure is not null) return Evaluated.Failed;

		var first = arguments[0].Value;
		try
		{
			var value = name switch
			{
				"floor" => Math.Floor(first),
				"ceil" => Math.Ceiling(first),
				// Halves go up, so -2.5 is -2.
				"round" => Math.Floor(first + 0.5m),
				"abs" => Math.Abs(first),
				"min" => arguments.Min(argument => argument.Value),
				"max" => arguments.Max(argument => argument.Value),
				"clamp" => Math.Min(Math.Max(first, arguments[1].Value), arguments[2].Value),
				"ternary" => first != 0 ? arguments[1].Value : arguments[2].Value,
				"when" => first != 0 ? arguments[1].Value : 0,
				"btwn" => Truth(first >= arguments[1].Value && first <= arguments[2].Value),
				"eq" => Truth(first == arguments[1].Value),
				"ne" => Truth(first != arguments[1].Value),
				"gt" => Truth(first > arguments[1].Value),
				"gte" => Truth(first >= arguments[1].Value),
				"lt" => Truth(first < arguments[1].Value),
				_ => Truth(first <= arguments[1].Value)
			};
			return new Evaluated(value, $"{name}({string.Join(", ", arguments.Select(argument => argument.Text))})",
				[.. arguments.SelectMany(argument => argument.Terms)]);
		}
		catch (OverflowException)
		{
			return Fail(ErrorMessages.Returns.NumberOutOfRange);
		}
	}

	private static decimal Truth(bool value) => value ? 1 : 0;

	/// <summary>
	/// match() gives the value of its first when() whose condition holds, or of its first argument that
	/// is not a when(), and 0 when there is neither. Parentheses round a when() leave it a when(), as in
	/// Foundry's pf2e system, where a when() that fails is null however it is grouped. Every argument is
	/// rolled, as Foundry rolls them, so the text shows each one.
	/// </summary>
	private Evaluated Match(FunctionNode match)
	{
		decimal? chosen = null;
		var shown = new List<Evaluated>();
		foreach (var argument in match.Arguments)
		{
			if (Ungrouped(argument) is FunctionNode { Name: "when" } when)
			{
				var condition = Evaluate(when.Arguments[0]);
				var value = Evaluate(when.Arguments[1]);
				if (chosen is null && condition.Value != 0) chosen = value.Value;
				shown.Add(Regrouped(argument, new Evaluated(value.Value, $"when({condition.Text}, {value.Text})", [.. condition.Terms, .. value.Terms])));
				continue;
			}

			var other = Evaluate(argument);
			chosen ??= other.Value;
			shown.Add(other);
		}

		if (_failure is not null) return Evaluated.Failed;
		return new Evaluated(chosen ?? 0, $"match({string.Join(", ", shown.Select(argument => argument.Text))})",
			[.. shown.SelectMany(argument => argument.Terms)]);
	}

	/// <summary>
	/// Every value match() could give: a when() whose condition is settled either decides or drops
	/// out, one that is not adds its value and reading goes on, and 0 when nothing has to decide. Every
	/// argument is taken in first, so a formula that cannot be rolled has no lowest or highest either.
	/// </summary>
	private Span MatchRange(FunctionNode match)
	{
		var arguments = match.Arguments
			.Select(argument => Ungrouped(argument) is FunctionNode { Name: "when" } when
				? (Condition: (Span?)Range(when.Arguments[0]), Value: Range(when.Arguments[1]))
				: (Condition: null, Value: Range(argument)))
			.ToList();
		if (_failure is not null) return Span.Failed;

		var cases = new List<Evaluated>();
		foreach (var (condition, value) in arguments)
		{
			if (condition is { } settled && AlwaysFalse(settled)) continue;
			cases.AddRange(value.Ends);
			if (condition is not { } open || AlwaysTrue(open)) return Span.Of(cases);
		}

		cases.Add(Evaluated.Of(0));
		return Span.Of(cases);
	}

	private static DiceNode Ungrouped(DiceNode node) => node is GroupNode group ? Ungrouped(group.Inner) : node;

	private static Evaluated Regrouped(DiceNode node, Evaluated inner) =>
		node is GroupNode group ? Group(Regrouped(group.Inner, inner)) : inner;

	private static bool IsPoint(Span span) => span.Low.Value == span.High.Value;

	/// <summary>A condition that cannot come out 0.</summary>
	private static bool AlwaysTrue(Span condition) => condition.Low.Value > 0 || condition.High.Value < 0;

	/// <summary>A condition that can only come out 0.</summary>
	private static bool AlwaysFalse(Span condition) => IsPoint(condition) && condition.Low.Value == 0;

	/// <summary>
	/// A comparison is settled when the ranges it compares settle it, such as gt(1d6, 0); otherwise it
	/// may come out 0 or 1. ternary() and when() follow a condition that cannot be 0, or can only be 0,
	/// and take in every value they could give otherwise.
	/// </summary>
	private Span ConditionalRange(string name, ImmutableArray<Span> arguments)
	{
		if (arguments.All(IsPoint)) return Span.Point(Function(name, [.. arguments.Select(argument => argument.Low)]));

		var condition = arguments[0];
		return name switch
		{
			"ternary" when AlwaysTrue(condition) => arguments[1],
			"ternary" when AlwaysFalse(condition) => arguments[2],
			"ternary" => Span.Of([.. arguments[1].Ends, .. arguments[2].Ends]),
			"when" when AlwaysTrue(condition) => arguments[1],
			"when" when AlwaysFalse(condition) => Span.Point(Evaluated.Of(0)),
			"when" => Span.Of([.. arguments[1].Ends, Evaluated.Of(0)]),
			_ => ComparisonRange(name, arguments)
		};
	}

	/// <summary>1 when every value the ranges hold makes the comparison true, 0 when none does, else 0 to 1.</summary>
	private static Span ComparisonRange(string name, ImmutableArray<Span> arguments)
	{
		var (low, high) = (arguments[0].Low.Value, arguments[0].High.Value);
		var (otherLow, otherHigh) = (arguments[1].Low.Value, arguments[1].High.Value);
		var apart = high < otherLow || otherHigh < low;
		int? settled = name switch
		{
			"gt" when low > otherHigh => 1,
			"gt" when high <= otherLow => 0,
			"gte" when low >= otherHigh => 1,
			"gte" when high < otherLow => 0,
			"lt" when high < otherLow => 1,
			"lt" when low >= otherHigh => 0,
			"lte" when high <= otherLow => 1,
			"lte" when low > otherHigh => 0,
			"eq" when apart => 0,
			"ne" when apart => 1,
			"btwn" when low >= otherHigh && high <= arguments[2].Low.Value => 1,
			"btwn" when high < otherLow || low > arguments[2].High.Value => 0,
			_ => null
		};
		return settled is int truth ? Span.Point(Evaluated.Of(truth)) : new Span(Evaluated.Of(0), Evaluated.Of(1));
	}

	/// <summary>
	/// Every function but abs() and the conditionals rises with its arguments, so the lowest arguments
	/// give the lowest value. abs() of a range that crosses zero can only be bounded below by 0.
	/// </summary>
	private Span FunctionRange(string name, ImmutableArray<Span> arguments)
	{
		if (_failure is not null) return Span.Failed;
		if (name is "ternary" or "when" or "btwn" or "eq" or "ne" or "gt" or "gte" or "lt" or "lte")
		{
			return ConditionalRange(name, arguments);
		}

		var low = Function(name, [.. arguments.Select(argument => argument.Low)]);
		var high = Function(name, [.. arguments.Select(argument => argument.High)]);
		if (name != "abs") return new Span(low, high);

		var ends = Span.Of([low, high]);
		return arguments[0].Low.Value < 0 && arguments[0].High.Value > 0 ? ends with { Low = Evaluated.Of(0) } : ends;
	}

	/// <summary>
	/// A remainder takes the dividend's sign and is smaller than the largest divisor, so outside two
	/// fixed numbers it is a bound: <c>1d6 % 4</c> is 0 to 3.
	/// </summary>
	private Span RemainderRange(Span left, Span right)
	{
		if (left.Ends.Count() == 1 && right.Ends.Count() == 1) return Span.Point(Binary('%', left.Low, right.Low));

		var largest = Math.Max(Math.Abs(right.Low.Value), Math.Abs(right.High.Value));
		var whole = new[] { left.Low.Value, left.High.Value, right.Low.Value, right.High.Value }.All(value => value == Math.Floor(value));
		var limit = whole ? largest - 1 : largest;
		return new Span(
			Evaluated.Of(left.Low.Value < 0 ? Math.Max(left.Low.Value, -limit) : 0),
			Evaluated.Of(left.High.Value > 0 ? Math.Min(left.High.Value, limit) : 0));
	}

	private Evaluated Term(DiceTermNode term)
	{
		var count = Evaluate(term.Count);
		var sides = term.Sides is { } sidesNode ? Evaluate(sidesNode) : Evaluated.Of(0);
		if (_failure is not null) return Evaluated.Failed;

		var dice = Math.Floor(count.Value);
		var faces = Math.Floor(sides.Value);
		if (!Fits(term, dice, faces)) return Evaluated.Failed;

		var die = new DieShape(term.Kind, (int)faces);
		var results = new ResultSet(this, die);
		for (var i = 0; i < dice; i++) results.Add(RollOne(die), 0);
		var rolled = new List<Evaluated>();
		var total = results.Apply(term.Modifiers, (modifier, _) => (Rolled(modifier.Amount, rolled), Rolled(modifier.Target, rolled)));
		if (_failure is not null) return Evaluated.Failed;

		return Finished(term, die, results, total, [count, sides, .. rolled]);
	}

	/// <summary>
	/// Each end of the count, the sides and every modifier's number, with all the dice on one face:
	/// the two ends of the die, the faces next to them (for even and odd), and each face a modifier
	/// names and its neighbours. Between those faces nothing a modifier does changes direction.
	/// </summary>
	private Span TermRange(DiceTermNode term)
	{
		var count = Range(term.Count);
		var sides = term.Sides is { } sidesNode ? Range(sidesNode) : Span.Point(Evaluated.Of(0));
		var parameters = term.Modifiers.Select(ParameterRange).ToImmutableArray();
		if (_failure is not null) return Span.Failed;

		var cases = new List<Evaluated>();
		foreach (var (countEnd, sidesEnd) in from countEnd in count.Ends from sidesEnd in sides.Ends select (countEnd, sidesEnd))
		{
			var dice = Math.Floor(countEnd.Value);
			var faces = Math.Floor(sidesEnd.Value);
			if (!Fits(term, dice, faces)) return Span.Failed;
			if (!AddTermCases(term, (int)dice, new DieShape(term.Kind, (int)faces), parameters, [countEnd, sidesEnd], cases)) return Span.Failed;
		}

		return Span.Of(cases);
	}

	/// <summary>The term's total with all <paramref name="dice"/> on each candidate face, for each choice of the modifiers' numbers.</summary>
	private bool AddTermCases(DiceTermNode term, int dice, DieShape die, ImmutableArray<Parameter> parameters,
		ImmutableArray<Evaluated> before, List<Evaluated> cases)
	{
		foreach (var (chosen, face) in from chosen in Choices(parameters) from face in Candidates(die, chosen) select (chosen, face))
		{
			if (!Counted()) return false;

			var results = new ResultSet(this, die);
			for (var i = 0; i < dice; i++) results.Add(face, 0);
			var total = results.Apply(term.Modifiers, (_, index) => (chosen[index].Amount?.Value, chosen[index].Target?.Value));
			if (_failure is not null) return false;
			cases.Add(Finished(term, die, results, total, [.. before, .. Witnesses(chosen)]));
		}

		return true;
	}

	private bool Fits(DiceTermNode term, decimal dice, decimal faces)
	{
		if (dice < 0) Fail(ErrorMessages.Returns.NumberOutOfRange);
		else if (dice > DiceRoll.MaxDice) Fail(ErrorMessages.Returns.RollTooManyDice);
		else if (term.Kind == DieKind.Numbered && faces is < 1 or > DiceRoll.MaxSides) Fail(ErrorMessages.Returns.RollSidesOutOfRange);
		return _failure is null;
	}

	private Evaluated Finished(DiceTermNode term, DieShape die, ResultSet results, decimal total, IEnumerable<Evaluated> before)
	{
		var rolled = results.ToResults();
		var result = new DiceTermResult(term.Formula, die.Description, term.Flavor, total, rolled);
		var text = $"{term.Formula}[{string.Join(", ", rolled.Select(die => die.Text))}]";
		return Flavored(new Evaluated(total, text, [.. before.SelectMany(evaluated => evaluated.Terms), result]), term.Flavor);
	}

	private Evaluated Pool(PoolNode pool)
	{
		var elements = pool.Elements.Select(Evaluate).ToImmutableArray();
		if (_failure is not null) return Evaluated.Failed;

		var rolled = new List<Evaluated>();
		return PoolOf(pool, elements, (modifier, _) => (Rolled(modifier.Amount, rolled), Rolled(modifier.Target, rolled)), rolled);
	}

	/// <summary>Each element at each of its ends, with each end of every modifier's number.</summary>
	private Span PoolRange(PoolNode pool)
	{
		var elements = pool.Elements.Select(Range).ToImmutableArray();
		var parameters = pool.Modifiers.Select(ParameterRange).ToImmutableArray();
		if (_failure is not null) return Span.Failed;

		var cases = new List<Evaluated>();
		foreach (var (chosen, picked) in from chosen in Choices(parameters) from picked in Product(elements.Select(element => element.Ends)) select (chosen, picked))
		{
			if (!Counted()) return Span.Failed;

			var result = PoolOf(pool, picked, (_, index) => (chosen[index].Amount?.Value, chosen[index].Target?.Value), Witnesses(chosen));
			if (_failure is not null) return Span.Failed;
			cases.Add(result);
		}

		return Span.Of(cases);
	}

	private Evaluated PoolOf(PoolNode pool, ImmutableArray<Evaluated> elements,
		Func<DiceModifier, int, (decimal? Amount, decimal? Target)> parameters, IEnumerable<Evaluated> before)
	{
		var results = new ResultSet(this, DieShape.Pool(elements.Select(element => element.Value)));
		foreach (var element in elements) results.Add(element.Value, 0);
		var total = results.Apply(pool.Modifiers, parameters);
		if (_failure is not null) return Evaluated.Failed;

		var rolled = results.ToResults();
		var result = new DiceTermResult(pool.Formula, DieShape.PoolDescription, pool.Flavor, total, rolled);
		var text = $"{{{string.Join(", ", elements.Select(element => element.Text))}}}{pool.ModifierText}"
			+ $"[{string.Join(", ", rolled.Select(die => die.Text))}]";
		ImmutableArray<DiceTermResult> terms = [.. elements.SelectMany(element => element.Terms), .. before.SelectMany(evaluated => evaluated.Terms), result];
		return Flavored(new Evaluated(total, text, terms), pool.Flavor);
	}

	/// <summary>One die, or a zero once the roll has failed.</summary>
	private decimal RollOne(DieShape die)
	{
		if (++_rolled > DiceRoll.MaxDice)
		{
			Fail(ErrorMessages.Returns.RollTooManyDice);
			return 0;
		}

		return die.Kind switch
		{
			DieKind.Fate => roll(3) - 2,
			DieKind.Coin => roll(2) - 1,
			_ => roll(die.Sides)
		};
	}

	/// <summary>A modifier's number or bracketed formula, rolled now; its dice join the term's.</summary>
	private decimal? Rolled(DiceNode? node, List<Evaluated> rolled)
	{
		if (node is null) return null;
		var value = Evaluate(node);
		rolled.Add(value);
		return value.Value;
	}

	private Parameter ParameterRange(DiceModifier modifier)
		=> new(modifier.Amount is { } amount ? Range(amount) : null, modifier.Target is { } target ? Range(target) : null);

	/// <summary>Every way of taking one end of each modifier's number and target.</summary>
	private static IEnumerable<ImmutableArray<Chosen>> Choices(ImmutableArray<Parameter> parameters)
		=> Product(parameters.Select(parameter =>
			from amount in Ends(parameter.Amount)
			from target in Ends(parameter.Target)
			select new Chosen(amount, target)));

	private static IEnumerable<Evaluated?> Ends(Span? span) => span is { } range ? range.Ends.Cast<Evaluated?>() : [null];

	private static IEnumerable<Evaluated> Witnesses(ImmutableArray<Chosen> chosen)
		=> chosen.SelectMany(choice => new[] { choice.Amount, choice.Target }).OfType<Evaluated>();

	private static IEnumerable<ImmutableArray<T>> Product<T>(IEnumerable<IEnumerable<T>> choices)
		=> choices.Aggregate<IEnumerable<T>, IEnumerable<ImmutableArray<T>>>([[]],
			(sofar, next) => sofar.SelectMany(prefix => next.Select(item => prefix.Add(item))));

	/// <summary>Where a term's total can turn: the die's ends and their neighbours, and each named face and its neighbours.</summary>
	private static IEnumerable<decimal> Candidates(DieShape die, ImmutableArray<Chosen> chosen)
	{
		var named = chosen
			.SelectMany(choice => new[] { choice.Amount?.Value, choice.Target?.Value })
			.OfType<decimal>()
			.SelectMany(value => new[] { Math.Floor(value), Math.Ceiling(value) });
		return new[] { die.Lowest, die.Highest }
			.Concat(named)
			.SelectMany(face => new[] { face - 1, face, face + 1 })
			.Where(face => face >= die.Lowest && face <= die.Highest)
			.Distinct()
			.Order();
	}

	private bool Counted()
	{
		if (++_cases <= MaxRangeCases) return true;
		Fail(ErrorMessages.Returns.RollTooManyDice);
		return false;
	}

	private Evaluated Fail(string message)
	{
		_failure ??= message;
		return Evaluated.Failed;
	}

	private static string Number(decimal value) => MushNumber.Unparse(value);

	/// <summary>A value, how it was reached, and the terms rolled on the way.</summary>
	private readonly record struct Evaluated(decimal Value, string Text, ImmutableArray<DiceTermResult> Terms)
	{
		public static Evaluated Failed => new(0, string.Empty, []);

		public static Evaluated Of(decimal value) => new(value, Number(value), []);
	}

	/// <summary>The results that reach a node's lowest and highest values.</summary>
	private readonly record struct Span(Evaluated Low, Evaluated High)
	{
		public static Span Failed => new(Evaluated.Failed, Evaluated.Failed);

		public static Span Point(Evaluated value) => new(value, value);

		/// <summary>The extremes of results that are all reachable.</summary>
		public static Span Of(IReadOnlyCollection<Evaluated> cases)
			=> cases.Count == 0 ? Failed : new Span(cases.MinBy(evaluated => evaluated.Value), cases.MaxBy(evaluated => evaluated.Value));

		public IEnumerable<Evaluated> Ends => Low.Value == High.Value ? [Low] : [Low, High];

		public Span Map(Func<Evaluated, Evaluated> map) => new(map(Low), map(High));
	}

	private readonly record struct Parameter(Span? Amount, Span? Target);

	private readonly record struct Chosen(Evaluated? Amount, Evaluated? Target);

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

		/// <summary>
		/// Applies <paramref name="modifiers"/> in order; <paramref name="parameters"/> gives each one's
		/// number and target, rolled as it is reached or fixed for a range.
		/// </summary>
		public decimal Apply(ImmutableArray<DiceModifier> modifiers, Func<DiceModifier, int, (decimal? Amount, decimal? Target)> parameters)
		{
			for (var i = 0; i < modifiers.Length && evaluator._failure is null; i++)
			{
				var (amount, target) = parameters(modifiers[i], i);
				if (evaluator._failure is not null) break;
				Apply(modifiers[i], amount, target);
			}

			try
			{
				var total = Active().Sum(die => die.Count ?? die.Value);
				foreach (var (comparison, target) in _margins)
				{
					total = comparison is DiceComparison.Less or DiceComparison.LessOrEqual ? target - total : total - target;
				}
				return total;
			}
			catch (OverflowException)
			{
				evaluator.Fail(ErrorMessages.Returns.NumberOutOfRange);
				return 0;
			}
		}

		public ImmutableArray<DieResult> ToResults() => [.. _dice.Select(die => die.ToResult())];

		private void Apply(DiceModifier modifier, decimal? amount, decimal? target)
		{
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
