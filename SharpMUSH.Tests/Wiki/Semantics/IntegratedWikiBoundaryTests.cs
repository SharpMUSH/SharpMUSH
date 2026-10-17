using LightningDB;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.Plugins;
using SharpMUSH.Library.Plugins.Storage.Lightning;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using LightningDatabase = SharpMUSH.Database.Lightning.LightningDatabase;
using SharpMUSH.Database.Lightning.Semantics;
using System.Runtime.CompilerServices;

namespace SharpMUSH.Tests.Wiki.Semantics;

public class IntegratedWikiBoundaryTests
{
	private const string IdentityKey = "prototype.semantic.fixture.identity/v1";
	private static string ScratchRoot => Path.Combine(Directory.Exists("/temp/codex/semantic-wiki-b2")
		? "/temp/codex/semantic-wiki-b2" : Path.Combine(Directory.GetCurrentDirectory(), "work"), "semantic-wiki-fixture-boundary");
	private static LightningStoreOptions Options(string path) => new() { Path = path, MapSize = 256L << 20 };
	private static LightningDatabase Open(SemanticWikiPrototypeFixture fixture, string? path = null,
		IReadOnlyList<IMigrationSource>? sources = null)
		=> fixture.OpenProvider(NullLogger<LightningDatabase>.Instance, Options(path ?? fixture.WorldPath),
			Substitute.For<IPasswordService>(), relations: null, sources ?? [], [], new AccountCredentialGate(), new(() => []));

	// Always close a mistakenly admitted provider BEFORE asserting rejection; failure cannot leak native ownership.
	private static async Task<bool> Rejected(SemanticWikiPrototypeFixture fixture, string? path = null)
	{
		LightningDatabase? admitted = null;
		try
		{
			admitted = Open(fixture, path);
			return false;
		}
		catch (InvalidOperationException) { return true; }
		finally
		{
			if (admitted is not null) await admitted.DisposeAsync();
		}
	}

	[Test]
	public async Task OrdinaryProviderMigrationDoesNotActivateFixtureIdentityOrSemanticTables()
	{
		var path = Path.Combine(ScratchRoot, Guid.NewGuid().ToString("N"));
		try
		{
			await using (var provider = new LightningDatabase(NullLogger<LightningDatabase>.Instance,
				Options(path), Substitute.For<IPasswordService>(), relations: null))
			{
				await provider.Migrate();
				await Assert.That(provider.Store.Read(tx => tx.TryGet(Tables.Meta, Keys.Str(IdentityKey), out _))).IsFalse();
			}
			await Assert.That(OnDiskNames(path).Any(name => name.Contains("semantic", StringComparison.OrdinalIgnoreCase))).IsFalse();
		}
		finally { if (Directory.Exists(path)) Directory.Delete(path, true); }
	}

	[Test]
	public async Task IssuedEmptyFixtureMigratesClosesAndReopensWithExactIdentity()
	{
		await using var fixture = new SemanticWikiPrototypeFixture(ScratchRoot);
		await using (var first = Open(fixture))
		{
			await first.Migrate();
			var identity = first.Store.Read(tx => tx.TryGet(Tables.Meta, Keys.Str(IdentityKey), out var bytes) ? bytes : []);
			await Assert.That(identity).IsEquivalentTo(fixture.FixtureId.ToByteArray(bigEndian: true));
			await first.Store.WriteAsync(tx => tx.Put(Tables.Meta, Keys.Str("retained-test"), [42]));
		}

		await using (var second = Open(fixture))
		{
			await second.Migrate();
			await Assert.That(second.Store.Read(tx => tx.TryGet(Tables.Meta, Keys.Str("retained-test"), out var bytes)
				&& bytes.SequenceEqual(new byte[] { 42 }))).IsTrue();
		}
		await Assert.That(OnDiskNames(fixture.WorldPath).Any(name => name.Contains("semantic", StringComparison.OrdinalIgnoreCase))).IsFalse();
	}

	[Test]
	public async Task ForeignOptionsPathIsRejectedBeforeNativeConstruction()
	{
		await using var fixture = new SemanticWikiPrototypeFixture(ScratchRoot);
		var foreign = fixture.WorldPath + "-foreign";
		try
		{
			var rejected = await Rejected(fixture, foreign);
			await Assert.That(rejected).IsTrue();
			await Assert.That(Directory.Exists(foreign)).IsFalse();
		}
		finally { if (Directory.Exists(foreign)) Directory.Delete(foreign, true); }
	}

