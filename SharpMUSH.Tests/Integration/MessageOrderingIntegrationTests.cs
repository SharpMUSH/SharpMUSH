using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Tests.Integration;

/// <summary>
/// Full integration test for message ordering.
/// Uses TUnit test factories for both servers.
/// </summary>
[Explicit]
[NotInParallel]
public class MessageOrderingIntegrationTests
{
[ClassDataSource<WebAppFactory>(Shared = SharedType.PerTestSession)]
public required WebAppFactory MainServer { get; init; }

[ClassDataSource<RedPandaTestServer>(Shared = SharedType.PerTestSession)]
public required RedPandaTestServer RedPandaTestServer { get; init; }

[ClassDataSource<RedisTestServer>(Shared = SharedType.PerTestSession)]
public required RedisTestServer RedisTestServer { get; init; }

private ConnectionServerFactory? _connectionServer;

private const int ConnectionTimeout = 15000; // 15 seconds
private const int IdleTimeout = 2000; // 2 seconds with no data

[Before(Test)]
public async Task Setup()
{
Console.WriteLine("=== Setting up ConnectionServer ===");
_connectionServer = new ConnectionServerFactory(RedPandaTestServer, RedisTestServer);
await _connectionServer.InitializeAsync();
Console.WriteLine($"✓ ConnectionServer ready on telnet:{_connectionServer.TelnetPort}, http:{_connectionServer.HttpPort}");
}

[After(Test)]
public async Task Cleanup()
{
if (_connectionServer != null)
{
Console.WriteLine("=== Cleaning up ConnectionServer ===");
await _connectionServer.DisposeAsync();
}
}

[Test]
[Timeout(600_000)] // 10 minutes
public async Task TelnetOutput_WithDolCommand_MaintainsMessageOrdering(CancellationToken cancellationToken)
{
Console.WriteLine("=== Message Ordering Integration Test ===\n");
Console.WriteLine("Using TUnit test factories for both servers\n");

if (_connectionServer == null)
{
throw new InvalidOperationException("ConnectionServer not initialized");
}

// Verify services are available
var parser = MainServer.Services.GetRequiredService<IMUSHCodeParser>();
var connectionService = MainServer.Services.GetRequiredService<IConnectionService>();
Console.WriteLine("✓ Main server services available");
Console.WriteLine($"✓ ConnectionServer listening on telnet port {_connectionServer.TelnetPort}");
Console.WriteLine($"✓ ConnectionServer listening on HTTP port {_connectionServer.HttpPort}");

try
{
// Connect via TCP
Console.WriteLine($"\nConnecting to telnet port {_connectionServer.TelnetPort}...");
using var client = new TcpClient();
await client.ConnectAsync("localhost", _connectionServer.TelnetPort);
Console.WriteLine("✓ Connected!");

var stream = client.GetStream();
var reader = new StreamReader(stream, Encoding.UTF8);
var writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };

// Read initial telnet negotiation and welcome message
Console.WriteLine("\nReading welcome message...");
var welcome = await ReadUntilIdle(reader, 3000);
Console.WriteLine($"✓ Received {welcome.Length} bytes of welcome/negotiation");

// Authenticate as wizard
Console.WriteLine("\nAuthenticating as wizard...");
await writer.WriteLineAsync("connect wizard password");
var authResponse = await ReadUntilIdle(reader, 2000);
Console.WriteLine($"✓ Auth response: {authResponse.Length} bytes");

// Execute the @dol command
Console.WriteLine("\nExecuting: @dol lnum(1,100)=think %iL");
await writer.WriteLineAsync("@dol lnum(1,100)=think %iL");

// Capture output
Console.WriteLine("Capturing output...");
var output = await ReadUntilIdle(reader, ConnectionTimeout, IdleTimeout);
Console.WriteLine($"✓ Received {output.Length} bytes ({output.Split('\n').Length} lines)");

// Extract numbers from output
var numbers = ExtractNumbers(output);
Console.WriteLine($"✓ Extracted {numbers.Count} numbers from output");

// Verify we got 100 messages
if (numbers.Count != 100)
{
Console.WriteLine($"\n❌ ERROR: Expected 100 messages, got {numbers.Count}");
Console.WriteLine($"Full output:\n{output}");
Assert.Fail($"Expected 100 messages but got {numbers.Count}");
return;
}

// Verify sequential order
var violations = new List<string>();
for (int i = 0; i < numbers.Count; i++)
{
var expected = i + 1;
var actual = numbers[i];
if (actual != expected)
{
var context = GetContext(numbers, i, 2);
violations.Add($"Position {i}: expected {expected}, got {actual} (context: {context})");
}
}

if (violations.Any())
{
Console.WriteLine($"\n=== ❌ ORDERING VIOLATIONS ({violations.Count}) ===");
foreach (var violation in violations)
{
Console.WriteLine(violation);
}
Console.WriteLine($"\nFull sequence: {string.Join(", ", numbers)}");
Assert.Fail($"Found {violations.Count} ordering violations. Messages are NOT in sequential order.");
}
else
{
Console.WriteLine("\n✓✓✓ SUCCESS: All 100 messages arrived in perfect sequential order (1-100) ✓✓✓");
Console.WriteLine("This proves the KafkaFlow implementation maintains message ordering correctly!");
}
}
catch (SocketException ex)
{
Console.WriteLine($"\n❌ Socket error: {ex.Message}");
Console.WriteLine("ConnectionServer factory should have started automatically via TUnit");
throw;
}
}

