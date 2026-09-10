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
	[Category("NeedsSetup")]
	[Test, Skip("Manual performance validation - requires actual servers running on 127.0.0.1:4201")]
	public async Task MeasureActualDoListPerformance()
	{
		const string host = "127.0.0.1";
		const int port = 4201;

		using var client = new TcpClient();
		await client.ConnectAsync(host, port);
		await using var stream = client.GetStream();
		using var reader = new StreamReader(stream, Encoding.UTF8);
		await using var writer = new StreamWriter(stream, Encoding.UTF8);
		writer.AutoFlush = true;

		var welcome = await ReadUntilPrompt(reader);
		TestDiagnostics.WriteLine("Server welcome:");
		TestDiagnostics.WriteLine(welcome);

		await writer.WriteLineAsync("connect #1");
		var loginResponse = await ReadUntilPrompt(reader);
		TestDiagnostics.WriteLine("Login response:");
		TestDiagnostics.WriteLine(loginResponse);

		TestDiagnostics.WriteLine("\n=== Test 1: @dolist lnum(100)=@pemit %#=%i0 ===");
		var sw1 = Stopwatch.StartNew();
		await writer.WriteLineAsync("@dolist lnum(100)=@pemit %#=%i0");
		var dolistPemitOutput = await ReadUntilPrompt(reader);
		sw1.Stop();
		TestDiagnostics.WriteLine($"Time: {sw1.ElapsedMilliseconds}ms");
		TestDiagnostics.WriteLine($"Lines received: {dolistPemitOutput.Split('\n').Length}");
		TestDiagnostics.WriteLine($"First few lines:\n{string.Join("\n", dolistPemitOutput.Split('\n').Take(5))}");

		TestDiagnostics.WriteLine("\n=== Test 2: @dolist lnum(100)=think %i0 ===");
		var sw2 = Stopwatch.StartNew();
		await writer.WriteLineAsync("@dolist lnum(100)=think %i0");
		var dolistThinkOutput = await ReadUntilPrompt(reader);
		sw2.Stop();
		TestDiagnostics.WriteLine($"Time: {sw2.ElapsedMilliseconds}ms");
		TestDiagnostics.WriteLine($"Lines received: {dolistThinkOutput.Split('\n').Length}");

		TestDiagnostics.WriteLine("\n=== Test 3: think iter(lnum(100),%i0,,%r) ===");
		var sw3 = Stopwatch.StartNew();
		await writer.WriteLineAsync("think iter(lnum(100),%i0,,%r)");
		var iterOutput = await ReadUntilPrompt(reader);
		sw3.Stop();
		TestDiagnostics.WriteLine($"Time: {sw3.ElapsedMilliseconds}ms");
		TestDiagnostics.WriteLine($"Lines received: {iterOutput.Split('\n').Length}");

		TestDiagnostics.WriteLine("\n=== Test 4: @dolist lnum(1000)=@pemit %#=%i0 ===");
		var sw4 = Stopwatch.StartNew();
		await writer.WriteLineAsync("@dolist lnum(1000)=@pemit %#=%i0");
		var dolistPemit1000Output = await ReadUntilPrompt(reader);
		sw4.Stop();
		TestDiagnostics.WriteLine($"Time: {sw4.ElapsedMilliseconds}ms");
		TestDiagnostics.WriteLine($"Lines received: {dolistPemit1000Output.Split('\n').Length}");

		TestDiagnostics.WriteLine("\n=== Test 5: think iter(lnum(1000),%i0,,%r) ===");
		var sw5 = Stopwatch.StartNew();
		await writer.WriteLineAsync("think iter(lnum(1000),%i0,,%r)");
		var iter1000Output = await ReadUntilPrompt(reader);
		sw5.Stop();
		TestDiagnostics.WriteLine($"Time: {sw5.ElapsedMilliseconds}ms");
		TestDiagnostics.WriteLine($"Lines received: {iter1000Output.Split('\n').Length}");

		TestDiagnostics.WriteLine("\n=== SUMMARY ===");
		TestDiagnostics.WriteLine($"@dolist 100 with @pemit: {sw1.ElapsedMilliseconds}ms");
		TestDiagnostics.WriteLine($"@dolist 100 with think:  {sw2.ElapsedMilliseconds}ms");
		TestDiagnostics.WriteLine($"iter 100:                 {sw3.ElapsedMilliseconds}ms");
		TestDiagnostics.WriteLine($"@dolist 1000 with @pemit: {sw4.ElapsedMilliseconds}ms");
		TestDiagnostics.WriteLine($"iter 1000:                {sw5.ElapsedMilliseconds}ms");
		TestDiagnostics.WriteLine($"\nRatio (dolist/iter 1000): {(double)sw4.ElapsedMilliseconds / sw5.ElapsedMilliseconds:F2}x");

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
			if (reader.BaseStream is NetworkStream ns && ns.DataAvailable)
			{
				var count = await reader.ReadAsync(buffer, 0, buffer.Length);
				sb.Append(buffer, 0, count);

				start = DateTime.UtcNow;
			}
			else
			{
				await Task.Delay(10);
			}

			var text = sb.ToString();
			if (text.Contains(">") || text.Contains("Huh?"))
			{
				break;
			}
		}

		return sb.ToString();
	}
}
