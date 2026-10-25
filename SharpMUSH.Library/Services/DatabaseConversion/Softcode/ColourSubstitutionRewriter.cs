using System.Text;
using System.Text.RegularExpressions;

namespace SharpMUSH.Library.Services.DatabaseConversion.Softcode;

/// <summary>Which server's colour substitutions a body was written for.</summary>
public enum ColourSubstitutionDialect
{
	/// <summary>
	/// TinyMUX: <c>%c</c>/<c>%x</c> with a letter, <c>&lt;#rrggbb&gt;</c>, <c>&lt;r g b&gt;</c> or a colour name; an
	/// unknown letter prints the letter itself.
	/// </summary>
	TinyMux,

	/// <summary>
	/// RhostMUSH (default build): the TinyMUX forms, plus xterm indexes (<c>%c0x46</c>, <c>%c0X15</c> for a
	/// background) and a code taken from a register (<c>%c%qy</c>); an unknown letter is left as literal text,
	/// and a code Rhost assembles at run time (<c>%c0X[printf(...)]</c>) is removed and reported.
	/// </summary>
	RhostMush
}

/// <summary>Whether a body is a command list or an expression; <see cref="Auto"/> decides from its text.</summary>
public enum ColourRewriteMode
{
	/// <summary>
	/// A command list when the body is a <c>$</c>/<c>^</c> pattern and its action, or starts with <c>@</c>,
	/// <c>&amp;</c> or a common command word; an expression otherwise (a description, a message, a function).
	/// </summary>
	Auto,

	/// <summary>Commands split at <c>;</c>, each command's arguments at its first <c>=</c>.</summary>
	CommandList,

	/// <summary>One expression, as a description or a <c>u()</c> function is evaluated.</summary>
	Expression
}

/// <summary>How <see cref="ColourSubstitutionRewriter.Rewrite(string, ColourRewriteOptions)"/> reads a body.</summary>
public sealed record ColourRewriteOptions(
	ColourSubstitutionDialect Dialect = ColourSubstitutionDialect.TinyMux,
	ColourRewriteMode Mode = ColourRewriteMode.Auto);

/// <summary>
/// The rewritten body, whether it differs from the original, and what the rewrite could not carry over
/// exactly (<see cref="ColourSubstitutionRewriter"/>'s flag names, for a person to read).
/// </summary>
public sealed record ColourRewriteResult(string Text, bool Changed, IReadOnlyList<string> Flags);

/// <summary>
/// Rewrites TinyMUX and RhostMUSH colour substitutions (<c>%cr</c>, <c>%xh</c>, <c>%cn</c> ...) into
/// <c>ansi()</c> calls, so softcode imported from those servers keeps its colour under SharpMUSH, where
/// <c>%c</c> is the last command and <c>%x?</c> an attribute.
/// </summary>
/// <remarks>
/// <para>Those servers treat a code as a change of state written into the text. SharpMUSH colours spans,
/// so the rewrite reads a body into the units its evaluator works in (each command's arguments, each
/// function argument, each <c>[...]</c> and <c>{...}</c>) and turns every run of text a code starts within
/// one unit into <c>ansi(codes,text)</c>: bare at the start of a function or command argument, in brackets
/// anywhere else. A code runs until <c>%cn</c>, the next code, or the end of its unit; codes combine
/// (<c>%ch%cr</c> is <c>hr</c>). Text moved into an argument keeps reading the same: <c>,</c> <c>(</c>
/// <c>)</c> are escaped and a run of spaces beside a code becomes <c>%b</c>. A call whose coloured
/// arguments hold only codes picks the colour for the text after it (<c>%ch[ifelse(x,%cc,%cr)]%0%cn</c>
/// becomes <c>ansi(h[ifelse(x,c,r)],%0)</c>).</para>
/// <para>What cannot be carried over exactly is rewritten as far as these rules go and named in
/// <see cref="ColourRewriteResult.Flags"/>: colour kept in a register, a reset or an <c>ansi()</c> inside a
/// call under open colour, colour that leaks out of a call into the caller, and (Rhost) codes read from a
/// register. A code that cannot be rewritten at all, one assembled at run time or one with no colour after it,
/// is removed, since SharpMUSH would read it as the last command or an attribute; that is named too. A body whose
/// brackets do not balance is left as it is.</para>
/// <para>The rules and their checks against live TinyMUX and RhostMUSH servers are in the colour
/// substitution design notes; this is a port of that prototype.</para>
/// </remarks>
public static partial class ColourSubstitutionRewriter
{
	/// <summary>Flag: a colour code is stored in a register, so it is data used elsewhere.</summary>
	public const string CodeInRegister = "colour-code-in-register";

