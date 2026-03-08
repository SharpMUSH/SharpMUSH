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
sb.AppendLine("  like { inParenDepth > 0 }? which depend on parser state at parse time.");
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
sb.AppendLine($"  inParenDepth  = {parser.inParenDepth}");

return new DiagnosticResult(
sb.ToString(),
fullContextListener.FullContextAttempts.Count,
fullContextListener.Ambiguities.Count,
fullContextListener.RealSyntaxErrors.Count,
parseTree);
}

/// <summary>
/// Shows the parse tree and Full Context Scan analysis for the Fix C test case:
/// ulambda(lit(#lambda/add(1,2)))
/// 
/// This demonstrates how Fix C's inParenDepth tracking correctly handles
/// bare parentheses inside function calls.
/// 
/// Parse tree analysis (with Fix C):
/// - Token [3] OPAREN "(" → consumed as beginGenericText, ++inParenDepth to 1
/// - Token [7] CPAREN ")" → predicate {inFunction==0 || inParenDepth>0} = TRUE (inParenDepth=1)
///   → consumed as beginGenericText, --inParenDepth to 0
/// - Token [8] CPAREN ")" → predicate FALSE (inParenDepth=0, inFunction>0)
///   → NOT generic text → closes lit() function
/// - Token [9] CPAREN ")" → closes ulambda() function
/// 
/// Without Fix C (old behavior):
/// - Token [7] CPAREN ")" → predicate {inFunction==0} = FALSE (inFunction=2)
///   → NOT generic text → closes lit() prematurely
/// - Remaining ")" would close ulambda(), but the extra ")" becomes generic text "3)"
/// </summary>
[Test]
public async Task FixC_ParseTree_UlambdaLitLambdaAdd()
{
var input = "ulambda(lit(#lambda/add(1,2)))";

Console.WriteLine("╔══════════════════════════════════════════════════════════════════════╗");
Console.WriteLine("║  FIX C PARSE TREE ANALYSIS: ulambda(lit(#lambda/add(1,2)))          ║");
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
.Because("Fix C should eliminate real syntax errors for this input");
await Assert.That(sllResult.RealSyntaxErrorCount).IsEqualTo(0)
.Because("Fix C should eliminate real syntax errors in SLL mode too");

// Parse trees should be identical in both modes
await Assert.That(llResult.ParseTree).IsEqualTo(sllResult.ParseTree)
.Because("LL and SLL modes should produce identical parse trees for this input");

// Verify the parse tree contains the expected function rule nodes
await Assert.That(llResult.ParseTree).Contains("function")
.Because("parse tree should contain function rules for ulambda and lit");
}

/// <summary>
/// Shows the parse tree for the inner expression that lit() receives:
/// #lambda/add(1,2)
/// 
/// When parsed standalone (outside a function), ( and ) are always generic text.
/// </summary>
[Test]
public async Task FixC_ParseTree_InnerExpression()
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
/// With Fix C:
/// - First ( → OPAREN, ++inParenDepth to 1
/// - ) after "text" → CPAREN, inParenDepth=1 > 0 so generic text, --inParenDepth to 0
/// - Final ) → CPAREN, inParenDepth=0 so NOT generic text → closes lit()
/// </summary>
[Test]
public async Task FixC_ParseTree_BareParensInFunction()
{
var input = "lit((text))";

Console.WriteLine("╔══════════════════════════════════════════════════════════════════════╗");
Console.WriteLine("║  FIX C BARE PARENS: lit((text))                                    ║");
Console.WriteLine("╚══════════════════════════════════════════════════════════════════════╝");
Console.WriteLine();

var result = ParseAndDiagnose(input, PredictionMode.LL);
Console.WriteLine(result.FullOutput);

await Assert.That(result.RealSyntaxErrorCount).IsEqualTo(0)
.Because("Fix C should handle bare parens inside function calls");
}

/// <summary>
/// Full Context Scan analysis across common MUSH patterns.
/// Documents which patterns trigger Full Context Scans and why.
/// 
/// Full Context Scans occur in LL mode when the parser encounters semantic
/// predicates that create context-dependent alternatives. This is EXPECTED
/// behavior with the predicate-based approach used in Fixes A, B, and C.
/// </summary>
[Test]
public async Task FixC_FullContextScan_Analysis()
{
var testCases = new[]
{
("add(1,2)", "Simple function"),
("lit(hello world)", "Function with space-separated args"),
("ulambda(lit(#lambda/add(1,2)))", "Nested functions with bare parens (Fix C case)"),
(@"ulambda(#lambda/add\(1\,2\))", "Escaped parens"),
("ulambda(#lambda/[add(1,2)])", "Bracket evaluation"),
("ulambda(#lambda/3)", "Simple lambda"),
("lit((text))", "Bare parens in function"),
("strcat(a,b,c)", "Multi-arg function"),
("switch(1,1,yes,no)", "Switch function"),
("if(1,yes,no)", "If function"),
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
Console.WriteLine("They occur because ANTLR4's LL prediction must evaluate predicates like");
Console.WriteLine("{ inParenDepth > 0 }? in full parser context to determine which alternative");
Console.WriteLine("to choose. This is NOT a performance bug - it's how predicate-based");
Console.WriteLine("context-sensitive parsing works.");
}

// The key assertion: NO syntax errors
await Assert.That(syntaxErrorInputs).IsEmpty()
.Because("All common MUSH patterns should parse without syntax errors");
}
}
