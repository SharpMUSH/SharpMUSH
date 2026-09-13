using System.Globalization;
using System.Text.RegularExpressions;

namespace SharpMUSH.Library.Utilities;

/// <summary>Literal incoming-filter patterns, using Penn's unevaluated comma parsing rules.</summary>
public static class IncomingFilterPatterns
{
	public static IEnumerable<string> Split(string text, CancellationToken cancellationToken = default)
	{
		var closers = new Stack<char>();
		var start = 0;
		for (var index = 0; index < text.Length; index++)
		{
			cancellationToken.ThrowIfCancellationRequested();
			var character = text[index];
			if (character == '\\') { if (index + 1 < text.Length) index++; continue; }
			if (character == '%')
			{
				if (++index == text.Length) break;
				var substitution = char.ToUpperInvariant(text[index]);
				if (substitution is 'Q' or 'V' or 'W' or 'X')
				{
					if (++index == text.Length) break;
					if (substitution == 'Q' && text[index] == '<') closers.Push('>');
				}
				continue;
			}
			if (character is '(' or '[' or '{')
			{
				closers.Push(character switch { '(' => ')', '[' => ']', _ => '}' });
				continue;
			}
			if (closers.TryPeek(out var closer) && character == closer) { closers.Pop(); continue; }
			if (character != ',' || closers.Count != 0) continue;
			if (index > start) yield return text[start..index];
			start = index + 1;
		}
		cancellationToken.ThrowIfCancellationRequested();
		if (start < text.Length) yield return text[start..];
	}

	/// <summary>Ordering prefixes belong to incoming filters, not to the general wildcard dialect.</summary>
	public static bool Matches(string pattern, string message, bool regexp, bool caseSensitive, NumericEvaluation numeric = default)
	{
		if (!regexp && pattern.Length > 0 && pattern[0] is '>' or '<')
		{
			var inclusive = pattern.Length > 1 && pattern[1] == '=';
			var operand = pattern[(inclusive ? 2 : 1)..];
			if (TryNumber(message, numeric, out var value) && TryNumber(operand, numeric, out var threshold))
				return pattern[0] == '>' ? inclusive ? value >= threshold : value > threshold
					: inclusive ? value <= threshold : value < threshold;
			var comparison = CultureInfo.CurrentCulture.CompareInfo.Compare(message, operand, CompareOptions.None);
			return pattern[0] == '>' ? inclusive ? comparison >= 0 : comparison > 0
				: inclusive ? comparison <= 0 : comparison < 0;
		}
		try
		{
			var options = caseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase;
			var regex = regexp ? SoftcodeRegex.Create(pattern, options)
				: SoftcodeRegex.Wildcard(pattern, options, caseSensitive);
			return SoftcodeRegex.IsMatch(regex, message);
		}
		catch (ArgumentException) { return false; }
	}

	private static bool TryNumber(string text, NumericEvaluation numeric, out double value)
	{
		var literal = text.TrimStart();
		if (!numeric.TinyMath && (literal.Equals("inf", StringComparison.OrdinalIgnoreCase)
			|| literal.Equals("+inf", StringComparison.OrdinalIgnoreCase) || literal.Equals("-inf", StringComparison.OrdinalIgnoreCase)))
		{
			value = literal[0] == '-' ? double.NegativeInfinity : double.PositiveInfinity;
			return true;
		}
		if (!numeric.TryDouble(text, out value)) return false;
		if (!numeric.TinyMath && value == 0 && HasNonzeroSignificand(literal)) return false;
		// Reject overflow and underflow-to-zero in strict mode; explicitly written infinity is numeric.
		return numeric.TinyMath || !double.IsInfinity(value)
			|| literal.Equals("Infinity", StringComparison.OrdinalIgnoreCase)
			|| literal.Equals("+Infinity", StringComparison.OrdinalIgnoreCase)
			|| literal.Equals("-Infinity", StringComparison.OrdinalIgnoreCase);
	}

	private static bool HasNonzeroSignificand(string literal)
	{
		var span = literal.AsSpan().TrimStart("+-");
		var hexadecimal = span.StartsWith("0x", StringComparison.OrdinalIgnoreCase);
		if (hexadecimal) span = span[2..];
		foreach (var character in span)
		{
			if (hexadecimal ? character is 'p' or 'P' : character is 'e' or 'E') break;
			if (character is >= '1' and <= '9' || hexadecimal && character is >= 'a' and <= 'f' or >= 'A' and <= 'F') return true;
		}
		return false;
	}
}