	/// <summary>Flag: a <c>%cn</c> inside a call ends the caller's colour on the source server, not here.</summary>
	public const string InnerResetUnderOpenColour = "inner-reset-under-open-colour";

	/// <summary>Flag: an <c>ansi()</c> inside open colour ends it on the source server, not here.</summary>
	public const string InnerAnsiUnderOpenColour = "inner-ansi-under-open-colour";

	/// <summary>Flag: colour opened in a call's argument coloured the caller's text after it on the source server.</summary>
	public const string ColourLeaksOutOfCall = "colour-leaks-out-of-call";

	/// <summary>Flag: codes at the end of a unit with no text after them were dropped.</summary>
	public const string TrailingCodesDropped = "trailing-codes-dropped";

	/// <summary>
	/// Flag: a code whose colour comes from a call or a substitution (<c>%c0X[printf(...)]</c>), which no rewrite
	/// can see. It was removed with what it reads, so the text after it is not coloured.
	/// </summary>
	public const string ComputedColourCode = "computed-colour-code-removed";

	/// <summary>Flag (RhostMUSH): a <c>%c</c> or <c>%x</c> with no colour after it, removed.</summary>
	public const string UnconvertibleColourCode = "unconvertible-colour-code-removed";

	/// <summary>Flag (RhostMUSH): a code read from a register, right only if the register holds code letters.</summary>
	public const string ColourCodeFromRegister = "colour-code-from-register";

	/// <summary>Flag: the body's brackets do not balance, so it was left as it is.</summary>
	public const string UnbalancedLeftAlone = "unbalanced-left-alone";

	/// <summary>
	/// The body with its colour substitutions rewritten into <c>ansi()</c>, read as TinyMUX writes them and
	/// as <see cref="ColourRewriteMode.Auto"/> decides.
	/// </summary>
	public static string Rewrite(string softcode) => Rewrite(softcode, new ColourRewriteOptions()).Text;

	/// <summary>The body rewritten under <paramref name="options"/>, with what could not be carried over exactly.</summary>
	public static ColourRewriteResult Rewrite(string softcode, ColourRewriteOptions options)
	{
		if (!ColourCode().IsMatch(softcode))
		{
			return new ColourRewriteResult(softcode, false, []);
		}

		var rhost = options.Dialect == ColourSubstitutionDialect.RhostMush;
		var mode = options.Mode == ColourRewriteMode.Auto ? ModeOf(softcode) : options.Mode;
		var (prefix, body) = mode == ColourRewriteMode.CommandList ? SplitPattern(softcode) : (string.Empty, softcode);

		var parser = new Parser(body, rhost);
		var root = parser.Parse(mode == ColourRewriteMode.CommandList);
		if (!root.Balanced)
		{
			return new ColourRewriteResult(softcode, false, [UnbalancedLeftAlone]);
		}

		var writer = new Writer();
		var text = prefix + writer.Unit(root, false);
		if (parser.Computed)
		{
			writer.Flags.Add(ComputedColourCode);
		}

		if (parser.Unconvertible)
		{
			writer.Flags.Add(UnconvertibleColourCode);
		}

		if (rhost && RegisterCode().IsMatch(body))
		{
			writer.Flags.Add(ColourCodeFromRegister);
		}

		return new ColourRewriteResult(text, text != softcode, [.. writer.Flags.Order(StringComparer.Ordinal)]);
	}

	/// <summary>The command words that make <see cref="ColourRewriteMode.Auto"/> read a body as a command list.</summary>
	private static readonly HashSet<string> CommandWords = new(StringComparer.OrdinalIgnoreCase)
	{
		"think", "say", "pose", "semipose", "emit", "page", "whisper", "look", "home", "drop", "get", "take",
		"give", "enter", "leave", "use", "kill", "go", "goto", "move", "mail", "inventory", "score"
	};

