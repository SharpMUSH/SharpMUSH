using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Services;

/// <summary>
/// What a <c>name(...)</c> does with the text between its parentheses. This — not "is it a function" —
/// is what decides whether a line break inside the call is safe, because the delimiters absorb the
/// whitespace that follows them and only a construct that <em>evaluates</em> its contents throws that
/// whitespace away.
/// </summary>
public enum SoftcodeCallKind
{
	/// <summary>
	/// Resolves to nothing the parser will dispatch. <c>LiteralFunctionCall</c> reproduces the call as
	/// text, slicing its <c>FUNCHAR</c>, <c>COMMAWS</c> and <c>CPAREN</c> terminals verbatim from the
	/// source, so none of those may be broken at. Its arguments <em>are</em> still visited, so a
	/// <c>[...]</c> inside re-enables evaluation and may be broken at.
	/// </summary>
	Unresolved,

	/// <summary>
	/// An ordinary function: its arguments are evaluated, so its delimiters are structural and the
	/// whitespace they absorb never reaches the output. Includes <c>NoParse</c> functions with more
	/// than one argument, whose deferred text is sliced from each argument's own start index and so
	/// excludes the preceding delimiter's whitespace.
	/// </summary>
	EvaluatesArguments,

	/// <summary>
	/// Copies the source between its parentheses instead of evaluating it — <c>Literal</c> via
	/// <c>LiteralArgumentText</c>, or <c>NoParse</c> with one argument via the whole-context
	/// <c>MModule.substring</c>. <b>Nothing</b> inside such a call may be broken at, not even a
	/// bracket, because no visitor ever runs over that span to discard the whitespace.
	/// </summary>
	CopiesArgumentSource
}

/// <summary>
/// A single line break in a softcode rendering: after emitting <paramref name="TokenIndex"/>'s text
/// <em>with its trailing whitespace trimmed</em>, emit <c>\n</c> followed by <paramref name="Indent"/> spaces.
/// </summary>
/// <param name="TokenIndex">Index into the token list the break follows.</param>
/// <param name="Indent">Number of spaces to open the next line with.</param>
public readonly record struct SoftcodeBreak(int TokenIndex, int Indent);

/// <summary>
/// Decides where a line break may be inserted when displaying MUSH softcode.
/// <para>
/// Softcode is whitespace-significant: whitespace is literal data almost everywhere. Seven lexer
/// rules carry <c>fragment WS: [ \r\n\f\t]*</c> and so swallow the whitespace that follows them —
/// <c>OBRACK</c>, <c>OBRACE</c>, <c>COMMAWS</c>, <c>EQUALS</c>, <c>SEMICOLON</c>, <c>OPAREN</c> and
/// <c>FUNCHAR</c>. That absorption is <b>not</b> on its own a licence to break.
/// <c>VisitBeginGenericText</c> emits <c>GetContextText</c> — the raw token text, absorbed
/// whitespace included — so wherever one of those tokens is not acting as a structural delimiter,
/// its whitespace is literal program data and a newline inserted there changes the output.
/// </para>
/// <para>
/// A break is therefore emitted only where the token is <em>structural</em>:
/// </para>
/// <list type="bullet">
/// <item><description>
/// After a <c>FUNCHAR</c> that opens a call which <b>evaluates</b> what is between its parentheses.
/// <c>FUNCHAR</c> is the only way <c>SharpMUSHParser.g4</c> opens a <c>function</c> (see its
/// <c>function:</c> rule), but that is not sufficient, and neither is "the name resolves":
/// <c>LiteralFunctionCall</c> reproduces an unresolved call from its terminals sliced verbatim, and
/// <c>LiteralArgumentText</c> reproduces a <c>Literal</c> call's contents from a raw source span. Both
/// carry the absorbed whitespace into the output. So the caller classifies each name — see
/// <see cref="SoftcodeCallKind"/>. A bare <c>OPAREN</c> reaches the parser through
/// <c>beginGenericText</c> and is plain text, so it is neither a group nor a break position.
/// </description></item>
/// <item><description>
/// After a <c>COMMAWS</c> whose immediately-enclosing group is opened by such a <c>FUNCHAR</c> — that
/// is, a genuine argument separator. A comma anywhere else is prose.
/// </description></item>
/// <item><description>
/// After a <c>SEMICOLON</c> at root <em>of text that will be parsed as a command list</em>. Only
/// <c>startCommandString</c> sets <c>inCommandList</c> (<c>SharpMUSHParser.g4:29</c>), and only under
/// that flag does <c>commandList</c> (<c>:55</c>) treat <c>;</c> as a separator; in every other
/// dialect <c>beginGenericText</c> (<c>:158</c>) claims it as text. A <c>;</c> inside a function's
/// arguments is literal in all dialects.
/// </description></item>
/// <item><description>
/// After an <c>OBRACK</c>. Confirmed by the equivalence corpus (Ruling 7 settled): unlike
/// <c>OPAREN</c>, <c>CPAREN</c>, <c>SEMICOLON</c>, <c>COMMAWS</c> and <c>EQUALS</c>, a <c>[</c> has no
/// text-position reading at all — it appears nowhere in <c>beginGenericText</c>, only in
/// <c>bracketPattern</c>, whose visitor discards the token's text.
/// </description></item>
/// </list>
/// <para>
/// Two further rules, neither of which may be weakened:
/// </para>
/// <list type="number">
/// <item><description>
/// Never break immediately before a closing delimiter. There is no whitespace absorption at
/// <c>CPAREN</c>, <c>CBRACK</c> or <c>CBRACE</c>, so a newline there becomes literal text inside the
/// final argument. Closers cuddle the last item.
/// </description></item>
/// <item><description>
/// Never break inside a brace group, nor inside a <see cref="SoftcodeCallKind.CopiesArgumentSource"/>
/// call. Brace contents are literal in some contexts and re-parsed as code in others, and a
/// source-copying call's contents are never visited at all, so both are atomic here: always rendered
/// flat, never recursed into.
/// </description></item>
/// </list>
/// <para>
/// <c>EQUALS</c> and <c>OBRACE</c> absorb whitespace too, and are structural in some positions, but
/// v1 never breaks at either.
/// </para>
/// </summary>
public static class SoftcodeLayout
{
	/// <summary>
	/// Token types that open a group. <c>OPAREN</c> is deliberately absent: the grammar opens
	/// <c>function</c> on <c>FUNCHAR</c> alone, so a bare <c>(</c> is text, not structure.
	/// </summary>
	private static readonly string[] Openers = ["FUNCHAR", "OBRACK", "OBRACE"];

