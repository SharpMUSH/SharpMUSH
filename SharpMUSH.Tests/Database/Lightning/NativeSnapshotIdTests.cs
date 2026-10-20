using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LightningDB;
using NativeDatabase = LightningDB.LightningDatabase;

namespace SharpMUSH.Tests.Database.Lightning;

public class NativeSnapshotIdTests
{
	private sealed record Sample(string Id, string Value);
	private sealed record Observation(Sample Initial, Sample HeldAfterCommit, Sample FreshAfterCommit,
		Sample RenewedAfterCommit, string TentativeAbortedValue, Sample FreshAfterAbort, Sample HeldAfterAbort,
		Sample BeforeEmpty, Sample AfterEmpty, Sample AfterEqualPut, Sample HeldAfterNoopCandidates,
		Sample FinalRenewed, Sample FinalFresh, object Provenance);

	[Test]
	public async Task ActiveReadIdsAndValuesTrackActualCommittedSnapshots()
	{
		// Same actual-loaded-native provenance scope as the existing category native proofs.
		if (!OperatingSystem.IsLinux() || RuntimeInformation.OSArchitecture != Architecture.X64
			|| RuntimeInformation.ProcessArchitecture != Architecture.X64 || IntPtr.Size != 8)
		{
			Skip.Test("Pinned native provenance proof requires Linux-x64; no snapshot-ID evidence on this platform.");
			return;
		}
		var configured = Environment.GetEnvironmentVariable("TMPDIR");
		var scratch = Path.GetFullPath(string.IsNullOrWhiteSpace(configured)
			? Path.Join(Environment.CurrentDirectory, "work/native-snapshot-id") : configured);
		Safe(scratch);
		Directory.CreateDirectory(scratch);
		var path = Path.Join(scratch, "sharpmush-native-snapshot-id-" + Guid.NewGuid().ToString("N"));
		Require(!Directory.Exists(path) && !File.Exists(path), "Owned native path already exists.");
		Directory.CreateDirectory(path);
		var nativeClosed = false;
		try
		{
			// Every native operation is synchronous in one call, with no awaited assertion, task or
			// thread switch while a native writer is open. Observe returns only after all disposal.
			var observed = Observe(path);
			nativeClosed = true;
			var json = JsonSerializer.Serialize(observed);
			Require(Encoding.UTF8.GetByteCount(json) <= 16384, "Observation report exceeded fixed cap.");
			Console.WriteLine("NativeSnapshotIdObservation " + json);
			await Assert.That(observed.Initial.Value).IsEqualTo("before");
			await Assert.That(observed.HeldAfterCommit).IsEqualTo(observed.Initial);
			await Assert.That(observed.FreshAfterCommit.Id == observed.Initial.Id).IsFalse();
			await Assert.That(observed.FreshAfterCommit.Value).IsEqualTo("after");
			await Assert.That(observed.RenewedAfterCommit).IsEqualTo(observed.FreshAfterCommit);
			await Assert.That(observed.TentativeAbortedValue).IsEqualTo("aborted");
			await Assert.That(observed.FreshAfterAbort).IsEqualTo(observed.FreshAfterCommit);
			await Assert.That(observed.HeldAfterAbort).IsEqualTo(observed.FreshAfterCommit);
			await Assert.That(observed.BeforeEmpty).IsEqualTo(observed.FreshAfterCommit);
			await Assert.That(observed.AfterEmpty.Value).IsEqualTo("after");
			await Assert.That(observed.AfterEqualPut.Value).IsEqualTo("after");
			// Empty commit and equal-value Put IDs are observations, not assumed increments/equalities.
			await Assert.That(observed.HeldAfterNoopCandidates).IsEqualTo(observed.FreshAfterCommit);
			await Assert.That(observed.FinalRenewed.Value).IsEqualTo("renewed");
			await Assert.That(observed.FinalRenewed.Id == observed.AfterEqualPut.Id).IsFalse();
			await Assert.That(observed.FinalFresh).IsEqualTo(observed.FinalRenewed);
		}
		finally
		{
			// A failed native operation/disposal does not authorize deleting a possibly open world.
			if (nativeClosed) Directory.Delete(path, recursive: true);
			else Console.Error.WriteLine("Retained owned native path after unconfirmed native disposal: " + path);
		}
	}

