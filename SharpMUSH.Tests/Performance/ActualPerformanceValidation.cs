using System.Diagnostics;
using System.Net.Sockets;
using System.Text;

namespace SharpMUSH.Tests.Performance;

/// <summary>
/// This test validates actual TCP performance behavior to understand the real bottleneck
/// before making optimization assumptions.
/// 
/// TO RUN THIS TEST:
/// 1. Start the ConnectionServer: cd SharpMUSH.ConnectionServer && dotnet run
/// 2. Start the Server: cd SharpMUSH.Server && dotnet run  
/// 3. Run this test manually (it's skipped by default)
/// 
/// This will measure the ACTUAL performance difference over TCP to validate assumptions.
/// </summary>
public class ActualPerformanceValidation
{
	[Test, Skip("Manual performance validation - requires actual servers running on 127.0.0.1:4201")]
	public async Task MeasureActualDoListPerformance()
	{
		const string host = "127.0.0.1";
		const int port = 4201;
		
		using var client = new TcpClient();
		await client.ConnectAsync(host, port);
		using var stream = client.GetStream();
		using var reader = new StreamReader(stream, Encoding.UTF8);
		using var writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };
		
		// Read initial connection message
		var welcome = await ReadUntilPrompt(reader);
		Console.WriteLine("Server welcome:");
		Console.WriteLine(welcome);
		
		// Login as #1
		await writer.WriteLineAsync("connect #1");
		var loginResponse = await ReadUntilPrompt(reader);
		Console.WriteLine("Login response:");
		Console.WriteLine(loginResponse);
		
		// Test 1: @dolist with @pemit
		Console.WriteLine("\n=== Test 1: @dolist lnum(100)=@pemit %#=%i0 ===");
		var sw1 = Stopwatch.StartNew();
		await writer.WriteLineAsync("@dolist lnum(100)=@pemit %#=%i0");
		var dolistPemitOutput = await ReadUntilPrompt(reader);
		sw1.Stop();
		Console.WriteLine($"Time: {sw1.ElapsedMilliseconds}ms");
		Console.WriteLine($"Lines received: {dolistPemitOutput.Split('\n').Length}");
		Console.WriteLine($"First few lines:\n{string.Join("\n", dolistPemitOutput.Split('\n').Take(5))}");
		
		// Test 2: @dolist with think
		Console.WriteLine("\n=== Test 2: @dolist lnum(100)=think %i0 ===");
		var sw2 = Stopwatch.StartNew();
		await writer.WriteLineAsync("@dolist lnum(100)=think %i0");
		var dolistThinkOutput = await ReadUntilPrompt(reader);
		sw2.Stop();
		Console.WriteLine($"Time: {sw2.ElapsedMilliseconds}ms");
		Console.WriteLine($"Lines received: {dolistThinkOutput.Split('\n').Length}");
		
		// Test 3: think iter()
		Console.WriteLine("\n=== Test 3: think iter(lnum(100),%i0,,%r) ===");
		var sw3 = Stopwatch.StartNew();
		await writer.WriteLineAsync("think iter(lnum(100),%i0,,%r)");
		var iterOutput = await ReadUntilPrompt(reader);
		sw3.Stop();
		Console.WriteLine($"Time: {sw3.ElapsedMilliseconds}ms");
		Console.WriteLine($"Lines received: {iterOutput.Split('\n').Length}");
		
		// Test 4: Measure with larger iteration count
		Console.WriteLine("\n=== Test 4: @dolist lnum(1000)=@pemit %#=%i0 ===");
		var sw4 = Stopwatch.StartNew();
		await writer.WriteLineAsync("@dolist lnum(1000)=@pemit %#=%i0");
		var dolistPemit1000Output = await ReadUntilPrompt(reader);
		sw4.Stop();
		Console.WriteLine($"Time: {sw4.ElapsedMilliseconds}ms");
		Console.WriteLine($"Lines received: {dolistPemit1000Output.Split('\n').Length}");
		
		// Test 5: iter with 1000
		Console.WriteLine("\n=== Test 5: think iter(lnum(1000),%i0,,%r) ===");
		var sw5 = Stopwatch.StartNew();
		await writer.WriteLineAsync("think iter(lnum(1000),%i0,,%r)");
		var iter1000Output = await ReadUntilPrompt(reader);
		sw5.Stop();
		Console.WriteLine($"Time: {sw5.ElapsedMilliseconds}ms");
		Console.WriteLine($"Lines received: {iter1000Output.Split('\n').Length}");
		
		// Summary
		Console.WriteLine("\n=== SUMMARY ===");
		Console.WriteLine($"@dolist 100 with @pemit: {sw1.ElapsedMilliseconds}ms");
		Console.WriteLine($"@dolist 100 with think:  {sw2.ElapsedMilliseconds}ms");
		Console.WriteLine($"iter 100:                 {sw3.ElapsedMilliseconds}ms");
		Console.WriteLine($"@dolist 1000 with @pemit: {sw4.ElapsedMilliseconds}ms");
		Console.WriteLine($"iter 1000:                {sw5.ElapsedMilliseconds}ms");
		Console.WriteLine($"\nRatio (dolist/iter 1000): {(double)sw4.ElapsedMilliseconds / sw5.ElapsedMilliseconds:F2}x");
		
		client.Close();
	}
	
	private async Task<string> ReadUntilPrompt(StreamReader reader)
	{
		var sb = new StringBuilder();
		var buffer = new char[4096];
		var timeout = TimeSpan.FromSeconds(10);
		var start = DateTime.UtcNow;
		
		while (DateTime.UtcNow - start < timeout)
		{
			// Check if data is available
			if (reader.BaseStream is NetworkStream ns && ns.DataAvailable)
			{
				var count = await reader.ReadAsync(buffer, 0, buffer.Length);
				sb.Append(buffer, 0, count);
				
				// Reset timeout on data received
				start = DateTime.UtcNow;
			}
			else
			{
				// Wait a bit before checking again
				await Task.Delay(10);
			}
			
			// Check if we've received a prompt (very basic check)
			var text = sb.ToString();
			if (text.Contains(">") || text.Contains("Huh?"))
			{
				break;
			}
		}
		
		return sb.ToString();
	}
}
