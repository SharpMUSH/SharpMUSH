using System.Diagnostics;
using System.Text;
using Antlr4.Runtime;
using Antlr4.Runtime.Atn;
using Antlr4.Runtime.Dfa;
using SharpMUSH.Implementation;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Tests.Integration;

/// <summary>
/// Comprehensive parser performance diagnostics measuring SLL vs LL prediction modes,
/// full context scan frequency, ambiguity reports, and context sensitivity events
/// across the full Myrddin BBS v4.0.6 install script.
///
/// These tests parse raw input through the ANTLR4 parser directly (no visitor evaluation)
/// to measure parser prediction behavior and performance characteristics.
///
/// Key metrics collected:
/// - SLL vs LL parse time comparison
/// - Full context scan count and locations (SLL→LL fallback in ANTLR4)
/// - Ambiguity reports (rules with multiple viable alternatives)
/// - Context sensitivity events (predicate-dependent decisions)
/// - Syntax error comparison between modes
/// </summary>
[NotInParallel]
public class ParserPerformanceDiagnosticTests
{
	private const string TestDataDir = "Integration/TestData";
	private const string ScriptFileName = "MyrddinBBS_v406.txt";
	private const string DiagnosticsOutputFileName = "ParserPerformanceDiagnostics_Output.txt";

	/// <summary>
	/// Listener that captures all ANTLR4 prediction events for performance analysis.
	/// </summary>
	private sealed class PredictionMetricsListener : BaseErrorListener
	{
		public List<(string Rule, int StartIndex, int StopIndex)> FullContextAttempts { get; } = [];
		public List<(string Rule, int StartIndex, int StopIndex, bool Exact)> Ambiguities { get; } = [];
		public List<(string Rule, int StartIndex, int StopIndex, int Prediction)> ContextSensitivities { get; } = [];
		public List<(int Line, int Column, string Message)> SyntaxErrors { get; } = [];

		public override void SyntaxError(
			TextWriter output, IRecognizer recognizer, IToken offendingSymbol,
			int line, int charPositionInLine, string msg, RecognitionException e)
		{
			// Filter out DiagnosticErrorListener's informational messages
			if (msg.StartsWith("report", StringComparison.Ordinal))
				return;

			SyntaxErrors.Add((line, charPositionInLine, msg));
		}

		public override void ReportAmbiguity(
			Antlr4.Runtime.Parser recognizer, DFA dfa, int startIndex, int stopIndex,
			bool exact, Antlr4.Runtime.Sharpen.BitSet ambigAlts, ATNConfigSet configs)
		{
			var ruleName = recognizer.RuleNames[dfa.atnStartState.ruleIndex];
			Ambiguities.Add((ruleName, startIndex, stopIndex, exact));
		}

		public override void ReportAttemptingFullContext(
			Antlr4.Runtime.Parser recognizer, DFA dfa, int startIndex, int stopIndex,
			Antlr4.Runtime.Sharpen.BitSet conflictingAlts, ATNConfigSet configs)
		{
			var ruleName = recognizer.RuleNames[dfa.atnStartState.ruleIndex];
			FullContextAttempts.Add((ruleName, startIndex, stopIndex));
		}

		public override void ReportContextSensitivity(
			Antlr4.Runtime.Parser recognizer, DFA dfa, int startIndex, int stopIndex,
			int prediction, ATNConfigSet configs)
		{
			var ruleName = recognizer.RuleNames[dfa.atnStartState.ruleIndex];
			ContextSensitivities.Add((ruleName, startIndex, stopIndex, prediction));
		}
	}

	/// <summary>
	/// Result of parsing a single line with diagnostics.
	/// </summary>
	private record LineParseResult(
		int LineNumber,
		long ElapsedMicroseconds,
		int FullContextScanCount,
		int AmbiguityCount,
		int ContextSensitivityCount,
		int SyntaxErrorCount,
		List<(string Rule, int StartIndex, int StopIndex)> FullContextAttempts,
		List<(string Rule, int StartIndex, int StopIndex, bool Exact)> Ambiguities,
		List<(string Rule, int StartIndex, int StopIndex, int Prediction)> ContextSensitivities);

