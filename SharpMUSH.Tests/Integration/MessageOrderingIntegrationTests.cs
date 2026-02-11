using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using TUnit.Core;

namespace SharpMUSH.Tests.Integration;

/// <summary>
/// Full integration test for message ordering through the entire system:
/// Server → Kafka → ConnectionServer → Telnet Client
/// 
/// IMPORTANT: This test is marked [Explicit] and must be run manually.
/// 
/// Prerequisites:
/// 1. Infrastructure must be running: docker-compose up -d
/// 2. Servers must be built: dotnet build
/// 3. Run with: dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/MessageOrderingIntegrationTests/*"
/// </summary>
public class MessageOrderingIntegrationTests
{
	private Process? _mainServerProcess;
	private Process? _connectionServerProcess;
	private const int TelnetPort = 4201;
	private const int StartupDelayMs = 10000; // Time for servers to start and connect to infrastructure

	[Before(Test)]
	public async Task Setup()
	{
		// Find the solution directory
		var solutionDir = FindSolutionDirectory();
		if (solutionDir == null)
		{
			throw new Exception("Could not find solution directory");
		}

		Console.WriteLine($"Solution directory: {solutionDir}");

		// Start SharpMUSH.Server
		var mainServerPath = Path.Combine(solutionDir, "SharpMUSH.Server", "bin", "Debug", "net10.0", "SharpMUSH.Server");
		if (!File.Exists(mainServerPath))
		{
			throw new Exception($"SharpMUSH.Server not found at: {mainServerPath}. Run 'dotnet build' first.");
		}
		_mainServerProcess = StartServer(mainServerPath, "SharpMUSH.Server");

		// Start SharpMUSH.ConnectionServer
		var connectionServerPath = Path.Combine(solutionDir, "SharpMUSH.ConnectionServer", "bin", "Debug", "net10.0", "SharpMUSH.ConnectionServer");
		if (!File.Exists(connectionServerPath))
		{
			throw new Exception($"SharpMUSH.ConnectionServer not found at: {connectionServerPath}. Run 'dotnet build' first.");
		}
		_connectionServerProcess = StartServer(connectionServerPath, "SharpMUSH.ConnectionServer");

		// Wait for servers to start and connect to infrastructure
		Console.WriteLine($"Waiting {StartupDelayMs}ms for servers to initialize...");
		await Task.Delay(StartupDelayMs);
		Console.WriteLine("Servers should be ready");
	}

	private static string? FindSolutionDirectory()
	{
		var currentDir = Directory.GetCurrentDirectory();
		while (currentDir != null)
		{
			if (File.Exists(Path.Combine(currentDir, "SharpMUSH.sln")))
			{
				return currentDir;
			}
			currentDir = Directory.GetParent(currentDir)?.FullName;
		}
		return null;
	}

	[After(Test)]
	public void Cleanup()
	{
		try
		{
			_connectionServerProcess?.Kill(true);
			_connectionServerProcess?.WaitForExit(5000);
		}
		catch { }

		try
		{
			_mainServerProcess?.Kill(true);
			_mainServerProcess?.WaitForExit(5000);
		}
		catch { }

		_connectionServerProcess?.Dispose();
		_mainServerProcess?.Dispose();
	}

	[Test, Explicit("Full integration test - requires infrastructure and takes several minutes")]
	public async Task TelnetOutput_WithDolCommand_MaintainsMessageOrdering()
	{
		Console.WriteLine("=== Starting Integration Test ===");
		Console.WriteLine($"Connecting to localhost:{TelnetPort}...");
		
		// Arrange
		using var client = new TcpClient();
		try
		{
			await client.ConnectAsync("localhost", TelnetPort);
			Console.WriteLine("✅ Connected to telnet server");
		}
		catch (Exception ex)
		{
			throw new Exception($"Failed to connect to localhost:{TelnetPort}. Is SharpMUSH.ConnectionServer running?", ex);
		}

		using var stream = client.GetStream();
		var reader = new StreamReader(stream, Encoding.UTF8);
		var writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };

		// Read initial telnet negotiation and welcome messages
		Console.WriteLine("Reading initial welcome messages...");
		var welcome = await ReadUntilPrompt(reader, TimeSpan.FromSeconds(5));
		Console.WriteLine($"Welcome message received ({welcome.Length} chars)");

		// Act: Connect as wizard
		Console.WriteLine("Authenticating as wizard...");
		await writer.WriteLineAsync("connect wizard password");
		var loginResponse = await ReadUntilPrompt(reader, TimeSpan.FromSeconds(5));
		Console.WriteLine($"Login response received ({loginResponse.Length} chars)");

		// Execute @dol command that generates 100 sequential messages
		Console.WriteLine("Executing: @dol lnum(1,100)=think %iL");
		await writer.WriteLineAsync("@dol lnum(1,100)=think %iL");

		// Capture all output for the next several seconds
		Console.WriteLine("Capturing output (max 15 seconds, or until 2 seconds idle)...");
		var output = await ReadOutput(reader, TimeSpan.FromSeconds(15));
		Console.WriteLine($"Output captured ({output.Length} chars, {output.Count(c => c == '\n')} lines)");

