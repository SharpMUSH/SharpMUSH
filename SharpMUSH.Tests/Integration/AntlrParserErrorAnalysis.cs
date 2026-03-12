using System.Text;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Integration;

/// <summary>
/// Research test: Systematic analysis of ANTLR4 parser errors on Myrddin BBS script lines.
/// This test does NOT execute the commands - it only validates them through the parser
/// to catalog the exact errors, positions, and grammar rules involved.
/// Output is written to Integration/TestData/AntlrParserErrorAnalysis_Output.txt
/// in the build output directory with per-line error details, escape sequence analysis,
/// root cause classification, and error category statistics.
/// </summary>
[NotInParallel]
public class AntlrParserErrorAnalysis
{
	private const string TestDataDir = "Integration/TestData";
	private const string ScriptFileName = "MyrddinBBS_v406.txt";
	private const string AnalysisOutputFileName = "AntlrParserErrorAnalysis_Output.txt";

	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;

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
	/// Determines if a line from the BBS script is a command that should be executed.
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
	/// Research test that systematically analyzes every BBS script line through
	/// multiple ParseType modes, capturing all ANTLR errors, positions, and context.
	/// </summary>
	[Test]
	[Timeout(180_000 /* 3 minutes */)]
	public async Task AnalyzeAllBBSLinesForParserErrors(CancellationToken cancellationToken)
	{
		var output = new StringBuilder();

		void Log(string message)
		{
			output.AppendLine(message);
			Console.WriteLine(message);
		}

		var scriptLines = ReadBBSInstallScript();

		// All parse types to test each line with
		var parseTypes = new[]
		{
			ParseType.CommandList,
			ParseType.Command,
			ParseType.Function,
		};

		Log("ANTLR4 PARSER ERROR ANALYSIS - MYRDDIN BBS v4.0.6");
		Log(new string('=', 80));
		Log($"Total script lines: {scriptLines.Length}");
		Log($"Executable lines: {scriptLines.Count(IsExecutableLine)}");
		Log($"Parse types tested: {string.Join(", ", parseTypes.Select(p => p.ToString()))}");
		Log("");

		var linesWithErrors = new Dictionary<int, Dictionary<ParseType, List<(int Column, string Message, string? OffendingToken, IReadOnlyList<string>? ExpectedTokens)>>>();

		for (var i = 0; i < scriptLines.Length; i++)
		{
			var line = scriptLines[i];
			if (!IsExecutableLine(line))
				continue;

			var lineErrors = new Dictionary<ParseType, List<(int Column, string Message, string? OffendingToken, IReadOnlyList<string>? ExpectedTokens)>>();

			foreach (var parseType in parseTypes)
			{
				var errors = Parser.ValidateAndGetErrors(MModule.single(line), parseType);
				if (errors.Count > 0)
				{
					lineErrors[parseType] = errors.Select(e => (e.Column, e.Message, e.OffendingToken, e.ExpectedTokens)).ToList();
				}
			}

			if (lineErrors.Count > 0)
			{
				linesWithErrors[i + 1] = lineErrors;
			}
		}

		Log($"Lines producing parser errors: {linesWithErrors.Count}");
		Log("");

		// Detailed analysis for each error-producing line
		foreach (var (lineNumber, errorsByType) in linesWithErrors.OrderBy(x => x.Key))
		{
			var lineContent = scriptLines[lineNumber - 1];
			Log(new string('-', 80));
			Log($"SCRIPT LINE {lineNumber}");
			Log(new string('-', 80));

			// Extract the attribute name and command prefix for readability
			var attrName = ExtractAttributeOrCommandName(lineContent);
			Log($"Attribute/Command: {attrName}");
			Log($"Line length: {lineContent.Length} chars");
			Log($"Full line (first 200 chars): {Truncate(lineContent, 200)}");
			Log("");

			// Identify escape sequences in the line
			var escapeSequences = FindEscapeSequences(lineContent);
			if (escapeSequences.Count > 0)
			{
				Log("MUSH Escape Sequences Found:");
				foreach (var (pos, seq, context) in escapeSequences)
				{
					Log($"  Position {pos}: '{seq}' in context: ...{context}...");
				}
				Log("");
			}

			// Report errors per parse type
			foreach (var (parseType, errors) in errorsByType)
			{
				Log($"  Parse Type: {parseType}");
				foreach (var (column, message, offendingToken, expectedTokens) in errors)
				{
					Log($"    Column {column}: {message}");
					if (offendingToken != null)
						Log($"      Offending token: '{offendingToken}'");
					if (expectedTokens?.Count > 0)
						Log($"      Expected: {string.Join(", ", expectedTokens.Take(10))}{(expectedTokens.Count > 10 ? "..." : "")}");

					// Show the content at the error position
					if (column >= 0 && column < lineContent.Length)
					{
						var start = Math.Max(0, column - 20);
						var end = Math.Min(lineContent.Length, column + 30);
						var snippet = lineContent[start..end];
						var pointer = new string(' ', Math.Min(20, column - start)) + "^";
						Log($"      Context: ...{snippet}...");
						Log($"               ...{pointer}");
					}
				}
				Log("");
			}

			// Analyze the root cause for this specific line
			var rootCause = AnalyzeRootCause(lineContent, errorsByType);
			Log($"  Root cause analysis: {rootCause}");
			Log("");
		}

		// Summary section: categorize all errors
		Log(new string('=', 80));
		Log("ERROR CATEGORY SUMMARY");
		Log(new string('=', 80));

		var allErrors = linesWithErrors
			.SelectMany(kvp => kvp.Value.SelectMany(pt => pt.Value.Select(e => (Line: kvp.Key, ParseType: pt.Key, e.Column, e.Message, e.OffendingToken))))
			.ToList();

		// Group by error pattern
		var errorPatterns = allErrors
			.GroupBy(e => ClassifyError(e.Message))
			.OrderByDescending(g => g.Count());

		foreach (var pattern in errorPatterns)
		{
			Log($"\n  Pattern: {pattern.Key} ({pattern.Count()} occurrences)");
			foreach (var e in pattern)
			{
				Log($"    Line {e.Line}, col {e.Column} [{e.ParseType}]: {Truncate(e.Message, 80)}");
			}
		}

		Log("");
		Log(new string('=', 80));
		Log("GRAMMAR RULE FLOW ANALYSIS");
		Log(new string('=', 80));
		Log("");
		Log("The ANTLR4 grammar processes commands through this rule chain:");
		Log("");
		Log("  startCommandString -> commandList -> command -> evaluationString");
		Log("                                                    |");
		Log("                                                    +-> function explicitEvaluationString?");
		Log("                                                    +-> explicitEvaluationString");
		Log("                                                          |");
		Log("                                                          +-> bracketPattern -> OBRACK evaluationString CBRACK");
		Log("                                                          +-> bracePattern -> OBRACE explicitEvaluationString? CBRACE");
		Log("                                                          +-> PERCENT validSubstitution");
		Log("                                                          +-> beginGenericText (incl. escapedText -> ESCAPE ANY)");
		Log("                                                          +-> function -> FUNCHAR evaluationString? (COMMAWS evaluationString?)* CPAREN");
		Log("");
		Log("Key lexer tokens involved in escape handling:");
		Log("  ESCAPE: '\\\\' -> pushMode(ESCAPING)");
		Log("  ANY: . -> popMode  (in ESCAPING mode, matches exactly ONE character)");
		Log("  OBRACK: '[' WS");
		Log("  CBRACK: WS ']'");
		Log("  OBRACE: '{' WS");
		Log("  CBRACE: WS '}'");
		Log("  CPAREN: WS ')'");
		Log("  FUNCHAR: [0-9a-zA-Z_~@`]+ '(' WS");
		Log("  OPAREN: '(' WS");
		Log("");
		Log("ESCAPE HANDLING ISSUE:");
		Log("  When the lexer sees '\\', it enters ESCAPING mode and consumes ONE next character.");
		Log("  For example, in '\\[', the lexer produces: ESCAPE + ANY('[')");
		Log("  This means '\\[' is NOT tokenized as OBRACK - it becomes escaped text.");
		Log("  However, '\\(' is: ESCAPE + ANY('(')");
		Log("  And for '\\%': ESCAPE + ANY('%') - BUT '%' would normally trigger SUBSTITUTION mode.");
		Log("");
		Log("  The problem arises in sequences like '\\[or(hasflag(\\%0,%2),...)]':");
		Log("  1. '\\[' -> ESCAPE ANY  (correctly escaped bracket)");
		Log("  2. 'or(' -> FUNCHAR    (function call begins, inFunction++ to 1)");
		Log("  3. 'hasflag(' -> FUNCHAR (nested function, inFunction++ to 2)");
		Log("  4. '\\%' -> ESCAPE ANY  (escapes the percent)");
		Log("  5. '0' -> OTHER");
		Log("  6. ',' -> COMMAWS      (function argument separator)");
		Log("  7. '%' -> enters SUBSTITUTION mode");
		Log("  8. '2' -> ARG_NUM (pops back)");
		Log("  9. ')' -> CPAREN       (closes hasflag, inFunction-- to 1)");
		Log("  10. ',' -> COMMAWS     (next arg in or())");
		Log("  11. ... more function processing ...");
		Log("  12. ')' -> CPAREN      (closes or(), inFunction-- to 0)");
		Log("  13. ']' -> CBRACK      (but we opened with ESCAPE ANY, not OBRACK!)");
		Log("");
		Log("  At step 13, CBRACK appears but there was no matching OBRACK.");
		Log("  Since '\\[' was consumed as escapedText (ESCAPE + ANY), the parser");
		Log("  is in an evaluationString context, not a bracketPattern context.");
		Log("  The CBRACK token has no matching bracket to close, causing errors.");
		Log("");

		// Write output to file
		var outputPath = Path.Combine(AppContext.BaseDirectory, TestDataDir, AnalysisOutputFileName);
		await File.WriteAllTextAsync(outputPath, output.ToString());
		Console.WriteLine($"\n[ANALYSIS] Full output written to: {outputPath}");

		// After Fixes A+B and inFunction scope isolation, all BBS lines parse without ANTLR errors
		await Assert.That(linesWithErrors.Count).IsEqualTo(0)
			.Because("all BBS script lines should parse without ANTLR parser errors after fixes A, B, and inFunction save/restore in bracePattern");
	}

