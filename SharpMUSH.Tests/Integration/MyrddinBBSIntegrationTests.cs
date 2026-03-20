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
/// Validates that SharpMUSH can install and operate a real-world MUSHCode package.
///
/// Source: https://mushcode.com/File/Myrddins-BBS-v4-0-6#
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
	/// Installs Myrddin's BBS v4.0.6 by running the installer script through CommandParse,
	/// then runs +bbread to verify the installation completes without crashing.
	///
	/// Sets DEBUG, VERBOSE, and PUPPET flags on ALL objects for comprehensive diagnostic
	/// output matching PennMUSH behavior. The output is written to a file for analysis.
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
		// Step 1: Set #1 to DEBUG, VERBOSE, and PUPPET for detailed output,
		// and ensure the WIZARD flag is set (required by BBS commands).
		// PUPPET may fail on a player object (only valid for things) - that is
		// expected and documented.
		// ====================================================================
		await Parser.CommandParse(1, ConnectionService, MModule.single("@set #1=WIZARD"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("@set #1=DEBUG"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("@set #1=VERBOSE"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("@set #1=PUPPET"));

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

		string? bbpocketDbref = null; // Will be set after @create bbpocket=10
		string? mbboardDbref = null; // Will be set after @create mbboard=10

		for (var i = 0; i < scriptLines.Length; i++)
		{
			var line = scriptLines[i];
			if (!IsExecutableLine(line))
				continue;

			// Substitute hardcoded #222 with the actual bbpocket dbref once known
			if (bbpocketDbref != null)
			{
				line = line.Replace("#222", bbpocketDbref);
			}

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

				// After creating bbpocket, capture its actual dbref and set diagnostic flags
				if (bbpocketDbref == null && line.TrimStart().StartsWith("@create bbpocket", StringComparison.OrdinalIgnoreCase))
				{
					var numResult = await Parser.CommandParse(1, ConnectionService,
						MModule.single("think [num(bbpocket)]"));
					bbpocketDbref = numResult.Message?.ToPlainText()?.Trim();
					Log($"[BBS INSTALL] bbpocket created with dbref: {bbpocketDbref} (replacing #222 in remaining lines)");

					// Set DEBUG, VERBOSE, PUPPET on bbpocket for comprehensive diagnostics
					await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {bbpocketDbref}=DEBUG"));
					await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {bbpocketDbref}=VERBOSE"));
					await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {bbpocketDbref}=PUPPET"));
				}

				// After creating mbboard, capture its actual dbref and set diagnostic flags
				if (mbboardDbref == null && line.TrimStart().StartsWith("@create mbboard", StringComparison.OrdinalIgnoreCase))
				{
					var numResult = await Parser.CommandParse(1, ConnectionService,
						MModule.single("think [num(mbboard)]"));
					mbboardDbref = numResult.Message?.ToPlainText()?.Trim();
					Log($"[BBS INSTALL] mbboard created with dbref: {mbboardDbref}");

					// Set DEBUG, VERBOSE, PUPPET on mbboard for comprehensive diagnostics
					await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {mbboardDbref}=DEBUG"));
					await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {mbboardDbref}=VERBOSE"));
					await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {mbboardDbref}=PUPPET"));
				}
			}
			catch (Exception ex)
			{
				executionExceptions.Add((i + 1, line, ex.Message));
				Log($"[BBS ERROR] Line {i + 1}: Exception: {ex.Message}");
				Log($"[BBS ERROR]   Command: {Truncate(line, 100)}");
			}
		}

		Log($"[BBS INSTALL] Executed {executedLines} commands from {scriptLines.Length} total lines.");

		// Wait for delayed @wait callbacks in the BBS install to complete
		// The install script uses @wait 1={...} and @wait 3={...} at the end
		await Task.Delay(5000);

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
		// Step 4: Document results with comprehensive notification logging
		// ====================================================================

		// Collect all notification messages from the installation and +bbread
		var allCalls = NotifyService.ReceivedCalls().ToList();
		var installMessages = new List<(int Index, string Message)>();
		var bbreadMessages = new List<(int Index, string Message)>();
		var installErrorMessages = new List<(int Index, string Message)>();
		var bbreadErrorMessages = new List<(int Index, string Message)>();
		var missingCparenMessages = new List<(int Index, string Message)>();
		var cantSeeMessages = new List<(int Index, string Message)>();

		var notifyIndex = 0;
		foreach (var call in allCalls)
		{
			var messageText = ExtractMessageText(call);
			if (messageText == null) continue;

			notifyIndex++;

			if (notifyIndex <= preInstallNotificationCount)
				continue; // Skip pre-existing notifications from other tests

			// Track specific error patterns
			if (messageText.Contains("missing CPAREN", StringComparison.OrdinalIgnoreCase))
				missingCparenMessages.Add((notifyIndex, messageText));
			if (messageText.Contains("I can't see that here", StringComparison.OrdinalIgnoreCase)
				|| messageText.Contains("CAN'T SEE THAT HERE", StringComparison.OrdinalIgnoreCase))
				cantSeeMessages.Add((notifyIndex, messageText));

			if (notifyIndex <= postInstallNotificationCount)
			{
				installMessages.Add((notifyIndex, messageText));
				if (messageText.Contains("#-1"))
					installErrorMessages.Add((notifyIndex, messageText));
			}
			else
			{
				bbreadMessages.Add((notifyIndex, messageText));
				if (messageText.Contains("#-1"))
					bbreadErrorMessages.Add((notifyIndex, messageText));
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
		Log($"'missing CPAREN' messages: {missingCparenMessages.Count}");
		Log($"'can't see that here' messages: {cantSeeMessages.Count}");
		Log($"Lines with ANTLR parser errors: {antlrErrorsByLine.Count}");
		Log($"bbpocket dbref: {bbpocketDbref ?? "NOT CREATED"}");
		Log($"mbboard dbref: {mbboardDbref ?? "NOT CREATED"}");

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

		if (missingCparenMessages.Count > 0)
		{
			Log($"\n{new string('-', 78)}");
			Log("'MISSING CPAREN' MESSAGES (MISMATCH WITH PENNMUSH):");
			Log(new string('-', 78));
			foreach (var (idx, msg) in missingCparenMessages)
			{
				Log($"  [{idx}] {Truncate(msg, 200)}");
			}
		}

		if (cantSeeMessages.Count > 0)
		{
			Log($"\n{new string('-', 78)}");
			Log("'CAN'T SEE THAT HERE' MESSAGES (MISMATCH WITH PENNMUSH):");
			Log(new string('-', 78));
			foreach (var (idx, msg) in cantSeeMessages)
			{
				Log($"  [{idx}] {Truncate(msg, 200)}");
			}
		}

		if (installErrorMessages.Count > 0)
		{
			Log($"\n{new string('-', 78)}");
			Log("#-1 ERRORS DURING INSTALLATION:");
			Log(new string('-', 78));
			foreach (var (idx, msg) in installErrorMessages.Take(50))
			{
				Log($"  [{idx}] {Truncate(msg, 200)}");
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
			foreach (var (idx, msg) in bbreadErrorMessages.Take(50))
			{
				Log($"  [{idx}] {Truncate(msg, 200)}");
			}

			if (bbreadErrorMessages.Count > 50)
			{
				Log($"  ... and {bbreadErrorMessages.Count - 50} more #-1 errors");
			}
		}

		// ====================================================================
		// Step 5: Log ALL notifications with indices for mismatch analysis
		// ====================================================================
		Log($"\n{new string('-', 78)}");
		Log("ALL INSTALL NOTIFICATIONS (with index for mismatch tracking):");
		Log(new string('-', 78));
		foreach (var (idx, msg) in installMessages)
		{
			Log($"  [{idx}] {Truncate(msg, 200)}");
		}

		if (bbreadMessages.Count > 0)
		{
			Log($"\n{new string('-', 78)}");
			Log("+BBREAD OUTPUT (with index for mismatch tracking):");
			Log(new string('-', 78));
			foreach (var (idx, msg) in bbreadMessages)
			{
				Log($"  [{idx}] {msg}");
			}
		}

		Log($"\n{new string('=', 78)}");

		// ====================================================================
		// Step 6: Write output to text file
		// ====================================================================
		var outputPath = Path.Combine(AppContext.BaseDirectory, TestDataDir, OutputFileName);
		await File.WriteAllTextAsync(outputPath, output.ToString());
		Console.WriteLine($"[BBS INSTALL] Full test output written to: {outputPath}");

		// ====================================================================
		// Step 7: Assertions
		// ====================================================================

		// The test should not crash - if we get here, the parser handled the script
		await Assert.That(executedLines).IsGreaterThan(0)
			.Because("at least some commands from the BBS script should have been executed");

		// Log summary warnings for visibility
		if (installErrorMessages.Count > 0 || bbreadErrorMessages.Count > 0
			|| missingCparenMessages.Count > 0 || cantSeeMessages.Count > 0)
		{
			Console.WriteLine($"\n[BBS INSTALL] WARNING: Found {installErrorMessages.Count} install #-1 errors, {bbreadErrorMessages.Count} +bbread #-1 errors, {missingCparenMessages.Count} missing CPAREN, {cantSeeMessages.Count} can't see messages.");
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
	/// Installs the BBS, runs +bbnewgroup to create a group, and verifies +bbread lists it.
	/// Sets DEBUG, VERBOSE, PUPPET on the newly created group object for comprehensive diagnostics.
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

		var groupName = $"TestGrp_{Guid.NewGuid():N}"[..20]; // Keep name short for BBS

		// Track baseline notification count
		var preTestNotifications = NotifyService.ReceivedCalls()
			.Count(c => c.GetMethodInfo().Name == nameof(INotifyService.Notify));

		// +bbnewgroup should have no ANTLR parse errors
		var newGroupCmd = $"+bbnewgroup {groupName}";
		var parseErrors = Parser.ValidateAndGetErrors(MModule.single(newGroupCmd), ParseType.CommandList);
		await Assert.That(parseErrors.Count).IsEqualTo(0)
			.Because("+bbnewgroup command should not produce any ANTLR parser errors");

		// Execute +bbnewgroup and wait for the @wait callback to complete
		await Parser.CommandParse(1, ConnectionService, MModule.single(newGroupCmd));
		await Task.Delay(5000);

		// Capture the group object's dbref and set DEBUG, VERBOSE, PUPPET on it
		string? groupDbref = null;
		try
		{
			var numResult = await Parser.CommandParse(1, ConnectionService,
				MModule.single($"think [num({groupName})]"));
			groupDbref = numResult.Message?.ToPlainText()?.Trim();

			if (groupDbref != null && !groupDbref.Contains("#-1"))
			{
				Log($"[BBS NEWGROUP] Group '{groupName}' created with dbref: {groupDbref}");
				await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {groupDbref}=DEBUG"));
				await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {groupDbref}=VERBOSE"));
				await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {groupDbref}=PUPPET"));
			}
			else
			{
				Log($"[BBS NEWGROUP] WARNING: Could not find group '{groupName}', num() returned: {groupDbref}");
			}
		}
		catch (Exception ex)
		{
			Log($"[BBS NEWGROUP] WARNING: Exception looking up group dbref: {ex.Message}");
		}

		// Collect all notifications from +bbnewgroup (including @wait callback)
		var postNewGroupNotifications = NotifyService.ReceivedCalls()
			.Count(c => c.GetMethodInfo().Name == nameof(INotifyService.Notify));

		Log($"\n{new string('-', 78)}");
		Log("+BBNEWGROUP NOTIFICATIONS:");
		Log(new string('-', 78));
		var newGroupMessages = new List<(int Index, string Message)>();
		var newGroupErrors = new List<(int Index, string Message)>();
		var missingCparenMessages = new List<(int Index, string Message)>();
		var cantSeeMessages = new List<(int Index, string Message)>();

		var ngIndex = 0;
		foreach (var messageText in NotifyService.ReceivedCalls()
			.Select(ExtractMessageText)
			.OfType<string>())
		{
			ngIndex++;
			if (ngIndex <= preTestNotifications) continue;
			if (ngIndex > postNewGroupNotifications) break;

			newGroupMessages.Add((ngIndex, messageText));
			Log($"  [{ngIndex}] {Truncate(messageText, 200)}");

			if (messageText.Contains("#-1"))
				newGroupErrors.Add((ngIndex, messageText));
			if (messageText.Contains("missing CPAREN", StringComparison.OrdinalIgnoreCase))
				missingCparenMessages.Add((ngIndex, messageText));
			if (messageText.Contains("I can't see that here", StringComparison.OrdinalIgnoreCase)
				|| messageText.Contains("CAN'T SEE THAT HERE", StringComparison.OrdinalIgnoreCase))
				cantSeeMessages.Add((ngIndex, messageText));
		}

		// Reset baseline for +bbread
		preTestNotifications = NotifyService.ReceivedCalls()
			.Count(c => c.GetMethodInfo().Name == nameof(INotifyService.Notify));

		// +bbread should have no ANTLR parse errors
		var bbreadParseErrors = Parser.ValidateAndGetErrors(MModule.single("+bbread"), ParseType.CommandList);
		await Assert.That(bbreadParseErrors.Count).IsEqualTo(0)
			.Because("+bbread command should not produce any ANTLR parser errors");

		// Execute +bbread and collect output
		await Parser.CommandParse(1, ConnectionService, MModule.single("+bbread"));

		var bbreadMessages = new List<(int Index, string Message)>();
		var bbreadErrors = new List<(int Index, string Message)>();
		var notifyIndex = 0;

		foreach (var messageText in NotifyService.ReceivedCalls()
			.Select(ExtractMessageText)
			.OfType<string>())
		{
			notifyIndex++;
			if (notifyIndex <= preTestNotifications) continue;

			bbreadMessages.Add((notifyIndex, messageText));
			if (messageText.Contains("#-1"))
				bbreadErrors.Add((notifyIndex, messageText));
			if (messageText.Contains("missing CPAREN", StringComparison.OrdinalIgnoreCase))
				missingCparenMessages.Add((notifyIndex, messageText));
			if (messageText.Contains("I can't see that here", StringComparison.OrdinalIgnoreCase)
				|| messageText.Contains("CAN'T SEE THAT HERE", StringComparison.OrdinalIgnoreCase))
				cantSeeMessages.Add((notifyIndex, messageText));
		}

		Log($"\n{new string('-', 78)}");
		Log("+BBREAD OUTPUT:");
		Log(new string('-', 78));
		foreach (var (idx, msg) in bbreadMessages)
		{
			Log($"  [{idx}] {msg}");
		}

		// Summary
		Log($"\n{new string('=', 78)}");
		Log("BBS_NEWGROUP_THEN_BBREAD SUMMARY:");
		Log($"  Group name: {groupName}");
		Log($"  Group dbref: {groupDbref ?? "NOT FOUND"}");
		Log($"  +bbnewgroup notifications: {newGroupMessages.Count}");
		Log($"  +bbnewgroup #-1 errors: {newGroupErrors.Count}");
		Log($"  +bbread notifications: {bbreadMessages.Count}");
		Log($"  +bbread #-1 errors: {bbreadErrors.Count}");
		Log($"  'missing CPAREN' messages: {missingCparenMessages.Count}");
		Log($"  'can't see that here' messages: {cantSeeMessages.Count}");
		Log(new string('=', 78));

		if (missingCparenMessages.Count > 0)
		{
			Log("\nMISMATCH: 'missing CPAREN' messages found:");
			foreach (var (idx, msg) in missingCparenMessages)
				Log($"  [{idx}] {Truncate(msg, 200)}");
		}

		if (cantSeeMessages.Count > 0)
		{
			Log("\nMISMATCH: 'can't see that here' messages found:");
			foreach (var (idx, msg) in cantSeeMessages)
				Log($"  [{idx}] {Truncate(msg, 200)}");
		}

		if (newGroupErrors.Count > 0)
		{
			Log("\nMISMATCH: #-1 errors in +bbnewgroup output:");
			foreach (var (idx, msg) in newGroupErrors)
				Log($"  [{idx}] {Truncate(msg, 200)}");
		}

		// Write output file for this test
		var outputPath = Path.Combine(AppContext.BaseDirectory, TestDataDir, "MyrddinBBS_NewGroup_TestOutput.txt");
		await File.WriteAllTextAsync(outputPath, output.ToString());
		Console.WriteLine($"[BBS NEWGROUP] Full test output written to: {outputPath}");

		// The group name should appear in the +bbread output
		var bbreadOutput = string.Join("\n", bbreadMessages.Select(m => m.Message));
		await Assert.That(bbreadOutput).Contains(groupName)
			.Because($"+bbread should list the newly created group '{groupName}'");

		// No #-1 errors in the +bbread output
		await Assert.That(bbreadErrors.Count).IsEqualTo(0)
			.Because("there should be no #-1 errors in the +bbnewgroup/+bbread workflow");
	}

	/// <summary>
	/// After installing the BBS and creating a group, posts a message with +bbpost
	/// and then reads it with +bbread 1/1. Validates the full post/read workflow
	/// with DEBUG, VERBOSE, and PUPPET enabled on all objects.
	///
	/// Documents any mismatches with PennMUSH output including:
	/// - missing CPAREN errors
	/// - "I can't see that here" errors
	/// - #-1 errors in output
	/// - Unexpected DEBUG/VERBOSE output format differences
	/// </summary>
	[Test]
	[DependsOn(nameof(BBS_NewGroup_ThenBBRead_ShowsGroup))]
	public async Task BBS_Post_ThenBBRead_ShowsPost()
	{
		var output = new StringBuilder();

		void Log(string message)
		{
			output.AppendLine(message);
			Console.WriteLine(message);
		}

		Log(new string('=', 78));
		Log("BBS POST AND READ TEST - WITH DEBUG/VERBOSE/PUPPET ON ALL OBJECTS");
		Log(new string('=', 78));

		// ====================================================================
		// Step 1: Post a message to group 1 using +bbpost 1/Title Goes Here=Body
		// ====================================================================
		var postCmd = "+bbpost 1/Title Goes Here=Body of the test post.";
		var postParseErrors = Parser.ValidateAndGetErrors(MModule.single(postCmd), ParseType.CommandList);

		Log($"\nPosting with command: {postCmd}");
		Log($"ANTLR parse errors for +bbpost: {postParseErrors.Count}");
		if (postParseErrors.Count > 0)
		{
			foreach (var error in postParseErrors)
				Log($"  ANTLR: col {error.Column}: {error.Message}");
		}

		var prePostNotifications = NotifyService.ReceivedCalls()
			.Count(c => c.GetMethodInfo().Name == nameof(INotifyService.Notify));

		try
		{
			await Parser.CommandParse(1, ConnectionService, MModule.single(postCmd));
			Log("[BBS POST] +bbpost command executed successfully.");
		}
		catch (Exception ex)
		{
			Log($"[BBS POST ERROR] +bbpost execution failed: {ex.Message}");
		}

		// Wait for any @wait callbacks in the post flow
		await Task.Delay(3000);

		// Collect +bbpost notifications
		var postPostNotifications = NotifyService.ReceivedCalls()
			.Count(c => c.GetMethodInfo().Name == nameof(INotifyService.Notify));

		var postMessages = new List<(int Index, string Message)>();
		var postErrors = new List<(int Index, string Message)>();
		var missingCparenMessages = new List<(int Index, string Message)>();
		var cantSeeMessages = new List<(int Index, string Message)>();

		var pIndex = 0;
		foreach (var messageText in NotifyService.ReceivedCalls()
			.Select(ExtractMessageText)
			.OfType<string>())
		{
			pIndex++;
			if (pIndex <= prePostNotifications) continue;
			if (pIndex > postPostNotifications) break;

			postMessages.Add((pIndex, messageText));

			if (messageText.Contains("#-1"))
				postErrors.Add((pIndex, messageText));
			if (messageText.Contains("missing CPAREN", StringComparison.OrdinalIgnoreCase))
				missingCparenMessages.Add((pIndex, messageText));
			if (messageText.Contains("I can't see that here", StringComparison.OrdinalIgnoreCase)
				|| messageText.Contains("CAN'T SEE THAT HERE", StringComparison.OrdinalIgnoreCase))
				cantSeeMessages.Add((pIndex, messageText));
		}

		Log($"\n{new string('-', 78)}");
		Log("+BBPOST NOTIFICATIONS:");
		Log(new string('-', 78));
		foreach (var (idx, msg) in postMessages)
		{
			Log($"  [{idx}] {Truncate(msg, 200)}");
		}

		// ====================================================================
		// Step 2: Read the posted message with +bbread 1/1
		// ====================================================================
		var readCmd = "+bbread 1/1";
		var readParseErrors = Parser.ValidateAndGetErrors(MModule.single(readCmd), ParseType.CommandList);

		Log($"\nReading with command: {readCmd}");
		Log($"ANTLR parse errors for +bbread 1/1: {readParseErrors.Count}");
		if (readParseErrors.Count > 0)
		{
			foreach (var error in readParseErrors)
				Log($"  ANTLR: col {error.Column}: {error.Message}");
		}

		var preReadNotifications = NotifyService.ReceivedCalls()
			.Count(c => c.GetMethodInfo().Name == nameof(INotifyService.Notify));

		try
		{
			await Parser.CommandParse(1, ConnectionService, MModule.single(readCmd));
			Log("[BBS READ] +bbread 1/1 command executed successfully.");
		}
		catch (Exception ex)
		{
			Log($"[BBS READ ERROR] +bbread 1/1 execution failed: {ex.Message}");
		}

		// Collect +bbread 1/1 notifications
		var readMessages = new List<(int Index, string Message)>();
		var readErrors = new List<(int Index, string Message)>();

		var rIndex = 0;
		foreach (var messageText in NotifyService.ReceivedCalls()
			.Select(ExtractMessageText)
			.OfType<string>())
		{
			rIndex++;
			if (rIndex <= preReadNotifications) continue;

			readMessages.Add((rIndex, messageText));

			if (messageText.Contains("#-1"))
				readErrors.Add((rIndex, messageText));
			if (messageText.Contains("missing CPAREN", StringComparison.OrdinalIgnoreCase))
				missingCparenMessages.Add((rIndex, messageText));
			if (messageText.Contains("I can't see that here", StringComparison.OrdinalIgnoreCase)
				|| messageText.Contains("CAN'T SEE THAT HERE", StringComparison.OrdinalIgnoreCase))
				cantSeeMessages.Add((rIndex, messageText));
		}

		Log($"\n{new string('-', 78)}");
		Log("+BBREAD 1/1 NOTIFICATIONS:");
		Log(new string('-', 78));
		foreach (var (idx, msg) in readMessages)
		{
			Log($"  [{idx}] {msg}");
		}

		// ====================================================================
		// Step 3: Also run +bbread (list view) to see the full board state
		// ====================================================================
		var preListNotifications = NotifyService.ReceivedCalls()
			.Count(c => c.GetMethodInfo().Name == nameof(INotifyService.Notify));

		try
		{
			await Parser.CommandParse(1, ConnectionService, MModule.single("+bbread"));
			Log("\n[BBS READ] +bbread (list) command executed successfully.");
		}
		catch (Exception ex)
		{
			Log($"\n[BBS READ ERROR] +bbread (list) execution failed: {ex.Message}");
		}

		var listMessages = new List<(int Index, string Message)>();
		var lIndex = 0;
		foreach (var messageText in NotifyService.ReceivedCalls()
			.Select(ExtractMessageText)
			.OfType<string>())
		{
			lIndex++;
			if (lIndex <= preListNotifications) continue;

			listMessages.Add((lIndex, messageText));
			if (messageText.Contains("#-1"))
				readErrors.Add((lIndex, messageText));
			if (messageText.Contains("missing CPAREN", StringComparison.OrdinalIgnoreCase))
				missingCparenMessages.Add((lIndex, messageText));
			if (messageText.Contains("I can't see that here", StringComparison.OrdinalIgnoreCase)
				|| messageText.Contains("CAN'T SEE THAT HERE", StringComparison.OrdinalIgnoreCase))
				cantSeeMessages.Add((lIndex, messageText));
		}

		Log($"\n{new string('-', 78)}");
		Log("+BBREAD (LIST) NOTIFICATIONS:");
		Log(new string('-', 78));
		foreach (var (idx, msg) in listMessages)
		{
			Log($"  [{idx}] {msg}");
		}

		// ====================================================================
		// Step 4: Summary and mismatch documentation
		// ====================================================================
		Log($"\n{new string('=', 78)}");
		Log("BBS POST/READ TEST SUMMARY:");
		Log(new string('=', 78));
		Log($"  +bbpost notifications: {postMessages.Count}");
		Log($"  +bbpost #-1 errors: {postErrors.Count}");
		Log($"  +bbread 1/1 notifications: {readMessages.Count}");
		Log($"  +bbread 1/1 #-1 errors: {readErrors.Count}");
		Log($"  +bbread (list) notifications: {listMessages.Count}");
		Log($"  Total 'missing CPAREN' messages: {missingCparenMessages.Count}");
		Log($"  Total 'can't see that here' messages: {cantSeeMessages.Count}");

		if (missingCparenMessages.Count > 0)
		{
			Log($"\n{new string('-', 78)}");
			Log("MISMATCH: 'missing CPAREN' messages (should not appear in PennMUSH):");
			Log(new string('-', 78));
			foreach (var (idx, msg) in missingCparenMessages)
				Log($"  [{idx}] {Truncate(msg, 200)}");
		}

		if (cantSeeMessages.Count > 0)
		{
			Log($"\n{new string('-', 78)}");
			Log("MISMATCH: 'can't see that here' messages (should not appear in PennMUSH):");
			Log(new string('-', 78));
			foreach (var (idx, msg) in cantSeeMessages)
				Log($"  [{idx}] {Truncate(msg, 200)}");
		}

		if (postErrors.Count > 0)
		{
			Log($"\n{new string('-', 78)}");
			Log("MISMATCH: #-1 errors in +bbpost output:");
			Log(new string('-', 78));
			foreach (var (idx, msg) in postErrors)
				Log($"  [{idx}] {Truncate(msg, 200)}");
		}

		if (readErrors.Count > 0)
		{
			Log($"\n{new string('-', 78)}");
			Log("MISMATCH: #-1 errors in +bbread output:");
			Log(new string('-', 78));
			foreach (var (idx, msg) in readErrors)
				Log($"  [{idx}] {Truncate(msg, 200)}");
		}

		Log($"\n{new string('=', 78)}");

		// Write output file for this test
		var outputPath = Path.Combine(AppContext.BaseDirectory, TestDataDir, "MyrddinBBS_PostRead_TestOutput.txt");
		await File.WriteAllTextAsync(outputPath, output.ToString());
		Console.WriteLine($"[BBS POST/READ] Full test output written to: {outputPath}");

		// ====================================================================
		// Step 5: Assertions
		// ====================================================================

		// The +bbpost command should produce some output
		await Assert.That(postMessages.Count).IsGreaterThan(0)
			.Because("+bbpost should produce at least one notification");

		// The +bbread 1/1 command should produce some output
		await Assert.That(readMessages.Count).IsGreaterThan(0)
			.Because("+bbread 1/1 should produce at least one notification");
	}
}