	private static ColourRewriteMode ModeOf(string body)
	{
		var trimmed = body.TrimStart();
		if (trimmed.Length == 0)
		{
			return ColourRewriteMode.Expression;
		}

		if ((trimmed[0] is '$' or '^') && UnescapedColon(trimmed) > 0)
		{
			return ColourRewriteMode.CommandList;
		}

		if (trimmed[0] is '@' or '&')
		{
			return ColourRewriteMode.CommandList;
		}

		var end = trimmed.IndexOfAny([' ', ';', '=']);
		var word = end < 0 ? trimmed : trimmed[..end];
		return CommandWords.Contains(word) ? ColourRewriteMode.CommandList : ColourRewriteMode.Expression;
	}

	/// <summary>A <c>$</c>/<c>^</c> pattern and its colon, kept as they are, and the action after them.</summary>
	private static (string Prefix, string Body) SplitPattern(string softcode)
	{
		var lead = softcode.Length - softcode.TrimStart().Length;
		if (lead >= softcode.Length || softcode[lead] is not ('$' or '^'))
		{
			return (string.Empty, softcode);
		}

		var colon = UnescapedColon(softcode);
		return colon < 0 ? (string.Empty, softcode) : (softcode[..(colon + 1)], softcode[(colon + 1)..]);
	}

	private static int UnescapedColon(string text)
	{
		for (var i = 0; i < text.Length; i++)
		{
			switch (text[i])
			{
				case '\\':
					i++;
					break;
				case ':':
					return i;
			}
		}

		return -1;
	}

	/// <summary>A code: <c>%c</c> or <c>%x</c> (either case) and its colour.</summary>
	[GeneratedRegex(@"\G%([cCxX])(<[^>]*>|0[xX][0-9a-fA-F]{2}|%[qQ][0-9a-zA-Z]|[nhufixrgybmcwXRGYBMCW])")]
	private static partial Regex Code();

	/// <summary>Anything that could be a code, to leave a body without one untouched.</summary>
	[GeneratedRegex("%[cCxX]")]
	private static partial Regex ColourCode();

	[GeneratedRegex(@"\G%[cCxX]([a-zA-Z])")]
	private static partial Regex UnknownCode();

	[GeneratedRegex(@"\G%[cCxX](0[xX])?(?=[\[%])")]
	private static partial Regex ComputedCode();

	/// <summary>A substitution a computed code reads its colour from: a register, an argument, a named argument.</summary>
	[GeneratedRegex(@"\G%([qQiIvVwW][0-9a-zA-Z]|<[^>]*>|.)")]
	private static partial Regex Substitution();

	[GeneratedRegex("%[cCxX]%[qQ]")]
	private static partial Regex RegisterCode();

	[GeneratedRegex(@"\G[A-Za-z_][A-Za-z0-9_.]*\(")]
	private static partial Regex FunctionName();

	[GeneratedRegex(@"\G[^ ;=\[\]{}]+ ?")]
	private static partial Regex CommandWord();

	[GeneratedRegex("^#?[0-9a-fA-F]{6}$")]
	private static partial Regex HexColour();

	[GeneratedRegex(@"^\d+ \d+ \d+$")]
	private static partial Regex RgbColour();

	private enum UnitKind { Top, Arg, CmdArg, Bracket, Brace, CmdBrace }

	private enum ItemKind { Text, Code, Reset, Separator, Child }

	/// <summary>A colour code: whether it was written <c>%C</c>/<c>%X</c>, and what follows the letter.</summary>
	private readonly record struct ColourCodeText(bool Upper, string Value);

	private readonly record struct Item(ItemKind Kind, string Text = "", ColourCodeText Code = default, Node? Child = null);

	private abstract class Node;

	/// <summary>One evaluation unit: a command argument, a function argument, a bracket or brace body.</summary>
	private sealed class Unit(UnitKind kind) : Node
	{
		public UnitKind Kind { get; set; } = kind;
		public List<Item> Items { get; } = [];
		public string Command { get; init; } = string.Empty;
		public bool AfterEquals { get; init; }
		public bool Balanced { get; set; }
	}

	private sealed class Bracket : Node
	{
		public Unit Body { get; } = new(UnitKind.Bracket);
	}

