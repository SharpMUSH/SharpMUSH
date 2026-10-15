using System.Globalization;
using System.Text;

namespace SharpMUSH.Library.Services.DatabaseConversion.TinyMux;

/// <summary>
/// Rewrites a TinyMUX lock key as a SharpMUSH one. The operators are the same; what differs is how
/// TinyMUX writes its operands in a dump: an object as <c>(#5)</c>, an attribute by TinyMUX's name or by
/// number (<c>Desc:*x*</c>, <c>300/1</c>), and an indirect lock's lock by its <c>@lock</c> switch
/// (<c>@(#5)/enterlock</c>).
/// </summary>
/// <remarks>
/// The key is parsed the way TinyMUX's <c>parse_boolexp</c> reads a stored lock and written back out,
/// so grouping survives. A version 1 or 2 flatfile keeps the default lock in the object's header in a
/// prefix form of its own; <see cref="ParseHeaderLock"/> reads that into the same tree.
/// </remarks>
public static class TinyMuxLockKey
{
	/// <summary>A parsed lock key.</summary>
	public abstract record Node;

	public sealed record Constant(string Text) : Node;

	public sealed record AttributeTest(string Attribute, string Pattern, bool Evaluate) : Node;

	public sealed record Not(Node Operand) : Node;

	public sealed record And(Node Left, Node Right) : Node;

	public sealed record Or(Node Left, Node Right) : Node;

	/// <summary><c>=</c>, <c>+</c> or <c>$</c> before an object.</summary>
	public sealed record Prefixed(char Token, Node Operand) : Node;

	public sealed record Indirect(Node Operand, string? LockSwitch) : Node;

	/// <summary>
	/// The SharpMUSH key for a TinyMUX key, or the empty string for none (an unlocked lock).
	/// <paramref name="attributeName"/> names a numbered attribute, or returns null when it names none.
	/// <paramref name="notes"/> hears about anything carried over approximately.
	/// </summary>
	public static string Convert(string muxKey, Func<int, string?> attributeName, Action<string> notes)
	{
		if (string.IsNullOrWhiteSpace(muxKey)) return string.Empty;
		var parser = new InfixParser(muxKey);
		var tree = parser.Parse();
		return tree is null ? string.Empty : Write(tree, attributeName, notes);
	}

	/// <summary>
	/// Writes a parsed key as SharpMUSH reads it. An <c>&amp;</c> binds tighter than <c>|</c> in both,
	/// so only an <c>|</c> under an <c>&amp;</c> and anything compound under a <c>!</c> need parentheses.
	/// </summary>
	public static string Write(Node node, Func<int, string?> attributeName, Action<string> notes) => node switch
	{
		Constant constant => ObjectReference(constant.Text),
		AttributeTest test => $"{AttributeReference(test.Attribute, attributeName, notes)}{(test.Evaluate ? '/' : ':')}{test.Pattern}",
		Not not => "!" + Grouped(not.Operand, not.Operand is And or Or, attributeName, notes),
		And and => $"{Grouped(and.Left, and.Left is Or, attributeName, notes)}&{Grouped(and.Right, and.Right is Or, attributeName, notes)}",
		Or or => $"{Write(or.Left, attributeName, notes)}|{Write(or.Right, attributeName, notes)}",
		Prefixed prefixed => prefixed.Token + Grouped(prefixed.Operand, prefixed.Operand is not Constant, attributeName, notes),
		Indirect indirect => "@" + Grouped(indirect.Operand, indirect.Operand is not Constant, attributeName, notes)
			+ IndirectLock(indirect.LockSwitch, notes),
		_ => string.Empty
	};

	private static string Grouped(Node node, bool group, Func<int, string?> attributeName, Action<string> notes)
		=> group ? $"({Write(node, attributeName, notes)})" : Write(node, attributeName, notes);

	/// <summary><c>#5</c> as it is; a name or <c>*player</c> as it is too, since only a dbref can move.</summary>
	private static string ObjectReference(string text) => text.Trim();

	private static string AttributeReference(string attribute, Func<int, string?> attributeName, Action<string> notes)
	{
		if (int.TryParse(attribute, NumberStyles.None, CultureInfo.InvariantCulture, out var number))
		{
			if (attributeName(number) is { } named) return named;
			notes($"a lock names attribute number {number}, which the flatfile does not name; it is kept as {number}");
			return attribute;
		}

		return TinyMuxNames.AttributeName(attribute);
	}

	private static string IndirectLock(string? lockSwitch, Action<string> notes)
	{
		if (lockSwitch is null) return string.Empty;
		if (TinyMuxNames.LockSwitch(lockSwitch) is { } name) return name == "Basic" ? string.Empty : "/" + name;
		notes($"an indirect lock names lock '{lockSwitch}', which TinyMUX does not have; it is kept as written");
		return "/" + lockSwitch;
	}

	/// <summary>The infix form TinyMUX stores a lock in, read as <c>parse_boolexp</c> reads it.</summary>
	private sealed class InfixParser(string text)
	{
		private int _at;

		public Node? Parse()
		{
			var node = ParseOr();
			SkipSpaces();
			return _at == text.Length ? node : throw new FormatException($"unexpected '{text[_at]}' in lock '{text}'");
		}

		private Node? ParseOr()
		{
			var left = ParseAnd();
			SkipSpaces();
			if (!Take('|')) return left;
			var right = ParseOr();
			return left is null || right is null ? left ?? right : new Or(left, right);
		}