	private static readonly string[] Closers = ["CPAREN", "CBRACK", "CBRACE"];

	/// <summary>
	/// The opener type a closing token may close, or <c>null</c> if the type is not a closer. A closer
	/// only ever matches its own kind: <c>)</c> closes a call, <c>]</c> a bracket group, <c>}</c> a
	/// brace group. Anything else is literal text.
	/// </summary>
	private static string? OpenerClosedBy(string closerType) => closerType switch
	{
		"CPAREN" => "FUNCHAR",
		"CBRACK" => "OBRACK",
		"CBRACE" => "OBRACE",
		_ => null
	};

	/// <summary>
	/// Computes the breaks needed to fit <paramref name="tokens"/> within <paramref name="width"/> columns.
	/// </summary>
	/// <param name="tokens">The lexed softcode, in source order.</param>
	/// <param name="width">Target line width in columns.</param>
	/// <param name="indentUnit">Columns of indent added per nesting level.</param>
	/// <param name="classifyFunction">
	/// Classifies a function name — see <see cref="SoftcodeCallKind"/>. Use
	/// <see cref="ClassifierFor"/> rather than assembling one; it is the tested path.
	/// <para>
	/// <b>Omitting this is the safe choice, not the convenient one:</b> the default classifies every
	/// name as <see cref="SoftcodeCallKind.CopiesArgumentSource"/>, the most restrictive answer, so a
	/// caller with no classifier renders every call flat rather than guessing optimistically.
	/// </para>
	/// <para>
	/// <b>An over-conservative answer is no longer free.</b> Under the two-state predicate this replaced,
	/// wrongly saying "not a function" only ever cost breaks. It no longer does:
	/// <see cref="SoftcodeCallKind.Unresolved"/> permits breaking at a <c>[...]</c> inside the call,
	/// which is correct for a genuinely unresolved name — <c>LiteralFunctionCall</c> visits its
	/// arguments, so a bracket really is evaluated — but would leak whitespace for a
	/// <see cref="SoftcodeCallKind.CopiesArgumentSource"/> name misreported as unresolved. A partial
	/// classifier must therefore fall back to <see cref="SoftcodeCallKind.CopiesArgumentSource"/>, not
	/// to <see cref="SoftcodeCallKind.Unresolved"/>.
	/// </para>
	/// </param>
	/// <param name="parseType">
	/// The dialect the text will be evaluated as — the same value <c>SharpAttribute.SyntaxParseType()</c>
	/// returns for a <c>CMDSYNTAX</c>/<c>FUNSYNTAX</c>-flagged attribute, and the same one
	/// <c>IMUSHCodeParser.ValidateAndGetErrors</c> takes. It decides one thing here: whether a root
	/// <c>;</c> separates commands or is literal text.
	/// <para>
	/// <b>The default is the conservative dialect,</b> matching every other <c>ParseType</c> parameter
	/// in the codebase: <see cref="ParseType.Function"/> emits no semicolon breaks at all.
	/// </para>
	/// </param>
	/// <returns>The breaks, in ascending token order. Empty when everything fits flat.</returns>
	public static IReadOnlyList<SoftcodeBreak> Compute(IReadOnlyList<TokenInfo> tokens, int width,
		int indentUnit = 2, Func<string, SoftcodeCallKind>? classifyFunction = null,
		ParseType parseType = ParseType.Function)
	{
		if (tokens.Count == 0)
		{
			return [];
		}

		var effectiveWidth = Math.Max(1, width);
		var effectiveIndentUnit = Math.Max(0, indentUnit);
		var root = BuildGroupTree(tokens, classifyFunction ?? (_ => SoftcodeCallKind.CopiesArgumentSource),
			SeparatesCommands(parseType));
		MeasureFlat(root, tokens);

		var breaks = new List<SoftcodeBreak>();
		Layout(root, tokens, depth: 0, column: 0, effectiveWidth, effectiveIndentUnit, breaks);

		return breaks;
	}

