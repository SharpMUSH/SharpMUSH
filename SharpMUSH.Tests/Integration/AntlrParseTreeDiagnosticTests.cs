using System.Text;
using Antlr4.Runtime;
using Antlr4.Runtime.Atn;
using Antlr4.Runtime.Dfa;
using Antlr4.Runtime.Tree;
using SharpMUSH.Implementation;

namespace SharpMUSH.Tests.Integration;

/// <summary>
/// Diagnostic tests to show ANTLR4 parse tree output and check for Full Context Scans.
/// These tests parse raw input through the ANTLR4 parser directly (no visitor evaluation)
/// to inspect the parse tree structure and prediction behavior.
/// </summary>
public class AntlrParseTreeDiagnosticTests
{
	/// <summary>
	/// Custom error listener that captures Full Context Scan attempts and separates
	/// DiagnosticErrorListener reports from actual syntax errors.
	/// </summary>
	private sealed class FullContextScanListener : BaseErrorListener
	{
		public List<string> FullContextAttempts { get; } = [];
		public List<string> ContextSensitivities { get; } = [];
		public List<string> Ambiguities { get; } = [];
		public List<string> RealSyntaxErrors { get; } = [];

		public override void SyntaxError(
			TextWriter output, IRecognizer recognizer, IToken offendingSymbol,
			int line, int charPositionInLine, string msg, RecognitionException e)
		{
			// DiagnosticErrorListener reports through SyntaxError with messages like
			// "reportAttemptingFullContext" and "reportAmbiguity" - these are NOT real errors.
			// Filter them out.
			if (msg.StartsWith("report", StringComparison.Ordinal))
				return;

			RealSyntaxErrors.Add($"  Line {line}:{charPositionInLine} - {msg}");
		}

		public override void ReportAmbiguity(
			Antlr4.Runtime.Parser recognizer, DFA dfa, int startIndex, int stopIndex,
			bool exact, Antlr4.Runtime.Sharpen.BitSet ambigAlts, ATNConfigSet configs)
		{
			var ruleName = recognizer.RuleNames[dfa.atnStartState.ruleIndex];
			Ambiguities.Add($"  Ambiguity in rule '{ruleName}' at [{startIndex}..{stopIndex}], exact={exact}");
		}

		public override void ReportAttemptingFullContext(
			Antlr4.Runtime.Parser recognizer, DFA dfa, int startIndex, int stopIndex,
			Antlr4.Runtime.Sharpen.BitSet conflictingAlts, ATNConfigSet configs)
		{
			var ruleName = recognizer.RuleNames[dfa.atnStartState.ruleIndex];
			FullContextAttempts.Add($"  ⚠️ FULL CONTEXT SCAN in rule '{ruleName}' at [{startIndex}..{stopIndex}]");
		}

		public override void ReportContextSensitivity(
			Antlr4.Runtime.Parser recognizer, DFA dfa, int startIndex, int stopIndex,
			int prediction, ATNConfigSet configs)
		{
			var ruleName = recognizer.RuleNames[dfa.atnStartState.ruleIndex];
			ContextSensitivities.Add(
				$"  Context sensitivity in rule '{ruleName}' at [{startIndex}..{stopIndex}], prediction={prediction}");
		}
	}

	/// <summary>
	/// Custom trace listener that captures rule entry/exit events for parse tree analysis.
	/// </summary>
	private sealed class TraceCapture : IParseTreeListener
	{
		private readonly SharpMUSHParser _parser;
		private int _depth;
		public StringBuilder Output { get; } = new();

		public TraceCapture(SharpMUSHParser parser)
		{
			_parser = parser;
		}

		public void EnterEveryRule(ParserRuleContext ctx)
		{
			var ruleName = _parser.RuleNames[ctx.RuleIndex];
			var indent = new string(' ', _depth * 2);
			Output.AppendLine($"{indent}enter {ruleName}");
			_depth++;
		}

		public void ExitEveryRule(ParserRuleContext ctx)
		{
			_depth--;
			var ruleName = _parser.RuleNames[ctx.RuleIndex];
			var indent = new string(' ', _depth * 2);
			Output.AppendLine($"{indent}exit  {ruleName} → \"{ctx.GetText()}\"");
		}

