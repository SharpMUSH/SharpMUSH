using System.Collections.Immutable;
using System.Globalization;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;

namespace SharpMUSH.Implementation.Dice;

/// <summary>A value that is itself a formula, such as <c>"1d6+SPEED"</c>.</summary>
public readonly record struct DiceFormula(string Text);

/// <summary>A name the caller has not looked up yet; see <see cref="DiceNeeds"/>.</summary>
public readonly record struct Unfetched;

/// <summary>What a name holds: a number, a formula, nothing, or why it cannot be used.</summary>
public union DiceValue(decimal, DiceFormula, NotFound, Unfetched, Error<string>);

/// <summary>The names a formula uses that were <see cref="Unfetched"/>, to look up and parse again.</summary>
public sealed record DiceNeeds(ImmutableArray<string> Names);

/// <summary>A parsed formula, the names it still needs, or why it cannot be read.</summary>
public union DiceParse(DiceNode, DiceNeeds, Error<string>);

/// <summary>
/// Reads a roll formula into a <see cref="DiceNode"/> tree.
/// </summary>
/// <remarks>
/// A name that is not a dice term or a function is a value, asked of the lookup. A number is used as
/// it is; a formula is parsed here with the same lookup, so <c>DEXTERITY</c> can be
/// <c>"1d6+SPEED"</c>. Names being resolved are kept on a stack, which refuses a value that leads back
/// to itself. A lookup that cannot answer yet (an attribute still to be read) says
/// <see cref="Unfetched"/>: parsing carries on to find every such name, and the caller looks them up
/// and parses again.
/// </remarks>
public sealed class DiceParser
{
	private readonly string _text;
	private readonly Func<string, DiceValue> _lookup;
	private readonly ImmutableStack<string> _resolving;
	private readonly HashSet<string> _needs;
	private readonly Budget _budget;
	private int _position;

	/// <summary>
	/// Shared by every parser of one formula, values included, so a value used many times or nested
	/// many levels counts against the whole formula.
	/// </summary>
	private sealed class Budget
	{
		public int Terms;
		public int Depth;
	}

	private DiceParser(string text, Func<string, DiceValue> lookup, ImmutableStack<string> resolving,
		HashSet<string> needs, Budget budget)
	{
		_text = text;
		_lookup = lookup;
		_resolving = resolving;
		_needs = needs;
		_budget = budget;
	}

	public static DiceParse Parse(string formula, Func<string, DiceValue> lookup)
	{
		var needs = new HashSet<string>(StringComparer.Ordinal);
		return new DiceParser(formula, lookup, ImmutableStack<string>.Empty, needs, new Budget()).ParseFormula() switch
		{
			DiceNode node when needs.Count > 0 => new DiceNeeds([.. needs]),
			DiceNode node => node,
			Error<string> error => error
		};
	}

	/// <summary>Whether <paramref name="formula"/> reads, whatever its names turn out to hold.</summary>
	public static bool IsValid(string formula)
		=> Parse(formula, _ => 0m) is DiceNode;

	private Result<DiceNode> ParseFormula()
	{
		var node = ParseSum();
		if (node is InvalidNode invalid) return new Error<string>(invalid.Message);

		SkipSpaces();
		return _position < _text.Length
			? new Error<string>(SyntaxError())
			: node;
	}

	private DiceNode ParseSum()
	{
		var left = ParseProduct();
		while (left is not InvalidNode && NextOperator('+', '-') is { } op)
		{
			var right = ParseProduct();
			left = right is InvalidNode ? right : new BinaryNode(op, left, right);
		}
		return left;
	}

	private DiceNode ParseProduct()
	{
		var left = ParseUnary();
		while (left is not InvalidNode && NextOperator('*', '/', '%') is { } op)
		{
			var right = ParseUnary();
			left = right is InvalidNode ? right : new BinaryNode(op, left, right);
		}
		return left;
	}

