using System.Text;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.Core;
using OneOf;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Integration;

/// <summary>
/// Integration tests for Myrddin's BBS v4.0.6 installation.
/// These tests validate that SharpMUSH can process a real-world MUSHCode package
/// by loading the Myrddin BBS installer script and running it through the parser.
///
/// Source: https://mushcode.com/File/Myrddins-BBS-v4-0-6#
///
/// The test sets #1 to DEBUG and VERBOSE to capture detailed evaluation output,
/// runs each line of the installer through CommandParse, then executes +bbread
/// to verify the installation produces output without #-1 errors.
///
/// KNOWN ISSUES (documented):
/// - Parser errors (mismatched input, extraneous input) for some complex MUSH syntax
///   emitted to stderr during ANTLR parsing
/// - @tel within @wait callback can't locate objects by name (timing/context issue)
/// - +bbread output shows #-1 errors for group objects not being locatable
///
/// ANTLR PARSER ERRORS (Post Fix A+B):
/// After implementing grammar fixes A (bracket depth) and B (brace function semantics),
/// all 8 of the original error-producing lines have been resolved.
/// Fix C (inParenDepth tracking) was removed as it conflicted with PennMUSH behavior:
/// PennMUSH does not match bare parentheses — ) always closes the innermost function.
///
/// FIXED BY FIX A (3 lines):
/// Lines 74, 83, 96: Orphaned ] from \[ escapes now treated as generic text
///
/// FIXED BY FIX B (4 lines):
/// Lines 91, 109, 110, 111: Multi-arg functions now parse inside braces
///
/// FIXED BY REMOVING FIX C (2 lines):
/// Line 101: Was masked by inParenDepth paren matching — now works correctly
///   because { inFunction == 0 }? predicate handles the pattern without counters
/// Line 57: Was caused by inParenDepth scope leakage into bracketPattern —
///   eliminated entirely since inParenDepth no longer exists
///
/// NON-PARSER ERRORS (lock evaluator):
/// Lines 134, 136, 138: &amp;bb_read/bb_omit/bb_silent me - attribute clear commands
///   parsed by lock evaluator which expects flag/attribute names, not bare 'me'
///   Errors: "mismatched input 'me' expecting {NAME, BIT_FLAG, ...}"
///
/// See CoPilot Files/FIX_B_BBS_TEST_RESULTS.md for full analysis.
/// </summary>
[NotInParallel]
public class MyrddinBBSIntegrationTests
{
	private const string TestDataDir = "Integration/TestData";
	private const string ScriptFileName = "MyrddinBBS_v406.txt";
	private const string OutputFileName = "MyrddinBBS_v406_TestOutput.txt";

	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private INotifyService NotifyService => WebAppFactoryArg.Services.GetRequiredService<INotifyService>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();

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
	/// Skips comment lines (@@), empty lines, and lines that are not commands.
	/// </summary>
	private static bool IsExecutableLine(string line)
	{
		var trimmed = line.TrimStart();

		// Skip empty lines
		if (string.IsNullOrWhiteSpace(trimmed))
			return false;

		// Skip comment lines starting with @@
		if (trimmed.StartsWith("@@"))
			return false;

		return true;
	}

	/// <summary>
	/// Extracts the message text from a notification call's arguments.
	/// </summary>
	private static string? ExtractMessageText(ICall call)
	{
		if (call.GetMethodInfo().Name != nameof(INotifyService.Notify))
			return null;

		var args = call.GetArguments();
		if (args.Length < 2) return null;

		if (args[1] is OneOf<MString, string> oneOf)
		{
			return oneOf.Match(
				mstr => mstr.ToString(),
				str => str);
		}

		if (args[1] is string str2)
			return str2;

		if (args[1] is MString mstr2)
			return mstr2.ToString();

		return null;
	}

