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
[NotInParallel]
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
ContextSensitivities.Add($"  Context sensitivity in rule '{ruleName}' at [{startIndex}..{stopIndex}], prediction={prediction}");
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

// Step 1: Lex the input
var inputStream = new AntlrInputStream(input);
var lexer = new SharpMUSHLexer(inputStream);
var tokenStream = new CommonTokenStream(lexer);
tokenStream.Fill();

// Show token stream
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

// Step 2: Parse with diagnostic listeners
tokenStream.Seek(0);
var parser = new SharpMUSHParser(tokenStream);
parser.Interpreter.PredictionMode = predictionMode;

// Add our custom Full Context Scan listener
var fullContextListener = new FullContextScanListener();
parser.RemoveErrorListeners();
parser.AddErrorListener(fullContextListener);

// Add ANTLR4's built-in DiagnosticErrorListener for verbose reporting
parser.AddErrorListener(new DiagnosticErrorListener(false));

// Add trace capture
var traceCapture = new TraceCapture(parser);
parser.AddParseListener(traceCapture);

// Parse as function (startPlainString entry point, same as FunctionParse)
var context = parser.startPlainString();

var parseTree = context.ToStringTree(parser);

// Step 3: Output parse tree
sb.AppendLine("PARSE TREE (ToStringTree):");
sb.AppendLine($"  {parseTree}");
sb.AppendLine();

// Step 4: Output rule trace
sb.AppendLine("RULE TRACE (entry/exit with text):");
sb.AppendLine(traceCapture.Output.ToString());

// Step 5: Report Full Context Scans
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
sb.AppendLine();
sb.AppendLine("  NOTE: Full Context Scans are expected with semantic predicates.");
sb.AppendLine("  ANTLR4's LL prediction mode uses full context to evaluate predicates");
sb.AppendLine("  like { inFunction == 0 }? which depend on parser state at parse time.");
sb.AppendLine("  This is correct behavior, not a performance bug.");
}
sb.AppendLine();

// Step 6: Report ambiguities
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

// Step 7: Report context sensitivities
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

// Step 8: Report REAL syntax errors (not DiagnosticErrorListener reports)
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

// Step 9: Show parser state
sb.AppendLine();
sb.AppendLine("PARSER STATE AFTER PARSE:");
sb.AppendLine($"  inFunction    = {parser.inFunction}");
sb.AppendLine($"  inBraceDepth  = {parser.inBraceDepth}");
sb.AppendLine($"  inBracketDepth= {parser.inBracketDepth}");

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
/// PennMUSH-compatible behavior (no inParenDepth):
/// The lexer tokenizes #lambda/add as OTHER and ( as OPAREN (not FUNCHAR).
/// This means there are only 2 function opens (ulambda, lit) but 4 tokens 
/// that could be CPARENs. The first ) after "2" closes lit() because
/// inFunction > 0 and there's no paren depth counter to override.
///
/// Parse tree analysis:
/// - Token [3] OPAREN "(" → consumed as beginGenericText (just text)
/// - Token [5] COMMAWS "," → lit()'s 2nd argument separator
/// - Token [7] CPAREN ")" → predicate {inFunction==0} = FALSE (inFunction=2)
///   → NOT generic text → closes lit()
/// - Token [8] CPAREN ")" → closes ulambda()
/// - Token [9] CPAREN ")" → inFunction=0 → generic text
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

Console.WriteLine("╔══════════════════════════════════════════════════════════════════════╗");
Console.WriteLine("║  PARSE TREE ANALYSIS: ulambda(lit(#lambda/add(1,2)))                ║");
Console.WriteLine("║  PennMUSH-compatible: ) always closes innermost function            ║");
Console.WriteLine("╚══════════════════════════════════════════════════════════════════════╝");
Console.WriteLine();

// Test with LL mode (default production mode)
Console.WriteLine("═══════════════════ LL MODE ═══════════════════");
var llResult = ParseAndDiagnose(input, PredictionMode.LL);
Console.WriteLine(llResult.FullOutput);

// Test with SLL mode (faster, to check if it produces same result)
Console.WriteLine("═══════════════════ SLL MODE ═══════════════════");
var sllResult = ParseAndDiagnose(input, PredictionMode.SLL);
Console.WriteLine(sllResult.FullOutput);

// Assertions
await Assert.That(llResult.RealSyntaxErrorCount).IsEqualTo(0)
.Because("Should parse without syntax errors - extra ) becomes generic text");
await Assert.That(sllResult.RealSyntaxErrorCount).IsEqualTo(0)
.Because("Should parse without syntax errors in SLL mode too");

// Parse trees should be identical in both modes
await Assert.That(llResult.ParseTree).IsEqualTo(sllResult.ParseTree)
.Because("LL and SLL modes should produce identical parse trees for this input");

// Verify the parse tree contains function rule nodes for ulambda and lit
await Assert.That(llResult.ParseTree).Contains("function")
.Because("parse tree should contain function rules for ulambda and lit");
}