	/// <summary>
	/// Reads the Myrddin BBS install script from the test data file.
	/// </summary>
	private static string[] ReadBBSInstallScript()
	{
		var scriptPath = Path.Combine(AppContext.BaseDirectory, TestDataDir, ScriptFileName);
		if (!File.Exists(scriptPath))
		{
			throw new FileNotFoundException(
				$"Myrddin BBS install script not found at: {scriptPath}. " +
				"Ensure the file is included in the project with CopyToOutputDirectory=Always.");
		}

		return File.ReadAllLines(scriptPath);
	}

	/// <summary>
	/// Determines if a line from the BBS script is executable (not blank/comment).
	/// </summary>
	private static bool IsExecutableLine(string line)
	{
		var trimmed = line.TrimStart();
		if (string.IsNullOrWhiteSpace(trimmed))
			return false;
		if (trimmed.StartsWith("@@"))
			return false;
		return true;
	}

	/// <summary>
	/// Parses a single line and collects performance metrics.
	/// </summary>
	private static LineParseResult ParseLineWithMetrics(string line, int lineNumber, PredictionMode mode)
	{
		var inputStream = new AntlrInputStream(line);
		var lexer = new SharpMUSHLexer(inputStream);
		var tokenStream = new CommonTokenStream(lexer);
		tokenStream.Fill();

		var parser = new SharpMUSHParser(tokenStream);
		parser.Interpreter.PredictionMode = mode;

		var listener = new PredictionMetricsListener();
		parser.RemoveErrorListeners();
		parser.AddErrorListener(listener);
		parser.AddErrorListener(new DiagnosticErrorListener(false));

		var sw = Stopwatch.StartNew();
		_ = parser.startCommandString();
		sw.Stop();

		return new LineParseResult(
			lineNumber,
			sw.ElapsedTicks * 1_000_000 / Stopwatch.Frequency,
			listener.FullContextAttempts.Count,
			listener.Ambiguities.Count,
			listener.ContextSensitivities.Count,
			listener.SyntaxErrors.Count,
			listener.FullContextAttempts,
			listener.Ambiguities,
			listener.ContextSensitivities);
	}