	/// <summary>Every operand passes through here, so the budget is kept here.</summary>
	private DiceNode ParseUnary()
	{
		if (++_budget.Terms > DiceRoll.MaxTerms) return new InvalidNode(ErrorMessages.Returns.RollFormulaTooLarge);
		if (_budget.Depth >= DiceRoll.MaxNesting) return new InvalidNode(ErrorMessages.Returns.RollFormulaTooDeep);

		_budget.Depth++;
		try
		{
			return ParseOperand();
		}
		finally
		{
			_budget.Depth--;
		}
	}

	private DiceNode ParseOperand()
	{
		SkipSpaces();
		if (Accept('-'))
		{
			return ParseUnary() switch
			{
				InvalidNode invalid => invalid,
				var operand => new NegateNode(operand)
			};
		}
		return Accept('+') ? ParseUnary() : ParseFlavored();
	}

	/// <summary>A term, with a <c>[label]</c> after it if there is one.</summary>
	private DiceNode ParseFlavored()
	{
		var term = ParsePrimary();
		if (term is InvalidNode || !Accept('[')) return term;

		var end = _text.IndexOf(']', _position);
		if (end < 0) return new InvalidNode(SyntaxError());
		var flavor = _text[_position..end].Trim();
		_position = end + 1;

		return term switch
		{
			DiceTermNode dice => dice with { Flavor = flavor },
			PoolNode pool => pool with { Flavor = flavor },
			_ => new FlavorNode(term, flavor)
		};
	}

	private DiceNode ParsePrimary()
	{
		SkipSpaces();
		if (_position >= _text.Length) return new InvalidNode(SyntaxError());

		var start = _position;
		var current = _text[_position];

		if (char.IsAsciiDigit(current) || current == '.')
		{
			if (ReadNumber() is not { } number) return new InvalidNode(SyntaxError());
			return IsDiceMarker()
				? ParseDiceTerm(start, new NumberNode(number)) ?? new InvalidNode(SyntaxError())
				: new NumberNode(number);
		}

		if (current == '(')
		{
			return ParseBracketed() switch
			{
				GroupNode group when IsDiceMarker() => ParseDiceTerm(start, group) ?? new InvalidNode(SyntaxError()),
				var bracketed => bracketed
			};
		}

		if (current == '{') return ParsePool();

		// A bare d6 is a dice term; anything else starting with d is a name (DEX, damage).
		if (IsDiceMarker() && ParseDiceTerm(start, new NumberNode(1)) is { } implicitCount)
		{
			return implicitCount;
		}
		_position = start;

		if (current == '@' || current == '_' || char.IsAsciiLetter(current))
		{
			return ParseName();
		}

		return new InvalidNode(SyntaxError());
	}

	/// <summary><c>(&lt;formula&gt;)</c>, or an <see cref="InvalidNode"/>.</summary>
	private DiceNode ParseBracketed()
	{
		_position++;
		var inner = ParseSum();
		if (inner is InvalidNode) return inner;
		SkipSpaces();
		return Accept(')') ? new GroupNode(inner) : new InvalidNode(SyntaxError());
	}

	/// <summary>
	/// The rest of a dice term from its <c>d</c>, or <see langword="null"/> when what follows the
	/// <c>d</c> does not make one, which leaves the caller to read it as something else.
	/// </summary>
	private DiceNode? ParseDiceTerm(int start, DiceNode count)
	{
		_position++;
		if (_position >= _text.Length) return null;

		DiceNode? sides = null;
		var kind = DieKind.Numbered;
		var current = _text[_position];
		if (char.IsAsciiDigit(current))
		{
			if (ReadInteger() is not { } faces) return null;
			sides = new NumberNode(faces);
		}
		else if (current == '%')
		{
			_position++;
			sides = new NumberNode(100);
		}
		else if (current is 'F' or 'f')
		{
			_position++;
			kind = DieKind.Fate;
		}
		else if (current is 'C' or 'c')
		{
			_position++;
			kind = DieKind.Coin;
		}
		else if (current == '(')
		{
			sides = ParseBracketed();
			if (sides is InvalidNode) return sides;
		}
		else
		{
			return null;
		}

		var modifiers = ImmutableArray.CreateBuilder<DiceModifier>();
		while (_position < _text.Length && char.IsAsciiLetter(_text[_position]))
		{
			switch (ParseModifier(kind == DieKind.Coin))
			{
				case DiceModifier modifier:
					modifiers.Add(modifier);
					continue;
				case Error<string> error:
					return new InvalidNode(error.Value);
				case None:
					// Letters that are no modifier: the whole thing is a name, not a dice term.
					return null;
			}
		}

		return new DiceTermNode(_text[start.._position], count, sides, kind, modifiers.ToImmutable(), null);
	}