/// <summary>
/// Shows the parse tree for the inner expression that lit() receives:
/// #lambda/add(1,2)
/// 
/// When parsed standalone (outside a function), ( and ) are always generic text
/// because inFunction == 0. This produces the full text "#lambda/add(1,2)".
/// </summary>
[Test]
public async Task ParseTree_InnerExpression()
{
var input = "#lambda/add(1,2)";

Console.WriteLine("╔══════════════════════════════════════════════════════════════════════╗");
Console.WriteLine("║  FIX C INNER EXPRESSION: #lambda/add(1,2)                          ║");
Console.WriteLine("╚══════════════════════════════════════════════════════════════════════╝");
Console.WriteLine();

var result = ParseAndDiagnose(input, PredictionMode.LL);
Console.WriteLine(result.FullOutput);

await Assert.That(result.RealSyntaxErrorCount).IsEqualTo(0)
.Because("This expression should parse cleanly");
}

/// <summary>
/// Shows the parse tree for bare parens inside a function:
/// lit((text))
/// 
/// Without paren depth tracking (PennMUSH-compatible):
/// - ( → OPAREN, just generic text (no counter)
/// - ) after "text" → CPAREN, inFunction=1 so NOT generic text → closes lit()
/// - Final ) → CPAREN, inFunction=0 so generic text (trailing paren)
///
/// In PennMUSH, ) always closes the innermost function. Bare ( doesn't create
/// a matching scope. Use escaped parens \(\) or bracket evaluation [...] instead.
/// </summary>
[Test]
public async Task ParseTree_BareParensInFunction()
{
var input = "lit((text))";

Console.WriteLine("╔══════════════════════════════════════════════════════════════════════╗");
Console.WriteLine("║  BARE PARENS IN FUNCTION: lit((text))                               ║");
Console.WriteLine("║  PennMUSH-compatible: ) always closes innermost function            ║");
Console.WriteLine("╚══════════════════════════════════════════════════════════════════════╝");
Console.WriteLine();

var result = ParseAndDiagnose(input, PredictionMode.LL);
Console.WriteLine(result.FullOutput);

// With PennMUSH-compatible behavior:
// The ) after "text" closes lit(), the final ) is extra generic text.
// This may produce a parser error (extraneous input) since the first ) closes
// the function leaving text behind, but it should not produce a syntax error
// in the parse itself since the trailing ) becomes generic text.
await Assert.That(result.RealSyntaxErrorCount).IsEqualTo(0)
.Because("Bare parens inside functions should parse without syntax errors - ) closes the function, extra ) is generic text");
}

/// <summary>
/// Full Context Scan analysis across common MUSH patterns.
/// Documents which patterns trigger Full Context Scans and why.
/// 
/// Full Context Scans occur in LL mode when the parser encounters semantic
/// predicates that create context-dependent alternatives. This is EXPECTED
/// behavior with the predicate-based approach used in Fixes A and B.
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

Console.WriteLine("╔══════════════════════════════════════════════════════════════════════╗");
Console.WriteLine("║  FULL CONTEXT SCAN ANALYSIS: Common MUSH Patterns                  ║");
Console.WriteLine("╚══════════════════════════════════════════════════════════════════════╝");
Console.WriteLine();

var syntaxErrorInputs = new List<string>();
var fullContextScanInputs = new List<(string Input, string Description, int Count)>();

foreach (var (input, description) in testCases)
{
var result = ParseAndDiagnose(input, PredictionMode.LL);
Console.WriteLine($"─── {description}: {input} ───");

if (result.RealSyntaxErrorCount > 0)
{
Console.WriteLine($"  ❌ {result.RealSyntaxErrorCount} SYNTAX ERROR(S)");
syntaxErrorInputs.Add(input);
}
else
{
Console.WriteLine("  ✅ No syntax errors");
}

if (result.FullContextScanCount > 0)
{
Console.WriteLine($"  ⚠️ {result.FullContextScanCount} Full Context Scan(s)");
fullContextScanInputs.Add((input, description, result.FullContextScanCount));
}
else
{
Console.WriteLine("  ✅ No Full Context Scans");
}

if (result.AmbiguityCount > 0)
{
Console.WriteLine($"  ℹ️ {result.AmbiguityCount} Ambiguity report(s)");
}
Console.WriteLine();
}

// Summary
Console.WriteLine(new string('═', 70));
Console.WriteLine("SUMMARY");
Console.WriteLine(new string('═', 70));
Console.WriteLine($"Total patterns tested: {testCases.Length}");
Console.WriteLine($"Patterns with syntax errors: {syntaxErrorInputs.Count}");
Console.WriteLine($"Patterns with Full Context Scans: {fullContextScanInputs.Count}");
if (fullContextScanInputs.Count > 0)
{
Console.WriteLine("\nFull Context Scan details:");
foreach (var (input, description, count) in fullContextScanInputs)
{
Console.WriteLine($"  {description}: \"{input}\" ({count} scan(s))");
}
Console.WriteLine("\nNOTE: Full Context Scans with semantic predicates are expected behavior.");
Console.WriteLine("They occur because ANTLR4's LL prediction must evaluate predicates");
Console.WriteLine("in full parser context to determine which alternative to choose.");
Console.WriteLine("This is NOT a performance bug - it's how predicate-based");
Console.WriteLine("context-sensitive parsing works.");
}

