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
/// ANTLR PARSER ERRORS: 0 remaining (all 8 original lines resolved)
///
/// FIXED BY FIX A (3 lines) — bracket depth tracking:
///   Lines 74, 83, 96: Orphaned ] from \[ escapes now treated as generic text
///   via { inBracketDepth == 0 }? predicate in beginGenericText
///
/// FIXED BY FIX B (4 lines) — brace function semantics:
///   Lines 91, 109, 110, 111: Multi-arg functions now parse inside braces
///   via inFunctionInsideBrace counter with save/restore stack
///
/// FIXED BY inFunction save/restore in bracePattern (1 line):
///   Line 57: Bare parens before bracket patterns no longer cause scope leakage.
///   inFunction is saved/restored in bracePattern (reset to 0 on entry, restore on exit)
///   so ) inside brackets always correctly closes functions.
///   Fix C (inParenDepth) was removed — PennMUSH does not match bare parentheses.
///   Line 101: Also resolved — { inFunction == 0 }? predicate handles this correctly.
///
/// FIXED: +bbread runtime #-1 errors — iter() phantom iteration:
///   Root cause: MModule.split() returned [| empty |] for empty input, causing
///   iter() to produce one phantom iteration with empty value. This led to:
///     name("") → #-1 CAN'T SEE THAT HERE
///     get(/LAST_MOD) → #-1 BAD ARGUMENT FORMAT TO GET
///     words(error_string) → misleading count displayed as message count
///   Fix: split() now returns empty array for empty input (PennMUSH behavior).
///   +bbread #-1 errors: 0 (was 1).
///
/// FIXED: Lock evaluator bare name support:
///   Lines 134, 136, 138: &amp;bb_read/bb_omit/bb_silent me — attribute clear commands
///   These previously caused "mismatched input 'me'" errors in the lock evaluator.
///   Fixed by adding defaultExpr rule to BoolExpParser grammar to handle bare names.
///   Lock evaluator errors: 0 (was 3).
///
/// REMAINING NON-PARSER ERRORS:
///
/// Install #-1 false positives (3 — not actual errors):
///   These are attribute values that contain "#-1" as part of conditional checks
///   (e.g., @switch [first(grep(me,*,a))]=#-1,...). They are detected by the
///   notification counter but are intentional error-checking patterns, not failures.
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

	/// <summary>
	/// Installs the BBS, runs +bbnewgroup, and verifies +bbread lists the new group.
	/// Validates no ANTLR parser errors and no #-1 errors during the workflow.
	/// </summary>
	[Test]
	[DependsOn(nameof(InstallMyrddinBBS_AndRunBBRead_ShouldNotCrash))]
	public async Task BBS_NewGroup_ThenBBRead_ShowsGroup()
	{
		var output = new StringBuilder();

		void Log(string message)
		{
			output.AppendLine(message);
			Console.WriteLine(message);
		}

		var groupName = $"TestGroup_{Guid.NewGuid():N}"[..30]; // BBS truncates names, keep it reasonable

		Log($"[BBS TEST] Creating new group with name: {groupName}");

		// Track pre-test notification count
		var preTestNotifications = NotifyService.ReceivedCalls()
			.Count(c => c.GetMethodInfo().Name == nameof(INotifyService.Notify));

		// ====================================================================
		// Step 1: Check for parser errors when running +bbnewgroup
		// ====================================================================
		var newGroupCmd = $"+bbnewgroup {groupName}";
		var parseErrors = Parser.ValidateAndGetErrors(MModule.single(newGroupCmd), ParseType.CommandList);
		Log($"[BBS TEST] +bbnewgroup ANTLR parse errors: {parseErrors.Count}");
		foreach (var err in parseErrors)
		{
			Log($"[BBS TEST]   Parse error: col {err.Column}: {err.Message}");
		}

		await Assert.That(parseErrors.Count).IsEqualTo(0)
			.Because("+bbnewgroup command should not produce any ANTLR parser errors");

		// ====================================================================
		// Step 2: Execute +bbnewgroup
		// ====================================================================
		await Parser.CommandParse(1, ConnectionService, MModule.single(newGroupCmd));
		Log("[BBS TEST] +bbnewgroup executed.");

		// Wait for the @wait 1={...} inside +bbnewgroup to complete
		await Task.Delay(3000);
		Log("[BBS TEST] Waited 3s for @wait completion.");

		// ====================================================================
		// Step 3: Check for parser errors when running +bbread
		// ====================================================================
		var bbreadParseErrors = Parser.ValidateAndGetErrors(MModule.single("+bbread"), ParseType.CommandList);
		Log($"[BBS TEST] +bbread ANTLR parse errors: {bbreadParseErrors.Count}");

		await Assert.That(bbreadParseErrors.Count).IsEqualTo(0)
			.Because("+bbread command should not produce any ANTLR parser errors");

		// ====================================================================
		// Step 4: Execute +bbread and capture output
		// ====================================================================
		await Parser.CommandParse(1, ConnectionService, MModule.single("+bbread"));
		Log("[BBS TEST] +bbread executed.");

		// Collect notifications after the test
		var allCalls = NotifyService.ReceivedCalls().ToList();
		var testMessages = new List<string>();
		var testErrorMessages = new List<string>();
		var notifyIndex = 0;

		foreach (var call in allCalls)
		{
			var messageText = ExtractMessageText(call);
			if (messageText == null) continue;

			notifyIndex++;
			if (notifyIndex <= preTestNotifications) continue;

			testMessages.Add(messageText);
			if (messageText.Contains("#-1"))
				testErrorMessages.Add(messageText);
		}

		// ====================================================================
		// Step 5: Report results
		// ====================================================================
		Log($"\n{new string('=', 78)}");
		Log("BBS +BBNEWGROUP / +BBREAD TEST RESULTS");
		Log(new string('=', 78));
		Log($"Group name: {groupName}");
		Log($"Total notifications: {testMessages.Count}");
		Log($"#-1 errors: {testErrorMessages.Count}");

		if (testErrorMessages.Count > 0)
		{
			Log($"\n{new string('-', 78)}");
			Log("#-1 ERRORS:");
			Log(new string('-', 78));
			foreach (var msg in testErrorMessages)
			{
				Log($"  {Truncate(msg, 120)}");
			}
		}

		// Display all +bbread output
		Log($"\n{new string('-', 78)}");
		Log("+BBREAD OUTPUT:");
		Log(new string('-', 78));
		foreach (var msg in testMessages)
		{
			Log($"  {msg}");
		}
		Log(new string('=', 78));

		// Write output to a separate file
		var outputPath = Path.Combine(AppContext.BaseDirectory, TestDataDir, "MyrddinBBS_NewGroup_TestOutput.txt");
		await File.WriteAllTextAsync(outputPath, output.ToString());
		Console.WriteLine($"[BBS TEST] Full output written to: {outputPath}");

		// ====================================================================
		// Step 6: Assertions
		// ====================================================================

		// The group name should appear in the +bbread output
		var bbreadOutput = string.Join("\n", testMessages);
		await Assert.That(bbreadOutput).Contains(groupName)
			.Because($"+bbread should list the newly created group '{groupName}'");

		// No #-1 errors in the +bbread output
		await Assert.That(testErrorMessages.Count).IsEqualTo(0)
			.Because("there should be no #-1 errors in the +bbnewgroup/+bbread workflow");
	}
}
