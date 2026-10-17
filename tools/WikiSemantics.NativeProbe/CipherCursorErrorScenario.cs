using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using LightningDB;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.Plugins.Storage.Lightning;

// Test-process-only proof, qualified for the pinned Linux-x64 encrypted-page callback path.
internal static class CipherCursorErrorScenario
{
	private const string Scenario = "cipher-cursor-error";
	private const string ManagedCommit = "46f8ca97ef9eb992db93e493b29493179d2e5343";
	private const string ManagedSha256 = "79f70654594367b7388a23a49923856ee8b8a88f8f70e03f53ca7f1039e81854";
	private const string NativeSha256 = "e518ad9bec8fdbc3e856f993edc17e6d476e04a44797d75d87280a400346fcb4";
	// Independent oracle from the reviewed ReadRange accounting: reservation 64 + owners 192 + array 1056.
	private const long InitialRangeReservation = 1312;
	private static readonly TableDef Table = TableDef.Node("prototype.semantic.cipher.rows");
	private static readonly byte[] FirstKey = [1];
	private static readonly byte[] FirstValue = [17];
	private static readonly byte[] SecondKey = [2];
	private static readonly byte[] SecondValue = Enumerable.Range(0, 8192).Select(i => (byte)(i % 251)).ToArray();

	public static void Run(string world, string evidence, Action<string> mark)
	{
		Admit(world, evidence);
		Directory.CreateDirectory(evidence);
		Directory.CreateDirectory(world);
		// Disposable fixture key only; never serialize it. Each environment's configuration copies it.
		var key = RandomNumberGenerator.GetBytes(32);
		try
		{
			Seed(world, key);
			mark("cipher-seeded");
			var provenance = Provenance();
			var control = HealthyRead(world, key, requireAdvanceDecryption: true);
			mark("cipher-control-closed");
			var raw = RawFailure(world, key);
			mark("cipher-raw-failure-closed");
			var bounded = BoundedFailure(world, key);
			mark("cipher-bounded-failure-closed");
			var reopened = HealthyRead(world, key, requireAdvanceDecryption: false);
			mark("cipher-healthy-reopen-closed");
			var proof = new
			{
				pid = Environment.ProcessId,
				world,
				scenario = Scenario,
				qualification = "Linux-x64; supported encrypted AES-GCM page-decryption callback failure",
				provenance,
				controlRows = control.Rows,
				overflowPages = control.OverflowPages,
				controlAdvanceDecryptions = control.AdvanceDecryptions,
				rawFirstRowVerified = raw.FirstRowVerified,
				rawAdvanceCode = raw.Code.ToString(),
				rawAdvanceNumericCode = (int)raw.Code,
				rawFailureCount = raw.Failures,
				boundedException = bounded.ExceptionName,
				boundedErrorCode = bounded.Code.ToString(),
				boundedNumericCode = (int)bounded.Code,
				returnedBatch = bounded.ReturnedBatch,
				boundedFailureCount = bounded.Failures,
				callbackProbes = bounded.Probes,
				callbackBytes = bounded.Bytes,
				callbackWorkingBytes = bounded.Working,
				initialRangeReservation = InitialRangeReservation,
				workingBefore = bounded.WorkingBefore,
				workingAfter = bounded.WorkingAfter,
				probesAfter = bounded.ProbesAfter,
				bytesAfter = bounded.BytesAfter,
				healthyReopenRows = reopened.Rows,
				healthyReopenVerified = reopened.Verified,
				firstValueSha256 = Convert.ToHexStringLower(SHA256.HashData(FirstValue)),
				secondValueSha256 = Convert.ToHexStringLower(SHA256.HashData(SecondValue))
			};
			File.WriteAllText(Path.Combine(evidence, "cipher-cursor-error.json"), JsonSerializer.Serialize(proof));
			mark("cipher-cursor-error-complete");
		}
		finally { CryptographicOperations.ZeroMemory(key); }
	}