	private static Observation Observe(string path)
	{
		using var environment = new LightningEnvironment(path) { MapSize = 16L << 20, MaxDatabases = 1 };
		environment.Open(EnvironmentOpenFlags.NoThreadLocalStorage);
		var provenance = Provenance(environment);
		using var initializer = environment.BeginTransaction();
		using var database = initializer.OpenDatabase("snapshot-id", new DatabaseConfiguration { Flags = DatabaseOpenFlags.Create });
		Success(initializer.Put(database, "value"u8, "before"u8), "initial put");
		Success(initializer.Commit(), "initial commit");
		using var held = environment.BeginTransaction(TransactionBeginFlags.ReadOnly);
		var initial = Read(held, database);
		Write(environment, database, "after"u8);
		var heldAfterCommit = Read(held, database);
		var freshAfterCommit = Fresh(environment, database);
		held.Reset();
		// Never inspect Id/data while reset, aborted, committed or disposed.
		Success(held.Renew(), "first reader renewal");
		var renewedAfterCommit = Read(held, database);
		string tentative;
		using (var aborted = environment.BeginTransaction())
		{
			Success(aborted.Put(database, "value"u8, "aborted"u8), "tentative put");
			tentative = ReadValue(aborted, database);
			aborted.Abort();
		}
		var freshAfterAbort = Fresh(environment, database);
		var heldAfterAbort = Read(held, database);
		var beforeEmpty = Fresh(environment, database);
		using (var empty = environment.BeginTransaction()) Success(empty.Commit(), "empty commit");
		var afterEmpty = Fresh(environment, database);
		Write(environment, database, "after"u8);
		var afterEqualPut = Fresh(environment, database);
		var heldAfterNoopCandidates = Read(held, database);
		held.Reset();
		Write(environment, database, "renewed"u8);
		Success(held.Renew(), "second reader renewal");
		var finalRenewed = Read(held, database);
		var finalFresh = Fresh(environment, database);
		return new(initial, heldAfterCommit, freshAfterCommit, renewedAfterCommit, tentative,
			freshAfterAbort, heldAfterAbort, beforeEmpty, afterEmpty, afterEqualPut, heldAfterNoopCandidates,
			finalRenewed, finalFresh, provenance);
	}

	private static Sample Fresh(LightningEnvironment environment, NativeDatabase database)
	{
		using var reader = environment.BeginTransaction(TransactionBeginFlags.ReadOnly);
		return Read(reader, database);
	}

	private static Sample Read(LightningTransaction reader, NativeDatabase database)
	{
		Require(reader.IsReadOnly, "Snapshot observation requires an active readonly transaction.");
		return new(reader.Id.ToString(), ReadValue(reader, database));
	}

	private static string ReadValue(LightningTransaction transaction, NativeDatabase database)
	{
		var (code, _, value) = transaction.Get(database, "value"u8);
		Success(code, "read controlled value");
		// This world contains only this test's short literal values; no untrusted/malformed row proof.
		var bytes = value.AsSpan();
		Require(bytes.Length <= 32, "Controlled fixture value exceeded fixed cap.");
		return Encoding.UTF8.GetString(bytes);
	}

	private static void Write(LightningEnvironment environment, NativeDatabase database, ReadOnlySpan<byte> value)
	{
		using var writer = environment.BeginTransaction();
		Success(writer.Put(database, "value"u8, value), "committed put");
		Success(writer.Commit(), "committed write");
	}