	/// <summary>
	/// <c>{&lt;formula&gt;, ...}</c> and its modifiers. A pool's results are totals, not dice, so it
	/// takes the modifiers that keep, drop, count or bound them, and not explode or reroll.
	/// </summary>
	private DiceNode ParsePool()
	{
		var start = _position;
		_position++;
		var elements = ImmutableArray.CreateBuilder<DiceNode>();
		do
		{
			var element = ParseSum();
			if (element is InvalidNode) return element;
			elements.Add(element);
			SkipSpaces();
		} while (Accept(','));

		if (!Accept('}')) return new InvalidNode(SyntaxError());

		var modifierStart = _position;
		var modifiers = ImmutableArray.CreateBuilder<DiceModifier>();
		while (_position < _text.Length && char.IsAsciiLetter(_text[_position]))
		{
			var at = _position;
			switch (ParseModifier(coin: false))
			{
				case DiceModifier { Kind: DiceModifierKind.Explode or DiceModifierKind.ExplodeOnce or DiceModifierKind.Reroll or DiceModifierKind.RerollRecursive }:
					_position = at;
					return new InvalidNode(SyntaxError());
				case DiceModifier modifier:
					modifiers.Add(modifier);
					continue;
				case Error<string> error:
					return new InvalidNode(error.Value);
				case None:
					return new InvalidNode(SyntaxError());
			}
		}

		return new PoolNode(_text[start.._position], elements.ToImmutable(), _text[modifierStart.._position], modifiers.ToImmutable(), null);
	}

	/// <summary>A modifier at the current position, an error inside one, or <see cref="None"/>.</summary>
	private DiceModifierParse ParseModifier(bool coin)
	{
		var start = _position;
		if (AcceptWord("kh")) return Amount(DiceModifierKind.KeepHighest);
		if (AcceptWord("kl")) return Amount(DiceModifierKind.KeepLowest);
		if (AcceptWord("k")) return Amount(DiceModifierKind.KeepHighest);
		if (AcceptWord("dh")) return Amount(DiceModifierKind.DropHighest);
		if (AcceptWord("dl")) return Amount(DiceModifierKind.DropLowest);
		if (AcceptWord("df")) return Target(DiceModifierKind.DeductFailures);
		if (AcceptWord("d")) return Amount(DiceModifierKind.DropLowest);
		if (AcceptWord("xo")) return Explode(DiceModifierKind.ExplodeOnce);
		if (AcceptWord("x")) return Explode(DiceModifierKind.Explode);
		if (AcceptWord("rr")) return Target(DiceModifierKind.RerollRecursive);
		if (AcceptWord("r")) return Target(DiceModifierKind.Reroll);
		if (AcceptWord("cs")) return Target(DiceModifierKind.CountSuccesses);
		if (AcceptWord("cf")) return Target(DiceModifierKind.CountFailures);
		if (AcceptWord("sf")) return Target(DiceModifierKind.SubtractFailures);
		if (AcceptWord("ms")) return Target(DiceModifierKind.MarginOfSuccess);
		if (AcceptWord("min")) return Bound(DiceModifierKind.Minimum);
		if (AcceptWord("max")) return Bound(DiceModifierKind.Maximum);
		if (AcceptWord("even")) return new DiceModifier(DiceModifierKind.CountEven, null, DiceComparison.Equal, null);
		if (AcceptWord("odd")) return new DiceModifier(DiceModifierKind.CountOdd, null, DiceComparison.Equal, null);
		// A coin's call: 3dcc1 counts heads, 3dcc0 tails.
		if (coin && AcceptWord("c")) return Target(DiceModifierKind.CountSuccesses);

		_position = start;
		return new None();

		DiceModifierParse Amount(DiceModifierKind kind) => ReadModifierNumber() switch
		{
			DiceNode amount => new DiceModifier(kind, amount, DiceComparison.Equal, null),
			None => new DiceModifier(kind, new NumberNode(1), DiceComparison.Equal, null),
			Error<string> error => error
		};

		DiceModifierParse Bound(DiceModifierKind kind) => ReadModifierNumber() switch
		{
			DiceNode bound => new DiceModifier(kind, bound, DiceComparison.Equal, null),
			None => new DiceModifier(kind, null, DiceComparison.Equal, null),
			Error<string> error => error
		};

		DiceModifierParse Target(DiceModifierKind kind) => ReadTarget(kind, null);

		// x5=10: a number followed by a comparison is the most explosions, then the faces.
		DiceModifierParse Explode(DiceModifierKind kind)
		{
			var capStart = _position;
			if (ReadInteger() is { } cap && _position < _text.Length && _text[_position] is '=' or '>' or '<')
			{
				return ReadTarget(kind, new NumberNode(cap));
			}
			_position = capStart;
			return ReadTarget(kind, null);
		}
	}