	/// <summary>
	/// Classifies a <em>resolved</em> function from its declaration, so that every caller building a
	/// <c>classifyFunction</c> delegate applies one rule rather than three divergent copies of it.
	/// <para>
	/// The two source-copying branches of <c>SharpMUSHParserVisitor.CallFunction</c> are:
	/// <c>Literal</c> (<c>:795-803</c>), whose single argument is <c>LiteralArgumentText</c> — the raw
	/// span from just past the <c>(</c> to the <c>)</c>, so every delimiter's absorbed whitespace is
	/// inside it — and <c>NoParse</c> with <c>MaxArgs == 1</c> (<c>:818-826</c>), which returns
	/// <c>MModule.substring</c> over the whole function context.
	/// </para>
	/// <para>
	/// <c>NoParse</c> with more than one argument is <b>not</b> in that set (<c>:827-839</c>): each
	/// argument's deferred text is <c>GetContextText(x)</c>, sliced from that argument's own
	/// <c>Start.StartIndex</c>, which begins after the preceding <c>COMMAWS</c> and so excludes the
	/// whitespace it absorbed. <c>switch</c> (<c>MaxArgs = int.MaxValue</c>) and <c>iter</c>
	/// (<c>MaxArgs = 4</c>) are both in that branch and both round-trip in the equivalence corpus.
	/// </para>
	/// </summary>
	public static SoftcodeCallKind Classify(SharpFunctionAttribute attribute) =>
		attribute.Flags.HasFlag(FunctionFlags.Literal)
		|| (attribute.Flags.HasFlag(FunctionFlags.NoParse) && attribute.MaxArgs == 1)
			? SoftcodeCallKind.CopiesArgumentSource
			: SoftcodeCallKind.EvaluatesArguments;

	/// <summary>
	/// The classifier to pass to <see cref="Compute"/>. Resolves names exactly as
	/// <c>SharpMUSHParserVisitor.CallFunction</c> does and in the same order — the parser's
	/// <c>FunctionLibrary</c> first, then the <c>@function</c> registry — and classifies a hit with
	/// <see cref="Classify"/>.
	/// <para>
	/// This exists so that no caller hand-writes that ladder. <see cref="Classify"/> alone is only half
	/// the rule: the third state comes from resolution failing, and three callers reimplementing "look
	/// here, then there, else Unresolved" is exactly the divergence a shared classifier prevents.
	/// </para>
	/// <para>
	/// The <c>FunctionLibrary</c> lookup is deliberately a plain one, matching <c>CallFunction</c>:
	/// <c>FunctionLibraryService</c> is constructed <c>OrdinalIgnoreCase</c>, so case is handled by the
	/// dictionary rather than by folding here. <c>DiscoverBuiltInFunction</c> reads the very same
	/// dictionary, so there is no lazily-registered built-in this misses. A user-defined entry is
	/// synthesized with <c>Flags = FunctionFlags.Regular</c>, so it always evaluates its arguments.
	/// </para>
	/// </summary>
	public static Func<string, SoftcodeCallKind> ClassifierFor(IMUSHCodeParser parser) =>
		name => parser.FunctionLibrary.TryGetValue(name, out var entry)
			? Classify(entry.LibraryInformation.Attribute)
			: parser.ServiceProvider.GetService<IUserDefinedFunctionService>()?.Resolve(name) is not null
				? SoftcodeCallKind.EvaluatesArguments
				: SoftcodeCallKind.Unresolved;