	private static object Provenance(LightningEnvironment environment)
	{
		Require(environment.Version.Major == 1 && environment.Version.Minor == 0 && environment.Version.Patch == 1, "Expected actual native LMDB1.0.1.");
		var test = typeof(NativeSnapshotIdTests).Assembly;
		using var dependencies = JsonDocument.Parse(ReadBounded(Path.Join(AppContext.BaseDirectory, test.GetName().Name + ".deps.json"), 4 << 20));
		var library = dependencies.RootElement.GetProperty("libraries").GetProperty("LightningDB/0.23.1");
		Require(library.GetProperty("path").GetString() == "lightningdb/0.23.1", "Unexpected resolved managed package.");
		var assets = dependencies.RootElement.GetProperty("targets").EnumerateObject()
			.SelectMany(target => target.Value.EnumerateObject()).Where(package => package.Name == "LightningDB/0.23.1")
			.SelectMany(package => package.Value.GetProperty("runtime").EnumerateObject())
			.Select(asset => asset.Name).Where(asset => asset.EndsWith("/LightningDB.dll", StringComparison.Ordinal))
			.Distinct(StringComparer.Ordinal).ToArray();
		Require(assets.Length == 1 && assets[0].StartsWith("lib/", StringComparison.Ordinal) && !assets[0].Contains("..", StringComparison.Ordinal), "Expected one resolved managed asset.");
		var packageRoot = Environment.GetEnvironmentVariable("NUGET_PACKAGES")
			?? Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");
		var packagePath = Path.Join(packageRoot, "lightningdb", "0.23.1");
		var managed = typeof(LightningEnvironment).Assembly;
		Require(FileHash(managed.Location) == FileHash(Path.Join(packagePath, assets[0])), "Loaded managed asset differs from resolved package.");
		using var process = Process.GetCurrentProcess();
		var modules = process.Modules.Cast<ProcessModule>().Where(module => Path.GetFileName(module.FileName) == "liblmdb.so")
			.Select(module => Path.GetFullPath(module.FileName)).Distinct(StringComparer.Ordinal).ToArray();
		Require(modules.Length == 1 && modules[0].Length <= 2048, "Expected one already-loaded native LMDB module.");
		var nativeHash = FileHash(modules[0]);
		Require(nativeHash == FileHash(Path.Join(packagePath, "runtimes", "linux-x64", "native", "liblmdb.so")), "Loaded native differs from pinned package.");
		object AssemblyIdentity(Assembly assembly)
		{
			Require(assembly.Location.Length <= 2048, "Assembly path exceeded provenance cap.");
			return new { path = assembly.Location, mvid = assembly.ManifestModule.ModuleVersionId, sha256 = FileHash(assembly.Location) };
		}
		return new
		{
			package = "LightningDB/0.23.1",
			lmdb = new { environment.Version.Major, environment.Version.Minor, environment.Version.Patch, environment.MaxKeySize },
			managed = AssemblyIdentity(managed),
			nativePath = modules[0],
			nativeHash,
			provider = AssemblyIdentity(typeof(SharpMUSH.Database.Lightning.LightningDatabase).Assembly),
			probe = AssemblyIdentity(test),
			providerAdapterExercised = false
		};
	}

	private static byte[] ReadBounded(string path, int cap)
	{
		using var stream = File.OpenRead(path);
		Require(stream.Length <= cap, "Provenance input exceeded fixed cap.");
		var bytes = new byte[checked((int)stream.Length)];
		stream.ReadExactly(bytes);
		Require(stream.ReadByte() == -1, "Provenance input changed while reading.");
		return bytes;
	}
	private static string FileHash(string path)
	{
		using var stream = File.OpenRead(path);
		Require(stream.Length <= 256L << 20, "Provenance hash input exceeded fixed cap.");
		return Convert.ToHexString(SHA256.HashData(stream));
	}
	private static void Success(MDBResultCode code, string operation) => Require(code == MDBResultCode.Success, operation + ": " + code);
	private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
	private static void Safe(string path)
	{
		var full = Path.GetFullPath(path);
		Require(full != "/tmp" && !full.StartsWith("/tmp/", StringComparison.Ordinal)
			&& full != "/var/tmp" && !full.StartsWith("/var/tmp/", StringComparison.Ordinal), "Disk-backed scratch required.");
		for (string? ancestor = full; ancestor is not null; ancestor = Path.GetDirectoryName(ancestor))
			Require((!File.Exists(ancestor) && !Directory.Exists(ancestor)) || (File.GetAttributes(ancestor) & FileAttributes.ReparsePoint) == 0, "Linked scratch ancestor refused.");
	}
}