	/// <summary><c>[=|&gt;|&gt;=|&lt;|&lt;=]&lt;number or (formula)&gt;</c>; either part may be missing.</summary>
	private DiceModifierParse ReadTarget(DiceModifierKind kind, DiceNode? amount)
	{
		var comparison =
			AcceptWord(">=") ? DiceComparison.GreaterOrEqual
			: AcceptWord("<=") ? DiceComparison.LessOrEqual
			: Accept('>') ? DiceComparison.Greater
			: Accept('<') ? DiceComparison.Less
			: Accept('=') ? DiceComparison.Equal
			: (DiceComparison?)null;

		return (comparison, ReadModifierNumber()) switch
		{
			(_, Error<string> error) => error,
			(null, None) => new DiceModifier(kind, amount, DiceComparison.Equal, null),
			(_, None) => new Error<string>(SyntaxError()),
			(var given, DiceNode target) => new DiceModifier(kind, amount, given ?? DiceComparison.Equal, target)
		};
	}

	/// <summary>A signed whole number, a bracketed formula, or <see cref="None"/>.</summary>
	private DiceNumberParse ReadModifierNumber()
	{
		if (_position < _text.Length && _text[_position] == '(')
		{
			return ParseBracketed() switch
			{
				InvalidNode invalid => new Error<string>(invalid.Message),
				var group => group
			};
		}

		var start = _position;
		var negative = Accept('-');
		if (ReadInteger() is { } value) return (DiceNode)new NumberNode(negative ? -value : value);
		_position = start;
		return new None();
	}

	private DiceNode ParseName()
	{
		var start = _position;
		Accept('@');
		while (_position < _text.Length && IsNameCharacter(_text[_position])) _position++;
		var name = _text[start.._position];
		if (name is "" or "@") return new InvalidNode(SyntaxError());

		SkipSpaces();
		return _position < _text.Length && _text[_position] == '('
			? ParseFunction(name)
			: ResolveName(name.TrimStart('@'));
	}

	private DiceNode ParseFunction(string name)
	{
		var function = name.ToLowerInvariant();
		if (!IsFunction(function))
		{
			return new InvalidNode(string.Format(ErrorMessages.Returns.RollUnknownFunctionFormat, name.ToUpperInvariant()));
		}

		_position++;
		var arguments = ImmutableArray.CreateBuilder<DiceNode>();
		do
		{
			var argument = ParseSum();
			if (argument is InvalidNode) return argument;
			arguments.Add(argument);
			SkipSpaces();
		} while (Accept(','));

		if (!Accept(')')) return new InvalidNode(SyntaxError());
		return AcceptsArguments(function, arguments.Count)
			? new FunctionNode(function, arguments.ToImmutable())
			: new InvalidNode(string.Format(ErrorMessages.Returns.RollUnknownFunctionFormat, name.ToUpperInvariant()));
	}