		// Assert: Extract numbers and verify they're in order
		var numbers = ExtractNumbers(output);
		Console.WriteLine($"Extracted {numbers.Count} numbers from output");

		// Should have exactly 100 numbers
		if (numbers.Count != 100)
		{
			Console.WriteLine($"❌ Expected 100 messages but got {numbers.Count}");
			Console.WriteLine($"Numbers found: [{string.Join(", ", numbers.Take(20))}...]");
			Console.WriteLine("\n=== RAW OUTPUT ===");
			Console.WriteLine(output);
			throw new Exception($"Expected 100 messages but got {numbers.Count}");
		}

		// Verify sequential ordering
		var violations = new List<string>();
		for (int i = 0; i < numbers.Count; i++)
		{
			var expected = i + 1;
			var actual = numbers[i];

			if (actual != expected)
			{
				var context = GetOrderingContext(numbers, i);
				var violation = $"Position {i}: Expected {expected}, got {actual}. Context: {context}";
				violations.Add(violation);
				Console.WriteLine($"❌ {violation}");
			}
		}

		if (violations.Any())
		{
			Console.WriteLine($"\n❌ Found {violations.Count} ordering violations:");
			foreach (var v in violations)
			{
				Console.WriteLine($"  - {v}");
			}
			Console.WriteLine($"\nFull sequence: [{string.Join(", ", numbers)}]");
			throw new Exception($"Message ordering violated in {violations.Count} places. See console output for details.");
		}

		Console.WriteLine("✅✅✅ SUCCESS! All 100 messages received in perfect order 1-100!");
		Console.WriteLine("This proves the KafkaFlow implementation maintains message ordering correctly.");
	}

	private static Process StartServer(string executablePath, string name)
	{
		var process = new Process
		{
			StartInfo = new ProcessStartInfo
			{
				FileName = executablePath,
				UseShellExecute = false,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				CreateNoWindow = true
			}
		};

		process.OutputDataReceived += (sender, args) =>
		{
			if (!string.IsNullOrEmpty(args.Data))
				Console.WriteLine($"[{name}] {args.Data}");
		};

		process.ErrorDataReceived += (sender, args) =>
		{
			if (!string.IsNullOrEmpty(args.Data))
				Console.WriteLine($"[{name} ERROR] {args.Data}");
		};

		process.Start();
		process.BeginOutputReadLine();
		process.BeginErrorReadLine();

		Console.WriteLine($"Started {name} with PID {process.Id}");
		return process;
	}

	private static async Task<string> ReadUntilPrompt(StreamReader reader, TimeSpan timeout)
	{
		var sb = new StringBuilder();
		var cts = new CancellationTokenSource(timeout);

		try
		{
			while (!cts.Token.IsCancellationRequested)
			{
				if (reader.Peek() > -1)
				{
					var line = await reader.ReadLineAsync(cts.Token);
					if (line != null)
					{
						sb.AppendLine(line);
						if (line.Contains(">") || line.Contains("connected", StringComparison.OrdinalIgnoreCase))
						{
							break;
						}
					}
				}
				else
				{
					await Task.Delay(100, cts.Token);
				}
			}
		}
		catch (OperationCanceledException)
		{
			// Timeout is acceptable
		}

		return sb.ToString();
	}

	private static async Task<string> ReadOutput(StreamReader reader, TimeSpan timeout)
	{
		var sb = new StringBuilder();
		var cts = new CancellationTokenSource(timeout);
		var lastReadTime = DateTime.UtcNow;
		var idleTimeout = TimeSpan.FromSeconds(2);

		try
		{
			while (!cts.Token.IsCancellationRequested)
			{
				if (reader.Peek() > -1)
				{
					var line = await reader.ReadLineAsync(cts.Token);
					if (line != null)
					{
						sb.AppendLine(line);
						lastReadTime = DateTime.UtcNow;
					}
				}
				else
				{
					// No data available, check if we've been idle too long
					if (DateTime.UtcNow - lastReadTime > idleTimeout)
					{
						// No more data coming, we're done
						break;
					}
					await Task.Delay(100, cts.Token);
				}
			}
		}
		catch (OperationCanceledException)
		{
			// Timeout reached
		}

		return sb.ToString();
	}

	private static List<int> ExtractNumbers(string output)
	{
		var numbers = new List<int>();
		var regex = new Regex(@"^\s*(\d+)\s*$", RegexOptions.Multiline);
		var matches = regex.Matches(output);

		foreach (Match match in matches)
		{
			if (int.TryParse(match.Groups[1].Value, out var number))
			{
				numbers.Add(number);
			}
		}

		return numbers;
	}

	private static string GetOrderingContext(List<int> numbers, int index)
	{
		var start = Math.Max(0, index - 5);
		var end = Math.Min(numbers.Count - 1, index + 5);
		var context = numbers.Skip(start).Take(end - start + 1).ToList();
		return $"[{string.Join(", ", context)}] (showing position {index} ± 5)";
	}
}