	private static void Admit(string world, string evidence)
	{
		Require(OperatingSystem.IsLinux() && RuntimeInformation.ProcessArchitecture == Architecture.X64,
			"This proof requires the reviewed Linux-x64 native binary");
		var parent = Path.GetDirectoryName(world) ?? throw new InvalidDataException("Missing fixture parent");
		var name = Path.GetFileName(parent);
		Require(name.StartsWith("semantic-cipher-", StringComparison.Ordinal)
			&& Guid.TryParseExact(name["semantic-cipher-".Length..], "N", out _), "Missing owned fixture identity");
		Require(world == Path.Combine(parent, "world") && evidence == Path.Combine(parent, "evidence"), "Fixture paths must be owned siblings");
		Require(parent != "/tmp" && !parent.StartsWith("/tmp/", StringComparison.Ordinal)
			&& parent != "/var/tmp" && !parent.StartsWith("/var/tmp/", StringComparison.Ordinal), "Fixture requires disk-backed scratch");
		Require(Directory.Exists(parent) && (File.GetAttributes(parent) & FileAttributes.ReparsePoint) == 0,
			"Fixture parent must be a real owned directory");
		Require(!Directory.EnumerateFileSystemEntries(parent).Any(), "Fixture parent must be empty");
		Require(!Directory.Exists(world) && !File.Exists(world) && !Directory.Exists(evidence) && !File.Exists(evidence), "Fixture paths already exist");
	}

	private static LightningEnvironment Open(string world, byte[] key, ObservedCipher cipher)
	{
		var environment = new LightningEnvironment(world, new EnvironmentConfiguration
		{
			MapSize = 16L << 20,
			MaxDatabases = 2,
			PageSize = 4096,
			Encryption = new EncryptionConfiguration(cipher, key)
		});
		try
		{
			environment.Open();
			Require(environment.PageSize == 4096, "Unexpected native page size");
			Require(environment.Version.Major == 1 && environment.Version.Minor == 0 && environment.Version.Patch == 1,
				"Unexpected native LMDB version");
			return environment;
		}
		catch { environment.Dispose(); throw; }
	}

	private static void Seed(string world, byte[] key)
	{
		using var cipher = new ObservedCipher();
		using var environment = Open(world, key, cipher);
		using var transaction = environment.BeginTransaction();
		// Default closeOnDispose=false: environment owns the DBI, including abort during failed setup.
		using var database = transaction.OpenDatabase(Table.Name, new DatabaseConfiguration { Flags = DatabaseOpenFlags.Create });
		Success(transaction.Put(database, FirstKey, FirstValue), "seed first row");
		Success(transaction.Put(database, SecondKey, SecondValue), "seed overflow row");
		Success(transaction.Commit(), "commit fixture");
	}

	private static LightningDatabase OpenPublishedDatabase(LightningEnvironment environment)
	{
		// lmdb.h: a DBI opened in a transaction is private until commit, and abort closes it automatically.
		// Publish using a healthy read-only setup transaction. Never manually close a DBI after its abort.
		using var setup = environment.BeginTransaction(TransactionBeginFlags.ReadOnly);
		var database = setup.OpenDatabase(Table.Name);
		try { Success(setup.Commit(), "publish existing DBI"); return database; }
		catch { database.Dispose(); throw; }
	}

	private sealed record HealthyResult(int Rows, long OverflowPages, int AdvanceDecryptions, bool Verified);