	[Test]
	public async Task CopiedMarkerAtForeignPathIsRejected()
	{
		await using var fixture = new SemanticWikiPrototypeFixture(ScratchRoot);
		var foreign = fixture.WorldPath + "-copied";
		Directory.CreateDirectory(foreign);
		try
		{
			foreach (var file in Directory.GetFiles(fixture.WorldPath)) File.Copy(file, Path.Combine(foreign, Path.GetFileName(file)));
			var rejected = await Rejected(fixture, foreign);
			await Assert.That(rejected).IsTrue();
			await Assert.That(File.Exists(Path.Combine(foreign, "data.mdb"))).IsFalse();
		}
		finally { Directory.Delete(foreign, true); }
	}

	[Test]
	public async Task ActiveLeaseRejectsAtomicSecondClaimWithoutOpeningAnotherEnvironment()
	{
		await using var fixture = new SemanticWikiPrototypeFixture(ScratchRoot);
		using var first = fixture.Capability.Claim(Options(fixture.WorldPath));
		var rejected = false;
		try { using var second = fixture.Capability.Claim(Options(fixture.WorldPath)); }
		catch (InvalidOperationException) { rejected = true; }
		await Assert.That(rejected).IsTrue();
	}

	[Test]
	public async Task ConcurrentManagedClaimsHaveExactlyOneWinner()
	{
		await using var fixture = new SemanticWikiPrototypeFixture(ScratchRoot);
		using var start = new ManualResetEventSlim(false);
		var attempts = Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
		{
			start.Wait();
			try { return fixture.Capability.Claim(Options(fixture.WorldPath)); }
			catch (InvalidOperationException) { return null; }
		})).ToArray();
		start.Set();
		var results = await Task.WhenAll(attempts);
		foreach (var lease in results) lease?.Dispose();
		await Assert.That(results.Count(lease => lease is not null)).IsEqualTo(1);
	}

	[Test]
	public async Task FirstOpenRefusesPreexistingContents()
	{
		await using var fixture = new SemanticWikiPrototypeFixture(ScratchRoot);
		File.WriteAllText(Path.Combine(fixture.WorldPath, "unexpected.txt"), "not empty");
		var rejected = await Rejected(fixture);
		await Assert.That(rejected).IsTrue();
		await Assert.That(File.Exists(Path.Combine(fixture.WorldPath, "data.mdb"))).IsFalse();
	}

	[Test]
	[Arguments("version")]
	[Arguments("identity")]
	[Arguments("path")]
	public async Task MarkerMustMatchVersionIdentityAndCanonicalPath(string changed)
	{
		await using var fixture = new SemanticWikiPrototypeFixture(ScratchRoot);
		var marker = Directory.GetFiles(fixture.WorldPath).Single();
		var original = File.ReadAllText(marker);
		try
		{
			File.WriteAllText(marker, System.Text.Json.JsonSerializer.Serialize(new
			{
				Version = changed == "version" ? 2 : 1,
				CanonicalPath = changed == "path" ? fixture.WorldPath + "-other" : fixture.WorldPath,
				FixtureId = changed == "identity" ? Guid.NewGuid() : fixture.FixtureId
			}));
			var rejected = await Rejected(fixture);
			await Assert.That(rejected).IsTrue();
			await Assert.That(File.Exists(Path.Combine(fixture.WorldPath, "data.mdb"))).IsFalse();
		}
		finally { File.WriteAllText(marker, original); }
	}

	[Test]
	public async Task LinkedScratchAncestorCannotIssueFixture()
	{
		var root = Path.Combine(ScratchRoot, "link-test-" + Guid.NewGuid().ToString("N"));
		var target = Path.Combine(root, "target");
		var linked = Path.Combine(root, "linked");
		Directory.CreateDirectory(target);
		Directory.CreateSymbolicLink(linked, target);
		try
		{
			SemanticWikiPrototypeFixture? admitted = null;
			var rejected = false;
			try { admitted = new SemanticWikiPrototypeFixture(Path.Combine(linked, "child")); }
			catch (InvalidOperationException) { rejected = true; }
			finally { if (admitted is not null) await admitted.DisposeAsync(); }
			await Assert.That(rejected).IsTrue();
			await Assert.That(Directory.GetFileSystemEntries(target).Length).IsEqualTo(0);
		}
		finally { Directory.Delete(linked); Directory.Delete(root, true); }
	}

	[Test]
	public async Task ManagedOptionRejectionDoesNotConsumeFirstClaim()
	{
		await using var fixture = new SemanticWikiPrototypeFixture(ScratchRoot);
		var rejected = false;
		LightningDatabase? admitted = null;
		try
		{
			admitted = fixture.OpenProvider(NullLogger<LightningDatabase>.Instance,
				Options(fixture.WorldPath) with { MaxBatch = 0 }, Substitute.For<IPasswordService>(), relations: null,
				[], [], new AccountCredentialGate(), new(() => []));
		}
		catch (ArgumentOutOfRangeException) { rejected = true; }
		finally { if (admitted is not null) await admitted.DisposeAsync(); }
		await Assert.That(rejected).IsTrue();
		await using var corrected = Open(fixture);
		await corrected.Migrate();
	}

	[Test]
	public async Task OwnerRefusesCleanupUntilLiveProviderFullyCloses()
	{
		var fixture = new SemanticWikiPrototypeFixture(ScratchRoot);
		try
		{
			await using (var provider = Open(fixture))
			{
				await provider.Migrate();
				var refused = false;
				try { await fixture.DisposeAsync(); }
				catch (InvalidOperationException) { refused = true; }
				await Assert.That(refused).IsTrue();
				await Assert.That(Directory.Exists(fixture.WorldPath)).IsTrue();
			}
			await using (var reopened = Open(fixture)) { await reopened.Migrate(); }
		}
		finally { await fixture.DisposeAsync(); }
		await Assert.That(Directory.Exists(fixture.WorldPath)).IsFalse();
	}

	[Test]
	public async Task ReservationCleanupCanRetryAfterRevocation()
	{
		if (OperatingSystem.IsWindows())
		{
			Skip.Test("Reservation deletion failure requires Unix directory permissions.");
			return;
		}

		var root = Path.Combine(ScratchRoot, "reservation-retry-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(root);
		var originalMode = File.GetUnixFileMode(root);
		SemanticWikiPrototypeFixture? fixture = null;
		try
		{
			fixture = new SemanticWikiPrototypeFixture(root);
			var reservation = fixture.WorldPath + ".semantic-reservation";
			var sibling = Path.Combine(root, "unowned");
			File.WriteAllText(sibling, "unowned");
			var denied = false;
			try
			{
				File.SetUnixFileMode(root, originalMode & ~(UnixFileMode.UserWrite | UnixFileMode.GroupWrite | UnixFileMode.OtherWrite));
				try { await fixture.DisposeAsync(); }
				catch (UnauthorizedAccessException) { denied = true; }
			}
			finally { File.SetUnixFileMode(root, originalMode); }

			await Assert.That(denied).IsTrue();
			await Assert.That(Directory.Exists(fixture.WorldPath)).IsTrue();
			await Assert.That(File.Exists(reservation)).IsTrue();
			await Assert.That(() => fixture.Capability.Claim(Options(fixture.WorldPath))).Throws<InvalidOperationException>();
			await fixture.DisposeAsync();
			await Assert.That(Directory.Exists(fixture.WorldPath)).IsFalse();
			await Assert.That(File.Exists(reservation)).IsFalse();
			await fixture.DisposeAsync();
			await Assert.That(File.ReadAllText(sibling)).IsEqualTo("unowned");
		}
		finally
		{
			File.SetUnixFileMode(root, originalMode);
			try { if (fixture is not null) await fixture.DisposeAsync(); }
			finally { Directory.Delete(root, true); }
		}
	}

	[Test]
	public async Task DisposedIssuanceRejectsReopenAndCleanupLeavesUnownedSibling()
	{
		var fixture = new SemanticWikiPrototypeFixture(ScratchRoot);
		var sibling = fixture.WorldPath + ".unowned";
		File.WriteAllText(sibling, "unowned");
		try
		{
			await fixture.DisposeAsync();
			var rejected = await Rejected(fixture);
			await Assert.That(rejected).IsTrue();
			await Assert.That(File.ReadAllText(sibling)).IsEqualTo("unowned");
			await fixture.DisposeAsync();
		}
		finally { File.Delete(sibling); }
	}

	[Test]
	public async Task CloseWithoutMigrationPermanentlyRejectsSameIssuance()
	{
		await using var fixture = new SemanticWikiPrototypeFixture(ScratchRoot);
		await using (var provider = Open(fixture)) { }
		await Assert.That(await Rejected(fixture)).IsTrue();
	}

	[Test]
	public async Task CancellationBeforeCredentialEntryPoisonsFirstInitialization()
	{
		await using var fixture = new SemanticWikiPrototypeFixture(ScratchRoot);
		var cancelled = false;
		await using (var provider = Open(fixture))
		{
			using var cancellation = new CancellationTokenSource();
			cancellation.Cancel();
			try { await provider.Migrate(cancellation.Token); }
			catch (OperationCanceledException) { cancelled = true; }
		}
		await Assert.That(cancelled).IsTrue();
		await Assert.That(await Rejected(fixture)).IsTrue();
	}

	[Test]
	public async Task ManagedMigrationFailureAfterIdentityBindingPoisonsSameIssuance()
	{
		await using var fixture = new SemanticWikiPrototypeFixture(ScratchRoot);
		var failed = false;
		var bound = false;
		await using (var provider = Open(fixture, sources: [new FailingMigration()]))
		{
			try { await provider.Migrate(); }
			catch (FixtureMigrationException) { failed = true; }
			bound = provider.Store.Read(tx => tx.TryGet(Tables.Meta, Keys.Str(IdentityKey), out var bytes)
				&& bytes.SequenceEqual(fixture.FixtureId.ToByteArray(bigEndian: true)));
		}
		await Assert.That(failed).IsTrue();
		await Assert.That(bound).IsTrue();
		await Assert.That(await Rejected(fixture)).IsTrue();
	}

	[Test]
	public async Task CancellationAfterBindingStillPoisonsWholeFirstMigration()
	{
		await using var fixture = new SemanticWikiPrototypeFixture(ScratchRoot);
		using var cancellation = new CancellationTokenSource();
		var cancelled = false;
		var bound = false;
		await using (var provider = Open(fixture, sources: [new CancellingMigration(cancellation)]))
		{
			try { await provider.Migrate(cancellation.Token); }
			catch (OperationCanceledException) { cancelled = true; }
			bound = provider.Store.Read(tx => tx.TryGet(Tables.Meta, Keys.Str(IdentityKey), out var bytes)
				&& bytes.SequenceEqual(fixture.FixtureId.ToByteArray(bigEndian: true)));
		}
		await Assert.That(cancelled).IsTrue();
		await Assert.That(bound).IsTrue();
		await Assert.That(await Rejected(fixture)).IsTrue();
	}

	[Test]
	[Arguments(0)]
	[Arguments(15)]
	[Arguments(17)]
	public async Task IdentityPrimitiveRequiresExactlySixteenBytes(int length)
	{
		await using var fixture = new SemanticWikiPrototypeFixture(ScratchRoot);
		await using (var provider = Open(fixture))
		{
			await provider.Migrate();
			await provider.Store.WriteAsync(tx => tx.Put(Tables.Meta, Keys.Str(IdentityKey), new byte[length]));
		}
		var before = File.ReadAllBytes(Path.Combine(fixture.WorldPath, "data.mdb"));
		var rejected = await Rejected(fixture);
		var after = File.ReadAllBytes(Path.Combine(fixture.WorldPath, "data.mdb"));
		await Assert.That(rejected).IsTrue();
		await Assert.That(after).IsEquivalentTo(before);
	}

	[Test]
	public async Task MatchingPrimitiveInDuplicateMetaIsRejectedBeforeWritingConstructor()
	{
		await using var fixture = new SemanticWikiPrototypeFixture(ScratchRoot);
		await using (var provider = Open(fixture)) { await provider.Migrate(); }
		File.Delete(Path.Combine(fixture.WorldPath, "data.mdb"));
		File.Delete(Path.Combine(fixture.WorldPath, "lock.mdb"));
		// Build a VALID but incompatible test-owned database while all prior environments are closed.
		using (var environment = new LightningEnvironment(fixture.WorldPath, new EnvironmentConfiguration { MaxDatabases = 256 }))
		{
			environment.Open();
			using var tx = environment.BeginTransaction();
			using var meta = tx.OpenDatabase(Tables.Meta.Name,
				new DatabaseConfiguration { Flags = DatabaseOpenFlags.Create | DatabaseOpenFlags.DuplicatesSort });
			var put = tx.Put(meta, Keys.Str(IdentityKey), fixture.FixtureId.ToByteArray(bigEndian: true));
			if (put != MDBResultCode.Success) throw new InvalidOperationException($"Creating valid test Meta failed: {put}.");
			var commit = tx.Commit();
			if (commit != MDBResultCode.Success) throw new InvalidOperationException($"Committing valid test Meta failed: {commit}.");
		}
		var before = File.ReadAllBytes(Path.Combine(fixture.WorldPath, "data.mdb"));
		var rejected = await Rejected(fixture);
		var after = File.ReadAllBytes(Path.Combine(fixture.WorldPath, "data.mdb"));
		await Assert.That(rejected).IsTrue();
		await Assert.That(after).IsEquivalentTo(before);
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task MissingOrMismatchedMetaRejectsBeforeWritingConstructor(bool mismatch)
	{
		await using var fixture = new SemanticWikiPrototypeFixture(ScratchRoot);
		await using (var provider = Open(fixture))
		{
			await provider.Migrate();
			await provider.Store.WriteAsync(tx =>
			{
				if (mismatch) tx.Put(Tables.Meta, Keys.Str(IdentityKey), Guid.NewGuid().ToByteArray(bigEndian: true));
				else tx.Delete(Tables.Meta, Keys.Str(IdentityKey));
			});
		}
		var before = File.ReadAllBytes(Path.Combine(fixture.WorldPath, "data.mdb"));
		var rejected = await Rejected(fixture);
		var after = File.ReadAllBytes(Path.Combine(fixture.WorldPath, "data.mdb"));
		await Assert.That(rejected).IsTrue();
		await Assert.That(after).IsEquivalentTo(before);
	}

	[Test]
	public async Task ValidClosedForeignWorldUnderOriginalMarkerIsNotAdopted()
	{
		await using var fixture = new SemanticWikiPrototypeFixture(ScratchRoot);
		await using (var provider = Open(fixture)) { await provider.Migrate(); }
		File.Delete(Path.Combine(fixture.WorldPath, "data.mdb"));
		File.Delete(Path.Combine(fixture.WorldPath, "lock.mdb"));
		// A separate VALID test-owned world, closed before the issued lineage attempts admission.
		await using (var foreign = new LightningDatabase(NullLogger<LightningDatabase>.Instance,
			Options(fixture.WorldPath), Substitute.For<IPasswordService>(), relations: null)) { await foreign.Migrate(); }
		var before = File.ReadAllBytes(Path.Combine(fixture.WorldPath, "data.mdb"));
		var rejected = await Rejected(fixture);
		var after = File.ReadAllBytes(Path.Combine(fixture.WorldPath, "data.mdb"));
		await Assert.That(rejected).IsTrue();
		await Assert.That(after).IsEquivalentTo(before);
	}

	[Test]
	public async Task ManagedUncertainClosureRetainsQuarantineOwnership()
	{
		// Actual production ownership disposition: capability Release and owner cleanup consume these
		// same decisions. No reservation/native handle is acquired just to simulate uncertain closure.
		var ownership = new SemanticFixtureOwnership();
		ownership.Quarantine();
		ownership.Revoke(hasActiveLease: true);
		await Assert.That(ownership.IsQuarantined).IsTrue();
		await Assert.That(ownership.MayReleaseLease).IsFalse();
		await Assert.That(ownership.MayRemoveArtifacts).IsFalse();
	}

	private sealed class FixtureMigrationException : Exception { }
	private sealed class FailingMigration : IMigrationSource
	{
		public IEnumerable<LightningMigrationStep> LightningSteps =>
			[new("fixture-managed-failure", _ => throw new FixtureMigrationException())];
	}
	private sealed class CancellingMigration(CancellationTokenSource cancellation) : IMigrationSource
	{
		public IEnumerable<LightningMigrationStep> LightningSteps =>
			[new("fixture-managed-cancellation", _ => { cancellation.Cancel(); return ValueTask.CompletedTask; })];
	}

	// Only called after all provider disposal is awaited.
	internal static string[] OnDiskNames(string path)
	{
		using var env = new LightningEnvironment(path, new EnvironmentConfiguration { MaxDatabases = 256 });
		env.Open(EnvironmentOpenFlags.ReadOnly | EnvironmentOpenFlags.NoThreadLocalStorage);
		using var tx = env.BeginTransaction(TransactionBeginFlags.ReadOnly);
		using var root = tx.OpenDatabase();
		using var cursor = tx.CreateCursor(root);
		var names = new List<string>();
		var positioned = cursor.First().resultCode;
		if (positioned == MDBResultCode.NotFound) return [];
		if (positioned != MDBResultCode.Success) throw new InvalidOperationException($"Native catalog seek failed: {positioned}.");
		do
		{
			var (code, key, _) = cursor.GetCurrent();
			if (code != MDBResultCode.Success) throw new InvalidOperationException($"Native catalog read failed: {code}.");
			var size = NativeSize(ref key);
			if (size < 1 || size > 4096) throw new InvalidOperationException("Native catalog key exceeds fixture inspection bounds.");
			names.Add(System.Text.Encoding.UTF8.GetString(key.AsSpan()).TrimEnd('\0'));
			positioned = cursor.Next().resultCode;
			if (positioned != MDBResultCode.Success && positioned != MDBResultCode.NotFound)
				throw new InvalidOperationException($"Native catalog advance failed: {positioned}.");
		} while (positioned == MDBResultCode.Success);
		return names.ToArray();
	}

	[UnsafeAccessor(UnsafeAccessorKind.Field, Name = "size")]
	private static extern ref nint NativeSize(ref MDBValue value);
}