	/// <summary>
	/// Extracts the attribute name or command prefix from a line.
	/// </summary>
	private static string ExtractAttributeOrCommandName(string line)
	{
		var trimmed = line.TrimStart();

		// &ATTR_NAME object=value pattern
		if (trimmed.StartsWith('&'))
		{
			var spaceIdx = trimmed.IndexOf(' ');
			if (spaceIdx > 0)
				return trimmed[..spaceIdx];
		}

		// @command pattern
		if (trimmed.StartsWith('@'))
		{
			var spaceIdx = trimmed.IndexOf(' ');
			if (spaceIdx > 0)
				return trimmed[..spaceIdx];
		}

		return Truncate(trimmed, 40);
	}

	/// <summary>
	/// Finds MUSH escape sequences in the line and their positions.
	/// </summary>
	private static List<(int Position, string Sequence, string Context)> FindEscapeSequences(string line)
	{
		var results = new List<(int, string, string)>();

		for (var i = 0; i < line.Length - 1; i++)
		{
			if (line[i] == '\\')
			{
				var nextChar = line[i + 1];
				// Check for known MUSH escape sequences
				if (nextChar is '[' or ']' or '(' or ')' or '%' or ',' or '{' or '}' or '\\')
				{
					var contextStart = Math.Max(0, i - 10);
					var contextEnd = Math.Min(line.Length, i + 12);
					var context = line[contextStart..contextEnd];
					results.Add((i, $"\\{nextChar}", context));
				}
			}
		}

		return results;
	}

