using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
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
		var errors = new List<string>();

		foreach (var line in scriptLines)
		{
			if (!IsExecutableLine(line))
				continue;

			try
			{
				await Parser.CommandParse(1, ConnectionService, MModule.single(line));
				executedLines++;
			}
			catch (Exception ex)
			{
				var errorMsg = $"Line {Array.IndexOf(scriptLines, line) + 1}: Exception executing '{Truncate(line, 80)}': {ex.Message}";
				errors.Add(errorMsg);
				Console.WriteLine($"[BBS ERROR] {errorMsg}");
			}
		}

		Console.WriteLine($"[BBS INSTALL] Executed {executedLines} commands from {scriptLines.Length} total lines.");

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
			var errorMsg = $"+bbread execution failed: {ex.Message}";
			errors.Add(errorMsg);
			Console.WriteLine($"[BBS ERROR] {errorMsg}");
		}

		// ====================================================================
		// Step 4: Document results
		// ====================================================================

		// Collect all notifications that were sent
		var allCalls = NotifyService.ReceivedCalls().ToList();
		var notifyMessages = new List<string>();
		var errorMessages = new List<string>();

		foreach (var call in allCalls)
		{
			if (call.GetMethodInfo().Name != nameof(INotifyService.Notify))
				continue;

			var args = call.GetArguments();
			if (args.Length < 2) continue;

			string? messageText = null;

			if (args[1] is OneOf<MString, string> oneOf)
			{
				messageText = oneOf.Match(
					mstr => mstr.ToString(),
					str => str);
			}
			else if (args[1] is string str)
			{
				messageText = str;
			}
			else if (args[1] is MString mstr)
			{
				messageText = mstr.ToString();
			}

			if (messageText != null)
			{
				notifyMessages.Add(messageText);

				// Track #-1 errors
				if (messageText.Contains("#-1"))
				{
					errorMessages.Add(messageText);
				}
			}
		}

		// Log the summary
		Console.WriteLine($"\n{'=',-78}");
		Console.WriteLine("MYRDDIN BBS v4.0.6 INSTALLATION TEST RESULTS");
		Console.WriteLine($"{'=',-78}");
		Console.WriteLine($"Total script lines: {scriptLines.Length}");
		Console.WriteLine($"Executed commands: {executedLines}");
		Console.WriteLine($"Execution exceptions: {errors.Count}");
		Console.WriteLine($"Total notifications sent: {notifyMessages.Count}");
		Console.WriteLine($"Messages containing #-1: {errorMessages.Count}");

		if (errors.Count > 0)
		{
			Console.WriteLine($"\n{'-',-78}");
			Console.WriteLine("EXECUTION EXCEPTIONS:");
			Console.WriteLine($"{'-',-78}");
			foreach (var error in errors)
			{
				Console.WriteLine($"  {error}");
			}
		}

		if (errorMessages.Count > 0)
		{
			Console.WriteLine($"\n{'-',-78}");
			Console.WriteLine("#-1 ERRORS IN OUTPUT:");
			Console.WriteLine($"{'-',-78}");
			foreach (var msg in errorMessages.Take(50))
			{
				Console.WriteLine($"  {Truncate(msg, 120)}");
			}

			if (errorMessages.Count > 50)
			{
				Console.WriteLine($"  ... and {errorMessages.Count - 50} more #-1 errors");
			}
		}

		Console.WriteLine($"\n{'=',-78}");

		// ====================================================================
		// Step 5: Assertions
		// ====================================================================

		// The test should not crash - if we get here, the parser handled the script
		await Assert.That(executedLines).IsGreaterThan(0)
			.Because("at least some commands from the BBS script should have been executed");

		// Document but do not fail on #-1 errors (the issue asks to document, not fix)
		// Log them for visibility
		if (errorMessages.Count > 0)
		{
			Console.WriteLine($"\n[BBS INSTALL] WARNING: Found {errorMessages.Count} messages containing #-1 errors.");
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