	private sealed class Brace : Node
	{
		public Unit Body { get; } = new(UnitKind.Brace);
	}

	private sealed class Function(string name) : Node
	{
		public string Name { get; } = name;
		public List<Unit> Args { get; } = [new(UnitKind.Arg)];
		public int Depth { get; set; }
	}

	/// <summary>Commands whose arguments after <c>=</c> split at each <c>,</c> as well.</summary>
	private static readonly HashSet<string> CommaCommands = ["@switch", "@sw", "@select", "@trigger", "@tr", "@verb"];

	/// <summary>Calls whose output goes somewhere other than the text, so they never pick a colour.</summary>
	private static readonly HashSet<string> SideEffects =
		["setq", "setqm", "setrm", "setr", "set", "pemit", "remit", "oemit", "emit", "lemit", "nsemit", "trigger", "attrib_set", "wipe"];

	private static readonly HashSet<string> Registers = ["setq", "setr", "setqm", "setrm"];

	private sealed class Parser(string body, bool rhost)
	{
		private readonly List<Node> _stack = [];
		private int _i;

		public bool Computed { get; private set; }

		public bool Unconvertible { get; private set; }

		public Unit Parse(bool commands)
		{
			var root = new Unit(commands ? UnitKind.Top : UnitKind.Arg);
			_stack.Add(root);
			if (commands)
			{
				StartCommand(root);
			}

			while (_i < body.Length)
			{
				Step(commands);
			}

			root.Balanced = _stack.Count == 1 || (_stack.Count == 2 && _stack[1] is Unit { Kind: UnitKind.CmdArg });
			return root;
		}

		private void StartCommand(Unit container)
		{
			var j = _i;
			while (j < body.Length && body[j] == ' ') j++;
			var word = CommandWord().Match(body, j) is { Success: true } match ? match.Value : string.Empty;
			container.Items.Add(new Item(ItemKind.Text, body[_i..j] + word));
			_i = j + word.Length;
			var arg = new Unit(UnitKind.CmdArg) { Command = word.Trim().Split('/')[0].ToLowerInvariant() };
			container.Items.Add(new Item(ItemKind.Child, Child: arg));
			_stack.Add(arg);
		}

		private void Pop() => _stack.RemoveAt(_stack.Count - 1);

		private void Step(bool commands)
		{
			var top = _stack[^1];
			var unit = top as Unit ?? ((Function)top).Args[^1];
			var ch = body[_i];

			if (top is Unit { Kind: UnitKind.CmdArg } argument)
			{
				if (ch == ';')
				{
					Pop();
					var parent = (Unit)_stack[^1];
					parent.Items.Add(new Item(ItemKind.Separator, ";"));
					_i++;
					StartCommand(parent);
					return;
				}

				if ((ch == '=' && !argument.AfterEquals) || (ch == ',' && argument.AfterEquals && CommaCommands.Contains(argument.Command)))
				{
					Pop();
					var parent = (Unit)_stack[^1];
					parent.Items.Add(new Item(ItemKind.Separator, ch.ToString()));
					_i++;
					var next = new Unit(UnitKind.CmdArg) { Command = argument.Command, AfterEquals = true };
					parent.Items.Add(new Item(ItemKind.Child, Child: next));
					_stack.Add(next);
					return;
				}
			}

			switch (ch)
			{
				case '\\' when _i + 1 < body.Length:
					unit.Items.Add(new Item(ItemKind.Text, body.Substring(_i, 2)));
					_i += 2;
					return;

				case '%':
					Percent(unit);
					return;

				case '[':
					var bracket = new Bracket();
					unit.Items.Add(new Item(ItemKind.Child, Child: bracket));
					_stack.Add(bracket.Body);
					_i++;
					return;

				case ']' when _stack.Exists(node => node is Unit { Kind: UnitKind.Bracket }):
					while (_stack[^1] is not Unit { Kind: UnitKind.Bracket }) Pop();
					Pop();
					_i++;
					return;

				case '{':
					var brace = new Brace();
					unit.Items.Add(new Item(ItemKind.Child, Child: brace));
					_stack.Add(brace.Body);
					_i++;
					if (unit.Kind == UnitKind.CmdArg && commands)
					{
						brace.Body.Kind = UnitKind.CmdBrace;
						StartCommand(brace.Body);
					}

					return;

				case '}' when _stack.Exists(node => node is Unit { Kind: UnitKind.Brace or UnitKind.CmdBrace }):
					while (_stack[^1] is not Unit { Kind: UnitKind.Brace or UnitKind.CmdBrace }) Pop();
					Pop();
					_i++;
					return;
			}

			if (FunctionName().Match(body, _i) is { Success: true } name && !Started(unit)
				&& unit.Kind is UnitKind.Arg or UnitKind.Bracket or UnitKind.CmdArg)
			{
				var function = new Function(name.Value[..^1]);
				unit.Items.Add(new Item(ItemKind.Child, Child: function));
				_stack.Add(function);
				_i += name.Length;
				return;
			}

			if (top is Function call)
			{
				switch (ch)
				{
					case '(':
						call.Depth++;
						unit.Items.Add(new Item(ItemKind.Text, "("));
						_i++;
						return;
					case ')' when call.Depth > 0:
						call.Depth--;
						unit.Items.Add(new Item(ItemKind.Text, ")"));
						_i++;
						return;
					case ',' when call.Depth == 0:
						call.Args.Add(new Unit(UnitKind.Arg));
						_i++;
						return;
					case ')':
						Pop();
						_i++;
						return;
				}
			}

			if (unit.Items.Count > 0 && unit.Items[^1] is { Kind: ItemKind.Text } last && !last.Text.StartsWith('\\') && !last.Text.StartsWith('%'))
			{
				unit.Items[^1] = last with { Text = last.Text + ch };
			}
			else
			{
				unit.Items.Add(new Item(ItemKind.Text, ch.ToString()));
			}

			_i++;
		}