// The key assertion: NO syntax errors
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
Console.WriteLine($"╔══════════════════════════════════════════════════════════════════════╗");
Console.WriteLine($"║  EXTRA TRAILING PARENS: {description,-44} ║");
Console.WriteLine($"╚══════════════════════════════════════════════════════════════════════╝");
Console.WriteLine();

Console.WriteLine("═══════════════════ LL MODE ═══════════════════");
var llResult = ParseAndDiagnose(input, PredictionMode.LL);
Console.WriteLine(llResult.FullOutput);

Console.WriteLine("═══════════════════ SLL MODE ═══════════════════");
var sllResult = ParseAndDiagnose(input, PredictionMode.SLL);
Console.WriteLine(sllResult.FullOutput);

// Assertions: no real syntax errors, parse trees match
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
/// Without inParenDepth (PennMUSH-compatible): bare ( is just text, and ) always
/// closes the innermost function. No paren depth leakage is possible because
/// there's no paren depth counter to leak. Bracket patterns naturally isolate
/// their function scope via inFunction tracking.
///
/// Minimal reproduction: "(text [name(%0)])"
/// 1. "(" → OPAREN, just generic text
/// 2. "[" → OBRACK, enters bracketPattern
/// 3. "name(" → FUNCHAR, ++inFunction to 1
/// 4. "%0" → substitution
/// 5. ")" → CPAREN, inFunction=1 so NOT generic text → closes name() correctly
/// 6. "]" → CBRACK, exits bracketPattern
/// 7. ")" → CPAREN, inFunction=0 so generic text → trailing paren text
/// </summary>
[Test]
public async Task Line57_BareParensBeforeBrackets_Analysis()
{
// Minimal reproduction patterns — from simplest to line 57 fragment
var testCases = new[]
{
// Pattern 1: Simplest — bare paren before bracket with function
("(x [name(%0)])", "Bare paren before bracket function"),

// Pattern 2: Like BBS — text before bracket with function
("(text [name(%0)] more)", "Bare paren text with bracket function"),

// Pattern 3: Multiple bracket functions after bare paren
("(text [add(1,2)] and [name(%0)])", "Two bracket functions after bare paren"),

// Pattern 4: Nested function inside bracket after bare paren
("(text [ifelse(1,name(%0),name(%1))])", "Nested function in bracket after bare paren"),

// Pattern 5: BBS line 57 fragment — the actual failing content
("(New BB message ([member(v(groups),%1)]/%2) posted to '[name(%1)]' by [ifelse(hasattr(%1,anonymous),get(%1/anonymous),name(%0))]: %3)", "BBS line 57 paren section"),
};

Console.WriteLine("╔══════════════════════════════════════════════════════════════════════╗");
Console.WriteLine("║  LINE 57 ANALYSIS: Bare Parens Before Brackets (PennMUSH-compatible)║");
Console.WriteLine("╚══════════════════════════════════════════════════════════════════════╝");
Console.WriteLine();

var results = new List<(string Input, string Desc, DiagnosticResult Result)>();

foreach (var (input, description) in testCases)
{
Console.WriteLine($"═══════════════════════════════════════════════════════════════");
Console.WriteLine($"  TEST: {description}");
Console.WriteLine($"  INPUT: {input}");
Console.WriteLine($"═══════════════════════════════════════════════════════════════");
Console.WriteLine();

var result = ParseAndDiagnose(input, PredictionMode.LL);
Console.WriteLine(result.FullOutput);
results.Add((input, description, result));

Console.WriteLine();
}

// Summary
Console.WriteLine("╔══════════════════════════════════════════════════════════════════════╗");
Console.WriteLine("║  SUMMARY                                                           ║");
Console.WriteLine("╚══════════════════════════════════════════════════════════════════════╝");
Console.WriteLine();

foreach (var (input, desc, result) in results)
{
var status = result.RealSyntaxErrorCount == 0 ? "✅ OK" : $"❌ {result.RealSyntaxErrorCount} error(s)";
Console.WriteLine($"  {status} | {desc}");
Console.WriteLine($"         | Input: {input}");
Console.WriteLine();
}

// Document which patterns fail
var failingCount = results.Count(r => r.Result.RealSyntaxErrorCount > 0);
Console.WriteLine($"Patterns with errors: {failingCount}/{results.Count}");
Console.WriteLine();

// Without inParenDepth, there's no scope leakage possible.
// The { inFunction == 0 }? predicate is purely based on function nesting,
// which is correctly tracked by the parser.
await Assert.That(failingCount).IsEqualTo(0)
.Because("Without inParenDepth, bare parens don't affect CPAREN predicate - no scope leakage possible");
}
}