	/// <summary>
	/// Whether a root-level <c>;</c> is a command separator in this dialect, and so a break position.
	/// <para>
	/// Only <see cref="ParseType.CommandList"/>. The grammar's <c>inCommandList</c> flag is set by
	/// exactly one start rule — <c>startCommandString</c> (<c>SharpMUSHParser.g4:29</c>), which
	/// <c>MUSHCodeParser</c> selects for <see cref="ParseType.CommandList"/> alone — and it is the only
	/// thing that stops <c>beginGenericText</c> (<c>:158</c>,
	/// <c>{ !inCommandList || inBraceDepth &gt; 0 }?</c>) from claiming the <c>;</c> as text.
	/// </para>
	/// <para>
	/// <see cref="ParseType.Command"/> does <b>not</b> qualify despite the name:
	/// <c>startSingleCommandString</c> is <c>command EOF</c> and never enters <c>commandList</c>, so a
	/// <c>;</c> in a single command is literal. Nor do the argument-splitting dialects
	/// (<c>startPlainSingleCommandArg</c>, <c>startPlainCommaCommandArgs</c>,
	/// <c>startEqSplitCommandArgs</c>, <c>startEqSplitCommand</c>), which split on <c>,</c> and
	/// <c>=</c> and leave <c>;</c> alone.
	/// </para>
	/// </summary>
	private static bool SeparatesCommands(ParseType parseType) => parseType == ParseType.CommandList;

	/// <summary>
	/// The name in a <c>FUNCHAR</c> token, which the lexer builds as <c>name '(' WS</c>. Matches what
	/// <c>VisitFunction</c> looks up (<c>FUNCHAR().GetText().TrimEnd()[..^1]</c>) while tolerating a
	/// token that is not shaped that way at all.
	/// </summary>
	private static string FunctionName(string funCharText)
	{
		var openParen = funCharText.IndexOf('(');

		return openParen <= 0 ? string.Empty : funCharText[..openParen];
	}

