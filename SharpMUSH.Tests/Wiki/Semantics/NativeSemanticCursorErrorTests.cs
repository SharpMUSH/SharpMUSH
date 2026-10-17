using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace SharpMUSH.Tests.Wiki.Semantics;

public class NativeSemanticCursorErrorTests
{
	[Test]
	public async Task SupportedCipherFailureAfterFirstRowThrowsWithoutPartialBatchOrLeakedReservation()
	{
		if (!OperatingSystem.IsLinux() || RuntimeInformation.ProcessArchitecture != Architecture.X64)
		{
			Skip.Test("Supported cipher proof is qualified for the reviewed Linux-x64 native binary");
			return;
		}
		var configured = Environment.GetEnvironmentVariable("TMPDIR");
		var scratchRoot = Path.GetFullPath(string.IsNullOrWhiteSpace(configured)
			? Path.Combine(Environment.CurrentDirectory, "work/semantic-native-cipher-error") : configured);
		if (scratchRoot == "/tmp" || scratchRoot.StartsWith("/tmp/", StringComparison.Ordinal)
			|| scratchRoot == "/var/tmp" || scratchRoot.StartsWith("/var/tmp/", StringComparison.Ordinal))
			throw new InvalidOperationException("Native cipher probe requires disk-backed scratch");
		var scratch = Path.Combine(scratchRoot, "semantic-cipher-run-" + Guid.NewGuid().ToString("N"));
		if (Directory.Exists(scratch) || File.Exists(scratch)) throw new InvalidOperationException("Scratch already exists");
		var fixtureParent = Path.Combine(scratch, "semantic-cipher-" + Guid.NewGuid().ToString("N"));
		var runtimeTemp = Path.Combine(scratch, "runtime-temp");
		var world = Path.Combine(fixtureParent, "world");
		var evidence = Path.Combine(fixtureParent, "evidence");
		Process? process = null;
		Task<string>? stdout = null;
		Task<string>? stderr = null;
		var failures = new List<Exception>();
		var childGone = false;
		using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(40));
		try
		{
			// Runtime startup may create temporary entries before Main; the admitted fixture stays empty.
			Directory.CreateDirectory(scratch);
			Directory.CreateDirectory(runtimeTemp);
			Directory.CreateDirectory(fixtureParent);
			var executable = OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet";
			var root = Environment.GetEnvironmentVariable("DOTNET_ROOT");
			var host = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
			if (string.IsNullOrWhiteSpace(host)) host = string.IsNullOrWhiteSpace(root) ? executable : Path.Combine(root, executable);
			var start = new ProcessStartInfo(host)
			{
				UseShellExecute = false,
				RedirectStandardOutput = true,
				RedirectStandardError = true
			};
			start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "native-semantic-probe", "WikiSemantics.NativeProbe.dll"));
			foreach (var arg in new[] { "--scenario", "cipher-cursor-error", "--world", world, "--evidence", evidence }) start.ArgumentList.Add(arg);
			foreach (var name in new[] { "TMPDIR", "TMP", "TEMP" }) start.Environment[name] = runtimeTemp;
			process = Process.Start(start) ?? throw new InvalidOperationException("Probe did not start");
			if (process.Id == Environment.ProcessId) throw new InvalidOperationException("Invalid child PID");
			stdout = ReadBounded(process.StandardOutput, timeout.Token);
			stderr = ReadBounded(process.StandardError, timeout.Token);
			await process.WaitForExitAsync(timeout.Token);
			await Task.WhenAll(stdout, stderr);
			var output = await stdout;
			var errors = await stderr;
			Console.WriteLine($"cipher-cursor-error child={process.Id} exit={process.ExitCode} stdout={output} stderr={errors}");
			if (process.ExitCode != 0) throw new InvalidOperationException($"Cipher probe exited {process.ExitCode}: {errors}");
			await Assert.That(errors).IsEqualTo(string.Empty);

			var phases = new List<string>();
			foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
			{
				using var marker = JsonDocument.Parse(line);
				var entry = marker.RootElement;
				await Assert.That(entry.GetProperty("pid").GetInt32()).IsEqualTo(process.Id);
				await Assert.That(entry.GetProperty("world").GetString()).IsEqualTo(world);
				await Assert.That(entry.GetProperty("scenario").GetString()).IsEqualTo("cipher-cursor-error");
				phases.Add(entry.GetProperty("phase").GetString() ?? throw new InvalidDataException("Missing phase"));
			}
			await Assert.That(string.Join(',', phases)).IsEqualTo(
				"cipher-seeded,cipher-control-closed,cipher-raw-failure-closed,cipher-bounded-failure-closed,cipher-healthy-reopen-closed,cipher-cursor-error-complete");
			var evidenceFile = Path.Combine(evidence, "cipher-cursor-error.json");
			await Assert.That(File.Exists(evidenceFile)).IsTrue();
			if (new FileInfo(evidenceFile).Length > 32768) throw new InvalidDataException("Probe evidence limit exceeded");
			using var document = JsonDocument.Parse(File.ReadAllText(evidenceFile));
			var proof = document.RootElement;
			await Assert.That(proof.GetProperty("pid").GetInt32()).IsEqualTo(process.Id);
			await Assert.That(proof.GetProperty("world").GetString()).IsEqualTo(world);
			await Assert.That(proof.GetProperty("scenario").GetString()).IsEqualTo("cipher-cursor-error");
			await Assert.That(proof.GetProperty("controlRows").GetInt32()).IsEqualTo(2);
			await Assert.That(proof.GetProperty("overflowPages").GetInt64() > 0).IsTrue();
			await Assert.That(proof.GetProperty("controlAdvanceDecryptions").GetInt32() > 0).IsTrue();
			await Assert.That(proof.GetProperty("rawFirstRowVerified").GetBoolean()).IsTrue();
			await Assert.That(proof.GetProperty("rawAdvanceCode").GetString()).IsEqualTo("CryptoFail");
			await Assert.That(proof.GetProperty("rawFailureCount").GetInt32()).IsEqualTo(1);
			await Assert.That(proof.GetProperty("boundedException").GetString()).IsEqualTo("LightningStoreException");
			await Assert.That(proof.GetProperty("boundedErrorCode").GetString()).IsEqualTo("CryptoFail");
			await Assert.That(proof.GetProperty("returnedBatch").GetBoolean()).IsFalse();
			await Assert.That(proof.GetProperty("boundedFailureCount").GetInt32()).IsEqualTo(1);
			await Assert.That(proof.GetProperty("callbackProbes").GetInt32()).IsEqualTo(3);
			await Assert.That(proof.GetProperty("callbackBytes").GetInt64()).IsEqualTo(2L);
			await Assert.That(proof.GetProperty("callbackWorkingBytes").GetInt64()
				> proof.GetProperty("initialRangeReservation").GetInt64()).IsTrue();
			await Assert.That(proof.GetProperty("initialRangeReservation").GetInt64()).IsEqualTo(1312L);
			await Assert.That(proof.GetProperty("workingBefore").GetInt64()).IsEqualTo(0L);
			await Assert.That(proof.GetProperty("workingAfter").GetInt64()).IsEqualTo(0L);
			await Assert.That(proof.GetProperty("probesAfter").GetInt32()).IsEqualTo(3);
			await Assert.That(proof.GetProperty("bytesAfter").GetInt64()).IsEqualTo(2L);
			await Assert.That(proof.GetProperty("healthyReopenRows").GetInt32()).IsEqualTo(2);
			await Assert.That(proof.GetProperty("healthyReopenVerified").GetBoolean()).IsTrue();
			Console.WriteLine("cipher-cursor-error proof=" + proof.GetRawText());
		}
		catch (Exception error) { failures.Add(error); }
		finally
		{
			if (process is not null)
			{
				try
				{
					// Only terminate this owned child. A concurrent normal exit may win the Kill race.
					try { if (!process.HasExited) process.Kill(); }
					catch (InvalidOperationException) when (process.HasExited) { }
					catch (Exception error) { failures.Add(error); }
					// The front deadline bounds the test; cleanup must not abandon a live native owner.
					await process.WaitForExitAsync();
					childGone = process.HasExited;
				}
				catch (Exception error) { failures.Add(error); }
				finally
				{
					try
					{
						// Observe each reader independently, including a second fault hidden by WhenAll's throw.
						foreach (var reader in new[] { stdout, stderr })
						{
							if (reader is null) continue;
							try { await reader; }
							catch (Exception error) { if (!failures.Contains(error)) failures.Add(error); }
						}
					}
					finally
					{
						try { process.Dispose(); }
						catch (Exception error) { failures.Add(error); }
					}
				}
			}
			if ((process is null || childGone) && Directory.Exists(scratch))
			{
				try { Directory.Delete(scratch, true); }
				catch (Exception error) { failures.Add(error); }
			}
		}
		if (failures.Count == 1) ExceptionDispatchInfo.Capture(failures[0]).Throw();
		if (failures.Count > 1) throw new AggregateException(failures);
	}

	private static async Task<string> ReadBounded(StreamReader reader, CancellationToken cancellationToken)
	{
		var output = new StringBuilder();
		var buffer = new char[1024];
		while (true)
		{
			var count = await reader.ReadAsync(buffer.AsMemory(), cancellationToken);
			if (count == 0) return output.ToString();
			if (output.Length + count > 32768) throw new InvalidDataException("Probe output limit exceeded");
			output.Append(buffer, 0, count);
		}
	}
}