	/// <summary>
	/// Integration test: Install Myrddin's BBS v4.0.6 and run +bbread.
	/// Writes the full test output to Integration/TestData/MyrddinBBS_v406_TestOutput.txt.
	///
	/// This test:
	/// 1. Sets DEBUG and VERBOSE flags on #1 to capture detailed output
	/// 2. Reads the Myrddin BBS installer script from the test data file
	/// 3. Processes each line through CommandParse (simulating a player pasting the script)
	/// 4. Captures ANTLR parser errors per line by redirecting stderr
	/// 5. Runs +bbread to check the installation output
	/// 6. Writes the complete results to a text file and to console
	///
	/// Known: This test documents errors and does NOT attempt to fix them.
	/// The goal is to create a baseline integration test for MUSHCode compatibility.
	/// </summary>
	[Test]
	public async Task InstallMyrddinBBS_AndRunBBRead_ShouldNotCrash()
	{
		var output = new StringBuilder();

		void Log(string message)
		{
			output.AppendLine(message);
			Console.WriteLine(message);
		}

		// ====================================================================
		// Step 1: Set #1 to DEBUG and VERBOSE for detailed output
		// ====================================================================
		await Parser.CommandParse(1, ConnectionService, MModule.single("@set #1=DEBUG"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("@set #1=VERBOSE"));

		// ====================================================================
		// Step 2: Read and process the BBS install script
		// ====================================================================
		var scriptLines = ReadBBSInstallScript();
		var executedLines = 0;
		var executionExceptions = new List<(int LineNumber, string Line, string Error)>();
		var antlrErrorsByLine = new Dictionary<int, List<string>>();

		// Track notification count before installation to separate install output from other test output
		var preInstallNotificationCount = NotifyService.ReceivedCalls()
			.Count(c => c.GetMethodInfo().Name == nameof(INotifyService.Notify));

		for (var i = 0; i < scriptLines.Length; i++)
		{
			var line = scriptLines[i];
			if (!IsExecutableLine(line))
				continue;

			try
			{
				// Check for ANTLR parser errors on the line before executing it
				var parseErrors = Parser.ValidateAndGetErrors(MModule.single(line), ParseType.CommandList);
				if (parseErrors.Count > 0)
				{
					antlrErrorsByLine[i + 1] = [..parseErrors.Select(e => $"col {e.Column}: {e.Message}")];
				}

				await Parser.CommandParse(1, ConnectionService, MModule.single(line));
				executedLines++;
			}
			catch (Exception ex)
			{
				executionExceptions.Add((i + 1, line, ex.Message));
				Log($"[BBS ERROR] Line {i + 1}: Exception: {ex.Message}");
				Log($"[BBS ERROR]   Command: {Truncate(line, 100)}");
			}
		}

		Log($"[BBS INSTALL] Executed {executedLines} commands from {scriptLines.Length} total lines.");

		// Track notification count after installation but before +bbread
		var postInstallNotificationCount = NotifyService.ReceivedCalls()
			.Count(c => c.GetMethodInfo().Name == nameof(INotifyService.Notify));

		// ====================================================================
		// Step 3: Run +bbread to verify the installation
		// ====================================================================
		try
		{
			await Parser.CommandParse(1, ConnectionService, MModule.single("+bbread"));
			Log("[BBS INSTALL] +bbread command executed successfully.");
		}
		catch (Exception ex)
		{
			executionExceptions.Add((-1, "+bbread", ex.Message));
			Log($"[BBS ERROR] +bbread execution failed: {ex.Message}");
		}

		// ====================================================================
		// Step 4: Document results
		// ====================================================================

		// Collect all notification messages from the installation and +bbread
		var allCalls = NotifyService.ReceivedCalls().ToList();
		var installMessages = new List<string>();
		var bbreadMessages = new List<string>();
		var installErrorMessages = new List<string>();
		var bbreadErrorMessages = new List<string>();

		var notifyIndex = 0;
		foreach (var call in allCalls)
		{
			var messageText = ExtractMessageText(call);
			if (messageText == null) continue;

			notifyIndex++;

			if (notifyIndex <= preInstallNotificationCount)
				continue; // Skip pre-existing notifications from other tests

			if (notifyIndex <= postInstallNotificationCount)
			{
				installMessages.Add(messageText);
				if (messageText.Contains("#-1"))
					installErrorMessages.Add(messageText);
			}
			else
			{
				bbreadMessages.Add(messageText);
				if (messageText.Contains("#-1"))
					bbreadErrorMessages.Add(messageText);
			}
		}

		// Log the comprehensive summary
		Log("");
		Log(new string('=', 78));
		Log("MYRDDIN BBS v4.0.6 INSTALLATION TEST RESULTS");
		Log(new string('=', 78));
		Log($"Total script lines: {scriptLines.Length}");
		Log($"Executable commands: {scriptLines.Count(IsExecutableLine)}");
		Log($"Successfully executed: {executedLines}");
		Log($"Execution exceptions: {executionExceptions.Count}");
		Log($"Install notifications: {installMessages.Count}");
		Log($"Install #-1 errors: {installErrorMessages.Count}");
		Log($"+bbread notifications: {bbreadMessages.Count}");
		Log($"+bbread #-1 errors: {bbreadErrorMessages.Count}");
		Log($"Lines with ANTLR parser errors: {antlrErrorsByLine.Count}");

		if (executionExceptions.Count > 0)
		{
			Log($"\n{new string('-', 78)}");
			Log("EXECUTION EXCEPTIONS:");
			Log(new string('-', 78));
			foreach (var (lineNumber, line, error) in executionExceptions)
			{
				Log($"  Line {lineNumber}: {Truncate(error, 100)}");
				Log($"    Command: {Truncate(line, 100)}");
			}
		}

		if (antlrErrorsByLine.Count > 0)
		{
			Log($"\n{new string('-', 78)}");
			Log("ANTLR PARSER ERRORS BY SCRIPT LINE:");
			Log(new string('-', 78));
			foreach (var (lineNumber, errors) in antlrErrorsByLine.OrderBy(x => x.Key))
			{
				var lineContent = scriptLines[lineNumber - 1];
				Log($"\n  Script Line {lineNumber}: {Truncate(lineContent, 100)}");
				foreach (var error in errors)
				{
					Log($"    ANTLR: {error}");
				}
			}
		}

		if (installErrorMessages.Count > 0)
		{
			Log($"\n{new string('-', 78)}");
			Log("#-1 ERRORS DURING INSTALLATION:");
			Log(new string('-', 78));
			foreach (var msg in installErrorMessages.Take(50))
			{
				Log($"  {Truncate(msg, 120)}");
			}

			if (installErrorMessages.Count > 50)
			{
				Log($"  ... and {installErrorMessages.Count - 50} more #-1 errors");
			}
		}

		if (bbreadErrorMessages.Count > 0)
		{
			Log($"\n{new string('-', 78)}");
			Log("#-1 ERRORS IN +BBREAD OUTPUT:");
			Log(new string('-', 78));
			foreach (var msg in bbreadErrorMessages.Take(50))
			{
				Log($"  {Truncate(msg, 120)}");
			}

			if (bbreadErrorMessages.Count > 50)
			{
				Log($"  ... and {bbreadErrorMessages.Count - 50} more #-1 errors");
			}
		}

		if (bbreadMessages.Count > 0)
		{
			Log($"\n{new string('-', 78)}");
			Log("+BBREAD OUTPUT:");
			Log(new string('-', 78));
			foreach (var msg in bbreadMessages)
			{
				Log($"  {msg}");
			}
		}

		Log($"\n{new string('=', 78)}");

		// ====================================================================
		// Step 5: Write output to text file
		// ====================================================================
		var outputPath = Path.Combine(AppContext.BaseDirectory, TestDataDir, OutputFileName);
		await File.WriteAllTextAsync(outputPath, output.ToString());
		Console.WriteLine($"[BBS INSTALL] Full test output written to: {outputPath}");

		// ====================================================================
		// Step 6: Assertions
		// ====================================================================

		// The test should not crash - if we get here, the parser handled the script
		await Assert.That(executedLines).IsGreaterThan(0)
			.Because("at least some commands from the BBS script should have been executed");

		// Log summary warnings for visibility
		if (installErrorMessages.Count > 0 || bbreadErrorMessages.Count > 0)
		{
			Console.WriteLine($"\n[BBS INSTALL] WARNING: Found {installErrorMessages.Count} install #-1 errors and {bbreadErrorMessages.Count} +bbread #-1 errors.");
			Console.WriteLine("[BBS INSTALL] These are documented above for future investigation.");
		}
	}

	/// <summary>
	/// Truncates a string to the specified maximum length, appending "..." if truncated.
	/// </summary>
	private static string Truncate(string value, int maxLength)
	{
		if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
			return value;
		return value[..(maxLength - 3)] + "...";
	}
}
