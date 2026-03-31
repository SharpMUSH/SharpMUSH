using System.Text;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.Core;
using OneOf;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
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

	/// <summary>
	/// Shared bbpocket dbref captured during install, reused by all dependent tests.
	/// Static so it survives across test method invocations within the same session.
	/// </summary>
	private static string? _bbpocketDbref;

	/// <summary>
	/// Connection handle for a non-God test player, used by BBS tests that require
	/// a regular user (not God). WIZARD BBS objects cannot set attributes on God (#1),
	/// matching PennMUSH's controls() behaviour (Wizard can't control God).
	/// </summary>
	private static long _regularUserHandle;

	/// <summary>
	/// DBRef of the regular test user created during BBS install.
	/// </summary>
	private static string? _regularUserDbref;

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
		var scriptRelative = Path.Combine(TestDataDir, ScriptFileName);
		var scriptPath = Path.Combine(AppContext.BaseDirectory, scriptRelative);
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

		await Parser.CommandParse(1, ConnectionService, MModule.single("@set #1=WIZARD"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("@set #1=DEBUG"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("@set #1=VERBOSE"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("@set #1=PUPPET"));

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
					_bbpocketDbref = bbpocketDbref; // Share with all dependent tests
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

		// Create a regular (non-God) player for BBS tests that need a normal user.
		// The WIZARD BBS object cannot set attributes on God (#1) — controls(WIZARD, God) → false
		// in both PennMUSH and SharpMUSH. BBS operations should be tested as a regular player.
		// Use pmatch() (not num()) to look up the player by name — num() requires the object to be
		// visible from the caller's location, but pmatch() does a global player-name search.
		await Parser.CommandParse(1, ConnectionService, MModule.single("@pcreate BBSTester=bbs_test_password_123"));
		var testerDbrefResult = await Parser.CommandParse(1, ConnectionService,
			MModule.single("think [pmatch(BBSTester)]"));
		_regularUserDbref = testerDbrefResult.Message?.ToPlainText()?.Trim();
		Log($"[BBS INSTALL] Regular test user created with dbref: {_regularUserDbref}");
		if (!string.IsNullOrEmpty(_regularUserDbref) && _regularUserDbref != "#-1"
			&& DBRef.TryParse(_regularUserDbref, out var testerDbRef))
		{
			_regularUserHandle = 2L;
			await ConnectionService.Register(2L, "localhost", "localhost", "test",
				_ => ValueTask.CompletedTask, _ => ValueTask.CompletedTask, () => System.Text.Encoding.UTF8);
			await ConnectionService.Bind(2L, testerDbRef!.Value);
			Log($"[BBS INSTALL] Regular test user bound to handle {_regularUserHandle}.");
		}
		else
		{
			Log("[BBS INSTALL] WARNING: Failed to create regular test user — some tests may use God.");
		}

		// Track notification count after installation but before +bbread
		var postInstallNotificationCount = NotifyService.ReceivedCalls()
			.Count(c => c.GetMethodInfo().Name == nameof(INotifyService.Notify));

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

		var outputFileRelative = Path.Combine(TestDataDir, OutputFileName);
		var outputPath = Path.Combine(AppContext.BaseDirectory, outputFileRelative);
		await File.WriteAllTextAsync(outputPath, output.ToString());
		Console.WriteLine($"[BBS INSTALL] Full test output written to: {outputPath}");

		await Assert.That(executedLines).IsGreaterThan(0)
			.Because("at least some commands from the BBS script should have been executed");

		await Assert.That(cantSeeMessages.Count).IsEqualTo(0)
			.Because("no 'I can't see that here' or 'CAN'T SEE THAT HERE' (#-1) messages should be emitted during BBS installation");

		if (installErrorMessages.Count > 0 || bbreadErrorMessages.Count > 0
			|| missingCparenMessages.Count > 0)
		{
			Console.WriteLine($"\n[BBS INSTALL] WARNING: Found {installErrorMessages.Count} install #-1 errors, {bbreadErrorMessages.Count} +bbread #-1 errors, {missingCparenMessages.Count} missing CPAREN.");
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

	/// <summary>Returns the current notification count.</summary>
	private int NotificationCount()
		=> NotifyService.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(INotifyService.Notify));

	/// <summary>Collects notification messages in the range (fromCount, toCount].</summary>
	private IReadOnlyList<string> GetNotificationMessages(int fromCount, int toCount)
	{
		var all = NotifyService.ReceivedCalls().Select(ExtractMessageText).OfType<string>();
		var sliced = all.Skip(fromCount);
		if (toCount > 0)
			sliced = sliced.Take(toCount - fromCount);
		return sliced.ToList();
	}

	/// <summary>Runs a command and collects all notifications it produces.</summary>
	private async Task<IReadOnlyList<string>> RunAndCollect(string command, int delayMs = 0)
	{
		var before = NotificationCount();
		await Parser.CommandParse(1, ConnectionService, MModule.single(command));
		if (delayMs > 0)
			await Task.Delay(delayMs);
		var after = NotificationCount();
		return GetNotificationMessages(before, after);
	}

	/// <summary>Runs a command as a specific connection handle and collects all notifications.</summary>
	private async Task<IReadOnlyList<string>> RunAndCollectAs(string command, long handle, int delayMs = 0)
	{
		var before = NotificationCount();
		await Parser.CommandParse(handle, ConnectionService, MModule.single(command));
		if (delayMs > 0)
			await Task.Delay(delayMs);
		var after = NotificationCount();
		return GetNotificationMessages(before, after);
	}

	/// <summary>Returns the name of the BBS group at the given 1-based position.</summary>
	private async Task<string> GetGroupName(int position = 1)
	{
		var bbpocket = _bbpocketDbref;
		if (string.IsNullOrEmpty(bbpocket))
			return string.Empty;
		var result = await Parser.CommandParse(1, ConnectionService,
			MModule.single($"think [name(extract(get({bbpocket}/groups),{position},1))]"));
		return result.Message?.ToPlainText()?.Trim() ?? string.Empty;
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
				try
				{
					await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {groupDbref}=DEBUG"));
					await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {groupDbref}=VERBOSE"));
					await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {groupDbref}=PUPPET"));
				}
				catch (Exception flagEx) when (flagEx is not OperationCanceledException and not TaskCanceledException)
				{
					Log($"[BBS NEWGROUP] WARNING: Failed to set diagnostic flags on group: {flagEx.Message}");
				}
			}
			else
			{
				Log($"[BBS NEWGROUP] WARNING: Could not find group '{groupName}', num() returned: {groupDbref}");
			}
		}
		catch (Exception ex) when (ex is not OperationCanceledException and not TaskCanceledException)
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
		var outputFileRelative = Path.Combine(TestDataDir, "MyrddinBBS_NewGroup_TestOutput.txt");
		var outputPath = Path.Combine(AppContext.BaseDirectory, outputFileRelative);
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
			throw;
		}
		// Wait for any @wait callbacks in the post flow
		await Task.Delay(5000);

		// Collect +bbpost notifications
		var postPostNotifications = NotifyService.ReceivedCalls()
			.Count(c => c.GetMethodInfo().Name == nameof(INotifyService.Notify));

		var postMessages = new List<(int Index, string Message)>();
		var postErrors = new List<(int Index, string Message)>();
		var missingCparenMessages = new List<(int Index, string Phase, string Message)>();
		var cantSeeMessages = new List<(int Index, string Phase, string Message)>();

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
				missingCparenMessages.Add((pIndex, "+bbpost", messageText));
			if (messageText.Contains("I can't see that here", StringComparison.OrdinalIgnoreCase)
				|| messageText.Contains("CAN'T SEE THAT HERE", StringComparison.OrdinalIgnoreCase))
				cantSeeMessages.Add((pIndex, "+bbpost", messageText));
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
			throw;
		}

		// Wait for async @dolist callbacks to complete (the BBS +bbread uses @dolist without
		// INLINE/INPLACE, so each iteration body is queued asynchronously via the scheduler).
		await Task.Delay(5000);
		var postReadNotifications = NotifyService.ReceivedCalls()
			.Count(c => c.GetMethodInfo().Name == nameof(INotifyService.Notify));

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
			if (rIndex > postReadNotifications) break;

			readMessages.Add((rIndex, messageText));

			if (messageText.Contains("#-1"))
				readErrors.Add((rIndex, messageText));
			if (messageText.Contains("missing CPAREN", StringComparison.OrdinalIgnoreCase))
				missingCparenMessages.Add((rIndex, "+bbread 1/1", messageText));
			if (messageText.Contains("I can't see that here", StringComparison.OrdinalIgnoreCase)
				|| messageText.Contains("CAN'T SEE THAT HERE", StringComparison.OrdinalIgnoreCase))
				cantSeeMessages.Add((rIndex, "+bbread 1/1", messageText));
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
			throw;
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
				missingCparenMessages.Add((lIndex, "+bbread list", messageText));
			if (messageText.Contains("I can't see that here", StringComparison.OrdinalIgnoreCase)
				|| messageText.Contains("CAN'T SEE THAT HERE", StringComparison.OrdinalIgnoreCase))
				cantSeeMessages.Add((lIndex, "+bbread list", messageText));
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
			foreach (var (idx, phase, msg) in missingCparenMessages)
				Log($"  [{idx}] ({phase}) {Truncate(msg, 200)}");
		}

		if (cantSeeMessages.Count > 0)
		{
			Log($"\n{new string('-', 78)}");
			Log("MISMATCH: 'can't see that here' messages (should not appear in PennMUSH):");
			Log(new string('-', 78));
			foreach (var (idx, phase, msg) in cantSeeMessages)
				Log($"  [{idx}] ({phase}) {Truncate(msg, 200)}");
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
		var postReadOutputRelative = Path.Combine(TestDataDir, "MyrddinBBS_PostRead_TestOutput.txt");
		var outputPath = Path.Combine(AppContext.BaseDirectory, postReadOutputRelative);
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

		// The post confirmation should include the correct title (not just the group number).
		// Use StartsWith to avoid matching the VERBOSE output line (e.g., "#4] ...@pemit %#=You post your note about '%1'...")
		// which contains the raw command code before %1 is substituted.
		var postConfirmation = postMessages
			.FirstOrDefault(m => m.Message.TrimStart().StartsWith("You post your note about", StringComparison.OrdinalIgnoreCase));
		await Assert.That(postConfirmation.Message).IsNotNull()
			.Because("+bbpost should emit 'You post your note about' confirmation");
		await Assert.That(postConfirmation.Message).Contains("Title Goes Here")
			.Because("+bbpost should use the correct title 'Title Goes Here', not the group number");

		// The attributes (mess_lst, hdr, bdy) should all be SET on the group
		var messLstSet = postMessages.Any(m => m.Message.Contains("mess_lst SET", StringComparison.OrdinalIgnoreCase)
			|| m.Message.Contains("/mess_lst - Set.", StringComparison.OrdinalIgnoreCase));
		await Assert.That(messLstSet).IsTrue()
			.Because("mess_lst attribute should be SET on the group after +bbpost");

		var hdrSet = postMessages.Any(m => m.Message.Contains("hdr_", StringComparison.OrdinalIgnoreCase)
			&& m.Message.Contains("SET", StringComparison.OrdinalIgnoreCase));
		await Assert.That(hdrSet).IsTrue()
			.Because("hdr_ attribute should be SET on the group after +bbpost");

		var bdySet = postMessages.Any(m => m.Message.Contains("bdy_", StringComparison.OrdinalIgnoreCase)
			&& m.Message.Contains("SET", StringComparison.OrdinalIgnoreCase));
		await Assert.That(bdySet).IsTrue()
			.Because("bdy_ attribute should be SET on the group after +bbpost");

		// +bbread 1/1 should show the message header with the correct title
		var readHasTitle = readMessages.Any(m => m.Message.Contains("Title Goes Here", StringComparison.OrdinalIgnoreCase));
		await Assert.That(readHasTitle).IsTrue()
			.Because("+bbread 1/1 should display the message title 'Title Goes Here'");

		// +bbread 1/1 should show the message body
		var readHasBody = readMessages.Any(m => m.Message.Contains("Body of the test post.", StringComparison.OrdinalIgnoreCase));
		await Assert.That(readHasBody).IsTrue()
			.Because("+bbread 1/1 should display the message body 'Body of the test post.'");
	}

	/// <summary>
	/// Reads the message list for group 1 with +bbread 1 and verifies the header,
	/// column headers, message row, and footer are present.
	/// </summary>
	[Test]
	[DependsOn(nameof(BBS_Post_ThenBBRead_ShowsPost))]
	public async Task BBS_BBRead_GroupScan()
	{
		var output = new StringBuilder();
		void Log(string message) { output.AppendLine(message); Console.WriteLine(message); }

		Log(new string('=', 78));
		Log("BBS_BBREAD_GROUPSCAN");
		Log(new string('=', 78));

		var group1Name = await GetGroupName(1);
		Log($"  Group 1 name: {group1Name}");

		var msgs = await RunAndCollect("+bbread 1", 5000);

		Log($"  Notifications received: {msgs.Count}");
		for (var i = 0; i < msgs.Count; i++)
			Log($"  [{i}] {Truncate(msgs[i], 200)}");

		var outputPath = Path.Combine(AppContext.BaseDirectory, TestDataDir, "MyrddinBBS_BBReadGroupScan_TestOutput.txt");
		await File.WriteAllTextAsync(outputPath, output.ToString());
		Console.WriteLine($"[BBS BBREAD GROUPSCAN] Full test output written to: {outputPath}");

		await Assert.That(msgs.Count).IsGreaterThanOrEqualTo(2)
			.Because("+bbread 1 should produce at least header + one message row");

		await Assert.That(msgs.Any(m => m.Contains($"**** {group1Name} ****"))).IsTrue()
			.Because("+bbread 1 should display the group name in the header");

		await Assert.That(msgs.Any(m => m.Contains("Message") && m.Contains("Posted") && m.Contains("By"))).IsTrue()
			.Because("+bbread 1 should display column headers: Message, Posted, By");

		await Assert.That(msgs.Any(m => m.Contains("1/1") && m.Contains("Title Goes Here") && m.Contains("God"))).IsTrue()
			.Because("+bbread 1 should list message 1/1 with title and author");

		await Assert.That(msgs.Any(m => m.EndsWith("=============================================================================="))).IsTrue()
			.Because("+bbread 1 should end with a footer separator");
	}

	/// <summary>
	/// Runs +bblist and verifies the full table showing available groups.
	/// </summary>
	[Test]
	[DependsOn(nameof(BBS_BBRead_GroupScan))]
	public async Task BBS_BBList_ShowsGroups()
	{
		var output = new StringBuilder();
		void Log(string message) { output.AppendLine(message); Console.WriteLine(message); }

		Log(new string('=', 78));
		Log("BBS_BBLIST_SHOWSGROUPS");
		Log(new string('=', 78));

		var group1Name = await GetGroupName(1);
		Log($"  Group 1 name: {group1Name}");

		var msgs = await RunAndCollect("+bblist");

		Log($"  Notifications received: {msgs.Count}");
		for (var i = 0; i < msgs.Count; i++)
			Log($"  [{i}] {Truncate(msgs[i], 200)}");

		var combinedOutput = string.Join("\n", msgs);

		var outputPath = Path.Combine(AppContext.BaseDirectory, TestDataDir, "MyrddinBBS_BBList_TestOutput.txt");
		await File.WriteAllTextAsync(outputPath, output.ToString());
		Console.WriteLine($"[BBS BBLIST] Full test output written to: {outputPath}");

		await Assert.That(combinedOutput).Contains("Available Bulletin Board Groups");
		await Assert.That(combinedOutput).Contains("Member?");
		await Assert.That(combinedOutput).Contains("Timeout (in days)");
		await Assert.That(combinedOutput).Contains(group1Name);
		await Assert.That(combinedOutput).Contains("Yes");
		await Assert.That(combinedOutput).Contains("none");
		await Assert.That(combinedOutput).Contains("To join groups, type '+bbjoin <group number or name>'");
	}

	/// <summary>
	/// Toggles post notification for group 1 off and back on with +bbnotify.
	/// </summary>
	[Test]
	[DependsOn(nameof(BBS_BBList_ShowsGroups))]
	public async Task BBS_BBNotify_Toggle()
	{
		var output = new StringBuilder();
		void Log(string message) { output.AppendLine(message); Console.WriteLine(message); }

		Log(new string('=', 78));
		Log("BBS_BBNOTIFY_TOGGLE");
		Log(new string('=', 78));

		var group1Name = await GetGroupName(1);
		Log($"  Group 1 name: {group1Name}");

		var offMsgs = await RunAndCollect("+bbnotify 1=off");
		Log($"  +bbnotify off notifications: {offMsgs.Count}");
		for (var i = 0; i < offMsgs.Count; i++)
			Log($"  [off/{i}] {Truncate(offMsgs[i], 200)}");

		var onMsgs = await RunAndCollect("+bbnotify 1=on");
		Log($"  +bbnotify on notifications: {onMsgs.Count}");
		for (var i = 0; i < onMsgs.Count; i++)
			Log($"  [on/{i}] {Truncate(onMsgs[i], 200)}");

		var outputPath = Path.Combine(AppContext.BaseDirectory, TestDataDir, "MyrddinBBS_BBNotify_TestOutput.txt");
		await File.WriteAllTextAsync(outputPath, output.ToString());
		Console.WriteLine($"[BBS BBNOTIFY] Full test output written to: {outputPath}");

		var expectedOff = $"Post notification for BB Group '{group1Name}' turned off. You will no longer be notified of new postings to that Group.";
		await Assert.That(offMsgs.Any(m => m == expectedOff)).IsTrue()
			.Because("+bbnotify 1=off should confirm notification was turned off");

		var expectedOn = $"Post notification for BB Group '{group1Name}' turned on. You will now be notified of new postings to that Group.";
		await Assert.That(onMsgs.Any(m => m == expectedOn)).IsTrue()
			.Because("+bbnotify 1=on should confirm notification was turned on");
	}

	/// <summary>
	/// Starts a staged post, appends text, proofs it, then tosses it.
	/// </summary>
	[Test]
	[DependsOn(nameof(BBS_BBNotify_Toggle))]
	public async Task BBS_StagedPost_WriteProofToss()
	{
		var output = new StringBuilder();
		void Log(string message) { output.AppendLine(message); Console.WriteLine(message); }

		Log(new string('=', 78));
		Log("BBS_STAGEDPOST_WRITEPROOFTOSS");
		Log(new string('=', 78));

		var startMsgs = await RunAndCollect("+bbpost 1/Proof Test Title");
		Log($"  +bbpost start notifications: {startMsgs.Count}");
		for (var i = 0; i < startMsgs.Count; i++)
			Log($"  [start/{i}] {Truncate(startMsgs[i], 200)}");

		var writeMsgs = await RunAndCollect("+bbwrite First line of staged body.");
		Log($"  +bbwrite notifications: {writeMsgs.Count}");
		for (var i = 0; i < writeMsgs.Count; i++)
			Log($"  [write/{i}] {Truncate(writeMsgs[i], 200)}");

		var bbMsgs = await RunAndCollect("+bb Appended line.");
		Log($"  +bb notifications: {bbMsgs.Count}");
		for (var i = 0; i < bbMsgs.Count; i++)
			Log($"  [bb/{i}] {Truncate(bbMsgs[i], 200)}");

		var proofMsgs = await RunAndCollect("+bbproof");
		Log($"  +bbproof notifications: {proofMsgs.Count}");
		for (var i = 0; i < proofMsgs.Count; i++)
			Log($"  [proof/{i}] {Truncate(proofMsgs[i], 200)}");

		var tossMsgs = await RunAndCollect("+bbtoss");
		Log($"  +bbtoss notifications: {tossMsgs.Count}");
		for (var i = 0; i < tossMsgs.Count; i++)
			Log($"  [toss/{i}] {Truncate(tossMsgs[i], 200)}");

		var outputPath = Path.Combine(AppContext.BaseDirectory, TestDataDir, "MyrddinBBS_StagedWriteProofToss_TestOutput.txt");
		await File.WriteAllTextAsync(outputPath, output.ToString());
		Console.WriteLine($"[BBS STAGED POST] Full test output written to: {outputPath}");

		await Assert.That(startMsgs.Any(m => m.Contains("You start your posting to Group #1"))).IsTrue()
			.Because("+bbpost 1/Proof Test Title should confirm start of posting");

		await Assert.That(writeMsgs.Any(m => m == "Text added to bbpost.")).IsTrue()
			.Because("+bbwrite should confirm text was added");

		await Assert.That(bbMsgs.Any(m => m == "Text added to bbpost.")).IsTrue()
			.Because("+bb should confirm text was appended");

		await Assert.That(proofMsgs.Any(m => m.Contains("BB Post in Progress"))).IsTrue()
			.Because("+bbproof should display BB Post in Progress header");
		await Assert.That(proofMsgs.Any(m => m.Contains("Proof Test Title"))).IsTrue()
			.Because("+bbproof should display the title");
		await Assert.That(proofMsgs.Any(m => m.Contains("First line of staged body."))).IsTrue()
			.Because("+bbproof should display the staged body text");

		await Assert.That(tossMsgs.Any(m => m == "Your bbpost has been discarded.")).IsTrue()
			.Because("+bbtoss should confirm the post was discarded");
	}

	/// <summary>
	/// Starts a staged post, writes the body, then posts it. Verifies the posted message
	/// can be read back with +bbread 1/2.
	/// </summary>
	[Test]
	[DependsOn(nameof(BBS_StagedPost_WriteProofToss))]
	public async Task BBS_StagedPost_WriteAndPost()
	{
		var output = new StringBuilder();
		void Log(string message) { output.AppendLine(message); Console.WriteLine(message); }

		Log(new string('=', 78));
		Log("BBS_STAGEDPOST_WRITEANDPOST");
		Log(new string('=', 78));

		var startMsgs = await RunAndCollect("+bbpost 1/Staged Test Post");
		Log($"  +bbpost start notifications: {startMsgs.Count}");
		for (var i = 0; i < startMsgs.Count; i++)
			Log($"  [start/{i}] {Truncate(startMsgs[i], 200)}");

		var writeMsgs = await RunAndCollect("+bbwrite Body of staged test post.");
		Log($"  +bbwrite notifications: {writeMsgs.Count}");
		for (var i = 0; i < writeMsgs.Count; i++)
			Log($"  [write/{i}] {Truncate(writeMsgs[i], 200)}");

		// +bbpost with no args finalises the staged post
		var postMsgs = await RunAndCollect("+bbpost", 5000);
		Log($"  +bbpost (finalise) notifications: {postMsgs.Count}");
		for (var i = 0; i < postMsgs.Count; i++)
			Log($"  [post/{i}] {Truncate(postMsgs[i], 200)}");

		// Read back message 2
		var readMsgs = await RunAndCollect("+bbread 1/2", 5000);
		Log($"  +bbread 1/2 notifications: {readMsgs.Count}");
		for (var i = 0; i < readMsgs.Count; i++)
			Log($"  [read/{i}] {Truncate(readMsgs[i], 200)}");

		var outputPath = Path.Combine(AppContext.BaseDirectory, TestDataDir, "MyrddinBBS_StagedWriteAndPost_TestOutput.txt");
		await File.WriteAllTextAsync(outputPath, output.ToString());
		Console.WriteLine($"[BBS STAGED POST AND READ] Full test output written to: {outputPath}");

		await Assert.That(startMsgs.Any(m => m.Contains("You start your posting"))).IsTrue()
			.Because("+bbpost 1/Staged Test Post should confirm start of posting");

		await Assert.That(writeMsgs.Any(m => m == "Text added to bbpost.")).IsTrue()
			.Because("+bbwrite should confirm text was added");

		await Assert.That(postMsgs.Any(m => m.Contains("You post your note about 'Staged Test Post'"))).IsTrue()
			.Because("+bbpost should confirm the post was submitted");

		await Assert.That(readMsgs.Any(m => m.Contains("Staged Test Post"))).IsTrue()
			.Because("+bbread 1/2 should show the title 'Staged Test Post'");

		await Assert.That(readMsgs.Any(m => m.Contains("Body of staged test post."))).IsTrue()
			.Because("+bbread 1/2 should show the body 'Body of staged test post.'");
	}

	/// <summary>
	/// Clears the player's bb_read list to simulate unread messages, then runs
	/// +bbscan to verify unread postings are reported.
	/// </summary>
	[Test]
	[DependsOn(nameof(BBS_StagedPost_WriteAndPost))]
	public async Task BBS_BBScan_ShowsUnread()
	{
		var output = new StringBuilder();
		void Log(string message) { output.AppendLine(message); Console.WriteLine(message); }

		Log(new string('=', 78));
		Log("BBS_BBSCAN_SHOWSUNREAD");
		Log(new string('=', 78));

		var group1Name = await GetGroupName(1);
		Log($"  Group 1 name: {group1Name}");

		// Clear bb_read to make all messages appear unread
		await Parser.CommandParse(1, ConnectionService, MModule.single("&bb_read #1"));

		var msgs = await RunAndCollect("+bbscan");
		Log($"  +bbscan notifications: {msgs.Count}");
		for (var i = 0; i < msgs.Count; i++)
			Log($"  [{i}] {Truncate(msgs[i], 200)}");

		var combinedOutput = string.Join("\n", msgs);

		var outputPath = Path.Combine(AppContext.BaseDirectory, TestDataDir, "MyrddinBBS_BBScanUnread_TestOutput.txt");
		await File.WriteAllTextAsync(outputPath, output.ToString());
		Console.WriteLine($"[BBS BBSCAN UNREAD] Full test output written to: {outputPath}");

		await Assert.That(combinedOutput).Contains("Unread Postings on the Global Bulletin Board");
		await Assert.That(combinedOutput).Contains(group1Name);
		await Assert.That(combinedOutput).Contains("2 unread");
	}

	/// <summary>
	/// Reads unread messages for group 1 with +bbread 1/u and verifies both messages appear.
	/// </summary>
	[Test]
	[DependsOn(nameof(BBS_BBScan_ShowsUnread))]
	public async Task BBS_BBRead_UnreadFilter()
	{
		var output = new StringBuilder();
		void Log(string message) { output.AppendLine(message); Console.WriteLine(message); }

		Log(new string('=', 78));
		Log("BBS_BBREAD_UNREADFILTER");
		Log(new string('=', 78));

		var msgs = await RunAndCollect("+bbread 1/u", 5000);
		Log($"  +bbread 1/u notifications: {msgs.Count}");
		for (var i = 0; i < msgs.Count; i++)
			Log($"  [{i}] {Truncate(msgs[i], 200)}");

		var outputPath = Path.Combine(AppContext.BaseDirectory, TestDataDir, "MyrddinBBS_BBReadUnread_TestOutput.txt");
		await File.WriteAllTextAsync(outputPath, output.ToString());
		Console.WriteLine($"[BBS BBREAD UNREAD] Full test output written to: {outputPath}");

		await Assert.That(msgs.Any(m => m.Contains("Title Goes Here"))).IsTrue()
			.Because("+bbread 1/u should include unread message 1 'Title Goes Here'");

		await Assert.That(msgs.Any(m => m.Contains("Staged Test Post"))).IsTrue()
			.Because("+bbread 1/u should include unread message 2 'Staged Test Post'");
	}

	/// <summary>
	/// Marks all postings on all boards as read with +bbcatchup all.
	/// </summary>
	[Test]
	[DependsOn(nameof(BBS_BBRead_UnreadFilter))]
	public async Task BBS_BBCatchup_All()
	{
		var output = new StringBuilder();
		void Log(string message) { output.AppendLine(message); Console.WriteLine(message); }

		Log(new string('=', 78));
		Log("BBS_BBCATCHUP_ALL");
		Log(new string('=', 78));

		var msgs = await RunAndCollect("+bbcatchup all");
		Log($"  +bbcatchup all notifications: {msgs.Count}");
		for (var i = 0; i < msgs.Count; i++)
			Log($"  [{i}] {Truncate(msgs[i], 200)}");

		var outputPath = Path.Combine(AppContext.BaseDirectory, TestDataDir, "MyrddinBBS_BBCatchupAll_TestOutput.txt");
		await File.WriteAllTextAsync(outputPath, output.ToString());
		Console.WriteLine($"[BBS BBCATCHUP ALL] Full test output written to: {outputPath}");

		await Assert.That(msgs.Any(m => m == "All postings on all boards marked as read.")).IsTrue()
			.Because("+bbcatchup all should confirm all postings marked as read");
	}

	/// <summary>
	/// After catching up, verifies +bbscan reports no unread postings.
	/// </summary>
	[Test]
	[DependsOn(nameof(BBS_BBCatchup_All))]
	public async Task BBS_BBScan_NoUnread()
	{
		var output = new StringBuilder();
		void Log(string message) { output.AppendLine(message); Console.WriteLine(message); }

		Log(new string('=', 78));
		Log("BBS_BBSCAN_NOUNREAD");
		Log(new string('=', 78));

		var msgs = await RunAndCollect("+bbscan");
		Log($"  +bbscan notifications: {msgs.Count}");
		for (var i = 0; i < msgs.Count; i++)
			Log($"  [{i}] {Truncate(msgs[i], 200)}");

		var combinedOutput = string.Join("", msgs);

		var outputPath = Path.Combine(AppContext.BaseDirectory, TestDataDir, "MyrddinBBS_BBScanNoUnread_TestOutput.txt");
		await File.WriteAllTextAsync(outputPath, output.ToString());
		Console.WriteLine($"[BBS BBSCAN NO UNREAD] Full test output written to: {outputPath}");

		await Assert.That(combinedOutput).Contains("There are no unread postings on the Global Bulletin Board.");
	}

	/// <summary>
	/// Edits message 1/2 body text with +bbedit and verifies the new body is shown.
	/// </summary>
	[Test]
	[DependsOn(nameof(BBS_BBScan_NoUnread))]
	public async Task BBS_BBEdit_EditsMessage()
	{
		var output = new StringBuilder();
		void Log(string message) { output.AppendLine(message); Console.WriteLine(message); }

		Log(new string('=', 78));
		Log("BBS_BBEDIT_EDITSMESSAGE");
		Log(new string('=', 78));

		var group1Name = await GetGroupName(1);
		Log($"  Group 1 name: {group1Name}");

		var msgs = await RunAndCollect("+bbedit 1/2=Body of staged test post./Edited body content.");
		Log($"  +bbedit notifications: {msgs.Count}");
		for (var i = 0; i < msgs.Count; i++)
			Log($"  [{i}] {Truncate(msgs[i], 200)}");

		var outputPath = Path.Combine(AppContext.BaseDirectory, TestDataDir, "MyrddinBBS_BBEdit_TestOutput.txt");
		await File.WriteAllTextAsync(outputPath, output.ToString());
		Console.WriteLine($"[BBS BBEDIT] Full test output written to: {outputPath}");

		await Assert.That(msgs.Any(m => m.Contains("Message 1/2 (") && m.Contains(group1Name) && m.Contains("/2) now reads:"))).IsTrue()
			.Because("+bbedit should show the 'Message X/Y now reads:' header");

		await Assert.That(msgs.Any(m => m.Contains("Edited body content."))).IsTrue()
			.Because("+bbedit output should include the new body text");

		await Assert.That(msgs.Any(m => m.StartsWith("=============================================================================="))).IsTrue()
			.Because("+bbedit output should include separator lines");
	}

	/// <summary>
	/// Searches group 1 for posts by God with +bbsearch 1/God and verifies both messages appear.
	/// </summary>
	[Test]
	[DependsOn(nameof(BBS_BBEdit_EditsMessage))]
	public async Task BBS_BBSearch_FindsMessages()
	{
		var output = new StringBuilder();
		void Log(string message) { output.AppendLine(message); Console.WriteLine(message); }

		Log(new string('=', 78));
		Log("BBS_BBSEARCH_FINDSMESSAGES");
		Log(new string('=', 78));

		var group1Name = await GetGroupName(1);
		Log($"  Group 1 name: {group1Name}");

		var msgs = await RunAndCollect("+bbsearch 1/God", 5000);
		Log($"  +bbsearch notifications: {msgs.Count}");
		for (var i = 0; i < msgs.Count; i++)
			Log($"  [{i}] {Truncate(msgs[i], 200)}");

		var outputPath = Path.Combine(AppContext.BaseDirectory, TestDataDir, "MyrddinBBS_BBSearch_TestOutput.txt");
		await File.WriteAllTextAsync(outputPath, output.ToString());
		Console.WriteLine($"[BBS BBSEARCH] Full test output written to: {outputPath}");

		await Assert.That(msgs.Any(m => m.Contains($"**** {group1Name} ****"))).IsTrue()
			.Because("+bbsearch should show group name in header");

		await Assert.That(msgs.Any(m => m.Contains("Title Goes Here"))).IsTrue()
			.Because("+bbsearch should find message 1 'Title Goes Here'");

		await Assert.That(msgs.Any(m => m.Contains("Staged Test Post"))).IsTrue()
			.Because("+bbsearch should find message 2 'Staged Test Post'");
	}

	/// <summary>
	/// Sets a 30-day timeout on message 1/2 with +bbtimeout.
	/// </summary>
	[Test]
	[DependsOn(nameof(BBS_BBSearch_FindsMessages))]
	public async Task BBS_BBTimeout_SetsTimeout()
	{
		var output = new StringBuilder();
		void Log(string message) { output.AppendLine(message); Console.WriteLine(message); }

		Log(new string('=', 78));
		Log("BBS_BBTIMEOUT_SETSTIMEOUT");
		Log(new string('=', 78));

		var group1Name = await GetGroupName(1);
		Log($"  Group 1 name: {group1Name}");

		var msgs = await RunAndCollect("+bbtimeout 1/2=30", 5000);
		Log($"  +bbtimeout notifications: {msgs.Count}");
		for (var i = 0; i < msgs.Count; i++)
			Log($"  [{i}] {Truncate(msgs[i], 200)}");

		var outputPath = Path.Combine(AppContext.BaseDirectory, TestDataDir, "MyrddinBBS_BBTimeout_TestOutput.txt");
		await File.WriteAllTextAsync(outputPath, output.ToString());
		Console.WriteLine($"[BBS BBTIMEOUT] Full test output written to: {outputPath}");

		await Assert.That(msgs.Any(m => m.Contains("Message 2 in group") && m.Contains("30 day timeout"))).IsTrue()
			.Because("+bbtimeout should confirm the 30-day timeout was set");
	}

	/// <summary>
	/// Removes message 1/2 from group 1 with +bbremove.
	/// </summary>
	[Test]
	[DependsOn(nameof(BBS_BBTimeout_SetsTimeout))]
	public async Task BBS_BBRemove_RemovesMessage()
	{
		var output = new StringBuilder();
		void Log(string message) { output.AppendLine(message); Console.WriteLine(message); }

		Log(new string('=', 78));
		Log("BBS_BBREMOVE_REMOVESMESSAGE");
		Log(new string('=', 78));

		var group1Name = await GetGroupName(1);
		Log($"  Group 1 name: {group1Name}");

		var msgs = await RunAndCollect("+bbremove 1/2", 5000);
		Log($"  +bbremove notifications: {msgs.Count}");
		for (var i = 0; i < msgs.Count; i++)
			Log($"  [{i}] {Truncate(msgs[i], 200)}");

		var outputPath = Path.Combine(AppContext.BaseDirectory, TestDataDir, "MyrddinBBS_BBRemove_TestOutput.txt");
		await File.WriteAllTextAsync(outputPath, output.ToString());
		Console.WriteLine($"[BBS BBREMOVE] Full test output written to: {outputPath}");

		await Assert.That(msgs.Any(m => m.Contains("Message 2 removed from group") && m.Contains(group1Name))).IsTrue()
			.Because("+bbremove should confirm message 2 was removed from the group");
	}

	/// <summary>
	/// Creates a second BBS group with +bbnewgroup BBTestGroup2.
	/// </summary>
	[Test]
	[DependsOn(nameof(BBS_BBRemove_RemovesMessage))]
	public async Task BBS_BBNewGroup_Second()
	{
		var output = new StringBuilder();
		void Log(string message) { output.AppendLine(message); Console.WriteLine(message); }

		Log(new string('=', 78));
		Log("BBS_BBNEWGROUP_SECOND");
		Log(new string('=', 78));

		var msgs = await RunAndCollect("+bbnewgroup BBTestGroup2", 5000);
		Log($"  +bbnewgroup BBTestGroup2 notifications: {msgs.Count}");
		for (var i = 0; i < msgs.Count; i++)
			Log($"  [{i}] {Truncate(msgs[i], 200)}");

		var outputPath = Path.Combine(AppContext.BaseDirectory, TestDataDir, "MyrddinBBS_BBNewGroupSecond_TestOutput.txt");
		await File.WriteAllTextAsync(outputPath, output.ToString());
		Console.WriteLine($"[BBS BBNEWGROUP SECOND] Full test output written to: {outputPath}");

		await Assert.That(msgs.Any(m => m.Contains("Group number 2 added as 'BBTestGroup2'"))).IsTrue()
			.Because("+bbnewgroup should confirm BBTestGroup2 was created as group 2");
	}

	/// <summary>
	/// Moves message 1/1 from group 1 to group 2 with +bbmove 1/1 to 2.
	/// </summary>
	[Test]
	[DependsOn(nameof(BBS_BBNewGroup_Second))]
	public async Task BBS_BBMove_MovesMessage()
	{
		var output = new StringBuilder();
		void Log(string message) { output.AppendLine(message); Console.WriteLine(message); }

		Log(new string('=', 78));
		Log("BBS_BBMOVE_MOVESMESSAGE");
		Log(new string('=', 78));

		var msgs = await RunAndCollect("+bbmove 1/1 to 2");
		Log($"  +bbmove notifications: {msgs.Count}");
		for (var i = 0; i < msgs.Count; i++)
			Log($"  [{i}] {Truncate(msgs[i], 200)}");

		var outputPath = Path.Combine(AppContext.BaseDirectory, TestDataDir, "MyrddinBBS_BBMove_TestOutput.txt");
		await File.WriteAllTextAsync(outputPath, output.ToString());
		Console.WriteLine($"[BBS BBMOVE] Full test output written to: {outputPath}");

		await Assert.That(msgs.Any(m =>
			m.Contains("removed from group '1'") && m.Contains("added to group '2' as message #1") ||
			m.Contains("Message '1'") && m.Contains("removed from group '1'") && m.Contains("added to group '2'"))).IsTrue()
			.Because("+bbmove should confirm message was moved from group 1 to group 2");
	}

	/// <summary>
	/// Leaves and re-joins BBTestGroup2 with +bbleave and +bbjoin.
	/// Uses a non-God regular player because WIZARD BBS objects cannot set attributes on God (#1)
	/// — this matches PennMUSH's controls() semantics where Wizard can't control God.
	/// </summary>
	[Test]
	[DependsOn(nameof(BBS_BBMove_MovesMessage))]
	public async Task BBS_BBLeaveAndJoin()
	{
		var output = new StringBuilder();
		void Log(string message) { output.AppendLine(message); Console.WriteLine(message); }

		Log(new string('=', 78));
		Log("BBS_BBLEAVE_AND_JOIN");
		Log(new string('=', 78));

		// Use the non-God test player: the WIZARD BBS executor cannot set bb_omit on God (#1).
		// In PennMUSH, controls(WIZARD_obj, God) → false — same as SharpMUSH.
		// The regular user handle was created during BBS installation.
		var userHandle = _regularUserHandle > 0 ? _regularUserHandle : 1L;
		Log($"  Using handle {userHandle} (regular user: {_regularUserDbref ?? "God fallback"})");

		var leaveMsgs = await RunAndCollectAs("+bbleave 2", userHandle);
		Log($"  +bbleave 2 notifications: {leaveMsgs.Count}");
		for (var i = 0; i < leaveMsgs.Count; i++)
			Log($"  [leave/{i}] {Truncate(leaveMsgs[i], 200)}");

		var joinMsgs = await RunAndCollectAs("+bbjoin 2", userHandle);
		Log($"  +bbjoin 2 notifications: {joinMsgs.Count}");
		for (var i = 0; i < joinMsgs.Count; i++)
			Log($"  [join/{i}] {Truncate(joinMsgs[i], 200)}");

		var outputPath = Path.Combine(AppContext.BaseDirectory, TestDataDir, "MyrddinBBS_BBLeaveAndJoin_TestOutput.txt");
		await File.WriteAllTextAsync(outputPath, output.ToString());
		Console.WriteLine($"[BBS BBLEAVE/BBJOIN] Full test output written to: {outputPath}");

		await Assert.That(leaveMsgs.Any(m => m == "You have removed yourself from the BBTestGroup2 board.")).IsTrue()
			.Because("+bbleave 2 should confirm leaving BBTestGroup2");

		await Assert.That(joinMsgs.Any(m => m == "You have joined the BBTestGroup2 board.")).IsTrue()
			.Because("+bbjoin 2 should confirm joining BBTestGroup2");
	}

	/// <summary>
	/// Shows and sets global BBS config values with +bbconfig.
	/// </summary>
	[Test]
	[DependsOn(nameof(BBS_BBLeaveAndJoin))]
	public async Task BBS_BBConfig_ShowsAndSets()
	{
		var output = new StringBuilder();
		void Log(string message) { output.AppendLine(message); Console.WriteLine(message); }

		Log(new string('=', 78));
		Log("BBS_BBCONFIG_SHOWSANDSETS");
		Log(new string('=', 78));

		var showMsgs = await RunAndCollect("+bbconfig");
		Log($"  +bbconfig show notifications: {showMsgs.Count}");
		for (var i = 0; i < showMsgs.Count; i++)
			Log($"  [show/{i}] {Truncate(showMsgs[i], 200)}");

		var set30Msgs = await RunAndCollect("+bbconfig timeout=30");
		Log($"  +bbconfig timeout=30 notifications: {set30Msgs.Count}");
		for (var i = 0; i < set30Msgs.Count; i++)
			Log($"  [set30/{i}] {Truncate(set30Msgs[i], 200)}");

		var reset0Msgs = await RunAndCollect("+bbconfig timeout=0");
		Log($"  +bbconfig timeout=0 notifications: {reset0Msgs.Count}");
		for (var i = 0; i < reset0Msgs.Count; i++)
			Log($"  [reset/{i}] {Truncate(reset0Msgs[i], 200)}");

		var outputPath = Path.Combine(AppContext.BaseDirectory, TestDataDir, "MyrddinBBS_BBConfig_TestOutput.txt");
		await File.WriteAllTextAsync(outputPath, output.ToString());
		Console.WriteLine($"[BBS BBCONFIG] Full test output written to: {outputPath}");

		var showOutput = string.Join("\n", showMsgs);
		await Assert.That(showOutput).Contains("Myrddin's Global BBS");
		await Assert.That(showOutput).Contains("timeout:");
		await Assert.That(showOutput).Contains("autotimeout:");

		await Assert.That(set30Msgs.Any(m => m.Contains("30 days"))).IsTrue()
			.Because("+bbconfig timeout=30 should confirm the new 30-day timeout");

		await Assert.That(reset0Msgs.Any(m => m.Contains("none"))).IsTrue()
			.Because("+bbconfig timeout=0 should confirm timeout reset to none");
	}

	/// <summary>
	/// Locks group 2 for read and write access using +bblock and +bbwritelock.
	/// </summary>
	[Test]
	[DependsOn(nameof(BBS_BBConfig_ShowsAndSets))]
	public async Task BBS_BBLock_RestrictsGroup()
	{
		var output = new StringBuilder();
		void Log(string message) { output.AppendLine(message); Console.WriteLine(message); }

		Log(new string('=', 78));
		Log("BBS_BBLOCK_RESTRICTSGROUP");
		Log(new string('=', 78));

		var lockMsgs = await RunAndCollect("+bblock 2=flag/wizard");
		Log($"  +bblock notifications: {lockMsgs.Count}");
		for (var i = 0; i < lockMsgs.Count; i++)
			Log($"  [lock/{i}] {Truncate(lockMsgs[i], 200)}");

		var wlockMsgs = await RunAndCollect("+bbwritelock 2=flag/wizard");
		Log($"  +bbwritelock notifications: {wlockMsgs.Count}");
		for (var i = 0; i < wlockMsgs.Count; i++)
			Log($"  [wlock/{i}] {Truncate(wlockMsgs[i], 200)}");

		var outputPath = Path.Combine(AppContext.BaseDirectory, TestDataDir, "MyrddinBBS_BBLock_TestOutput.txt");
		await File.WriteAllTextAsync(outputPath, output.ToString());
		Console.WriteLine($"[BBS BBLOCK] Full test output written to: {outputPath}");

		await Assert.That(lockMsgs.Any(m => m.Contains("locked") && m.Contains("flag=wizard"))).IsTrue()
			.Because("+bblock should confirm group 2 was locked for flag=wizard");

		await Assert.That(wlockMsgs.Any(m => m.Contains("locked") && m.Contains("flag=wizard"))).IsTrue()
			.Because("+bbwritelock should confirm group 2 write access locked for flag=wizard");
	}

	/// <summary>
	/// Creates a temporary group, prompts for confirmation with +bbcleargroup, then deletes it.
	/// </summary>
	[Test]
	[DependsOn(nameof(BBS_BBLock_RestrictsGroup))]
	public async Task BBS_BBClearGroup_DeletesGroup()
	{
		var output = new StringBuilder();
		void Log(string message) { output.AppendLine(message); Console.WriteLine(message); }

		Log(new string('=', 78));
		Log("BBS_BBCLEARGROUP_DELETESGROUP");
		Log(new string('=', 78));

		var newGroupMsgs = await RunAndCollect("+bbnewgroup TempGroupForDeletion", 5000);
		Log($"  +bbnewgroup TempGroupForDeletion notifications: {newGroupMsgs.Count}");
		for (var i = 0; i < newGroupMsgs.Count; i++)
			Log($"  [newgroup/{i}] {Truncate(newGroupMsgs[i], 200)}");

		var clearMsgs = await RunAndCollect("+bbcleargroup 3");
		Log($"  +bbcleargroup 3 notifications: {clearMsgs.Count}");
		for (var i = 0; i < clearMsgs.Count; i++)
			Log($"  [clear/{i}] {Truncate(clearMsgs[i], 200)}");

		var confirmMsgs = await RunAndCollect("+bbconfirm 3");
		Log($"  +bbconfirm 3 notifications: {confirmMsgs.Count}");
		for (var i = 0; i < confirmMsgs.Count; i++)
			Log($"  [confirm/{i}] {Truncate(confirmMsgs[i], 200)}");

		var outputPath = Path.Combine(AppContext.BaseDirectory, TestDataDir, "MyrddinBBS_BBClearGroup_TestOutput.txt");
		await File.WriteAllTextAsync(outputPath, output.ToString());
		Console.WriteLine($"[BBS BBCLEARGROUP] Full test output written to: {outputPath}");

		await Assert.That(newGroupMsgs.Any(m => m.Contains("Group number 3 added as 'TempGroupForDeletion'"))).IsTrue()
			.Because("+bbnewgroup should confirm TempGroupForDeletion was created as group 3");

		await Assert.That(clearMsgs.Any(m => m.Contains("Warning") && m.Contains("+bbconfirm 3"))).IsTrue()
			.Because("+bbcleargroup should warn and prompt for +bbconfirm 3");

		await Assert.That(confirmMsgs.Any(m => m.Contains("Group number 3 removed."))).IsTrue()
			.Because("+bbconfirm should confirm group 3 was removed");
	}
}