		public void VisitTerminal(ITerminalNode node)
		{
			var indent = new string(' ', _depth * 2);
			var tokenName = _parser.Vocabulary.GetSymbolicName(node.Symbol.Type);
			Output.AppendLine($"{indent}TOKEN {tokenName} = \"{node.GetText()}\"");
		}

		public void VisitErrorNode(IErrorNode node)
		{
			var indent = new string(' ', _depth * 2);
			Output.AppendLine($"{indent}ERROR {node.GetText()}");
		}
	}

	/// <summary>
	/// Result of parsing with diagnostics.
	/// </summary>
	private record DiagnosticResult(
		string FullOutput,
		int FullContextScanCount,
		int AmbiguityCount,
		int RealSyntaxErrorCount,
		string ParseTree);

	/// <summary>
	/// Parses input text and returns diagnostic information including:
	/// - Token stream
	/// - Parse tree (ToStringTree)
	/// - Rule trace (entry/exit)
	/// - Full Context Scan detection
	/// - Syntax errors (real ones only, excluding DiagnosticErrorListener reports)
	/// </summary>
	private static DiagnosticResult ParseAndDiagnose(string input, PredictionMode predictionMode)
	{
		var sb = new StringBuilder();
		sb.AppendLine($"INPUT: \"{input}\"");
		sb.AppendLine($"PREDICTION MODE: {predictionMode}");
		sb.AppendLine(new string('─', 70));

		var inputStream = new AntlrInputStream(input);
		var lexer = new SharpMUSHLexer(inputStream);
		var tokenStream = new CommonTokenStream(lexer);
		tokenStream.Fill();

		sb.AppendLine("TOKEN STREAM:");
		var tokens = tokenStream.GetTokens();
		for (var i = 0; i < tokens.Count; i++)
		{
			var token = tokens[i];
			var tokenName = lexer.Vocabulary.GetSymbolicName(token.Type);
			if (token.Type == TokenConstants.EOF) tokenName = "EOF";
			sb.AppendLine($"  [{i}] {tokenName,-20} = \"{token.Text}\"  (pos {token.StartIndex}..{token.StopIndex})");
		}

		sb.AppendLine();

		tokenStream.Seek(0);
		var parser = new SharpMUSHParser(tokenStream);
		parser.Interpreter.PredictionMode = predictionMode;

		var fullContextListener = new FullContextScanListener();
		parser.RemoveErrorListeners();
		parser.AddErrorListener(fullContextListener);

		parser.AddErrorListener(new DiagnosticErrorListener(false));

		var traceCapture = new TraceCapture(parser);
		parser.AddParseListener(traceCapture);

		// Parse as function (startPlainString entry point, same as FunctionParse)
		var context = parser.StartPlainString();

		var parseTree = context.ToStringTree(parser);

		sb.AppendLine("PARSE TREE (ToStringTree):");
		sb.AppendLine($"  {parseTree}");
		sb.AppendLine();

		sb.AppendLine("RULE TRACE (entry/exit with text):");
		sb.AppendLine(traceCapture.Output.ToString());

		sb.AppendLine("FULL CONTEXT SCAN REPORT:");
		if (fullContextListener.FullContextAttempts.Count == 0)
		{
			sb.AppendLine("  ✅ No Full Context Scans detected");
		}
		else
		{
			sb.AppendLine($"  ⚠️ {fullContextListener.FullContextAttempts.Count} Full Context Scan(s) detected:");
			foreach (var attempt in fullContextListener.FullContextAttempts)
				sb.AppendLine(attempt);
		}

		sb.AppendLine();

		sb.AppendLine("AMBIGUITY REPORT:");
		if (fullContextListener.Ambiguities.Count == 0)
		{
			sb.AppendLine("  ✅ No ambiguities detected");
		}
		else
		{
			sb.AppendLine($"  ⚠️ {fullContextListener.Ambiguities.Count} ambiguity(ies) detected:");
			foreach (var ambiguity in fullContextListener.Ambiguities)
				sb.AppendLine(ambiguity);
		}

		sb.AppendLine();

		sb.AppendLine("CONTEXT SENSITIVITY REPORT:");
		if (fullContextListener.ContextSensitivities.Count == 0)
		{
			sb.AppendLine("  ✅ No context sensitivities detected");
		}
		else
		{
			sb.AppendLine($"  {fullContextListener.ContextSensitivities.Count} context sensitivity(ies):");
			foreach (var cs in fullContextListener.ContextSensitivities)
				sb.AppendLine(cs);
		}

		sb.AppendLine();

		sb.AppendLine("REAL SYNTAX ERRORS:");
		if (fullContextListener.RealSyntaxErrors.Count == 0)
		{
			sb.AppendLine("  ✅ No syntax errors");
		}
		else
		{
			sb.AppendLine($"  ❌ {fullContextListener.RealSyntaxErrors.Count} error(s):");
			foreach (var error in fullContextListener.RealSyntaxErrors)
				sb.AppendLine(error);
		}

		return new DiagnosticResult(
			sb.ToString(),
			fullContextListener.FullContextAttempts.Count,
			fullContextListener.Ambiguities.Count,
			fullContextListener.RealSyntaxErrors.Count,
			parseTree);
	}