	private DiceNode ResolveName(string name)
	{
		var key = name.ToUpperInvariant();
		if (_resolving.Contains(key))
		{
			return new InvalidNode(string.Format(ErrorMessages.Returns.RollCircularVariableFormat, key));
		}
		if (_resolving.Count() >= DiceRoll.MaxVariableDepth)
		{
			return new InvalidNode(ErrorMessages.Returns.RollVariablesTooDeep);
		}

		return _lookup(name) switch
		{
			decimal number => new VariableNode(name, new NumberNode(number)),
			DiceFormula formula => ParseValueFormula(name, key, formula.Text),
			NotFound => new InvalidNode(string.Format(ErrorMessages.Returns.RollNoSuchVariableFormat, key)),
			Unfetched => NeedLookup(name),
			Error<string> error => new InvalidNode(error.Value)
		};
	}

	/// <summary>Notes a name to look up and stands in a zero, so parsing finds the rest.</summary>
	private VariableNode NeedLookup(string name)
	{
		_needs.Add(name);
		return new VariableNode(name, new NumberNode(0));
	}

	private DiceNode ParseValueFormula(string name, string key, string formula)
	{
		if (decimal.TryParse(formula.Trim(), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
			CultureInfo.InvariantCulture, out var number))
		{
			return new VariableNode(name, new NumberNode(number));
		}

		return new DiceParser(formula, _lookup, _resolving.Push(key), _needs, _budget).ParseFormula() switch
		{
			DiceNode inner => new VariableNode(name, inner),
			Error<string> error => new InvalidNode(error.Value)
		};
	}

	private char? NextOperator(params ReadOnlySpan<char> operators)
	{
		SkipSpaces();
		if (_position >= _text.Length || !operators.Contains(_text[_position])) return null;
		return _text[_position++];
	}

	private bool IsDiceMarker() => _position < _text.Length && _text[_position] is 'd' or 'D';

	private static bool IsNameCharacter(char character)
		=> char.IsAsciiLetterOrDigit(character) || character is '_' or '.' or '`';

	private decimal? ReadNumber()
	{
		var start = _position;
		while (_position < _text.Length && (char.IsAsciiDigit(_text[_position]) || _text[_position] == '.')) _position++;
		return decimal.TryParse(_text.AsSpan(start, _position - start), NumberStyles.AllowDecimalPoint,
			CultureInfo.InvariantCulture, out var value)
			? value
			: null;
	}

	private int? ReadInteger()
	{
		var start = _position;
		while (_position < _text.Length && char.IsAsciiDigit(_text[_position])) _position++;
		if (_position == start) return null;
		return int.TryParse(_text.AsSpan(start, _position - start), NumberStyles.None,
			CultureInfo.InvariantCulture, out var value)
			? value
			: int.MaxValue;
	}

	private bool Accept(char expected)
	{
		if (_position >= _text.Length || _text[_position] != expected) return false;
		_position++;
		return true;
	}

	private bool AcceptWord(string word)
	{
		if (!_text.AsSpan(_position).StartsWith(word, StringComparison.OrdinalIgnoreCase)) return false;
		_position += word.Length;
		return true;
	}

	private void SkipSpaces()
	{
		while (_position < _text.Length && char.IsWhiteSpace(_text[_position])) _position++;
	}

	private string SyntaxError()
		=> string.Format(ErrorMessages.Returns.RollSyntaxErrorFormat, _position + 1);

	private static bool IsFunction(string name) => name is "floor" or "ceil" or "round" or "abs" or "min" or "max";

	private static bool AcceptsArguments(string name, int count) => name switch
	{
		"min" or "max" => count >= 1,
		_ => count == 1
	};
}

/// <summary>What <see cref="DiceParser"/> finds where a modifier may start.</summary>
internal union DiceModifierParse(DiceModifier, Error<string>, None);

/// <summary>What <see cref="DiceParser"/> finds where a modifier's number may start.</summary>
internal union DiceNumberParse(DiceNode, Error<string>, None);