		private void Percent(Unit unit)
		{
			if (Code().Match(body, _i) is { Success: true } code)
			{
				var value = code.Groups[2].Value;
				if (rhost || value[0] is not ('0' or '%'))
				{
					unit.Items.Add(value is "n" or "N"
						? new Item(ItemKind.Reset)
						: new Item(ItemKind.Code, Code: new ColourCodeText(char.IsUpper(code.Groups[1].Value[0]), value)));
					_i += code.Length;
					return;
				}
			}

			if (ComputedCode().Match(body, _i) is { Success: true } computed)
			{
				// What the call or substitution returns is code letters, which would print as text without the code.
				Computed = true;
				_i = EndOfOperand(_i + computed.Length);
				return;
			}

			if (UnknownCode().Match(body, _i) is { Success: true } unknown)
			{
				// TinyMUX prints an unknown colour letter as itself; RhostMUSH leaves the whole code as text.
				unit.Items.Add(new Item(ItemKind.Text, rhost ? "\\" + unknown.Value : unknown.Groups[1].Value));
				_i += unknown.Length;
				return;
			}

			if (body.Length > _i + 1 && body[_i + 1] is 'c' or 'C' or 'x' or 'X')
			{
				// TinyMUX prints what follows a code it cannot read, as here; RhostMUSH prints the code too, which
				// SharpMUSH would read as the last command. A code at the end is a reset on both.
				Unconvertible |= rhost && _i + 2 < body.Length;
				_i += 2;
				return;
			}

			unit.Items.Add(new Item(ItemKind.Text, body.Substring(_i, Math.Min(2, body.Length - _i))));
			_i += 2;
		}

		/// <summary>Where the bracketed call or the substitution starting at <paramref name="start"/> ends.</summary>
		private int EndOfOperand(int start)
		{
			if (body[start] == '%')
			{
				return start + Substitution().Match(body, start).Length;
			}

			var depth = 0;
			for (var i = start; i < body.Length; i++)
			{
				switch (body[i])
				{
					case '\\':
						i++;
						break;
					case '[':
						depth++;
						break;
					case ']' when --depth == 0:
						return i + 1;
				}
			}

			return body.Length;
		}

		private static bool Started(Unit unit) => unit.Items.Exists(item => !(item.Kind == ItemKind.Text && string.IsNullOrWhiteSpace(item.Text)));
	}

	/// <summary>The colour state open at a point in a unit: attributes, foreground, background, register codes.</summary>
	private sealed class Style
	{
		private string? _fg;
		private string? _bg;
		private readonly List<string> _attributes = [];