	/// <summary>
	/// Shows the parse tree for ulambda(lit(#lambda/add(1,2)))
	/// 
	/// PennMUSH-compatible behavior (paren_groups off):
	/// The lexer tokenizes #lambda/add as OTHER and ( as OPAREN (not FUNCHAR).
	/// This means there are only 2 function opens (ulambda, lit) but 4 tokens 
	/// that could be CPARENs. The first ) after "2" closes lit(): inside a call,
	/// the function__Call_* rules take ) as the call's closer, never as text, and
	/// a bare ( opens no group of its own.
	///
	/// Parse tree analysis:
	/// - Token [3] OPAREN "(" → consumed as beginGenericText (just text)
	/// - Token [5] COMMAWS "," → lit()'s 2nd argument separator
	/// - Token [7] CPAREN ")" → inside lit(), whose rule copies do not list ) as text
	///   → closes lit()
	/// - Token [8] CPAREN ")" → closes ulambda()
	/// - Token [9] CPAREN ")" → back in a Top_R* rule, where ) is text → generic text
	///
	/// Note: To pass add(1,2) literally to lit(), use escaped parens:
	///   ulambda(#lambda/add\(1\,2\)) — produces "3"
	/// Or bracket evaluation:
	///   ulambda(#lambda/[add(1,2)]) — produces "3"
	/// </summary>
	[Test]
	public async Task ParseTree_UlambdaLitLambdaAdd()
	{
		var input = "ulambda(lit(#lambda/add(1,2)))";

		TestDiagnostics.WriteLine("╔══════════════════════════════════════════════════════════════════════╗");
		TestDiagnostics.WriteLine("║  PARSE TREE ANALYSIS: ulambda(lit(#lambda/add(1,2)))                ║");
		TestDiagnostics.WriteLine("║  PennMUSH-compatible: ) always closes innermost function            ║");
		TestDiagnostics.WriteLine("╚══════════════════════════════════════════════════════════════════════╝");
		TestDiagnostics.WriteLine();

		TestDiagnostics.WriteLine("═══════════════════ LL MODE ═══════════════════");
		var llResult = ParseAndDiagnose(input, PredictionMode.LL);
		TestDiagnostics.WriteLine(llResult.FullOutput);

		TestDiagnostics.WriteLine("═══════════════════ SLL MODE ═══════════════════");
		var sllResult = ParseAndDiagnose(input, PredictionMode.SLL);
		TestDiagnostics.WriteLine(sllResult.FullOutput);

		await Assert.That(llResult.RealSyntaxErrorCount).IsEqualTo(0)
			.Because("Should parse without syntax errors - extra ) becomes generic text");
		await Assert.That(sllResult.RealSyntaxErrorCount).IsEqualTo(0)
			.Because("Should parse without syntax errors in SLL mode too");

		await Assert.That(llResult.ParseTree).IsEqualTo(sllResult.ParseTree)
			.Because("LL and SLL modes should produce identical parse trees for this input");

		await Assert.That(llResult.ParseTree).Contains("function")
			.Because("parse tree should contain function rules for ulambda and lit");
	}

	/// <summary>
	/// Shows the parse tree for the inner expression that lit() receives:
	/// #lambda/add(1,2)
	/// 
	/// When parsed standalone (outside a function), ( and ) are always generic text
	/// because the Top_R* rules list ) as text. This produces the full text "#lambda/add(1,2)".
	/// </summary>
	[Test]
	public async Task ParseTree_InnerExpression()
	{
		var input = "#lambda/add(1,2)";

		TestDiagnostics.WriteLine("╔══════════════════════════════════════════════════════════════════════╗");
		TestDiagnostics.WriteLine("║  FIX C INNER EXPRESSION: #lambda/add(1,2)                          ║");
		TestDiagnostics.WriteLine("╚══════════════════════════════════════════════════════════════════════╝");
		TestDiagnostics.WriteLine();

		var result = ParseAndDiagnose(input, PredictionMode.LL);
		TestDiagnostics.WriteLine(result.FullOutput);

		await Assert.That(result.RealSyntaxErrorCount).IsEqualTo(0)
			.Because("This expression should parse cleanly");
	}

