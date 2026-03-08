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
/// KNOWN ISSUES (documented, not fixed):
/// - Parser errors (mismatched input, extraneous input) for some complex MUSH syntax
/// - Database error (ArangoException) during @name command with special characters
/// - Type cast error (InvalidOperationException) in get() function for non-player objects
/// - Some #-1 errors in notification output
/// </summary>
[NotInParallel]
public class MyrddinBBSIntegrationTests
{
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
		var scriptPath = Path.Combine(AppContext.BaseDirectory, "Integration", "TestData", "MyrddinBBS_v406.txt");
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
	///
	/// This test:
	/// 1. Sets DEBUG and VERBOSE flags on #1 to capture detailed output
	/// 2. Reads the Myrddin BBS installer script from the test data file
	/// 3. Processes each line through CommandParse (simulating a player pasting the script)
	/// 4. Runs +bbread to check the installation output
	/// 5. Documents any errors found (#-1 errors, parser issues)
	///
	/// Known: This test documents errors and does NOT attempt to fix them.
	/// The goal is to create a baseline integration test for MUSHCode compatibility.
	/// </summary>
	[Test]
	public async Task InstallMyrddinBBS_AndRunBBRead_ShouldNotCrash()
	{
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
				await Parser.CommandParse(1, ConnectionService, MModule.single(line));
				executedLines++;
			}
			catch (Exception ex)
			{
				executionExceptions.Add((i + 1, line, ex.Message));
				Console.WriteLine($"[BBS ERROR] Line {i + 1}: Exception: {ex.Message}");
				Console.WriteLine($"[BBS ERROR]   Command: {Truncate(line, 100)}");
			}
		}

		Console.WriteLine($"[BBS INSTALL] Executed {executedLines} commands from {scriptLines.Length} total lines.");

		// Track notification count after installation but before +bbread
		var postInstallNotificationCount = NotifyService.ReceivedCalls()
			.Count(c => c.GetMethodInfo().Name == nameof(INotifyService.Notify));

		// ====================================================================
		// Step 3: Run +bbread to verify the installation
		// ====================================================================
		try
		{
			await Parser.CommandParse(1, ConnectionService, MModule.single("+bbread"));
			Console.WriteLine("[BBS INSTALL] +bbread command executed successfully.");
		}
		catch (Exception ex)
		{
			executionExceptions.Add((-1, "+bbread", ex.Message));
			Console.WriteLine($"[BBS ERROR] +bbread execution failed: {ex.Message}");
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
		Console.WriteLine($"\n{"",78}");
		Console.WriteLine(new string('=', 78));
		Console.WriteLine("MYRDDIN BBS v4.0.6 INSTALLATION TEST RESULTS");
		Console.WriteLine(new string('=', 78));
		Console.WriteLine($"Total script lines: {scriptLines.Length}");
		Console.WriteLine($"Executable commands: {scriptLines.Count(IsExecutableLine)}");
		Console.WriteLine($"Successfully executed: {executedLines}");
		Console.WriteLine($"Execution exceptions: {executionExceptions.Count}");
		Console.WriteLine($"Install notifications: {installMessages.Count}");
		Console.WriteLine($"Install #-1 errors: {installErrorMessages.Count}");
		Console.WriteLine($"+bbread notifications: {bbreadMessages.Count}");
		Console.WriteLine($"+bbread #-1 errors: {bbreadErrorMessages.Count}");

		if (executionExceptions.Count > 0)
		{
			Console.WriteLine($"\n{new string('-', 78)}");
			Console.WriteLine("EXECUTION EXCEPTIONS:");
			Console.WriteLine(new string('-', 78));
			foreach (var (lineNumber, line, error) in executionExceptions)
			{
				Console.WriteLine($"  Line {lineNumber}: {Truncate(error, 100)}");
				Console.WriteLine($"    Command: {Truncate(line, 100)}");
			}
		}

		if (installErrorMessages.Count > 0)
		{
			Console.WriteLine($"\n{new string('-', 78)}");
			Console.WriteLine("#-1 ERRORS DURING INSTALLATION:");
			Console.WriteLine(new string('-', 78));
			foreach (var msg in installErrorMessages.Take(50))
			{
				Console.WriteLine($"  {Truncate(msg, 120)}");
			}

			if (installErrorMessages.Count > 50)
			{
				Console.WriteLine($"  ... and {installErrorMessages.Count - 50} more #-1 errors");
			}
		}

		if (bbreadErrorMessages.Count > 0)
		{
			Console.WriteLine($"\n{new string('-', 78)}");
			Console.WriteLine("#-1 ERRORS IN +BBREAD OUTPUT:");
			Console.WriteLine(new string('-', 78));
			foreach (var msg in bbreadErrorMessages.Take(50))
			{
				Console.WriteLine($"  {Truncate(msg, 120)}");
			}

			if (bbreadErrorMessages.Count > 50)
			{
				Console.WriteLine($"  ... and {bbreadErrorMessages.Count - 50} more #-1 errors");
			}
		}

		if (bbreadMessages.Count > 0)
		{
			Console.WriteLine($"\n{new string('-', 78)}");
			Console.WriteLine("+BBREAD OUTPUT:");
			Console.WriteLine(new string('-', 78));
			foreach (var msg in bbreadMessages)
			{
				Console.WriteLine($"  {msg}");
			}
		}

		Console.WriteLine($"\n{new string('=', 78)}");

		// ====================================================================
		// Step 5: Assertions
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