	/// <summary>
	/// Comprehensive parser performance diagnostic test.
	///
	/// Runs every executable line of the Myrddin BBS v4.0.6 script through the
	/// ANTLR4 parser in both SLL and LL prediction modes, collecting:
	///
	/// 1. Parse time per line (SLL vs LL)
	/// 2. Full context scan count per line (SLL→LL fallback events)
	/// 3. Ambiguity reports per rule
	/// 4. Context sensitivity events per rule
	/// 5. Syntax error comparison between modes
	///
	/// Output is written to Integration/TestData/ParserPerformanceDiagnostics_Output.txt
	/// </summary>
	[Test]
	public async Task BBSScript_ParserPerformanceDiagnostics()
	{
		var output = new StringBuilder();

		void Log(string message)
		{
			output.AppendLine(message);
			Console.WriteLine(message);
		}

		var scriptLines = ReadBBSInstallScript();
		var executableLines = scriptLines
			.Select((line, index) => (Line: line, Number: index + 1))
			.Where(x => IsExecutableLine(x.Line))
			.ToList();

		Log("╔══════════════════════════════════════════════════════════════════════════════╗");
		Log("║  ANTLR4 PARSER PERFORMANCE DIAGNOSTICS                                     ║");
		Log("║  Myrddin BBS v4.0.6 — Full Script Analysis                                 ║");
		Log("╚══════════════════════════════════════════════════════════════════════════════╝");
		Log("");
		Log($"Total script lines: {scriptLines.Length}");
		Log($"Executable lines:   {executableLines.Count}");
		Log($"Timestamp:          {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
		Log("");

		// ─── Phase 1: LL Mode (default production mode) ───
		Log("═══════════════════════════════════════════════════════════════════════════════");
		Log("  PHASE 1: LL MODE (Full LL(*) Prediction — Default Production Mode)");
		Log("═══════════════════════════════════════════════════════════════════════════════");
		Log("");

		var llResults = new List<LineParseResult>();
		var llTotalSw = Stopwatch.StartNew();
		foreach (var (line, number) in executableLines)
		{
			llResults.Add(ParseLineWithMetrics(line, number, PredictionMode.LL));
		}
		llTotalSw.Stop();

		var llTotalFullContext = llResults.Sum(r => r.FullContextScanCount);
		var llTotalAmbiguities = llResults.Sum(r => r.AmbiguityCount);
		var llTotalContextSensitivities = llResults.Sum(r => r.ContextSensitivityCount);
		var llTotalSyntaxErrors = llResults.Sum(r => r.SyntaxErrorCount);
		var llLinesWithFullContext = llResults.Count(r => r.FullContextScanCount > 0);
		var llTotalParseTimeUs = llResults.Sum(r => r.ElapsedMicroseconds);

		Log($"  Total parse time:           {llTotalSw.ElapsedMilliseconds}ms (wall clock)");
		Log($"  Sum of per-line parse time: {llTotalParseTimeUs / 1000.0:F1}ms");
		Log($"  Lines parsed:               {executableLines.Count}");
		Log($"  Avg per line:               {llTotalParseTimeUs / (double)executableLines.Count:F1}µs");
		Log($"  Syntax errors:              {llTotalSyntaxErrors}");
		Log($"  Full context scans:         {llTotalFullContext} (across {llLinesWithFullContext} lines)");
		Log($"  Ambiguity reports:          {llTotalAmbiguities}");
		Log($"  Context sensitivities:      {llTotalContextSensitivities}");
		Log("");

		// ─── Phase 2: SLL Mode (Strong LL — faster, less powerful) ───
		Log("═══════════════════════════════════════════════════════════════════════════════");
		Log("  PHASE 2: SLL MODE (Strong LL Prediction — Faster Alternative)");
		Log("═══════════════════════════════════════════════════════════════════════════════");
		Log("");

		var sllResults = new List<LineParseResult>();
		var sllTotalSw = Stopwatch.StartNew();
		foreach (var (line, number) in executableLines)
		{
			sllResults.Add(ParseLineWithMetrics(line, number, PredictionMode.SLL));
		}
		sllTotalSw.Stop();

		var sllTotalFullContext = sllResults.Sum(r => r.FullContextScanCount);
		var sllTotalAmbiguities = sllResults.Sum(r => r.AmbiguityCount);
		var sllTotalContextSensitivities = sllResults.Sum(r => r.ContextSensitivityCount);
		var sllTotalSyntaxErrors = sllResults.Sum(r => r.SyntaxErrorCount);
		var sllLinesWithFullContext = sllResults.Count(r => r.FullContextScanCount > 0);
		var sllTotalParseTimeUs = sllResults.Sum(r => r.ElapsedMicroseconds);

		Log($"  Total parse time:           {sllTotalSw.ElapsedMilliseconds}ms (wall clock)");
		Log($"  Sum of per-line parse time: {sllTotalParseTimeUs / 1000.0:F1}ms");
		Log($"  Lines parsed:               {executableLines.Count}");
		Log($"  Avg per line:               {sllTotalParseTimeUs / (double)executableLines.Count:F1}µs");
		Log($"  Syntax errors:              {sllTotalSyntaxErrors}");
		Log($"  Full context scans:         {sllTotalFullContext} (across {sllLinesWithFullContext} lines)");
		Log($"  Ambiguity reports:          {sllTotalAmbiguities}");
		Log($"  Context sensitivities:      {sllTotalContextSensitivities}");
		Log("");

		// ─── Phase 3: Comparison ───
		Log("═══════════════════════════════════════════════════════════════════════════════");
		Log("  PHASE 3: SLL vs LL COMPARISON");
		Log("═══════════════════════════════════════════════════════════════════════════════");
		Log("");

		var speedup = llTotalParseTimeUs > 0 ? (double)llTotalParseTimeUs / sllTotalParseTimeUs : 0;
		Log($"  LL total parse time:        {llTotalParseTimeUs / 1000.0:F1}ms");
		Log($"  SLL total parse time:       {sllTotalParseTimeUs / 1000.0:F1}ms");
		Log($"  SLL speedup factor:         {speedup:F2}x");
		Log("");

		// Compare syntax errors
		var llOnlyErrors = llTotalSyntaxErrors - sllTotalSyntaxErrors;
		var sllOnlyErrors = sllTotalSyntaxErrors - llTotalSyntaxErrors;
		Log($"  LL syntax errors:           {llTotalSyntaxErrors}");
		Log($"  SLL syntax errors:          {sllTotalSyntaxErrors}");

		if (llTotalSyntaxErrors == sllTotalSyntaxErrors)
			Log("  ✅ Both modes produce the same number of syntax errors");
		else
			Log($"  ⚠️ Error difference: LL has {llOnlyErrors} more, SLL has {sllOnlyErrors} more");

		// Check for lines where results differ
		var differingLines = new List<int>();
		for (var i = 0; i < llResults.Count; i++)
		{
			if (llResults[i].SyntaxErrorCount != sllResults[i].SyntaxErrorCount)
			{
				differingLines.Add(llResults[i].LineNumber);
			}
		}

		if (differingLines.Count > 0)
		{
			Log($"\n  Lines with different error counts between modes:");
			foreach (var lineNum in differingLines)
			{
				var llErr = llResults.First(r => r.LineNumber == lineNum).SyntaxErrorCount;
				var sllErr = sllResults.First(r => r.LineNumber == lineNum).SyntaxErrorCount;
				Log($"    Line {lineNum}: LL={llErr} errors, SLL={sllErr} errors");
			}
		}
		else
		{
			Log("  ✅ Both modes produce identical error results on every line");
		}
		Log("");

		// ─── Phase 4: Full Context Scan Details (LL mode) ───
		Log("═══════════════════════════════════════════════════════════════════════════════");
		Log("  PHASE 4: FULL CONTEXT SCAN DETAILS (LL MODE)");
		Log("═══════════════════════════════════════════════════════════════════════════════");
		Log("");

		if (llTotalFullContext == 0)
		{
			Log("  ✅ No full context scans detected in LL mode");
		}
		else
		{
			// Aggregate by rule
			var fullContextByRule = llResults
				.SelectMany(r => r.FullContextAttempts.Select(f => (r.LineNumber, f.Rule)))
				.GroupBy(x => x.Rule)
				.OrderByDescending(g => g.Count())
				.ToList();

			Log($"  Total full context scans: {llTotalFullContext}");
			Log($"  Lines affected:           {llLinesWithFullContext} / {executableLines.Count} ({100.0 * llLinesWithFullContext / executableLines.Count:F1}%)");
			Log("");
			Log("  By grammar rule:");
			foreach (var group in fullContextByRule)
			{
				Log($"    {group.Key}: {group.Count()} scan(s) across {group.Select(x => x.LineNumber).Distinct().Count()} line(s)");
			}
			Log("");

			// Show top 10 lines with most full context scans
			var topLines = llResults
				.Where(r => r.FullContextScanCount > 0)
				.OrderByDescending(r => r.FullContextScanCount)
				.Take(10)
				.ToList();

			Log("  Top 10 lines by full context scan count:");
			foreach (var r in topLines)
			{
				var lineContent = scriptLines[r.LineNumber - 1];
				var preview = lineContent.Length > 80 ? lineContent[..77] + "..." : lineContent;
				Log($"    Line {r.LineNumber}: {r.FullContextScanCount} scan(s) | {preview}");
			}
		}
		Log("");

		// ─── Phase 5: Ambiguity Details (LL mode) ───
		Log("═══════════════════════════════════════════════════════════════════════════════");
		Log("  PHASE 5: AMBIGUITY DETAILS (LL MODE)");
		Log("═══════════════════════════════════════════════════════════════════════════════");
		Log("");

		if (llTotalAmbiguities == 0)
		{
			Log("  ✅ No ambiguities detected in LL mode");
		}
		else
		{
			var ambiguityByRule = llResults
				.SelectMany(r => r.Ambiguities.Select(a => (r.LineNumber, a.Rule, a.Exact)))
				.GroupBy(x => x.Rule)
				.OrderByDescending(g => g.Count())
				.ToList();

			Log($"  Total ambiguity reports: {llTotalAmbiguities}");
			Log("");
			Log("  By grammar rule:");
			foreach (var group in ambiguityByRule)
			{
				var exactCount = group.Count(x => x.Exact);
				var inexactCount = group.Count() - exactCount;
				Log($"    {group.Key}: {group.Count()} report(s) (exact={exactCount}, inexact={inexactCount})");
			}
		}
		Log("");

		// ─── Phase 6: Context Sensitivity Details (LL mode) ───
		Log("═══════════════════════════════════════════════════════════════════════════════");
		Log("  PHASE 6: CONTEXT SENSITIVITY DETAILS (LL MODE)");
		Log("═══════════════════════════════════════════════════════════════════════════════");
		Log("");

		if (llTotalContextSensitivities == 0)
		{
			Log("  ✅ No context sensitivities detected in LL mode");
		}
		else
		{
			var csByRule = llResults
				.SelectMany(r => r.ContextSensitivities.Select(c => (r.LineNumber, c.Rule, c.Prediction)))
				.GroupBy(x => x.Rule)
				.OrderByDescending(g => g.Count())
				.ToList();

			Log($"  Total context sensitivity events: {llTotalContextSensitivities}");
			Log($"  Lines affected:                   {llResults.Count(r => r.ContextSensitivityCount > 0)}");
			Log("");
			Log("  By grammar rule:");
			foreach (var group in csByRule)
			{
				var predictions = group.Select(x => x.Prediction).Distinct().OrderBy(x => x).ToList();
				Log($"    {group.Key}: {group.Count()} event(s), predictions: [{string.Join(", ", predictions)}]");
			}
		}
		Log("");

		// ─── Phase 7: Summary and Recommendations ───
		Log("═══════════════════════════════════════════════════════════════════════════════");
		Log("  PHASE 7: SUMMARY AND RECOMMENDATIONS");
		Log("═══════════════════════════════════════════════════════════════════════════════");
		Log("");
		Log("  PARSER ERRORS:");
		Log($"    LL mode syntax errors:  {llTotalSyntaxErrors}");
		Log($"    SLL mode syntax errors: {sllTotalSyntaxErrors}");
		Log($"    Mode agreement:         {(differingLines.Count == 0 ? "✅ Identical" : $"⚠️ {differingLines.Count} lines differ")}");
		Log("");
		Log("  PREDICTION PERFORMANCE:");
		Log($"    Full context scans:     {llTotalFullContext} across {llLinesWithFullContext}/{executableLines.Count} lines");
		Log($"    Ambiguity reports:      {llTotalAmbiguities}");
		Log($"    Context sensitivities:  {llTotalContextSensitivities}");
		Log("");
		Log("  SLL vs LL:");
		Log($"    SLL speedup:            {speedup:F2}x");
		Log($"    SLL correctness:        {(differingLines.Count == 0 ? "✅ SLL produces same results as LL" : "⚠️ SLL differs from LL on some lines")}");
		Log("");

		if (differingLines.Count == 0 && llTotalSyntaxErrors == 0)
		{
			Log("  RECOMMENDATION: SLL mode can safely be used as the default prediction mode");
			Log("  for this grammar. All BBS script lines parse identically in both modes with");
			Log("  zero syntax errors. SLL mode provides a performance advantage without");
			Log("  sacrificing correctness.");
		}
		else if (differingLines.Count == 0)
		{
			Log("  RECOMMENDATION: Both modes produce identical results. Current LL default is");
			Log("  safe but SLL would provide a performance improvement.");
		}
		else
		{
			Log("  RECOMMENDATION: Keep LL as the default prediction mode. SLL mode produces");
			Log("  different results on some lines, which may indicate grammar ambiguities");
			Log("  that SLL cannot resolve correctly.");
		}

		Log("");
		Log("  NOTE ON FULL CONTEXT SCANS:");
		Log("  Full context scans are expected with semantic predicates like");
		Log("  { inFunction == 0 }? and { inBracketDepth == 0 }?. These predicates");
		Log("  depend on parser state at parse time, requiring ANTLR4 to evaluate them");
		Log("  in full context. This is correct behavior, not a performance bug.");
		Log("  The scans are O(n) in the size of the ambiguous region and are typically");
		Log("  very fast for the short token spans involved in MUSH code.");

		// Write output to file
		var outputPath = Path.Combine(AppContext.BaseDirectory, TestDataDir, DiagnosticsOutputFileName);
		await File.WriteAllTextAsync(outputPath, output.ToString());
		Console.WriteLine($"\n[DIAGNOSTICS] Full output written to: {outputPath}");

		// Assertions — 2 syntax errors expected from BBS lines 74 and 96
		// (orphaned CBRACK after escaped brackets — Fix A reverted to prevent AdaptivePredict hang)
		await Assert.That(llTotalSyntaxErrors).IsEqualTo(2)
			.Because("BBS lines 74 and 96 have syntax errors from orphaned CBRACK (Fix A reverted)");
		await Assert.That(sllTotalSyntaxErrors).IsEqualTo(2)
			.Because("BBS lines 74 and 96 have syntax errors from orphaned CBRACK (Fix A reverted)");
		await Assert.That(differingLines).IsEmpty()
			.Because("SLL and LL modes should produce identical error results on every line");
	}
}