	/// <summary>
	/// Shows the parse tree for bare parens inside a function:
	/// lit((text))
	/// 
	/// Without paren depth tracking (PennMUSH-compatible):
	/// - ( → OPAREN, just generic text (opens no group)
	/// - ) after "text" → CPAREN, in a function__Call_* rule so NOT generic text → closes lit()
	/// - Final ) → CPAREN, back in a Top_R* rule so generic text (trailing paren)
	///
	/// In PennMUSH, ) always closes the innermost function. Bare ( doesn't create
	/// a matching scope. Use escaped parens \(\) or bracket evaluation [...] instead.
	/// </summary>
	[Test]
	public async Task ParseTree_BareParensInFunction()
	{
		var input = "lit((text))";

		TestDiagnostics.WriteLine("╔══════════════════════════════════════════════════════════════════════╗");
		TestDiagnostics.WriteLine("║  BARE PARENS IN FUNCTION: lit((text))                               ║");
		TestDiagnostics.WriteLine("║  PennMUSH-compatible: ) always closes innermost function            ║");
		TestDiagnostics.WriteLine("╚══════════════════════════════════════════════════════════════════════╝");
		TestDiagnostics.WriteLine();

		var result = ParseAndDiagnose(input, PredictionMode.LL);
		TestDiagnostics.WriteLine(result.FullOutput);

		// With PennMUSH-compatible behavior:
		// The ) after "text" closes lit(), the final ) is extra generic text.
		// This may produce a parser error (extraneous input) since the first ) closes
		// the function leaving text behind, but it should not produce a syntax error
		// in the parse itself since the trailing ) becomes generic text.
		await Assert.That(result.RealSyntaxErrorCount).IsEqualTo(0)
			.Because(
				"Bare parens inside functions should parse without syntax errors - ) closes the function, extra ) is generic text");
	}

