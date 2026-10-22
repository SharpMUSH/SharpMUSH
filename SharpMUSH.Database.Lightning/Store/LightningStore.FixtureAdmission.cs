using LightningDB;
using SharpMUSH.Database.Lightning.Semantics;
using SharpMUSH.Library.Plugins.Storage.Lightning;
using LmdbDb = LightningDB.LightningDatabase;

namespace SharpMUSH.Database.Lightning.Store;

public sealed partial class LightningStore
{
	internal const string FixtureIdentityKey = "prototype.semantic.fixture.identity/v1";

	/// <summary>Only for an exclusively claimed, fully closed fixture, before the writing constructor.</summary>
	internal static AtomicWikiActivation? RequireFixtureIdentity(LightningStoreOptions options, Guid expected)
	{
		if (!File.Exists(System.IO.Path.Combine(options.Path, "data.mdb")))
			throw new InvalidOperationException("An initialized fixture must have existing native data.");
		foreach (var name in new[] { "data.mdb", "lock.mdb" })
		{
			var file = new FileInfo(System.IO.Path.Combine(options.Path, name));
			if (file.LinkTarget is not null || (file.Exists && (file.Attributes & FileAttributes.ReparsePoint) != 0))
				throw new InvalidOperationException("Fixture native files cannot be linked.");
		}
		try
		{
			using var environment = new LightningEnvironment(options.Path, new EnvironmentConfiguration
			{
				MapSize = options.MapSize,
				MaxDatabases = options.MaxDatabases,
				MaxReaders = options.MaxReaders,
				PageSize = options.PageSize
			});
			environment.Open(EnvironmentOpenFlags.ReadOnly | EnvironmentOpenFlags.NoThreadLocalStorage);
			using var tx = environment.BeginTransaction(TransactionBeginFlags.ReadOnly);
			using var meta = tx.OpenDatabase(Tables.Meta.Name, new DatabaseConfiguration { Flags = DatabaseOpenFlags.None });
			if (!Tx.TryReadFixtureIdentity(tx, meta, out var actual) || actual != expected)
				throw new InvalidOperationException("Existing fixture Meta identity does not match issuance.");
			_ = ReadFixturePackageGeneration(tx, meta, expected);
			return RequireAtomicLayout(tx, meta, expected);
		}
		catch (Exception error) when (error is not InvalidOperationException)
		{
			throw new InvalidOperationException("Existing fixture identity could not be corroborated read-only.", error);
		}
	}

	internal void BindFixtureIdentity(Guid expected, bool firstOpen, CancellationToken ct)
	{
		WriteClassifiedFixtureRaw(tx =>
		{
			ct.ThrowIfCancellationRequested();
			var meta = _tables[Tables.Meta];
			var exists = Tx.TryReadFixtureIdentity(tx, meta, out var actual);
			if (exists)
			{
				if (actual != expected) throw new InvalidOperationException("Live fixture identity does not match issuance.");
			}
			else
			{
				if (!firstOpen) throw new InvalidOperationException("Reopen cannot create missing fixture identity.");
				var code = tx.Put(meta, Keys.Str(FixtureIdentityKey), expected.ToByteArray(bigEndian: true));
				if (code != MDBResultCode.Success) throw LightningStoreException.From(code, "bind fixture identity");
			}
			if (!exists)
			{
				Span<byte> state = stackalloc byte[32];
				EncodeUnknownPackageState(state, expected, 0);
				var code = tx.Put(meta, FixturePackageStateKey, state);
				if (code != MDBResultCode.Success) throw LightningStoreException.From(code, "initialize fixture package state");
			}
			else _ = ReadFixturePackageGeneration(tx, meta, expected);
			ct.ThrowIfCancellationRequested();
			Commit(tx);
			return true;
		});
	}

	private sealed partial class Tx
	{
		internal static bool TryReadFixtureIdentity(LightningTransaction tx, LmdbDb meta, out Guid identity)
		{
			identity = default;
			if (meta.GetFlags(tx) != DatabaseOpenFlags.None)
				throw new InvalidOperationException("Fixture Meta must be a plain nonduplicate database.");
			var (code, _, value) = tx.Get(meta, Keys.Str(FixtureIdentityKey));
			if (!FoundNative(code, "read fixture identity")) return false;
			// Reuse B1's native-size binding BEFORE any span/copy or Guid decoding. No unchecked
			// CopyToNewArray/AsSpan may narrow a native-sized value before this exact-length guard.
			if (CheckedLength(value) != 16) throw new InvalidOperationException("Fixture identity must contain exactly 16 bytes.");
			identity = new Guid(value.AsSpan(), bigEndian: true);
			return true;
		}
	}
}