	private static HealthyResult HealthyRead(string world, byte[] key, bool requireAdvanceDecryption)
	{
		using var cipher = new ObservedCipher();
		using var environment = Open(world, key, cipher);
		using var database = OpenPublishedDatabase(environment);
		using var transaction = environment.BeginTransaction(TransactionBeginFlags.ReadOnly);
		var stats = transaction.GetStats(database);
		var overflow = Convert.ToInt64(stats.OverflowPages);
		Require(stats.PageSize == 4096 && overflow > 0 && Convert.ToInt64(stats.Entries) == 2, "Fixture is not the expected overflow table");
		using var cursor = transaction.CreateCursor(database);
		VerifyRow(cursor.First(), FirstKey, FirstValue);
		VerifyRow(cursor.GetCurrent(), FirstKey, FirstValue);
		var before = cipher.Decryptions;
		VerifyRow(cursor.Next(), SecondKey, SecondValue);
		var advanceDecryptions = cipher.Decryptions - before;
		Require(!requireAdvanceDecryption || advanceDecryptions > 0, "Healthy Next did not decrypt a later page");
		Require(cursor.Next().resultCode == MDBResultCode.NotFound, "Unexpected trailing row");
		Require(cipher.Failures == 0, "Healthy control failed decryption");
		return new(2, overflow, advanceDecryptions, true);
	}

	private sealed record RawResult(bool FirstRowVerified, MDBResultCode Code, int Failures);

	private static RawResult RawFailure(string world, byte[] key)
	{
		using var cipher = new ObservedCipher();
		using var environment = Open(world, key, cipher);
		using var database = OpenPublishedDatabase(environment);
		using var transaction = environment.BeginTransaction(TransactionBeginFlags.ReadOnly);
		using var cursor = transaction.CreateCursor(database);
		VerifyRow(cursor.First(), FirstKey, FirstValue);
		VerifyRow(cursor.GetCurrent(), FirstKey, FirstValue);
		cipher.ArmRawFailure();
		var code = cursor.Next().resultCode;
		// No GetCurrent, retry, stats or other read after the failed native advance in this environment.
		Require(code == MDBResultCode.CryptoFail && cipher.Failures == 1, "Raw Next did not produce one native CryptoFail");
		return new(true, code, cipher.Failures);
	}

	private sealed record BoundedResult(string ExceptionName, MDBResultCode Code, bool ReturnedBatch, int Failures,
		int Probes, long Bytes, long Working, long WorkingBefore, long WorkingAfter, int ProbesAfter, long BytesAfter);

	private static BoundedResult BoundedFailure(string world, byte[] key)
	{
		var budget = new NativeReadBudget(16, 32768, 1 << 20, default);
		using var cipher = new ObservedCipher(budget);
		using var environment = Open(world, key, cipher);
		using var database = OpenPublishedDatabase(environment);
		using var transaction = environment.BeginTransaction(TransactionBeginFlags.ReadOnly);
		var reader = ActualBoundedReader.Create(transaction, Table, database);
		Require(budget.Probes == 0 && budget.Bytes == 0 && budget.WorkingBytes == 0, "Bounded pass requires fresh zero budget");
		var workingBefore = budget.WorkingBytes;
		var returnedBatch = false;
		LightningStoreException? observed = null;
		try
		{
			using var batch = reader.ReadRange(Table, [], [], [], [], budget);
			returnedBatch = true;
		}
		catch (LightningStoreException error) { observed = error; }
		// Only managed observations follow. The failed transaction/environment receive no further reads.
		if (returnedBatch || observed is not { Code: MDBResultCode.CryptoFail })
			throw new InvalidOperationException("Actual bounded ReadRange did not throw CryptoFail without a batch");
		Require(cipher.Failures == 1 && cipher.CallbackProbes == 3 && cipher.CallbackBytes == 2
			&& cipher.CallbackWorking > InitialRangeReservation, "Failure did not occur after the bounded reader copied its first row");
		Require(budget.WorkingBytes == workingBefore && budget.Probes == 3 && budget.Bytes == 2,
			"Bounded failure leaked working storage or lost cumulative work charges");
		return new(observed.GetType().Name, observed.Code, returnedBatch, cipher.Failures,
			cipher.CallbackProbes, cipher.CallbackBytes, cipher.CallbackWorking, workingBefore, budget.WorkingBytes, budget.Probes, budget.Bytes);
	}