		public List<string> Dynamic { get; } = [];

		public bool Active => _fg is not null || _bg is not null || _attributes.Count > 0 || Dynamic.Count > 0;

		/// <summary>Whether colour shows on a space: a background, underline or inverse, or a code this cannot see.</summary>
		public bool VisibleOnSpace => _bg is not null || _attributes.Contains("u") || _attributes.Contains("i") || Dynamic.Count > 0;

		public void Apply(ColourCodeText code)
		{
			var letters = Letters(code);
			if (letters.StartsWith('%'))
			{
				Dynamic.Add(letters);
				return;
			}

			if (letters is "h" or "u" or "f" or "i")
			{
				if (!_attributes.Contains(letters)) _attributes.Add(letters);
			}
			else if (letters.StartsWith('/') || (letters.Length == 1 && char.IsUpper(letters[0])))
			{
				_bg = letters;
			}
			else
			{
				_fg = letters;
			}
		}

		public string Codes()
		{
			var output = new StringBuilder();
			foreach (var part in _attributes.Concat(_fg is null ? [] : [_fg]).Concat(_bg is null ? [] : [_bg]))
			{
				var lastSegment = output.ToString().Split(' ')[^1];
				if (output.Length > 0 && (part.Length > 1 || lastSegment.Length > 1))
				{
					output.Append(' ');
				}

				output.Append(part);
			}

			return output + string.Concat(Dynamic);
		}

		private static string Letters(ColourCodeText code)
		{
			var value = code.Value;
			if (value[0] == '0')
			{
				// RhostMUSH xterm: %c0x46 foreground, %c0X15 background, by hex index.
				var xterm = $"+xterm{Convert.ToInt32(value[2..], 16)}";
				return value[1] == 'X' ? "/" + xterm : xterm;
			}

			if (value[0] == '%')
			{
				return value;
			}

			if (value.StartsWith('<'))
			{
				var inner = value[1..^1].Trim();
				var colour = HexColour().IsMatch(inner) ? "#" + inner.TrimStart('#')
					: RgbColour().IsMatch(inner) ? $"<{inner}>"
					: "+" + inner;
				return code.Upper ? "/" + colour : colour;
			}

			return code.Upper ? value.ToUpperInvariant() : value;
		}
	}

	private sealed class Writer
	{
		public HashSet<string> Flags { get; } = [];

		private string Child(Node node, bool codesMode) => node switch
		{
			Bracket bracket => "[" + Unit(bracket.Body, codesMode) + "]",
			Brace brace => "{" + Unit(brace.Body, codesMode) + "}",
			Unit unit => Unit(unit, codesMode),
			Function function => function.Name + "(" + string.Join(",", function.Args.Select(arg => Unit(arg, codesMode))) + ")",
			_ => throw new InvalidOperationException("Unknown node")
		};