	/// <summary>
	/// Full Context Scan analysis across common MUSH patterns.
	/// Documents which patterns trigger Full Context Scans and why.
	/// 
	/// Full Context Scans occur in LL mode where SLL prediction cannot settle a
	/// decision on its own. The grammar has no semantic predicates (each rule exists
	/// once per context), so a scan is not expected by design, nor a bug by itself.
	/// </summary>
	[Test]
	public async Task FullContextScan_Analysis()
	{
		var testCases = new[]
		{
			("add(1,2)", "Simple function"),
			("lit(hello world)", "Function with space-separated args"),
			("ulambda(lit(#lambda/add(1,2)))", "Nested functions (PennMUSH: ) closes innermost)"),
			(@"ulambda(#lambda/add\(1\,2\))", "Escaped parens"),
			("ulambda(#lambda/[add(1,2)])", "Bracket evaluation"),
			("ulambda(#lambda/3)", "Simple lambda"),
			("lit((text))", "Bare parens in function"),
			("strcat(a,b,c)", "Multi-arg function"),
			("switch(1,1,yes,no)", "Switch function"),
			("if(1,yes,no)", "If function"),
// Extra trailing parens - these should be consumed as generic text
			("ulambda(lit(#lambda/add(1,2))))", "One extra trailing paren"),
			("ulambda(lit(#lambda/add(1,2)))))", "Two extra trailing parens"),
		};

		TestDiagnostics.WriteLine("╔══════════════════════════════════════════════════════════════════════╗");
		TestDiagnostics.WriteLine("║  FULL CONTEXT SCAN ANALYSIS: Common MUSH Patterns                  ║");
		TestDiagnostics.WriteLine("╚══════════════════════════════════════════════════════════════════════╝");
		TestDiagnostics.WriteLine();

		var syntaxErrorInputs = new List<string>();
		var fullContextScanInputs = new List<(string Input, string Description, int Count)>();

		foreach (var (input, description) in testCases)
		{
			var result = ParseAndDiagnose(input, PredictionMode.LL);
			TestDiagnostics.WriteLine($"─── {description}: {input} ───");

			if (result.RealSyntaxErrorCount > 0)
			{
				TestDiagnostics.WriteLine($"  ❌ {result.RealSyntaxErrorCount} SYNTAX ERROR(S)");
				syntaxErrorInputs.Add(input);
			}
			else
			{
				TestDiagnostics.WriteLine("  ✅ No syntax errors");
			}

			if (result.FullContextScanCount > 0)
			{
				TestDiagnostics.WriteLine($"  ⚠️ {result.FullContextScanCount} Full Context Scan(s)");
				fullContextScanInputs.Add((input, description, result.FullContextScanCount));
			}
			else
			{
				TestDiagnostics.WriteLine("  ✅ No Full Context Scans");
			}

			if (result.AmbiguityCount > 0)
			{
				TestDiagnostics.WriteLine($"  ℹ️ {result.AmbiguityCount} Ambiguity report(s)");
			}

			TestDiagnostics.WriteLine();
		}

		TestDiagnostics.WriteLine(new string('═', 70));
		TestDiagnostics.WriteLine("SUMMARY");
		TestDiagnostics.WriteLine(new string('═', 70));
		TestDiagnostics.WriteLine($"Total patterns tested: {testCases.Length}");
		TestDiagnostics.WriteLine($"Patterns with syntax errors: {syntaxErrorInputs.Count}");
		TestDiagnostics.WriteLine($"Patterns with Full Context Scans: {fullContextScanInputs.Count}");
		if (fullContextScanInputs.Count > 0)
		{
			TestDiagnostics.WriteLine("\nFull Context Scan details:");
			foreach (var (input, description, count) in fullContextScanInputs)
			{
				TestDiagnostics.WriteLine($"  {description}: \"{input}\" ({count} scan(s))");
			}

			TestDiagnostics.WriteLine("\nNOTE: The grammar has no semantic predicates; each rule exists once per context.");
			TestDiagnostics.WriteLine("A Full Context Scan means SLL prediction could not settle a decision");
			TestDiagnostics.WriteLine("and ANTLR4 fell back to full LL context for it.");
			TestDiagnostics.WriteLine("That is NOT a performance bug by itself.");
		}

		await Assert.That(syntaxErrorInputs).IsEmpty()
			.Because("All common MUSH patterns should parse without syntax errors");
	}

	/// <summary>
	/// Shows detailed ANTLR4 parse tree output for extra trailing parens.
	/// Demonstrates that after function closure, remaining ) tokens become generic text
	/// appended to the output.
	///
	/// PennMUSH-compatible: ) always closes the innermost function, no paren matching.
	/// ulambda(lit(#lambda/add(1,2))) has bare ( in add(, so ) closes lit() early.
	/// The remaining ) close ulambda and become extra generic text.
	/// </summary>
	[Test]
	public async Task ParseTree_ExtraTrailingParens()
	{
		var testCases = new[]
		{
			("ulambda(lit(#lambda/add(1,2))))", "One extra trailing paren"),
			("ulambda(lit(#lambda/add(1,2)))))", "Two extra trailing parens"),
		};

		foreach (var (input, description) in testCases)
		{
			TestDiagnostics.WriteLine($"╔══════════════════════════════════════════════════════════════════════╗");
			TestDiagnostics.WriteLine($"║  EXTRA TRAILING PARENS: {description,-44} ║");
			TestDiagnostics.WriteLine($"╚══════════════════════════════════════════════════════════════════════╝");
			TestDiagnostics.WriteLine();

			TestDiagnostics.WriteLine("═══════════════════ LL MODE ═══════════════════");
			var llResult = ParseAndDiagnose(input, PredictionMode.LL);
			TestDiagnostics.WriteLine(llResult.FullOutput);

			TestDiagnostics.WriteLine("═══════════════════ SLL MODE ═══════════════════");
			var sllResult = ParseAndDiagnose(input, PredictionMode.SLL);
			TestDiagnostics.WriteLine(sllResult.FullOutput);

			await Assert.That(llResult.RealSyntaxErrorCount).IsEqualTo(0)
				.Because($"'{input}' should parse without syntax errors - extra ) become generic text");
			await Assert.That(sllResult.RealSyntaxErrorCount).IsEqualTo(0)
				.Because($"'{input}' should parse without syntax errors in SLL mode too");
			await Assert.That(llResult.ParseTree).IsEqualTo(sllResult.ParseTree)
				.Because("LL and SLL modes should produce identical parse trees");
		}
	}