	private static void VerifyRow((MDBResultCode resultCode, MDBValue key, MDBValue value) row, byte[] key, byte[] value)
	{
		Success(row.resultCode, "read expected row");
		Require(row.key.AsSpan().SequenceEqual(key) && row.value.AsSpan().SequenceEqual(value), "Full key/value mismatch");
	}

	private static void Success(MDBResultCode code, string operation)
	{
		if (code != MDBResultCode.Success) throw LightningStoreException.From(code, operation);
	}

	private static void Require(bool condition, string message)
	{
		if (!condition) throw new InvalidOperationException(message);
	}

	private static object Provenance()
	{
		// Read mapped file names only, never native addresses/handles. Hash the library actually loaded.
		var libraries = File.ReadLines("/proc/self/maps").Select(line => line.IndexOf('/') is var start && start >= 0 ? line[start..] : "")
			.Where(path => Path.GetFileName(path) == "liblmdb.so").Distinct(StringComparer.Ordinal).ToArray();
		Require(libraries.Length == 1, "Cannot identify one mapped native LMDB binary");
		using var nativeFile = File.OpenRead(libraries[0]);
		var nativeHash = Convert.ToHexStringLower(SHA256.HashData(nativeFile));
		Require(nativeHash == NativeSha256, "Loaded native binary differs from the reviewed pinned Linux-x64 binary");
		var managed = typeof(LightningEnvironment).Assembly;
		using var managedFile = File.OpenRead(managed.Location);
		var managedHash = Convert.ToHexStringLower(SHA256.HashData(managedFile));
		Require(managedHash == ManagedSha256, "Loaded managed assembly differs from the reviewed pinned net10.0 package asset");
		return new
		{
			package = "LightningDB 0.23.1",
			managedCommit = ManagedCommit,
			managedAssembly = managed.FullName,
			managedInformationalVersion = managed.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
			managedSha256 = managedHash,
			nativeVersion = "1.0.1",
			nativePath = libraries[0],
			nativeSha256 = nativeHash
		};
	}

	private sealed class ObservedCipher(NativeReadBudget? budget = null) : LightningCipher(16), IDisposable
	{
		private readonly object _sync = new();
		private readonly AesGcmCipher _real = new();
		private bool _rawArmed;
		private bool _disposed;
		public int Decryptions { get; private set; }
		public int Failures { get; private set; }
		public int CallbackProbes { get; private set; }
		public long CallbackBytes { get; private set; }
		public long CallbackWorking { get; private set; }

		public void ArmRawFailure() { lock (_sync) _rawArmed = true; }

		public override bool Encrypt(ReadOnlySpan<byte> source, Span<byte> destination, ReadOnlySpan<byte> key,
			ReadOnlySpan<byte> iv, Span<byte> authData)
		{
			lock (_sync) return _real.Encrypt(source, destination, key, iv, authData);
		}

		public override bool Decrypt(ReadOnlySpan<byte> source, Span<byte> destination, ReadOnlySpan<byte> key,
			ReadOnlySpan<byte> iv, ReadOnlySpan<byte> authData)
		{
			lock (_sync)
			{
				Decryptions++;
				if (_rawArmed || (budget is not null && budget.Probes >= 3 && budget.Bytes >= 2))
				{
					Failures++;
					if (Failures == 1 && budget is not null)
					{
						CallbackProbes = budget.Probes;
						CallbackBytes = budget.Bytes;
						CallbackWorking = budget.WorkingBytes;
					}
					// Supported failure only: no throw, I/O, reentry, budget mutation or buffer alteration.
					return false;
				}
				return _real.Decrypt(source, destination, key, iv, authData);
			}
		}

		public void Dispose()
		{
			lock (_sync)
			{
				if (_disposed) return;
				_disposed = true;
				_real.Dispose();
			}
		}
	}
}
