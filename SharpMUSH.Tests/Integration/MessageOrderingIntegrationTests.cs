using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using TUnit.Core;

namespace SharpMUSH.Tests.Integration;

/// <summary>
/// Full integration test for message ordering through the entire system:
/// Server → Kafka → ConnectionServer → Telnet Client
/// </summary>
public class MessageOrderingIntegrationTests
{
	private Process? _mainServerProcess;
	private Process? _connectionServerProcess;
	private const int TelnetPort = 4201;
	private const int StartupDelayMs = 5000; // Time for servers to start

	[Before(Test)]
	public async Task Setup()
	{
		// Start SharpMUSH.Server
		_mainServerProcess = StartServer(
			"/home/runner/work/SharpMUSH/SharpMUSH/SharpMUSH.Server/bin/Debug/net9.0/SharpMUSH.Server",
			"SharpMUSH.Server"
		);

		// Start SharpMUSH.ConnectionServer
		_connectionServerProcess = StartServer(
			"/home/runner/work/SharpMUSH/SharpMUSH/SharpMUSH.ConnectionServer/bin/Debug/net9.0/SharpMUSH.ConnectionServer",
			"SharpMUSH.ConnectionServer"
		);

		// Wait for servers to start
		await Task.Delay(StartupDelayMs);
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

	[Test]
	public async Task TelnetOutput_WithDolCommand_MaintainsMessageOrdering()
	{
		// Arrange
		using var client = new TcpClient();
		await client.ConnectAsync("localhost", TelnetPort);
		using var stream = client.GetStream();
		var reader = new StreamReader(stream, Encoding.UTF8);
		var writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };

		// Read initial telnet negotiation and welcome messages
		await ReadUntilPrompt(reader, TimeSpan.FromSeconds(5));

		// Act: Connect as wizard
		await writer.WriteLineAsync("connect wizard password");
		await ReadUntilPrompt(reader, TimeSpan.FromSeconds(5));

		// Execute @dol command that generates 100 sequential messages
		await writer.WriteLineAsync("@dol lnum(1,100)=think %iL");

		// Capture all output for the next several seconds
		var output = await ReadOutput(reader, TimeSpan.FromSeconds(15));

		// Assert: Extract numbers and verify they're in order
		var numbers = ExtractNumbers(output);

		// Should have exactly 100 numbers
		if (numbers.Count != 100)
		{
			throw new Exception($"Expected 100 messages but got {numbers.Count}. Output:\n{output}");
		}

		// Verify sequential ordering
		for (int i = 0; i < numbers.Count; i++)
		{
			var expected = i + 1;
			var actual = numbers[i];

			if (actual != expected)
			{
				// Find where this number appeared out of order
				var context = GetOrderingContext(numbers, i);
				throw new Exception($"Message ordering violation at position {i}:\n" +
							$"Expected: {expected}\n" +
							$"Actual: {actual}\n" +
							$"Context: {context}\n" +
							$"Full sequence: [{string.Join(", ", numbers)}]");
			}
		}

		Console.WriteLine("✅ All 100 messages received in perfect order!");
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