	/// <summary>
	/// Analysis of BBS line 57 pattern with bare parentheses before bracket patterns.
	///
	/// With paren_groups off (PennMUSH-compatible): bare ( is just text, and ) always
	/// closes the innermost function. No paren state can leak because none is kept:
	/// whether ) is text depends only on which rule copy the token lands in.
	///
	/// Minimal reproduction: "(text [name(%0)])"
	/// 1. "(" → OPAREN, just generic text
	/// 2. "[" → OBRACK, enters bracketPattern
	/// 3. "name(" → FUNCHAR, call begins; its argument parses in function__Call_* rules
	/// 4. "%0" → substitution
	/// 5. ")" → CPAREN, in a function__Call_* rule so NOT generic text → closes name() correctly
	/// 6. "]" → CBRACK, exits bracketPattern
	/// 7. ")" → CPAREN, back in a Top_R* rule so generic text → trailing paren text
	/// </summary>
	[Test]
	public async Task Line57_BareParensBeforeBrackets_Analysis()
	{
		// Minimal reproduction patterns — from simplest to line 57 fragment
		var testCases = new[]
		{
			("(x [name(%0)])", "Bare paren before bracket function"),

			("(text [name(%0)] more)", "Bare paren text with bracket function"),

			("(text [add(1,2)] and [name(%0)])", "Two bracket functions after bare paren"),

			("(text [ifelse(1,name(%0),name(%1))])", "Nested function in bracket after bare paren"),

			// BBS line 57 fragment — the actual failing content
			("(New BB message ([member(v(groups),%1)]/%2) posted to '[name(%1)]' by [ifelse(hasattr(%1,anonymous),get(%1/anonymous),name(%0))]: %3)",
				"BBS line 57 paren section"),
		};

		TestDiagnostics.WriteLine("╔══════════════════════════════════════════════════════════════════════╗");
		TestDiagnostics.WriteLine("║  LINE 57 ANALYSIS: Bare Parens Before Brackets (PennMUSH-compatible)║");
		TestDiagnostics.WriteLine("╚══════════════════════════════════════════════════════════════════════╝");
		TestDiagnostics.WriteLine();

		var results = new List<(string Input, string Desc, DiagnosticResult Result)>();

		foreach (var (input, description) in testCases)
		{
			TestDiagnostics.WriteLine($"═══════════════════════════════════════════════════════════════");
			TestDiagnostics.WriteLine($"  TEST: {description}");
			TestDiagnostics.WriteLine($"  INPUT: {input}");
			TestDiagnostics.WriteLine($"═══════════════════════════════════════════════════════════════");
			TestDiagnostics.WriteLine();

			var result = ParseAndDiagnose(input, PredictionMode.LL);
			TestDiagnostics.WriteLine(result.FullOutput);
			results.Add((input, description, result));

			TestDiagnostics.WriteLine();
		}

		TestDiagnostics.WriteLine("╔══════════════════════════════════════════════════════════════════════╗");
		TestDiagnostics.WriteLine("║  SUMMARY                                                           ║");
		TestDiagnostics.WriteLine("╚══════════════════════════════════════════════════════════════════════╝");
		TestDiagnostics.WriteLine();

		foreach (var (input, desc, result) in results)
		{
			var status = result.RealSyntaxErrorCount == 0 ? "✅ OK" : $"❌ {result.RealSyntaxErrorCount} error(s)";
			TestDiagnostics.WriteLine($"  {status} | {desc}");
			TestDiagnostics.WriteLine($"         | Input: {input}");
			TestDiagnostics.WriteLine();
		}

		var failingCount = results.Count(r => r.Result.RealSyntaxErrorCount > 0);
		TestDiagnostics.WriteLine($"Patterns with errors: {failingCount}/{results.Count}");
		TestDiagnostics.WriteLine();

		// No paren state is kept, so no scope leakage is possible.
		// Whether ) is text follows from the rule copy it lands in: a function__Call_*
		// rule closes the call on it, a Top_R* rule takes it as text.
		await Assert.That(failingCount).IsEqualTo(0)
			.Because("With paren_groups off, bare parens don't change how CPAREN parses - no scope leakage possible");
	}
}