	/// <summary>
	/// Walks the tokens with a stack, from a synthetic root group, recording each group's structural
	/// break points as it goes. Unbalanced input never throws: a closer with nothing but the root on
	/// the stack is ignored, and groups still open at end of input close implicitly at the last token.
	/// <para>
	/// Also tracks whether function recognition is live at each point, mirroring
	/// <c>_suppressFunctionEval</c> in <c>SharpMUSHParserVisitor</c>: an unresolved call switches it
	/// off for everything inside it (:505-511 routes nested calls to <c>LiteralFunctionCall</c> too),
	/// and a <c>[...]</c> switches it back on (<c>VisitBracketPattern</c>, :2457-2461). A call with
	/// recognition off is text, so it opens no break positions.
	/// </para>
	/// </summary>
	private static Group BuildGroupTree(IReadOnlyList<TokenInfo> tokens,
		Func<string, SoftcodeCallKind> classifyFunction, bool semicolonsSeparateCommands)
	{
		var root = new Group(openIndex: -1, openType: string.Empty);
		var stack = new Stack<Group>();
		stack.Push(root);

		for (var i = 0; i < tokens.Count; i++)
		{
			var type = tokens[i].Type;
			if (Array.IndexOf(Openers, type) >= 0)
			{
				var enclosingSuppresses = stack.Peek().SuppressesFunctions;
				var kind = type == "FUNCHAR"
					? classifyFunction(FunctionName(tokens[i].Text))
					: SoftcodeCallKind.EvaluatesArguments;

				// Exhaustive over SoftcodeCallKind with no discard arm, deliberately: a member added later
				// must be CS8509 here — an error under TreatWarningsAsErrors — rather than falling through
				// to (false, false), which is the non-atomic, unsafe direction. A discard arm, even one
				// that threw, would defer that to run time.
				//
				// CS8524 is the *other* exhaustiveness diagnostic: an int cast to an undeclared enum value.
				// That cannot arise here — kind comes from our own classifier delegate — and suppressing it
				// is what leaves CS8509 free to fire on a genuinely new member.
#pragma warning disable CS8524
				var (isTextAtItsDelimiters, copiesSource) = kind switch
				{
					// Reproduced from its terminals, so its own delimiters are text; but LiteralFunctionCall
					// still visits each argument, so a [...] inside really is evaluated and stays breakable.
					SoftcodeCallKind.Unresolved => (true, false),
					SoftcodeCallKind.EvaluatesArguments => (false, false),
					// Never visited at all, so the whole span is atomic — brackets included.
					SoftcodeCallKind.CopiesArgumentSource => (true, true)
				};
#pragma warning restore CS8524

				var child = new Group(i, type)
				{
					SuppressesFunctions = type switch
					{
						// A bracket re-enables function recognition, however deeply it is buried — but only
						// where a visitor actually runs over it, which CopiesSource above rules out.
						"OBRACK" => false,
						// Anything but a call that evaluates its arguments is text at its own delimiters.
						"FUNCHAR" => enclosingSuppresses || isTextAtItsDelimiters,
						// Braces are never recursed into, so their own state is never consulted.
						_ => enclosingSuppresses
					},
					CopiesSource = copiesSource
				};
				stack.Peek().Children.Add(child);
				stack.Push(child);
			}
			else if (OpenerClosedBy(type) is { } closedOpener)
			{
				// A closer that does not match the innermost group is text, not structure. bracePattern
				// (SharpMUSHParser.g4:96) resets inFunction, so a stray ')' inside {...} is plain text to
				// the grammar — popping the brace group on it would hand the brace's own commas to the
				// enclosing call and make them break points inside literal text. Ignore such a closer,
				// exactly as a closer that would unwind the root is ignored.
				if (stack.Count > 1 && stack.Peek().OpenType == closedOpener)
				{
					stack.Pop().CloseIndex = i;
				}
			}
			else if (type == "COMMAWS")
			{
				// Only an argument separator inside a name(...) the parser will dispatch. Elsewhere — at
				// root, inside a bracket or brace group, or inside a call that is being reproduced as
				// text — the comma is prose and its absorbed whitespace is literal.
				if (stack.Peek().OpenType == "FUNCHAR" && !stack.Peek().SuppressesFunctions)
				{
					stack.Peek().BreakPoints.Add(i);
				}
			}
			else if (type == "SEMICOLON")
			{
				// Only a command separator at root, and only in the command-list dialect. Inside a
				// function's arguments, or in any other dialect, a ';' is literal text whose absorbed
				// whitespace VisitBeginGenericText emits.
				if (semicolonsSeparateCommands && stack.Peek().OpenIndex < 0)
				{
					stack.Peek().BreakPoints.Add(i);
				}
			}
		}

		var last = tokens.Count - 1;
		while (stack.Count > 0)
		{
			stack.Pop().CloseIndex = last;
		}

		return root;
	}

	/// <summary>
	/// Bottom-up measurement of each group rendered on one line. Attribute text may contain literal
	/// newlines, so this records both the column a flat rendering ends on and whether it spans lines.
	/// </summary>
	private static void MeasureFlat(Group group, IReadOnlyList<TokenInfo> tokens)
	{
		foreach (var child in group.Children)
		{
			MeasureFlat(child, tokens);
		}

		var start = group.OpenIndex < 0 ? 0 : group.OpenIndex;
		var column = 0;
		var hasNewline = false;
		for (var i = start; i <= group.CloseIndex; i++)
		{
			var text = tokens[i].Text;
			hasNewline |= text.Contains('\n');
			column = Advance(column, text);
		}

		group.FlatWidth = column;
		group.HasNewline = hasNewline;
	}

	/// <summary>
	/// Advances a column past a token's text. Attributes have held literal newlines since PR #775, so
	/// a token carrying one starts a fresh line and the column becomes the width of that final line
	/// rather than an accumulation across the whole token.
	/// </summary>
	private static int Advance(int column, string text)
	{
		var lastNewline = text.LastIndexOf('\n');

		return lastNewline < 0
			? column + text.Length
			: text.Length - lastNewline - 1;
	}

