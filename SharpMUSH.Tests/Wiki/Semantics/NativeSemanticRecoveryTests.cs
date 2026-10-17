using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LightningDB;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Plugins.Storage;
using SharpMUSH.Library.Plugins.Storage.Lightning;
using SharpMUSH.Tools.WikiSemantics;
using SharpMUSH.Tools.WikiSemantics.Native;

namespace SharpMUSH.Tests.Wiki.Semantics;

public class NativeSemanticRecoveryTests
{
	private static string Snapshot(NativeFixtureStore store) => NativeSemanticCommitTests.Snapshot(store);
	private sealed class OwnedAbort : Exception;
	private static async Task ObserveAbort(Task task)
	{
		try { await task; throw new InvalidOperationException("Expected owned abort"); }
		catch (OwnedAbort) { return; }
	}
	private static async Task ObserveCancellation(Task task)
	{
		try { await task; throw new InvalidOperationException("Expected cancellation"); }
		catch (OperationCanceledException) { return; }
	}
	private static async Task Drain(IEnumerable<Task> tasks)
	{
		var errors = new List<Exception>();
		foreach (var task in tasks)
		{
			// Observe every submitted job before disposal, then propagate all unexpected failures.
			try { await task; } catch (OwnedAbort) { continue; } catch (OperationCanceledException) { continue; } catch (Exception error) { errors.Add(error); }
		}
		if (errors.Count != 0) throw new AggregateException(errors);
	}
	private static (Task Job, Task Started) Block(NativeSemanticFixture fixture, ManualResetEventSlim release)
	{
		var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var job = Task.Run(async () => await fixture.WriteAsync(_ =>
		{
			started.SetResult();
			if (!release.Wait(TimeSpan.FromSeconds(20))) throw new TimeoutException("Writer barrier expired");
			return true;
		}));
		return (job, started.Task);
	}
	private static async Task Ready(Task blocker, Task started, ManualResetEventSlim release)
	{
		try { await started.WaitAsync(TimeSpan.FromSeconds(20)); }
		catch
		{
			release.Set();
			await blocker; // A failed admission wait must still observe actual writer exit before disposal.
			throw;
		}
	}
	private static NativeCommitted Receipt(NativeFixtureStore store, Guid mutation)
	{
		var budget = NativeSemanticFixture.Budget(); using var lifetime = budget.ReserveWorking(0);
		return store.GetMutation(mutation, budget, lifetime).Expect<NativeMutationStatus>().Committed;
	}

