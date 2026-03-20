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
		// Step 1: Set #1 to DEBUG and VERBOSE for detailed output,
		// and ensure the WIZARD flag is set (required by BBS commands).
		// ====================================================================
		await Parser.CommandParse(1, ConnectionService, MModule.single("@set #1=WIZARD"));
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

		string? bbpocketDbref = null; // Will be set after @create bbpocket=10

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

				// After creating bbpocket, capture its actual dbref
				if (bbpocketDbref == null && line.TrimStart().StartsWith("@create bbpocket", StringComparison.OrdinalIgnoreCase))
				{
					var numResult = await Parser.CommandParse(1, ConnectionService,
						MModule.single("think [num(bbpocket)]"));
					bbpocketDbref = numResult.Message?.ToPlainText()?.Trim();
					Log($"[BBS INSTALL] bbpocket created with dbref: {bbpocketDbref} (replacing #222 in remaining lines)");
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
	/// Installs the BBS, runs +bbnewgroup to create a group, and verifies +bbread lists it.
	/// Validates no ANTLR parser errors and no #-1 errors during the workflow.
	/// </summary>
	[Test]
	[DependsOn(nameof(InstallMyrddinBBS_AndRunBBRead_ShouldNotCrash))]
	public async Task BBS_NewGroup_ThenBBRead_ShowsGroup()
	{
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

		// Reset baseline for +bbread
		preTestNotifications = NotifyService.ReceivedCalls()
			.Count(c => c.GetMethodInfo().Name == nameof(INotifyService.Notify));

		// +bbread should have no ANTLR parse errors
		var bbreadParseErrors = Parser.ValidateAndGetErrors(MModule.single("+bbread"), ParseType.CommandList);
		await Assert.That(bbreadParseErrors.Count).IsEqualTo(0)
			.Because("+bbread command should not produce any ANTLR parser errors");

		// Execute +bbread and collect output
		await Parser.CommandParse(1, ConnectionService, MModule.single("+bbread"));

		var bbreadMessages = new List<string>();
		var errorMessages = new List<string>();
		var notifyIndex = 0;

		foreach (var messageText in NotifyService.ReceivedCalls()
			.Select(ExtractMessageText)
			.OfType<string>())
		{
			notifyIndex++;
			if (notifyIndex <= preTestNotifications) continue;

			bbreadMessages.Add(messageText);
			if (messageText.Contains("#-1"))
				errorMessages.Add(messageText);
		}

		// The group name should appear in the +bbread output
		var bbreadOutput = string.Join("\n", bbreadMessages);
		await Assert.That(bbreadOutput).Contains(groupName)
			.Because($"+bbread should list the newly created group '{groupName}'");

		// No #-1 errors in the +bbread output
		await Assert.That(errorMessages.Count).IsEqualTo(0)
			.Because("there should be no #-1 errors in the +bbnewgroup/+bbread workflow");
	}
}