		private Node? ParseAnd()
		{
			var left = ParseFactor();
			SkipSpaces();
			if (!Take('&')) return left;
			var right = ParseAnd();
			return left is null || right is null ? left ?? right : new And(left, right);
		}

		private Node? ParseFactor()
		{
			SkipSpaces();
			if (_at >= text.Length) return null;
			switch (text[_at])
			{
				case '!':
					_at++;
					return ParseFactor() is { } negated ? new Not(negated) : null;
				case '@':
					_at++;
					return ParseIndirect();
				case '=' or '+' or '$':
					var token = text[_at++];
					return ParseLeaf() is { } operand ? new Prefixed(token, operand) : null;
				default:
					return ParseLeaf();
			}
		}

		private Node? ParseIndirect()
		{
			var operand = ParseLeaf(stopAtSlash: true);
			string? lockSwitch = null;
			if (Take('/'))
			{
				var start = _at;
				while (_at < text.Length && text[_at] is not ('&' or '|' or ')')) _at++;
				lockSwitch = text[start.._at].Trim();
			}

			return operand is null ? null : new Indirect(operand, lockSwitch);
		}

		private Node? ParseLeaf(bool stopAtSlash = false)
		{
			SkipSpaces();
			if (Take('('))
			{
				var inner = ParseOr();
				SkipSpaces();
				return Take(')') ? inner : throw new FormatException($"missing ')' in lock '{text}'");
			}

			var start = _at;
			while (_at < text.Length && text[_at] is not ('&' or '|' or ')') && !(stopAtSlash && text[_at] == '/')) _at++;
			var leaf = text[start.._at].TrimEnd();
			if (leaf.Length == 0) return null;

			var split = leaf.IndexOfAny([':', '/']);
			return split > 0 && !stopAtSlash
				? new AttributeTest(leaf[..split], leaf[(split + 1)..], leaf[split] == '/')
				: new Constant(leaf);
		}

		private void SkipSpaces()
		{
			while (_at < text.Length && char.IsWhiteSpace(text[_at])) _at++;
		}

		private bool Take(char expected)
		{
			if (_at >= text.Length || text[_at] != expected) return false;
			_at++;
			return true;
		}
	}

	/// <summary>
	/// Reads the lock a version 1 or 2 flatfile keeps in each object's header (<c>getboolexp</c>): a
	/// prefix form in parentheses, <c>(!x)</c>, <c>(@x)</c>, <c>(=x)</c>, <c>(+x)</c>, <c>($x)</c>,
	/// <c>(x&amp;y)</c>, <c>(x|y)</c>, with bare numbers for objects and <c>name:pattern</c> or
	/// <c>name/pattern</c> for attributes. An empty line is no lock. The terminating newline, and the
	/// optional blank line after it, are consumed.
	/// </summary>
	public static Node? ParseHeaderLock(TinyMuxFlatfileReader reader)
	{
		var node = HeaderNode(reader);
		if (reader.Read() != '\n') throw reader.Error("a header lock is not followed by a newline");
		if (reader.Peek() == '\n') reader.Read();
		return node;
	}

	private static Node? HeaderNode(TinyMuxFlatfileReader reader)
	{
		var c = reader.Peek();
		switch (c)
		{
			case '\n':
				return null;
			case -1:
				throw reader.Error("the file ends inside a header lock");
			case '(':
				reader.Read();
				Node? node;
				var token = reader.Peek();
				if (token is '!' or '@' or '=' or '+' or '$')
				{
					reader.Read();
					var operand = HeaderNode(reader) ?? throw reader.Error("an empty operand in a header lock");
					node = token switch
					{
						'!' => new Not(operand),
						'@' => new Indirect(operand, null),
						_ => new Prefixed((char)token, operand)
					};
				}
				else
				{
					var left = HeaderNode(reader) ?? throw reader.Error("an empty operand in a header lock");
					var connective = reader.Read();
					if (connective == '\n') connective = reader.Read();
					var right = HeaderNode(reader) ?? throw reader.Error("an empty operand in a header lock");
					node = connective switch
					{
						'&' => new And(left, right),
						'|' => new Or(left, right),
						_ => throw reader.Error($"'{(char)connective}' is not a lock connective")
					};
				}

				var close = reader.Read();
				if (close == '\n') close = reader.Read();
				return close == ')' ? node : throw reader.Error("a header lock is missing its ')'");
			default:
				return HeaderLeaf(reader);
		}
	}

	private static Node HeaderLeaf(TinyMuxFlatfileReader reader)
	{
		var name = new StringBuilder();
		int c;
		if (char.IsAsciiDigit((char)reader.Peek()))
		{
			while (reader.Peek() is var digit && digit >= '0' && digit <= '9') name.Append((char)reader.Read());
			c = reader.Peek();
			if (c is not (':' or '/')) return new Constant("#" + name);
			reader.Read();
		}
		else
		{
			while ((c = reader.Read()) is not (-1 or '\n' or ':' or '/')) name.Append((char)c);
			if (c is -1 or '\n') throw reader.Error($"a header lock names '{name}' without a pattern");
		}

		var pattern = new StringBuilder();
		while (reader.Peek() is not (-1 or '\n' or ')' or '|' or '&')) pattern.Append((char)reader.Read());
		return new AttributeTest(name.ToString(), pattern.ToString(), c == '/');
	}
}