	/// <summary>
	/// Renders one group top-down, returning the column the following text starts at.
	/// </summary>
	private static int Layout(Group group, IReadOnlyList<TokenInfo> tokens, int depth, int column, int width,
		int indentUnit, List<SoftcodeBreak> breaks)
	{
		// A brace group and a source-copying call are both atomic: their contents reach the output as
		// raw source, so no break anywhere inside them is safe — not even at a bracket, since no visitor
		// runs over that span to discard the whitespace a delimiter absorbed.
		// Anything else stays flat only if it is genuinely one line and that line fits.
		if (group.IsAtomic || (!group.HasNewline && column + group.FlatWidth <= width))
		{
			return group.HasNewline ? group.FlatWidth : column + group.FlatWidth;
		}

		var indent = Math.Min(depth * indentUnit, width / 2);
		var start = group.OpenIndex < 0 ? 0 : group.OpenIndex;

		// Breaking immediately before a closer would put a literal newline inside the last argument, so
		// the last real content token never carries a break. Trailing closers are skipped rather than
		// assumed to be exactly one: a group closed implicitly by end of input has none, and the root
		// may end in any number of stray ones.
		var lastContent = group.CloseIndex;
		while (lastContent >= start && Array.IndexOf(Closers, tokens[lastContent].Type) >= 0)
		{
			lastContent--;
		}

		var i = start;
		if (group.OpenIndex >= 0)
		{
			// The synthetic root has no opening delimiter and so emits no opener break; it breaks only
			// at its own structural separators. An empty group emits none either — there is nothing to
			// put on the next line but the closer. Nor does a call the parser will reproduce as text,
			// whose FUNCHAR is sliced from the source with its absorbed whitespace intact.
			if (group.OpenIndex + 1 < lastContent && !group.SuppressesFunctions)
			{
				breaks.Add(new SoftcodeBreak(group.OpenIndex, indent));
				column = indent;
			}
			else
			{
				column = Advance(column, tokens[group.OpenIndex].Text);
			}

			i++;
		}

		var breakPoints = group.BreakPoints;
		var breakCursor = 0;
		var childCursor = 0;

		while (i <= group.CloseIndex)
		{
			if (childCursor < group.Children.Count && group.Children[childCursor].OpenIndex == i)
			{
				var child = group.Children[childCursor++];
				column = Layout(child, tokens, depth + 1, column, width, indentUnit, breaks);
				i = child.CloseIndex + 1;
				continue;
			}

			column = Advance(column, tokens[i].Text);

			while (breakCursor < breakPoints.Count && breakPoints[breakCursor] < i)
			{
				breakCursor++;
			}

			if (breakCursor < breakPoints.Count && breakPoints[breakCursor] == i && i < lastContent)
			{
				breakCursor++;
				breaks.Add(new SoftcodeBreak(i, indent));
				column = indent;
			}

			i++;
		}

		return column;
	}

	/// <summary>A delimiter-bounded span of tokens. The root has no delimiters and covers everything.</summary>
	private sealed class Group(int openIndex, string openType)
	{
		/// <summary>Index of the opening token, or -1 for the synthetic root.</summary>
		public int OpenIndex { get; } = openIndex;

		/// <summary>Token type of the opener, or empty for the synthetic root.</summary>
		public string OpenType { get; } = openType;

		/// <summary>Index of the last token in the group, inclusive — the closer when there is one.</summary>
		public int CloseIndex { get; set; }

		/// <summary>
		/// Indices of this group's own structural separators, ascending. Only separators that are
		/// genuinely delimiters in this group's context are recorded; prose commas and literal
		/// semicolons never reach here.
		/// </summary>
		public List<int> BreakPoints { get; } = [];

		/// <summary>Nested groups, in source order.</summary>
		public List<Group> Children { get; } = [];

		/// <summary>Column a flat rendering of the group ends on, measured from column 0.</summary>
		public int FlatWidth { get; set; }

		/// <summary>Whether the group's text already contains a literal newline.</summary>
		public bool HasNewline { get; set; }

		/// <summary>
		/// Whether function recognition is off inside this group — and, for a <c>FUNCHAR</c> group,
		/// whether the group is itself being reproduced as text rather than dispatched. A group in that
		/// state opens no break positions: its delimiters' absorbed whitespace reaches the output.
		/// </summary>
		public bool SuppressesFunctions { get; init; }

		/// <summary>
		/// Whether this group is a call whose contents are copied from the source rather than evaluated
		/// (<see cref="SoftcodeCallKind.CopiesArgumentSource"/>).
		/// </summary>
		public bool CopiesSource { get; init; }

		/// <summary>
		/// Whether the group is rendered flat unconditionally, so that nothing inside it is ever broken.
		/// </summary>
		public bool IsAtomic => OpenType == "OBRACE" || CopiesSource;
	}
}