	/// <summary>
	/// Analyzes the root cause of errors for a specific line.
	/// </summary>
	private static string AnalyzeRootCause(string line,
		Dictionary<ParseType, List<(int Column, string Message, string? OffendingToken, IReadOnlyList<string>? ExpectedTokens)>> errors)
	{
		var escapes = FindEscapeSequences(line);
		var hasEscapedBracket = escapes.Any(e => e.Sequence is "\\[" or "\\]");
		var hasEscapedParen = escapes.Any(e => e.Sequence is "\\(" or "\\)");
		var hasEscapedPercent = escapes.Any(e => e.Sequence == "\\%");
		var hasEscapedComma = escapes.Any(e => e.Sequence == "\\,");

		var parts = new List<string>();

		if (hasEscapedBracket)
			parts.Add("\\[ or \\] (escaped brackets break bracketPattern matching - lexer consumes '\\[' as ESCAPE+ANY but matching ']' becomes orphan CBRACK)");

		if (hasEscapedParen)
			parts.Add("\\( or \\) (escaped parens - lexer sees ESCAPE+ANY for '\\(' but matching ')' becomes CPAREN with no function to close)");

		if (hasEscapedPercent)
			parts.Add("\\% (escaped percent - lexer sees ESCAPE+ANY('%') but content after may be misinterpreted if it looks like a substitution pattern)");

		if (hasEscapedComma)
			parts.Add("\\, (escaped comma - similar to bracket issue)");

		if (parts.Count == 0)
		{
			// Check for bare 'me' pattern (lock evaluator issues)
			if (line.TrimStart().EndsWith(" me"))
				parts.Add("Bare 'me' at end of attribute clear command - lock evaluator expects NAME/BIT_FLAG tokens, not identifier 'me'");
			else
				parts.Add("Unknown - requires manual investigation");
		}

		return string.Join("; ", parts);
	}

	/// <summary>
	/// Classifies an error message into a pattern category.
	/// </summary>
	private static string ClassifyError(string message)
	{
		if (message.Contains("extraneous input") && message.Contains("CBRACK"))
			return "ExtraneousInput_ExpectingCBRACK";
		if (message.Contains("missing CBRACK"))
			return "MissingCBRACK";
		if (message.Contains("extraneous input") && message.Contains("CBRACE"))
			return "ExtraneousInput_ExpectingCBRACE";
		if (message.Contains("no viable alternative"))
			return "NoViableAlternative";
		if (message.Contains("mismatched input") && message.Contains("EOF"))
			return "MismatchedInput_EOF";
		if (message.Contains("mismatched input") && message.Contains("NAME"))
			return "MismatchedInput_NAME_BITFLAG";
		if (message.Contains("Unexpected token"))
			return "UnexpectedToken";
		if (message.Contains("end of input"))
			return "UnexpectedEndOfInput";
		if (message.Contains("extraneous input"))
			return "ExtraneousInput_Other";
		if (message.Contains("mismatched input"))
			return "MismatchedInput_Other";
		return "Other: " + Truncate(message, 50);
	}

	private static string Truncate(string value, int maxLength)
	{
		if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
			return value;
		return value[..(maxLength - 3)] + "...";
	}
}