	private static void Missing(NativeFixtureStore store, Guid mutation)
	{
		var budget = NativeSemanticFixture.Budget(); using var lifetime = budget.ReserveWorking(0);
		if (store.GetMutation(mutation, budget, lifetime) is not NotFound) throw new InvalidOperationException("Aborted mutation left receipt");
	}
	[Test]
	public async Task ComposedApplyAbortsEveryTableAndReceiptAfterReopen()
	{
		using var fixture = new NativeSemanticFixture(); var store = new NativeFixtureStore(fixture, NativeSemanticFixture.Schema);
		await store.CommitAsync(NativeSemanticFixture.Prepare(value: new PageRef("old-target")));
		var before = Snapshot(store); var edit = NativeSemanticFixture.Prepare(version: 1, value: new Keyword("new-keyword"));
		await ObserveAbort(fixture.WriteAsync<bool>(tx => { if (store.Apply(tx, edit, default) is not NativeCommitted) throw new InvalidOperationException("Apply rejected before abort"); throw new OwnedAbort(); }).AsTask());
		fixture.Reopen(); store = new(fixture, NativeSemanticFixture.Schema);
		await Assert.That(Snapshot(store)).IsEqualTo(before); Missing(store, edit.MutationId);
	}
	[Test]
	public async Task CancellationPhasesHaveActualDurableOutcomes()
	{
		using var fixture = new NativeSemanticFixture(); var store = new NativeFixtureStore(fixture, NativeSemanticFixture.Schema);
		await store.CommitAsync(NativeSemanticFixture.Prepare(value: 1L)); var before = Snapshot(store);
		using var canceled = new CancellationTokenSource(); canceled.Cancel();
		var pre = NativeSemanticFixture.Prepare(version: 1, value: 2L);
		await ObserveCancellation(store.CommitAsync(pre, canceled.Token).AsTask()); Missing(store, pre.MutationId);
		using var release = new ManualResetEventSlim(false); var (blocker, started) = Block(fixture, release);
		await Ready(blocker, started, release); using var queuedToken = new CancellationTokenSource();
		var queuedEdit = NativeSemanticFixture.Prepare(version: 1, value: 3L);
		var queued = store.CommitAsync(queuedEdit, queuedToken.Token).AsTask();
		try { queuedToken.Cancel(); release.Set(); await blocker; await ObserveCancellation(queued); }
		finally { release.Set(); await Drain([blocker, queued]); }
		Missing(store, queuedEdit.MutationId);
		using var cooperative = new CancellationTokenSource(); var abort = NativeSemanticFixture.Prepare(version: 1, value: new PageRef("aborted-target"));
		await ObserveCancellation(fixture.WriteAsync(tx =>
		{
			var result = store.Apply(tx, abort, cooperative.Token); if (result is not NativeCommitted) throw new InvalidOperationException("Apply rejected before cancellation"); cooperative.Cancel(); cooperative.Token.ThrowIfCancellationRequested(); return result;
		}).AsTask());
		fixture.Reopen(); store = new(fixture, NativeSemanticFixture.Schema);
		await Assert.That(Snapshot(store)).IsEqualTo(before); Missing(store, abort.MutationId);
		// The provider does not turn late token cancellation into an abort without a cooperative throw.
		using var late = new CancellationTokenSource(); var committedEdit = NativeSemanticFixture.Prepare(version: 1, value: 4L);
		var result = await fixture.WriteAsync(tx => { var applied = store.Apply(tx, committedEdit, late.Token); late.Cancel(); return applied; }, late.Token);
		await Assert.That(result is NativeCommitted { StateVersion: 2 }).IsTrue();
		var committed = Snapshot(store); fixture.Reopen(); store = new(fixture, NativeSemanticFixture.Schema);
		await Assert.That(Snapshot(store)).IsEqualTo(committed);
		await Assert.That(Receipt(store, committedEdit.MutationId)).IsEqualTo(result.Expect<NativeCommitted>());
		using var after = new CancellationTokenSource(); var finalEdit = NativeSemanticFixture.Prepare(version: 2, value: 5L);
		var finalResult = (await store.CommitAsync(finalEdit, after.Token)).Expect<NativeCommitted>(); after.Cancel();
		var final = Snapshot(store); fixture.Reopen(); store = new(fixture, NativeSemanticFixture.Schema);
		await Assert.That(Snapshot(store)).IsEqualTo(final);
		await Assert.That(Receipt(store, finalEdit.MutationId)).IsEqualTo(finalResult);
	}
	[Test]
	public async Task GroupChildRollbackIsIsolatedAndSuccessWaitsForParentCommit()
	{
		using var fixture = new NativeSemanticFixture(); var store = new NativeFixtureStore(fixture, NativeSemanticFixture.Schema);
		var seed = NativeSemanticFixture.Prepare(value: 1L); await store.CommitAsync(seed);
		using var release = new ManualResetEventSlim(false); using var parentRelease = new ManualResetEventSlim(false);
		var (blocker, started) = Block(fixture, release); await Ready(blocker, started, release);
		var parentHeld = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var edit = NativeSemanticFixture.Prepare(version: 1, value: 2L); var doomed = NativeSemanticFixture.Prepare(NativeSemanticFixture.Page("doomed"), value: new PageRef("ghost"));
		var success = store.CommitAsync(edit).AsTask();
		var failure = fixture.WriteAsync<bool>(tx => { if (store.Apply(tx, doomed, default) is not NativeCommitted) throw new InvalidOperationException("Apply rejected before child abort"); throw new OwnedAbort(); }).AsTask();
		var hold = fixture.WriteAsync(_ => { parentHeld.SetResult(); if (!parentRelease.Wait(TimeSpan.FromSeconds(20))) throw new TimeoutException(); return true; }).AsTask();
		try
		{
			release.Set(); await blocker; await parentHeld.Task.WaitAsync(TimeSpan.FromSeconds(20));
			await Assert.That(success.IsCompleted).IsFalse();
			parentRelease.Set(); await hold; await ObserveAbort(failure);
			await Assert.That(await success is NativeCommitted { StateVersion: 2 }).IsTrue();
		}
		finally { release.Set(); parentRelease.Set(); await Drain([blocker, success, failure, hold]); }
		var before = Snapshot(store); fixture.Reopen(); store = new(fixture, NativeSemanticFixture.Schema);
		await Assert.That(Snapshot(store)).IsEqualTo(before); Missing(store, doomed.MutationId);
		using var reference = new NativeSemanticFixture(); var expected = new NativeFixtureStore(reference, NativeSemanticFixture.Schema);
		await expected.CommitAsync(seed); await expected.CommitAsync(edit);
		await Assert.That(Snapshot(store)).IsEqualTo(Snapshot(expected));
	}
	[Test]
	public async Task LostSuccessfulObservationResolvesReceiptAndRetryWithoutNewWrites()
	{
		using var fixture = new NativeSemanticFixture(); var store = new NativeFixtureStore(fixture, NativeSemanticFixture.Schema);
		var edit = NativeSemanticFixture.Prepare(value: new PageRef("target"));
		await store.CommitAsync(edit); // Harness intentionally drops the successful response only after actual completion.
		var before = Snapshot(store); fixture.Reopen(); store = new(fixture, NativeSemanticFixture.Schema);
		var budget = NativeSemanticFixture.Budget(); using var lifetime = budget.ReserveWorking(0);
		var receipt = store.GetMutation(edit.MutationId, budget, lifetime).Expect<NativeMutationStatus>();
		await Assert.That((await store.CommitAsync(edit)).Expect<NativeCommitted>()).IsEqualTo(receipt.Committed);
		await Assert.That(await store.CommitAsync(NativeSemanticFixture.Prepare(value: 9L, mutation: edit.MutationId)) is NativeMutationMismatch).IsTrue();
		await Assert.That(Snapshot(store)).IsEqualTo(before);
	}
	[Test]
	public async Task ActualAggregateMapFullPreservesPriorCompleteState()
	{
		using var fixture = new NativeSemanticFixture(4L << 20); var store = new NativeFixtureStore(fixture, NativeSemanticFixture.Schema);
		await store.CommitAsync(NativeSemanticFixture.Prepare(value: 1L));
		var filler = fixture.OpenTable("prototype.semantic.recovery.filler", false); var payload = new byte[16 * 1024];
		var filled = false;
		for (var i = 0; i < 1024; i++)
		{
			try { await fixture.WriteAsync(tx => { tx.Put(filler, BitConverter.GetBytes(i), payload); return true; }); }
			catch (LightningStoreException error) when (error.Code == MDBResultCode.MapFull) { filled = true; break; }
		}
		if (!filled) throw new InvalidOperationException("Bounded filler did not reach actual MapFull");
		var before = Snapshot(store);
		var edit = NativeSemanticFixture.Prepare(NativeSemanticFixture.Page() with { Body = new string('b', 100000) }, 1, new Keyword("growth"));
		using var release = new ManualResetEventSlim(false); var (blocker, started) = Block(fixture, release);
		await Ready(blocker, started, release);
		var aggregate = store.CommitAsync(edit).AsTask();
		var sibling = fixture.WriteAsync(_ => 713).AsTask();
		var aggregateFailed = false;
		try
		{
			release.Set(); await blocker;
			try { await aggregate; }
			catch (LightningStoreException error) when (error.Code == MDBResultCode.MapFull) { aggregateFailed = true; Console.WriteLine("aggregate child MapFull: " + error.Message); }
			// A read-only sibling needs no spare map pages: its successful completion establishes parent completion.
			await Assert.That(await sibling).IsEqualTo(713);
			await Assert.That(aggregateFailed).IsTrue();
		}
		finally
		{
			release.Set();
			var errors = new List<Exception>();
			foreach (var job in new Task[] { blocker, aggregate, sibling })
			{
				try { await job; } catch (LightningStoreException error) when (error.Code == MDBResultCode.MapFull) { continue; } catch (Exception error) { errors.Add(error); }
			}
			if (errors.Count != 0) throw new AggregateException(errors);
		}
		fixture.Reopen(); store = new(fixture, NativeSemanticFixture.Schema);
		await Assert.That(Snapshot(store)).IsEqualTo(before); Missing(store, edit.MutationId);
	}
	private static string Scratch()
	{
		var root = Environment.GetEnvironmentVariable("TMPDIR") ?? Path.Combine(Environment.CurrentDirectory, "work/semantic-wiki-prototype/b1/tmp");
		var path = Path.Combine(root, "semantic-recovery-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(path); return path;
	}
	private static async Task RunProbe(string scenario, string world, string evidence, string phase, Guid? fixtureId = null)
	{
		var executable = OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet";
		var root = Environment.GetEnvironmentVariable("DOTNET_ROOT");
		var host = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
		if (string.IsNullOrWhiteSpace(host)) host = string.IsNullOrWhiteSpace(root) ? executable : Path.Combine(root, executable);
		var start = new ProcessStartInfo(host) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
		start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "native-semantic-probe", "WikiSemantics.NativeProbe.dll"));
		foreach (var arg in new[] { "--scenario", scenario, "--world", world, "--evidence", evidence }) start.ArgumentList.Add(arg);
		if (fixtureId is Guid id) { start.ArgumentList.Add("--fixture-id"); start.ArgumentList.Add(id.ToString()); }
		foreach (var key in new[] { "TMPDIR", "TMP", "TEMP" }) start.Environment[key] = Path.GetDirectoryName(world)!;
		using var process = Process.Start(start) ?? throw new InvalidOperationException("Probe did not start");
		if (process.Id == Environment.ProcessId) throw new InvalidOperationException("Invalid child PID");
		using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(40));
		var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var output = new StringBuilder(); var errors = new StringBuilder();
		var sawSeed = false;
		async Task Pump(StreamReader reader, StringBuilder target, bool stdout)
		{
			var buffer = new char[1024]; var pending = new StringBuilder(); var total = 0;
			while (true)
			{
				var count = await reader.ReadAsync(buffer.AsMemory(), timeout.Token); if (count == 0) break;
				total += count; if (total > 32768) throw new InvalidDataException("Probe output limit exceeded");
				target.Append(buffer, 0, count);
				if (!stdout) continue;
				pending.Append(buffer, 0, count);
				while (pending.ToString().IndexOf('\n') is var end && end >= 0)
				{
					var line = pending.ToString(0, end); pending.Remove(0, end + 1);
					using var record = JsonDocument.Parse(line); var entry = record.RootElement;
					if (entry.GetProperty("pid").GetInt32() != process.Id || entry.GetProperty("world").GetString() != world || entry.GetProperty("scenario").GetString() != scenario)
						throw new InvalidDataException("Probe ownership marker mismatch");
					var observed = entry.GetProperty("phase").GetString();
					if (observed == "seeded") sawSeed = true;
					if (observed == phase)
					{
						if (scenario != "persisted-flags" && !sawSeed) throw new InvalidDataException("Checkpoint preceded seed");
						reached.TrySetResult();
					}
				}
			}
			if (stdout && !reached.Task.IsCompleted) throw new InvalidDataException("EOF before required checkpoint");
		}
		var stdout = Pump(process.StandardOutput, output, true); var stderr = Pump(process.StandardError, errors, false);
		try
		{
			await AwaitCheckpoint(reached.Task, stdout, stderr, timeout.Token);
			if (scenario != "persisted-flags") process.Kill(); // Only this owned Process handle, never a PID lookup or broad process match.
			await process.WaitForExitAsync(timeout.Token); await Task.WhenAll(stdout, stderr);
			if (scenario == "persisted-flags" && process.ExitCode != 0) throw new InvalidOperationException("Flag probe failed: " + errors);
			File.WriteAllText(Path.Combine(evidence, "stdout.jsonl"), output.ToString());
			File.WriteAllText(Path.Combine(evidence, "stderr.txt"), errors.ToString());
			Console.WriteLine($"{scenario} child={process.Id} parent={Environment.ProcessId} exit={process.ExitCode} phase={phase} fullsync map={256L << 20}");
		}
		finally
		{
			if (!process.HasExited) process.Kill();
			using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
			await process.WaitForExitAsync(cleanup.Token);
			// Kill closes the streams; observe both pumps even if the primary assertion failed.
			try { await Task.WhenAll(stdout, stderr); } catch (Exception) when (stdout.IsCompleted && stderr.IsCompleted) { }
		}
	}
	private static async Task AwaitCheckpoint(Task reached, Task stdout, Task stderr, CancellationToken timeout)
	{
		using var deadlineCancellation = CancellationTokenSource.CreateLinkedTokenSource(timeout);
		var deadline = Task.Delay(Timeout.Infinite, deadlineCancellation.Token);
		try
		{
			var stderrDrained = false;
			while (true)
			{
				var first = stderrDrained
					? await Task.WhenAny(reached, stdout, deadline)
					: await Task.WhenAny(reached, stdout, stderr, deadline);
				if (first == reached) { await reached; return; }
				await first; // Propagate pump faults/cancellation and the deadline.
				if (first == stderr) { stderrDrained = true; continue; }
				throw new InvalidOperationException("Stdout ended before required checkpoint");
			}
		}
		finally
		{
			deadlineCancellation.Cancel();
			try { await deadline; } catch (OperationCanceledException) { }
		}
	}

	[Test]
	public async Task NormalStderrEofWaitsForDelayedStdoutCheckpoint()
	{
		var checkpoint = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var stdout = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
		var coordination = AwaitCheckpoint(checkpoint.Task, stdout.Task, Task.CompletedTask, timeout.Token);
		try
		{
			// stderr EOF is already observed; stdout parsing and its valid checkpoint are deliberately pending.
			await Assert.That(coordination.IsCompleted).IsFalse();
			checkpoint.SetResult();
			await coordination;
		}
		finally
		{
			checkpoint.TrySetResult(); stdout.TrySetResult();
			try { await coordination; } catch (InvalidOperationException) { }
			await Task.WhenAll(checkpoint.Task, stdout.Task);
		}
	}

	private sealed class RecoveryStorage(string path) : ILightningStorageAccessor, IDisposable
	{
		private readonly LightningStore _store = new(new LightningStoreOptions { Path = path, MapSize = 256L << 20, Sync = LightningSyncMode.Full });
		public T Read<T>(Func<ITx, T> read) => _store.Read(read);
		public ValueTask<T> WriteAsync<T>(Func<ITx, T> job, CancellationToken ct = default) => _store.WriteAsync(job, ct);
		public TableDef OpenTable(string name, bool duplicates) => _store.OpenTable(name, duplicates);
		public IAsyncEnumerable<(byte[] Key, byte[] Value)> RangeAsync(TableDef table, byte[] prefix, int pageSize = 256, CancellationToken ct = default) => _store.RangeAsync(table, prefix, pageSize, ct);
		public ValueTask CopyToAsync(string path, bool compact = true, CancellationToken ct = default) { ct.ThrowIfCancellationRequested(); _store.CopyTo(path, compact); return ValueTask.CompletedTask; }
		public void Dispose() => _store.Dispose();
	}
	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task OwnedProcessKillRespectsActualCommitBoundary(bool afterCommit)
	{
		var scratch = Scratch(); var world = Path.GetFullPath(Path.Combine(scratch, "world")); var evidence = Path.Combine(scratch, "evidence");
		try
		{
			await RunProbe(afterCommit ? "crash-after-commit" : "crash-before-commit", world, evidence, afterCommit ? "committed" : "applied-uncommitted");
			using var storage = new RecoveryStorage(world); var store = new NativeFixtureStore(storage, NativeSemanticFixture.Schema);
			var expected = File.ReadAllText(Path.Combine(evidence, afterCommit ? "after.txt" : "before.txt"));
			var reopened = Snapshot(store);
			await Assert.That(reopened).IsEqualTo(expected);
			Console.WriteLine($"crash checkpoint sha256={Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(expected)))} reopened={Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(reopened)))} bytes={Encoding.UTF8.GetByteCount(reopened)}");
			var mutation = Guid.Parse(File.ReadAllText(Path.Combine(evidence, "mutation.txt")));
			if (!afterCommit) Missing(store, mutation);
			else
			{
				var budget = NativeSemanticFixture.Budget(); using var lifetime = budget.ReserveWorking(0);
				await Assert.That(store.GetMutation(mutation, budget, lifetime).Expect<NativeMutationStatus>().Committed.CommitSequence).IsEqualTo(2L);
			}
		}
		finally { Directory.Delete(scratch, true); }
	}
	[Test]
	public async Task FreshProcessRejectsPersistedDuplicateFlagsBeforeProbing()
	{
		var scratch = Scratch(); var world = Path.GetFullPath(Path.Combine(scratch, "world")); var evidence = Path.Combine(scratch, "evidence"); Directory.CreateDirectory(evidence);
		try
		{
			var fixtureId = Guid.NewGuid();
			using (var storage = new RecoveryStorage(world))
			{
				var table = storage.OpenTable("prototype.semantic.recovery.duplicates", true);
				await storage.WriteAsync(tx => { tx.Put(table, [1], [1]); tx.Put(table, [1], [2]); return true; });
			}
			File.WriteAllText(Path.Combine(evidence, "fixture.json"), JsonSerializer.Serialize(new { world, fixtureId, parentPid = Environment.ProcessId }));
			await RunProbe("persisted-flags", world, evidence, "persisted-flags-rejected", fixtureId);
		}
		finally { Directory.Delete(scratch, true); }
	}
}