		public string Unit(Unit unit, bool codesMode)
		{
			if (codesMode && CodeOnly(unit))
			{
				var codes = new Style();
				var last = string.Empty;
				foreach (var item in unit.Items)
				{
					switch (item.Kind)
					{
						case ItemKind.Code:
							codes.Apply(item.Code);
							break;
						case ItemKind.Reset:
							codes = new Style();
							last = "n";
							break;
					}
				}

				var letters = codes.Codes();
				return letters.Length > 0 ? letters : last;
			}

			var output = new StringBuilder();
			var buffer = new StringBuilder();
			var style = new Style();
			var afterCode = false;
			// A code-producing call no text has followed yet: if none does, the call is dropped.
			var unusedCall = false;

			void DropUnusedCall()
			{
				if (unusedCall) Flags.Add(TrailingCodesDropped);
				unusedCall = false;
			}

			void Flush(bool atCode = false)
			{
				if (buffer.Length == 0) return;

				var raw = buffer.ToString();
				var wrapped = style.Active;
				var atStart = string.IsNullOrWhiteSpace(output.ToString()) && !afterCode;
				if (!(atStart && string.IsNullOrWhiteSpace(raw)))
				{
					raw = EdgeSpaces(raw, (afterCode || wrapped) && !atStart, atCode || wrapped);
				}

				if (style.Active && ((!string.IsNullOrWhiteSpace(raw) && !string.IsNullOrWhiteSpace(raw.Replace("%b", string.Empty)))
						|| (style.VisibleOnSpace && raw.Length > 0)))
				{
					var call = $"ansi({style.Codes()},{raw})";
					var bare = (unit.Kind is UnitKind.Arg or UnitKind.CmdArg) && string.IsNullOrWhiteSpace(output.ToString());
					output.Append(bare ? call : "[" + call + "]");
				}
				else
				{
					output.Append(raw);
				}

				buffer.Clear();
				afterCode = false;
				unusedCall = false;
			}

			for (var index = 0; index < unit.Items.Count; index++)
			{
				var item = unit.Items[index];
				switch (item.Kind)
				{
					case ItemKind.Code:
						Flush(true);
						style.Apply(item.Code);
						afterCode = true;
						break;

					case ItemKind.Reset:
						Flush(true);
						DropUnusedCall();
						style = new Style();
						afterCode = true;
						break;

					case ItemKind.Separator:
						Flush();
						DropUnusedCall();
						style = new Style();
						afterCode = false;
						output.Append(item.Text);
						break;

					case ItemKind.Text:
						buffer.Append(style.Active ? EscapeText(item.Text) : item.Text);
						break;

					case ItemKind.Child:
						var child = item.Child!;
						if (style.Active && child is Function { Name: var name } && name.Equals("ansi", StringComparison.OrdinalIgnoreCase))
						{
							Flags.Add(InnerAnsiUnderOpenColour);
						}

						if (style.Active && UnitsOf(child).Any(HasReset) && !CodeProducing(child))
						{
							Flags.Add(InnerResetUnderOpenColour);
						}

						if (CodeProducing(child) && !codesMode)
						{
							Flush(true);
							afterCode = true;
							style.Dynamic.Add(Child(child, true));
							unusedCall = true;
							break;
						}

						if (child is not Unit { Kind: UnitKind.CmdArg })
						{
							if (UnitsOf(child).Any(TrailingOpen) && RestHasText(unit.Items, index + 1))
							{
								Flags.Add(ColourLeaksOutOfCall);
							}

							if (child is Function function && Registers.Contains(function.Name.ToLowerInvariant())
								&& function.Args.Exists(arg => HasCode(arg) && !UnitHasText(arg)))
							{
								Flags.Add(CodeInRegister);
							}
						}

						buffer.Append(Child(child, codesMode));
						break;
				}
			}

			if (style.Active && buffer.Length == 0 && unit.Kind is not (UnitKind.Top or UnitKind.CmdArg) && RestIsCodesTail(unit.Items))
			{
				Flags.Add(TrailingCodesDropped);
			}

			Flush();
			DropUnusedCall();
			return output.ToString();
		}
	}

	private static string EscapeText(string text)
	{
		var output = new StringBuilder(text.Length);
		for (var i = 0; i < text.Length; i++)
		{
			if ((text[i] is '\\' or '%') && i + 1 < text.Length)
			{
				output.Append(text, i, 2);
				i++;
				continue;
			}

			if (text[i] is ',' or '(' or ')')
			{
				output.Append('\\');
			}

			output.Append(text[i]);
		}

		return output.ToString();
	}

	/// <summary>A run of spaces beside a colour code is one space once the source compresses it; it stays one as %b.</summary>
	private static string EdgeSpaces(string text, bool left, bool right)
	{
		var lead = text.Length - text.TrimStart(' ').Length;
		var trail = text.Length - text.TrimEnd(' ').Length;
		if (lead == text.Length)
		{
			return left || right ? "%b" : text;
		}

		return (lead > 0 && left ? "%b" : text[..lead])
			+ text[lead..(text.Length - trail)]
			+ (trail > 0 && right ? "%b" : text[(text.Length - trail)..]);
	}

	private static IEnumerable<Unit> UnitsOf(Node node) => node switch
	{
		Unit unit => [unit],
		Function function => function.Args,
		Bracket bracket => [bracket.Body],
		Brace brace => [brace.Body],
		_ => []
	};

	private static bool HasCode(Unit unit) => unit.Items.Exists(item => item.Kind is ItemKind.Code or ItemKind.Reset);