private static async Task<string> ReadUntilIdle(StreamReader reader, int totalTimeout, int idleTimeout = 1000)
{
var sb = new StringBuilder();
var buffer = new char[4096];
var lastReadTime = DateTime.UtcNow;
var startTime = DateTime.UtcNow;
var readCount = 0;

Console.WriteLine($"  ReadUntilIdle: totalTimeout={totalTimeout}ms, idleTimeout={idleTimeout}ms");

while (true)
{
var elapsed = (DateTime.UtcNow - startTime).TotalMilliseconds;
if (elapsed > totalTimeout)
{
Console.WriteLine($"  ReadUntilIdle: Hit total timeout after {elapsed}ms, {readCount} reads, {sb.Length} bytes");
break;
}

var timeSinceLastRead = (DateTime.UtcNow - lastReadTime).TotalMilliseconds;
if (timeSinceLastRead > idleTimeout && sb.Length > 0)
{
// No data for idle timeout, assume we're done
Console.WriteLine($"  ReadUntilIdle: Idle timeout after {timeSinceLastRead}ms, {readCount} reads, {sb.Length} bytes");
break;
}

if (reader.Peek() >= 0)
{
var count = await reader.ReadAsync(buffer, 0, buffer.Length);
if (count > 0)
{
sb.Append(buffer, 0, count);
lastReadTime = DateTime.UtcNow;
readCount++;
if (readCount % 10 == 0)
{
Console.WriteLine($"  ReadUntilIdle: {readCount} reads, {sb.Length} bytes so far...");
}
}
}
else
{
await Task.Delay(50);
}
}

Console.WriteLine($"  ReadUntilIdle: Complete - {readCount} reads, {sb.Length} bytes total");
return sb.ToString();
}

private static List<int> ExtractNumbers(string output)
{
var numbers = new List<int>();
var regex = new Regex(@"\b(\d+)\b");

foreach (Match match in regex.Matches(output))
{
if (int.TryParse(match.Value, out var num))
{
numbers.Add(num);
}
}

return numbers;
}

private static string GetContext(List<int> numbers, int index, int contextSize)
{
var start = Math.Max(0, index - contextSize);
var end = Math.Min(numbers.Count - 1, index + contextSize);
var contextNumbers = numbers.Skip(start).Take(end - start + 1);
return string.Join(", ", contextNumbers);
}
}
