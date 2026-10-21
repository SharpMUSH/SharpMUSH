using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Database.Lightning.Migration;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Plugins.Storage.Lightning;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Database.Lightning;

public class WikiCategoryNativeCommitFailureTests
{
	private const string Category = "play\0rules";
	private const string Timestamp = "2023-11-14T22:13:20.0000000+00:00";
	private const string Phases = "category-seeded,category-commit-failed,category-limits-restored,category-reopened,category-retry-complete,category-commit-failure-complete";

	[Test]
	[Arguments(false, false)]
	[Arguments(true, false)]
	[Arguments(false, true)]
	[Arguments(true, true)]
	public async Task NativeCommitFailureRestoresLimitsAndRecoversActualCategoryCoordinator(bool resumed, bool grouped)
	{
		if (!OperatingSystem.IsLinux() || RuntimeInformation.OSArchitecture != Architecture.X64
			|| RuntimeInformation.ProcessArchitecture != Architecture.X64 || IntPtr.Size != 8 || !BitConverter.IsLittleEndian)
		{
			Skip.Test("Native COMMIT EFBIG proof requires the reviewed Linux-x64 libc ABI and LightningDB native binary; unavailable on this platform");
			return;
		}
		var configured = Environment.GetEnvironmentVariable("TMPDIR");
		var scratchRoot = Path.GetFullPath(string.IsNullOrWhiteSpace(configured)
			? Path.Join(Environment.CurrentDirectory, "work/category-native-commit") : configured);
		if (scratchRoot == "/tmp" || scratchRoot.StartsWith("/tmp/", StringComparison.Ordinal)
			|| scratchRoot == "/var/tmp" || scratchRoot.StartsWith("/var/tmp/", StringComparison.Ordinal))
			throw new InvalidOperationException("Native category probe requires disk-backed scratch");
		RejectLinkedAncestors(scratchRoot);
		var scratch = Path.Join(scratchRoot, "category-commit-run-" + Guid.NewGuid().ToString("N"));
		RequireAbsent(scratch);
		var fixtureParent = Path.Join(scratch, "fixture");
		var runtimeTemp = Path.Join(scratch, "runtime-temp");
		var world = Path.Join(fixtureParent, "world");
		var evidence = Path.Join(fixtureParent, "evidence");
		var scenario = (grouped ? "category-group-commit-failure-" : "category-commit-failure-") + (resumed ? "resume" : "initial");
		Process? process = null;
		Task<string>? stdout = null;
		Task<string>? stderr = null;
		var failures = new List<Exception>();
		var childGone = false;
		var pumpsFinished = true;
		using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
		try
		{
			Directory.CreateDirectory(runtimeTemp);
			Directory.CreateDirectory(fixtureParent);
			RequireAbsent(world);
			RequireAbsent(evidence);
			RejectLinkedAncestors(fixtureParent);
			var host = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
			var root = Environment.GetEnvironmentVariable("DOTNET_ROOT");
			if (string.IsNullOrWhiteSpace(host)) host = string.IsNullOrWhiteSpace(root) ? "dotnet" : Path.Join(root, "dotnet");
			var start = new ProcessStartInfo(host)
			{
				UseShellExecute = false,
				RedirectStandardOutput = true,
				RedirectStandardError = true
			};
			start.ArgumentList.Add(Path.Join(AppContext.BaseDirectory, "native-semantic-probe", "WikiSemantics.NativeProbe.dll"));
			foreach (var arg in new[] { "--scenario", scenario, "--world", world, "--evidence", evidence }) start.ArgumentList.Add(arg);
			foreach (var name in new[] { "TMPDIR", "TMP", "TEMP" }) start.Environment[name] = runtimeTemp;
			process = Process.Start(start) ?? throw new InvalidOperationException("Category probe did not start");
			if (process.Id == Environment.ProcessId) throw new InvalidOperationException("Invalid child PID");
			stdout = ReadBounded(process.StandardOutput, timeout.Token);
			stderr = ReadBounded(process.StandardError, timeout.Token);
			await process.WaitForExitAsync(timeout.Token);
			await Task.WhenAll(stdout, stderr);
			var output = await stdout;
			var errors = await stderr;
			Console.WriteLine($"{scenario} child={process.Id} exit={process.ExitCode} stdout={output} stderr={errors}");
			Check(process.ExitCode == 0, $"Category probe exited {process.ExitCode}: {errors}");
			Check(errors.Length == 0, "Category probe wrote stderr");
			var phases = new List<string>();
			foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
			{
				using var marker = JsonDocument.Parse(line);
				Identity(marker.RootElement, process.Id, world, scenario);
				phases.Add(marker.RootElement.GetProperty("phase").GetString() ?? throw new InvalidDataException("Missing phase"));
			}
			Check(string.Join(',', phases) == Phases, "Incomplete or out-of-order category proof phases");
			RejectLinkedAncestors(world);
			RejectLinkedAncestors(evidence);
			var evidenceFile = Path.Join(evidence, "category-commit-failure.json");
			RejectLinkedAncestors(evidenceFile);
			Check(File.Exists(evidenceFile), "Missing category COMMIT evidence");
			Check(new FileInfo(evidenceFile).Length <= 32768, "Category COMMIT evidence limit exceeded");
			using var document = ReadEvidence(evidenceFile);
			var proof = document.RootElement;
			Identity(proof, process.Id, world, scenario);
			Check(proof.GetProperty("schema").GetInt32() == 1, "Unexpected evidence schema");
			Check(proof.GetProperty("resumed").GetBoolean() == resumed, "Wrong resume path");
			if (grouped) VerifyGroupedProof(proof);
			else if (proof.TryGetProperty("grouped", out var groupMode)) Check(!groupMode.GetBoolean(), "Single proof reported grouped execution");
			var pageSize = proof.GetProperty("pageSize").GetUInt64();
			Check(pageSize > 0 && proof.GetProperty("armedSoft").GetUInt64() == checked(2UL * pageSize), "Limit was not the actual two metadata-page boundary");
			Check(proof.GetProperty("softAfter").GetUInt64() == proof.GetProperty("softBefore").GetUInt64(), "Soft limit not restored");
			Check(proof.GetProperty("hardAfter").GetUInt64() == proof.GetProperty("hardBefore").GetUInt64(), "Hard limit changed");
			Check(proof.GetProperty("signalRestored").GetBoolean(), "Signal disposition not restored");
			Check(Hash(proof, "signalStateBeforeHash") == Hash(proof, "signalStateAfterHash"), "Full kernel signal action was not restored");
			Check(proof.GetProperty("nativeCode").GetInt32() == 27, "Expected native EFBIG");
			Check(proof.GetProperty("operation").GetString() == "commit", "Failure did not occur at COMMIT");
			Check(proof.GetProperty("commitStack").GetString()?.Contains("LightningStore.Commit", StringComparison.Ordinal) == true, "Missing actual Store.Commit stack");
			Check(proof.GetProperty("commitsBefore").GetInt64() == proof.GetProperty("commitsAfterFailure").GetInt64(), "Failed COMMIT advanced commit count");
			var before = Hash(proof, "beforeHash");
			Check(Hash(proof, "afterFailureHash") == before && Hash(proof, "afterReopenHash") == before, "Aborted migration changed logical rows");
			var source = Hash(proof, "sourceBeforeHash");
			Check(Hash(proof, "sourceAfterRetryHash") == source, "Retry changed source bytes");
			Check(proof.GetProperty("retryCommits").GetInt64() > 0, "Genuine retry made no commits");
			await VerifyRecoveredWorld(world, source);
			Console.WriteLine(scenario + " proof=" + proof.GetRawText());
		}
		catch (Exception error) { failures.Add(error); }
		finally
		{
			// Cleanup has its own deadline; the front deadline may already have expired.
			using var cleanupDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
			try { timeout.Cancel(); }
			catch (Exception error) { failures.Add(error); }
			if (process is not null)
			{
				var ownedPid = process.Id;
				try
				{
					try { if (!process.HasExited) process.Kill(); }
					catch (InvalidOperationException) when (process.HasExited) { }
					catch (Exception error) { failures.Add(error); }
					await process.WaitForExitAsync(cleanupDeadline.Token);
					childGone = process.HasExited;
				}
				catch (Exception error)
				{
					failures.Add(new InvalidOperationException($"Could not confirm exit of owned probe PID {ownedPid}; retaining scratch {scratch} and world {world}", error));
				}
				finally
				{
					foreach (var reader in new[] { stdout, stderr })
					{
						if (reader is null) continue;
						try { await reader.WaitAsync(cleanupDeadline.Token); }
						catch (Exception error)
						{
							if (!failures.Contains(error)) failures.Add(error);
							if (reader.IsFaulted && reader.Exception is { } pipeErrors)
								foreach (var pipeError in pipeErrors.Flatten().InnerExceptions)
									if (!failures.Contains(pipeError)) failures.Add(pipeError);
							// Also covers a reader completing concurrently with WaitAsync cancellation.
							_ = reader.ContinueWith(completed => { _ = completed.Exception; }, CancellationToken.None,
								TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
							if (!reader.IsCompleted)
							{
								pumpsFinished = false;
								failures.Add(new InvalidOperationException($"Owned probe PID {ownedPid} pipe did not finish within cleanup deadline; retaining scratch {scratch}, world {world}"));
							}
						}
					}
					try { process.Dispose(); }
					catch (Exception error) { failures.Add(error); }
				}
			}
			if ((process is null || childGone) && pumpsFinished && Directory.Exists(scratch))
			{
				try { RejectLinkedAncestors(scratch); Directory.Delete(scratch, true); }
				catch (Exception error) { failures.Add(error); }
			}
		}
		if (failures.Count == 1) ExceptionDispatchInfo.Capture(failures[0]).Throw();
		if (failures.Count > 1) throw new AggregateException(failures);
	}

	private static void VerifyGroupedProof(JsonElement proof)
	{
		Check(proof.GetProperty("grouped").GetBoolean(), "Group proof did not report grouped execution");
		Check(proof.GetProperty("cohortSize").GetInt32() == 3 && proof.GetProperty("maxBatchJobs").GetInt32() == 3,
			"Group proof did not use the exact three-job cohort");
		Check(proof.GetProperty("allQueuedPending").GetBoolean(), "All original producers were not pending while paused");
		Check(proof.GetProperty("sameCommitException").GetBoolean(), "Producer failures were not the same actual parent exception");
		Check(proof.GetProperty("neighborACallbacks").GetInt32() == 1 && proof.GetProperty("neighborBCallbacks").GetInt32() == 1,
			"Successful neighbor callbacks did not each return exactly once");
		Check(proof.GetProperty("neighborASawCandidate").GetBoolean() && proof.GetProperty("neighborBSawCandidate").GetBoolean()
			&& proof.GetProperty("neighborBSawA").GetBoolean(), "Missing actual uncommitted coordinator/A/B visibility chain");
		var stack = proof.GetProperty("commitStack").GetString() ?? throw new InvalidDataException("Missing group commit stack");
		Check(stack.Contains("LightningStore.WriteBatch", StringComparison.Ordinal)
			&& stack.Contains("LightningWriter.RunBatch", StringComparison.Ordinal), "Missing actual grouped native parent COMMIT stack");
		var producers = proof.GetProperty("producers");
		Check(producers.ValueKind == JsonValueKind.Array && producers.GetArrayLength() == 3, "Wrong observed group producer count");
		var roles = new[] { "category", "neighbor-a", "neighbor-b" };
		for (var index = 0; index < roles.Length; index++)
		{
			var producer = producers[index];
			UniqueProperties(producer);
			Check(producer.EnumerateObject().Count() == 4, "Unexpected group producer proof fields");
			Check(producer.GetProperty("role").GetString() == roles[index] && producer.GetProperty("faulted").GetBoolean()
				&& producer.GetProperty("nativeCode").GetInt32() == 27 && producer.GetProperty("operation").GetString() == "commit",
				"Original producer did not report the selected parent native COMMIT failure: " + roles[index]);
		}
	}

	private static async Task VerifyRecoveredWorld(string world, string expectedSourceHash)
	{
		Check(Directory.Exists(world) && File.Exists(Path.Join(world, "data.mdb")), "Child-closed recovered world is missing");
		RejectLinkedAncestors(Path.Join(world, "data.mdb"));
		await using var db = new LightningDatabase(NullLogger<LightningDatabase>.Instance,
			new LightningStoreOptions { Path = world, MapSize = 256L << 20, Sync = LightningSyncMode.Full },
			Substitute.For<IPasswordService>(), relations: null);
		var source = db.Store.Read(tx =>
		{
			VerifyGroupNeighborsAbsent(tx);
			Check(tx.Count(Tables.WikiPage) == 1, "Recovered world has unexpected source rows");
			if (!tx.TryGet(Tables.WikiPage, new byte[8], out var bytes)) throw new InvalidDataException("Missing id-zero source");
			return bytes.ToArray();
		});
		Check(Convert.ToHexString(SHA256.HashData(source)).Equals(expectedSourceHash, StringComparison.OrdinalIgnoreCase), "Parent source hash disagrees with child evidence");
		var authority = db.Store.Read(tx =>
		{
			if (!tx.TryGet(Tables.Meta, "core.wiki.cat.value-upgrade/state"u8, out var state)
				|| !tx.TryGet(Tables.Meta, "mig:0004_wiki_category_value_v1"u8, out var completion))
				throw new InvalidDataException("Missing durable Complete authority pair");
			Check(WikiCategoryAuthorityCodec.Validate(true, state, true, completion, db.Store.MaxKeySize), "Invalid durable Complete authority pair");
			var checkpoint = WikiCategoryCheckpointCodec.Decode(state, db.Store.MaxKeySize);
			Check(checkpoint.Phase == WikiCategoryMigrationPhase.Complete && checkpoint.CompletedPages == 1
				&& checkpoint.CategoryPuts == 1 && checkpoint.ClearedRows == 1 && checkpoint.CompletionUnixMs > 0,
				"Recovered authority does not describe the exact completed fixture");
			return (State: state.ToArray(), Marker: completion.ToArray());
		});
		// The expected wire literals are independent of the child and category codec under test.
		// Label includes its embedded NUL; the final ten zeros are title separator plus page id zero.
		var expectedIndexKey = "PLAY\0RULES\0"u8.ToArray().Concat(Encoding.BigEndianUnicode.GetBytes("Guide")).Concat(new byte[10]).ToArray();
		byte[] expectedIndexValue = [1, 35, 52, 50, 0, 0, 0, 10, 255, 67, 0, 1];
		db.Store.Read(tx =>
		{
			Check(tx.Count(Tables.WikiByCategory) == 1, "Recovered index contains unexpected rows before parent admission");
			var row = tx.Range(Tables.WikiByCategory, []).Single();
			Check(row.Key.AsSpan().SequenceEqual(expectedIndexKey) && row.Value.AsSpan().SequenceEqual(expectedIndexValue),
				"Recovered raw category row differs before parent admission");
			return true;
		});
		var beforeAdmission = RecoveredRows(db);
		var commits = db.Store.CommitCount;
		await db.MigrateWikiCategoryValuesAsync(WikiCategoryMigrationOptions.Parse(_ => null));
		Check(db.Store.CommitCount == commits, "Reopened Complete coordinator rewrote the world");
		Check(beforeAdmission.SequenceEqual(RecoveredRows(db)), "Parent Complete admission changed raw source/category/authority snapshot");
		Check(db.Store.Read(tx => tx.TryGet(Tables.Meta, "core.wiki.cat.value-upgrade/state"u8, out var state)
			&& state.SequenceEqual(authority.State) && tx.TryGet(Tables.Meta, "mig:0004_wiki_category_value_v1"u8, out var marker)
			&& marker.SequenceEqual(authority.Marker)), "Complete authority changed on parent admission");
		var pages = await db.GetPagesByCategoryAsync(Category, 0, 10, WikiVisibility.All);
		Check(pages.Count == 1, "Exact category did not return exactly one page");
		var page = pages[0];
		Check(page.Id == "wiki_page/0" && page.Title == "Guide" && page.AuthorDbref == "#42" && page.Published
			&& page.Slug == "guide" && page.Namespace == "main" && page.MarkdownSource == "body"
			&& page.RenderedHtml == "<p>body</p>" && page.PlainText == "body" && page.LastEditorDbref == "#42"
			&& page.CreatedAt == DateTimeOffset.Parse(Timestamp, System.Globalization.CultureInfo.InvariantCulture)
			&& page.UpdatedAt == DateTimeOffset.Parse(Timestamp, System.Globalization.CultureInfo.InvariantCulture)
			&& page.RevisionNumber == 1 && page.SourceLocale == "en"
			&& page.Categories.SequenceEqual(new[] { Category }), "Recovered category page metadata differs");
		Check(await db.CountPagesInCategoryAsync(Category, WikiVisibility.All) == 1, "Exact category count differs");
		Check((await db.GetPagesByCategoryAsync("play", 0, 10, WikiVisibility.All)).Count == 0, "NUL category leaked a play alias");
		Check(await db.CountPagesInCategoryAsync("play", WikiVisibility.All) == 0, "NUL category leaked an alias count");
		var counts = await db.CountPagesByCategoryAsync(WikiVisibility.All);
		Check(counts.Count == 1 && counts.TryGetValue(Category, out var count) && count == 1 && !counts.ContainsKey("play"), "Category dictionary differs from exact membership");
		Check(db.Store.Read(tx => tx.Count(Tables.WikiByCategory)) == 1, "Recovered index contains unexpected rows");
		Check(db.Store.CommitCount == commits, "Parent verification wrote to recovered world");
		Check(beforeAdmission.SequenceEqual(RecoveredRows(db)), "Parent verification changed raw source/category/authority snapshot");
	}

	private static void VerifyGroupNeighborsAbsent(ITx tx)
	{
		Check(!tx.TryGet(Tables.Meta, "test.category.failed-group/A"u8, out _)
			&& !tx.TryGet(Tables.Meta, "test.category.failed-group/B"u8, out _),
			"Failed group neighbor writes survived in the recovered world");
	}

	private static string[] RecoveredRows(LightningDatabase db) => db.Store.Read(tx =>
	{
		var rows = new List<string>();
		foreach (var table in new[] { Tables.WikiPage, Tables.WikiByCategory })
			foreach (var row in tx.Range(table, []))
				rows.Add(table.Name + ":" + Convert.ToHexString(row.Key) + ":" + Convert.ToHexString(row.Value));
		foreach (var key in new[] { "core.wiki.cat.value-upgrade/state", "mig:0004_wiki_category_value_v1" })
		{
			if (!tx.TryGet(Tables.Meta, Encoding.UTF8.GetBytes(key), out var value)) throw new InvalidDataException("Missing authority snapshot row");
			rows.Add(key + ":" + Convert.ToHexString(value));
		}
		VerifyGroupNeighborsAbsent(tx);
		rows.Add("test.category.failed-group/A:absent");
		rows.Add("test.category.failed-group/B:absent");
		return rows.ToArray();
	});

	private static JsonDocument ReadEvidence(string path)
	{
		RejectLinkedAncestors(path);
		using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
		RejectLinkedAncestors(path);
		Check(stream.Length <= 32768, "Category evidence file exceeds cap");
		// One overflow byte detects growth after the length check before JSON allocates anything.
		var bytes = new byte[32769];
		var total = 0;
		while (total < bytes.Length)
		{
			var count = stream.Read(bytes, total, bytes.Length - total);
			if (count == 0) break;
			total += count;
		}
		Check(total <= 32768, "Category evidence grew beyond cap");
		return JsonDocument.Parse(bytes.AsMemory(0, total));
	}

	private static void Identity(JsonElement entry, int pid, string world, string scenario)
	{
		UniqueProperties(entry);
		Check(entry.GetProperty("pid").GetInt32() == pid, "Wrong proof PID");
		Check(entry.GetProperty("world").GetString() == world, "Wrong proof world");
		Check(entry.GetProperty("scenario").GetString() == scenario, "Wrong proof scenario");
	}

	private static void UniqueProperties(JsonElement entry)
	{
		Check(entry.ValueKind == JsonValueKind.Object, "Expected JSON proof object");
		var names = new HashSet<string>(StringComparer.Ordinal);
		foreach (var property in entry.EnumerateObject()) Check(names.Add(property.Name), "Duplicate JSON proof property: " + property.Name);
	}

	private static string Hash(JsonElement proof, string name)
	{
		var value = proof.GetProperty(name).GetString() ?? throw new InvalidDataException("Missing hash " + name);
		Check(value.Length == 64 && value.All(Uri.IsHexDigit), "Invalid SHA256 " + name);
		return value.ToUpperInvariant();
	}

	private static void Check(bool condition, string message)
	{
		if (!condition) throw new InvalidDataException(message);
	}

	private static void RequireAbsent(string path)
	{
		Check(!Directory.Exists(path) && !File.Exists(path) && new FileInfo(path).LinkTarget is null
			&& new DirectoryInfo(path).LinkTarget is null, "Owned path already exists: " + path);
	}

	private static void RejectLinkedAncestors(string path)
	{
		for (string? current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
		{
			Check(new FileInfo(current).LinkTarget is null && new DirectoryInfo(current).LinkTarget is null,
				"Linked owned path or ancestor: " + current);
			if (File.Exists(current) || Directory.Exists(current))
				Check((File.GetAttributes(current) & FileAttributes.ReparsePoint) == 0, "Reparse point in owned path: " + current);
		}
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