	private static bool CodeOnly(Unit unit)
		=> HasCode(unit) && unit.Items.TrueForAll(item =>
			item.Kind is ItemKind.Code or ItemKind.Reset || (item.Kind == ItemKind.Text && string.IsNullOrWhiteSpace(item.Text)));

	private static bool UnitHasText(Unit unit)
		=> unit.Items.Exists(item => (item.Kind == ItemKind.Text && !string.IsNullOrWhiteSpace(item.Text))
			|| (item.Kind == ItemKind.Child && ChildHasText(item.Child!)));

	/// <summary>Whether a child adds text; a call always may, since its output cannot be seen.</summary>
	private static bool ChildHasText(Node node) => node switch
	{
		Function => true,
		Unit unit => UnitHasText(unit),
		Bracket bracket => UnitHasText(bracket.Body),
		Brace brace => UnitHasText(brace.Body),
		_ => false
	};

	private static bool HasReset(Unit unit)
		=> unit.Items.Exists(item => item.Kind == ItemKind.Reset || (item.Kind == ItemKind.Child && UnitsOf(item.Child!).Any(HasReset)));

	/// <summary>A call or bracket whose only coloured output is bare codes, as in <c>%ch[ifelse(x,%cc,%cr)]</c>.</summary>
	private static bool CodeProducing(Node node)
	{
		if (node is Unit { Kind: UnitKind.CmdArg })
		{
			return false;
		}

		var found = false;
		return Walk(node) && found;

		bool Walk(Node current)
		{
			if (current is Function function && SideEffects.Contains(function.Name.ToLowerInvariant()))
			{
				return false;
			}

			foreach (var unit in UnitsOf(current))
			{
				if (HasCode(unit))
				{
					if (!CodeOnly(unit)) return false;
					found = true;
				}
				else if (CodesBesideText(unit))
				{
					return false;
				}

				if (unit.Items.Where(item => item.Kind == ItemKind.Child).Any(item => !Walk(item.Child!)))
				{
					return false;
				}
			}

			return true;
		}
	}

	/// <summary>
	/// A unit whose codes come from a call inside it, beside text of its own, as in
	/// <c>if(x,%c0x00,%c0xEF)%c0X[printf(...)]</c>: it writes text, not only codes.
	/// </summary>
	private static bool CodesBesideText(Unit unit)
		=> unit.Items.Exists(item => item.Kind == ItemKind.Child && ContainsCode(item.Child!))
			&& unit.Items.Exists(item => (item.Kind == ItemKind.Text && !string.IsNullOrWhiteSpace(item.Text))
				|| (item.Kind == ItemKind.Child && !ContainsCode(item.Child!)));

	private static bool ContainsCode(Node node)
		=> UnitsOf(node).Any(unit => HasCode(unit) || unit.Items.Exists(item => item.Kind == ItemKind.Child && ContainsCode(item.Child!)));

	private static bool TrailingOpen(Unit unit)
	{
		var active = false;
		var textAfter = false;
		foreach (var item in unit.Items)
		{
			switch (item.Kind)
			{
				case ItemKind.Code:
					active = true;
					textAfter = false;
					break;
				case ItemKind.Reset or ItemKind.Separator:
					active = false;
					break;
				case ItemKind.Text when !string.IsNullOrWhiteSpace(item.Text):
				case ItemKind.Child when ChildHasText(item.Child!):
					textAfter = true;
					break;
			}
		}

		return active && textAfter;
	}

	private static bool RestHasText(List<Item> items, int from)
	{
		for (var i = from; i < items.Count; i++)
		{
			var item = items[i];
			if (item.Kind is ItemKind.Code or ItemKind.Reset or ItemKind.Separator) return false;
			if ((item.Kind == ItemKind.Text && !string.IsNullOrWhiteSpace(item.Text))
				|| (item.Kind == ItemKind.Child && ChildHasText(item.Child!))) return true;
		}

		return false;
	}

	private static bool RestIsCodesTail(List<Item> items)
	{
		for (var i = items.Count - 1; i >= 0; i--)
		{
			var item = items[i];
			if (item.Kind == ItemKind.Code) return true;
			if (item.Kind == ItemKind.Text && string.IsNullOrWhiteSpace(item.Text)) continue;
			return false;
		}

		return false;
	}
}
